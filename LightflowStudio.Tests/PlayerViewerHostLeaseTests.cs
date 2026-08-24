using System.Windows;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// #110: MediaPlaybackCoordinator allows only one active session at a time — a different consumer (e.g.
/// TrimEditorWindow) acquiring the lease while PlayerViewerHost's video is still open forcibly closes that
/// source out from under it. These tests prove PlayerViewerHost notices (via its own StateChanged subscription
/// observing an externally-published Empty snapshot) and recovers — raising BackRequested and resetting its
/// asset — rather than silently leaving stale transport controls enabled over a source that no longer exists.
/// </summary>
[Collection("STA dispatcher tests")]
public sealed class PlayerViewerHostLeaseTests
{
    [Fact]
    public async Task OpeningManyAssetsUsesCachedLutsWithoutAnyDiscoveryRefresh()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var cache = new FakeLutLibrary();
            var colors = new FakeColorStore();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, lutCache: cache, assetColors: colors,
                cameraLutFolder: () => Path.GetTempPath(), creativeLutFolder: () => Path.GetTempPath());

            for (var index = 0; index < 10; index++)
            {
                var asset = new PlayerViewerAsset(Guid.NewGuid(), $"clip-{index}.mp4", $"clip-{index}.mp4",
                    $"clip-{index}.mp4", MediaPresentationKind.Video, Guid.NewGuid());
                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key,
                    Path.GetFullPath(asset.RelativePath), MediaRootAvailability.Online, true));
            }

            Assert.Equal(0, cache.RefreshCount);
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SavedColor_IsAppliedAfterMediaPresentationWithoutGatingBackendOpen(bool hasCamera, bool hasCreative)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"lightflow-first-color-{Guid.NewGuid():N}"));
            try
            {
                var cameraId = Guid.NewGuid(); var creativeId = Guid.NewGuid();
                var cameraPath = WriteIdentityCube(folder.FullName, "camera.cube");
                var creativePath = WriteIdentityCube(folder.FullName, "creative.cube");
                var library = new FakeLutLibrary(new Dictionary<Guid, string>
                    { [cameraId] = cameraPath, [creativeId] = creativePath });
                var camera = hasCamera ? new ColorLutReference(cameraId, "Camera", "camera",
                    LutResourceAvailability.Available) : null;
                var creative = hasCreative ? new ColorLutReference(creativeId, "Creative", "creative",
                    LutResourceAvailability.Available) : null;
                var colors = new FakeColorStore(new AssetColorIntent(Guid.Empty, camera, creative, "saved"));
                var backend = new FakeBackend();
                await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
                var host = new PlayerViewerHost(coordinator, lutCache: library, assetColors: colors,
                    cameraLutFolder: () => folder.FullName, creativeLutFolder: () => folder.FullName);
                var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4",
                    MediaPresentationKind.Video, Guid.NewGuid());

                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key,
                    Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true));
                await WaitUntilAsync(() => host.ColorToggleButton.IsEnabled, "saved Color publication");

                Assert.Equal(["open", "presentation", "color"], backend.OpenPresentationOperations);
                Assert.Null(backend.PipelineAtOpen);
                Assert.Equal(hasCamera, backend.ColorCalls[^1].Pipeline?.Camera is not null);
                Assert.Equal(hasCreative, backend.ColorCalls[^1].Pipeline?.Creative is not null);
                Assert.False(backend.ColorCalls[^1].Bypass);
            }
            finally { folder.Delete(true); }
        });
    }

    [Fact]
    public async Task DelayedLutInitialization_NeverGatesPlaybackOpenOrPlayerPublication()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var initialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cache = new FakeLutLibrary(initialization: initialization.Task);
            var backend = new FakeBackend();
            var milestones = new List<PlayerOpenMilestone>();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, lutCache: cache, assetColors: new FakeColorStore(),
                cameraLutFolder: () => Path.GetTempPath(), creativeLutFolder: () => Path.GetTempPath(),
                openMilestone: milestones.Add);
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4",
                MediaPresentationKind.Video, Guid.NewGuid());

            var opened = host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true));
            Assert.Same(opened, await Task.WhenAny(opened, Task.Delay(1000)));
            await opened;

            Assert.Equal(["open", "presentation"], backend.OpenPresentationOperations);
            Assert.True(host.PositionSlider.IsEnabled);
            Assert.Contains(PlayerOpenMilestone.PlayerControlsPublished, milestones);
            Assert.Contains(PlayerOpenMilestone.ColorCacheWaitStarted, milestones);
            Assert.DoesNotContain(PlayerOpenMilestone.ColorCacheWaitCompleted, milestones);
            Assert.True(milestones.IndexOf(PlayerOpenMilestone.PlaybackBackendOpenStarted)
                < milestones.IndexOf(PlayerOpenMilestone.ColorCacheWaitStarted));

            initialization.SetResult();
            await WaitUntilAsync(() => milestones.Contains(PlayerOpenMilestone.ColorPublished),
                "background Color publication after LUT initialization");
            Assert.True(host.CameraLutCombo.IsEnabled);
            Assert.True(host.CreativeLutCombo.IsEnabled);
        });
    }

    [Fact]
    public async Task RepeatedPlayerOpensReuseParsedRuntimeLutByStableIdentity()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var lutId = Guid.NewGuid();
            var path = Path.Combine(Path.GetTempPath(), $"cached-{lutId:N}.cube");
            var resource = Resource(lutId, "Cached", path);
            var scanner = new DynamicLutLibrary((folder, _) => Task.FromResult(
                new LutLibrarySnapshot(folder, [resource], [])), new Dictionary<Guid, string> { [lutId] = path });
            var parseCount = 0;
            using var cache = new ApplicationLutLibraryCache(scanner, _ =>
            {
                parseCount++;
                return new CubeLutData(2, new float[32]);
            });
            await cache.InitializeAsync(Path.GetTempPath(), Path.GetTempPath());
            var colors = new FakeColorStore(new AssetColorIntent(Guid.Empty,
                new(lutId, "Cached", lutId.ToString("N"), LutResourceAvailability.Available), null, "saved"));
            var backend = new FakeBackend();
            var milestones = new List<PlayerOpenMilestone>();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, lutCache: cache, assetColors: colors,
                cameraLutFolder: () => Path.GetTempPath(), creativeLutFolder: () => Path.GetTempPath(),
                openMilestone: milestones.Add);

            for (var index = 0; index < 5; index++)
            {
                var asset = new PlayerViewerAsset(Guid.NewGuid(), $"clip-{index}.mp4", $"clip-{index}.mp4",
                    $"clip-{index}.mp4", MediaPresentationKind.Video, Guid.NewGuid());
                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key,
                    Path.GetFullPath(asset.RelativePath), MediaRootAvailability.Online, true));
                var expectedPublications = index + 1;
                await WaitUntilAsync(() => milestones.Count(item => item == PlayerOpenMilestone.ColorPublished)
                    == expectedPublications, $"Color publication {expectedPublications}");
            }

            Assert.Equal(1, parseCount);
        });
    }

    [Fact]
    public async Task ColorToggleAndHoldC_BypassPresentationWithoutChangingAssignments()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            var colors = new FakeColorStore();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, lutCache: new FakeLutLibrary(), assetColors: colors,
                cameraLutFolder: () => Path.GetTempPath(), creativeLutFolder: () => Path.GetTempPath());
            var window = new Window { Content = host, Width = 600, Height = 400, ShowInTaskbar = false };
            window.Show();
            try
            {
                var assetId = Guid.NewGuid();
                var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4",
                    MediaPresentationKind.Video, assetId);
                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key,
                    Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true));
                await WaitUntilAsync(() => host.ColorToggleButton.IsEnabled, "Color controls");

                Assert.True(host.ColorToggleButton.IsChecked);
                Assert.Null(host.ColorToggleButton.Content);
                Assert.True(host.CameraLutCombo.IsEnabled);
                Assert.True(host.CreativeLutCombo.IsEnabled);
                Assert.False(backend.ColorCalls[^1].Bypass); // Both-No-LUT remains Color On.

                host.ColorToggleButton.IsChecked = false;
                host.ColorToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.False(host.ColorToggleButton.IsChecked);
                Assert.Null(host.ColorToggleButton.Content);
                Assert.False(host.CameraLutCombo.IsEnabled);
                Assert.False(host.CreativeLutCombo.IsEnabled);
                Assert.True(backend.ColorCalls[^1].Bypass);
                Assert.Equal(0, colors.SetCount);

                var callsWhileOff = backend.ColorCalls.Count;
                Key(host, window, System.Windows.Input.Key.C, UIElement.PreviewKeyDownEvent);
                Assert.False(host.ColorToggleButton.IsChecked);
                Assert.False(host.CameraLutCombo.IsEnabled);
                Assert.False(host.CreativeLutCombo.IsEnabled);
                Key(host, window, System.Windows.Input.Key.C, UIElement.PreviewKeyUpEvent);
                Assert.False(host.ColorToggleButton.IsChecked);
                Assert.Equal(callsWhileOff, backend.ColorCalls.Count);

                host.ColorToggleButton.IsChecked = true;
                host.ColorToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.True(host.ColorToggleButton.IsChecked);
                Assert.True(host.CameraLutCombo.IsEnabled);
                Assert.True(host.CreativeLutCombo.IsEnabled);
                Assert.False(backend.ColorCalls[^1].Bypass);

                Key(host, window, System.Windows.Input.Key.C, UIElement.PreviewKeyDownEvent);
                Assert.True(backend.ColorCalls[^1].Bypass);
                Assert.False(host.ColorToggleButton.IsChecked);
                Assert.False(host.CameraLutCombo.IsEnabled);
                Assert.False(host.CreativeLutCombo.IsEnabled);
                Key(host, window, System.Windows.Input.Key.C, UIElement.PreviewKeyUpEvent);
                Assert.False(backend.ColorCalls[^1].Bypass);
                Assert.True(host.ColorToggleButton.IsChecked);
                Assert.True(host.CameraLutCombo.IsEnabled);
                Assert.True(host.CreativeLutCombo.IsEnabled);
                Assert.Equal(0, colors.SetCount);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task OpenLutFolderChoice_RestoresStageSelectionAndDoesNotAssignIt()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend(); var colors = new FakeColorStore(); var folders = new FakeFolderLauncher();
            var cameraFolder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"camera-{Guid.NewGuid():N}")).FullName;
            var creativeFolder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"creative-{Guid.NewGuid():N}")).FullName;
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, folderLauncher: folders, lutCache: new FakeLutLibrary(),
                assetColors: colors, cameraLutFolder: () => cameraFolder,
                creativeLutFolder: () => creativeFolder);
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4",
                MediaPresentationKind.Video, Guid.NewGuid());
            await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true));
            await WaitUntilAsync(() => host.ColorToggleButton.IsEnabled, "Color controls");
            var original = host.CameraLutCombo.SelectedItem;
            host.CameraLutCombo.SelectedIndex = host.CameraLutCombo.Items.Count - 1;
            Assert.Equal(cameraFolder, folders.OpenedDirectory);
            Assert.Same(original, host.CameraLutCombo.SelectedItem);
            Assert.Equal(0, colors.SetCount);
            Assert.Contains("Open LUT Folder", host.CameraLutCombo.Items[^1]!.ToString());
            Assert.Contains("Open LUT Folder", host.CreativeLutCombo.Items[^1]!.ToString());
            host.CreativeLutCombo.SelectedIndex = host.CreativeLutCombo.Items.Count - 1;
            Assert.Equal(creativeFolder, folders.OpenedDirectory);
            Directory.Delete(cameraFolder);
            Directory.Delete(creativeFolder);
        });
    }

    [Fact]
    public async Task LiveFolderRefresh_IsStageSpecificIdentitySafeAndSuppressesStaleScans()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"live-luts-{Guid.NewGuid():N}"));
            try
            {
                var initial = Directory.CreateDirectory(Path.Combine(root.FullName, "initial")).FullName;
                var same = Directory.CreateDirectory(Path.Combine(root.FullName, "same")).FullName;
                var missing = Directory.CreateDirectory(Path.Combine(root.FullName, "missing")).FullName;
                var slow = Directory.CreateDirectory(Path.Combine(root.FullName, "slow")).FullName;
                var fast = Directory.CreateDirectory(Path.Combine(root.FullName, "fast")).FullName;
                var creativeFolder = Directory.CreateDirectory(Path.Combine(root.FullName, "creative")).FullName;
                var cameraId = Guid.NewGuid(); var creativeId = Guid.NewGuid();
                var slowId = Guid.NewGuid(); var fastId = Guid.NewGuid();
                var paths = new Dictionary<Guid, string>
                {
                    [cameraId] = WriteIdentityCube(initial, "Camera.cube"),
                    [creativeId] = WriteIdentityCube(creativeFolder, "Creative.cube"),
                    [slowId] = WriteIdentityCube(slow, "Slow.cube"),
                    [fastId] = WriteIdentityCube(fast, "Fast.cube")
                };
                File.Copy(paths[cameraId], Path.Combine(same, "Moved Camera.cube"));
                var snapshots = new Dictionary<string, ManagedLutResource[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [initial] = [Resource(cameraId, "Camera", paths[cameraId])],
                    [same] = [Resource(cameraId, "Moved Camera", Path.Combine(same, "Moved Camera.cube"))],
                    [missing] = [],
                    [slow] = [Resource(slowId, "Slow", paths[slowId])],
                    [fast] = [Resource(fastId, "Fast", paths[fastId])],
                    [creativeFolder] = [Resource(creativeId, "Creative", paths[creativeId])]
                };
                var currentCamera = initial;
                var scanner = new DynamicLutLibrary(async (folder, token) =>
                {
                    if (string.Equals(folder, slow, StringComparison.OrdinalIgnoreCase)) await Task.Delay(500, token);
                    return new(folder, snapshots[folder], []);
                }, paths.Concat(new[] { new KeyValuePair<Guid, string>(cameraId, Path.Combine(same, "Moved Camera.cube")) })
                    .GroupBy(item => item.Key).ToDictionary(group => group.Key, group => group.Last().Value));
                using var library = new ApplicationLutLibraryCache(scanner);
                await library.InitializeAsync(initial, creativeFolder);
                var colors = new FakeColorStore(assetId => new(assetId,
                    new(cameraId, "Camera", "camera", (currentCamera == initial || currentCamera == same)
                        ? LutResourceAvailability.Available : LutResourceAvailability.Missing,
                        "Not present in Camera root."),
                    new(creativeId, "Creative", "creative", LutResourceAvailability.Available), "saved"));
                var backend = new FakeBackend();
                await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
                var host = new PlayerViewerHost(coordinator, lutCache: library, assetColors: colors,
                    cameraLutFolder: () => currentCamera, creativeLutFolder: () => creativeFolder);
                var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4",
                    MediaPresentationKind.Video, Guid.NewGuid());
                await host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key,
                    Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true));
                await WaitUntilAsync(() => host.ColorToggleButton.IsEnabled, "initial Color publication");
                var creativeItems = host.CreativeLutCombo.ItemsSource;

                currentCamera = missing;
                await library.RefreshAsync(ColorLutStage.Camera, currentCamera);
                await host.RefreshColorFoldersAsync(cameraChanged: true, creativeChanged: false);
                Assert.Contains("Unavailable", host.CameraLutCombo.SelectedItem!.ToString());
                Assert.Same(creativeItems, host.CreativeLutCombo.ItemsSource);
                Assert.Equal(0, colors.SetCount);

                currentCamera = same;
                await library.RefreshAsync(ColorLutStage.Camera, currentCamera);
                await host.RefreshColorFoldersAsync(cameraChanged: true, creativeChanged: false);
                Assert.Contains("Moved Camera", host.CameraLutCombo.SelectedItem!.ToString());

                currentCamera = slow;
                var stale = library.RefreshAsync(ColorLutStage.Camera, currentCamera);
                await Task.Delay(25);
                currentCamera = fast;
                var latest = library.RefreshAsync(ColorLutStage.Camera, currentCamera);
                await Task.WhenAll(stale, latest);
                await host.RefreshColorFoldersAsync(cameraChanged: true, creativeChanged: false);
                Assert.Contains(host.CameraLutCombo.Items.Cast<object>(), item => item.ToString()!.Contains("Fast"));
                Assert.DoesNotContain(host.CameraLutCombo.Items.Cast<object>(), item => item.ToString()!.Contains("Slow"));
            }
            finally { root.Delete(true); }
        });
    }

    [Fact]
    public async Task AnotherConsumerAcquiringTheLease_RaisesBackRequestedAndClearsTheCurrentAsset()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            var backRequestedCount = 0;
            host.BackRequested += (_, _) => backRequestedCount++;

            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4", MediaPresentationKind.Video);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);
            await host.OpenAsync(asset, resolution);
            Assert.Equal("clip.mp4", host.CurrentAsset?.Name);

            // A different owner acquiring the coordinator forcibly closes PlayerViewerHost's still-open source,
            // exactly like TrimEditorPlayback.OpenAsync would when the user opens Trim on an unrelated file.
            await using var intruderLease = await coordinator.AcquireAsync(Guid.NewGuid());

            await WaitUntilAsync(() => backRequestedCount > 0, "PlayerViewerHost to notice the stolen lease");
            Assert.Null(host.CurrentAsset);
        });
    }

    [Fact]
    public async Task InvalidSource_ReleasesTheLeaseBeforeThrowingSoSpaceCannotReachTheRejectedSession()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            // A zero Duration is exactly what OpenVideoAsync treats as "could not be decoded for preview" —
            // the same outcome a corrupt/unsupported real file produces (MediaPlaybackService.OpenAsync
            // swallows a genuine backend failure into a Failed snapshot rather than throwing).
            var backend = new FakeBackend(duration: TimeSpan.Zero);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            // KeyEventArgs requires a real PresentationSource — a bare, never-shown UserControl has none, so
            // it needs a real (offscreen) Window, matching BrowserPlayerViewerLiveInteractionTests' pattern.
            var window = new Window
            {
                Content = host, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000, Top = -32000, ShowInTaskbar = false, Width = 400, Height = 300
            };
            window.Show();
            try
            {
                var asset = new PlayerViewerAsset(Guid.NewGuid(), "broken.mp4", "broken.mp4", "broken.mp4", MediaPresentationKind.Video);
                var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                    Path.GetFullPath("broken.mp4"), MediaRootAvailability.Online, true);
                await host.OpenAsync(asset, resolution);

                // Before the fix, _service stayed assigned to the rejected session, so Space's
                // "_service is not null" guard alone would still invoke Play/Pause on it despite the failure.
                Space(host, window);
                Assert.Equal(0, backend.PlayCallCount);
                Assert.Equal(0, backend.PauseCallCount);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task VideoOnlySource_LeavesVolumeAndMuteControlsDisabledButKeepsTheRestOfTransportUsable()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend(hasAudio: false);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);

            var asset = new PlayerViewerAsset(Guid.NewGuid(), "silent.mp4", "silent.mp4", "silent.mp4", MediaPresentationKind.Video);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("silent.mp4"), MediaRootAvailability.Online, true);
            await host.OpenAsync(asset, resolution);

            Assert.False(host.MuteButton.IsEnabled);
            Assert.False(host.VolumeSlider.IsEnabled);
            Assert.True(host.PlayPauseButton.IsEnabled);
        });
    }

    [Fact]
    public async Task SourceWithAudio_EnablesVolumeAndMuteAndClickingMuteTogglesTheSharedSession()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend(hasAudio: true);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);

            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4", MediaPresentationKind.Video);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);
            await host.OpenAsync(asset, resolution);

            Assert.True(host.MuteButton.IsEnabled);
            Assert.True(host.VolumeSlider.IsEnabled);
            Assert.Equal(100, host.VolumeSlider.Value);
            Assert.False(backend.Mute);

            host.MuteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.True(backend.Mute);

            host.MuteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.False(backend.Mute);

            host.VolumeSlider.Value = 40;
            Assert.Equal(40, backend.Volume);
        });
    }

    [Fact]
    public async Task SavedIn_OpensPausedOnThatAuthoritativeTimestamp()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var assetId = Guid.NewGuid();
            var savedIn = TimeSpan.FromTicks(123_456_789);
            var store = new FakeRangeStore(new MediaRange(TimeSpan.FromSeconds(60), savedIn, TimeSpan.FromSeconds(40)));
            var host = new PlayerViewerHost(coordinator, store);
            var committedRangeStates = new List<MediaRangeStateChangedEventArgs>();
            host.RangeStateChanged += (_, change) => committedRangeStates.Add(change);
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4",
                MediaPresentationKind.Video, assetId);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);

            await host.OpenAsync(asset, resolution);

            Assert.Equal(assetId, store.RestoredAssetId);
            Assert.Equal([savedIn], backend.SeekPositions);
            Assert.Equal(["open", "seek", "presentation"], backend.OpenPresentationOperations);
            Assert.Equal(0, backend.PlayCallCount);
            Assert.Equal("Play", host.PlayPauseButton.Content);
            Assert.Equal(savedIn.TotalMilliseconds, host.PositionSlider.Value, 3);
            Assert.Equal("Active", host.SetInButton.Tag);
            Assert.Equal("Active", host.SetOutButton.Tag);
            Assert.True(host.SetInButton.IsEnabled);
            Assert.True(host.SetOutButton.IsEnabled);

            host.SetInButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitUntilAsync(() => store.SaveCount == 1, "active Set In replacement save");
            Assert.Collection(committedRangeStates, change =>
            {
                Assert.Equal(assetId, change.AssetId);
                Assert.True(change.HasSavedRange);
            });
            Assert.Equal("Active", host.SetInButton.Tag);
            Assert.True(host.SetInButton.IsEnabled);

            host.ClearInButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitUntilAsync(() => store.SaveCount == 2, "clear In save");
            Assert.Equal(2, committedRangeStates.Count);
            Assert.True(committedRangeStates[1].HasSavedRange); // The saved Out boundary remains.
            Assert.Null(store.SavedRange?.In);
            Assert.Null(host.SetInButton.Tag);
            Assert.Equal("Active", host.SetOutButton.Tag);
        });
    }

    [Fact]
    public async Task RangeWithoutSavedIn_PreservesNormalOpeningPosition()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var store = new FakeRangeStore(new MediaRange(TimeSpan.FromSeconds(60), Out: TimeSpan.FromSeconds(40)));
            var host = new PlayerViewerHost(coordinator, store);
            MediaRangeStateChangedEventArgs? committedRangeState = null;
            host.RangeStateChanged += (_, change) => committedRangeState = change;
            var assetId = Guid.NewGuid();
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4",
                MediaPresentationKind.Video, assetId);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);

            await host.OpenAsync(asset, resolution);

            Assert.Empty(backend.SeekPositions);
            Assert.Equal(["open", "presentation"], backend.OpenPresentationOperations);
            Assert.Equal(0, backend.PlayCallCount);
            Assert.Equal(TimeSpan.Zero.TotalMilliseconds, host.PositionSlider.Value);
            Assert.Null(host.SetInButton.Tag);
            Assert.Equal("Active", host.SetOutButton.Tag);

            host.ClearOutButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitUntilAsync(() => store.SaveCount == 1, "clear only saved boundary");
            Assert.NotNull(committedRangeState);
            Assert.Equal(assetId, committedRangeState.AssetId);
            Assert.False(committedRangeState.HasSavedRange);
        });
    }

    [Fact]
    public async Task PreviousFrame_RetainsCurrentFrameWhileNativeSurfaceReconstructsThenPublishesSettledFrame()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4", MediaPresentationKind.Video);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);
            await host.OpenAsync(asset, resolution);

            host.PreviousFrameButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            await WaitUntilAsync(() => backend.Operations.Count == 3, "Previous Frame presentation handoff");
            Assert.Equal(["capture", "backward", "capture"], backend.Operations);
            Assert.Equal(Visibility.Hidden, host.VideoHost.Visibility);
            Assert.Equal(Visibility.Visible, host.SteppedFrameSurface.Visibility);
            Assert.NotNull(host.SteppedFrameSurface.Source);

            host.PreviousFrameButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitUntilAsync(() => backend.Operations.Count == 5, "repeated Previous Frame presentation");
            Assert.Equal(["capture", "backward", "capture", "backward", "capture"], backend.Operations);
        });
    }

    [Fact]
    public async Task Screengrab_AfterPreviousFrameSavesRetainedDecodedFrameWithoutAnotherBackendCapture()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            var screengrabs = new FakeScreengrabService();
            var folders = new FakeFolderLauncher();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, screengrabService: screengrabs, folderLauncher: folders);
            var asset = new PlayerViewerAsset(Guid.NewGuid(), "clip.mp4", "clip.mp4", "clip.mp4", MediaPresentationKind.Video);
            var resolution = new MediaPathResolution(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath("clip.mp4"), MediaRootAvailability.Online, true);
            await host.OpenAsync(asset, resolution);

            host.PreviousFrameButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitUntilAsync(() => backend.Operations.Count == 3, "Previous Frame presentation handoff");
            var pauseCallsBeforeCapture = backend.PauseCallCount;
            host.ScreengrabButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await WaitUntilAsync(() => screengrabs.SavedFrame is not null, "screengrab save");

            Assert.Equal(["capture", "backward", "capture"], backend.Operations);
            Assert.Equal(0, backend.PlayCallCount);
            Assert.Equal(pauseCallsBeforeCapture, backend.PauseCallCount);
            Assert.Equal((1, 1), (screengrabs.SavedFrame!.Width, screengrabs.SavedFrame.Height));
            Assert.Equal(Visibility.Collapsed, host.ScreengrabFeedbackText.Visibility);
            Assert.Equal(Visibility.Visible, host.ScreengrabSuccessButton.Visibility);

            host.ScreengrabSuccessButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(Path.Combine(Path.GetTempPath(), "screengrabs"), folders.OpenedDirectory);
        });
    }

    private static void Space(PlayerViewerHost host, Window window) => host.RaiseEvent(
        new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Space)
        { RoutedEvent = UIElement.PreviewKeyDownEvent });

    private static void Key(PlayerViewerHost host, Window window, System.Windows.Input.Key key, RoutedEvent routedEvent) =>
        host.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(window), 0, key) { RoutedEvent = routedEvent });

    private static async Task WaitUntilAsync(Func<bool> condition, string waitingFor, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {waitingFor}.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeBackend(TimeSpan? duration = null, bool hasAudio = false) : IMediaPlaybackBackend
    {
        public List<string> Operations { get; } = [];
        public List<string> OpenPresentationOperations { get; } = [];
        public List<TimeSpan> SeekPositions { get; } = [];
        public int PlayCallCount { get; private set; }
        public int PauseCallCount { get; private set; }
        public event EventHandler<MediaPresentationTimestamp>? FramePresented { add { } remove { } }
        public event EventHandler<MediaPlaybackError>? Failed { add { } remove { } }
        public int Volume { get; set; } = 100;
        public bool Mute { get; set; }
        public List<(PlayerColorPipeline? Pipeline, bool Bypass)> ColorCalls { get; } = [];
        public PlayerColorPipeline? PipelineAtOpen { get; private set; }
        public bool BypassAtOpen { get; private set; }
        public void SetColorPipeline(PlayerColorPipeline? pipeline, bool bypass)
        {
            ColorCalls.Add((pipeline, bypass));
            OpenPresentationOperations.Add("color");
        }
        public FrameworkElement CreatePresentationSurface()
        {
            OpenPresentationOperations.Add("presentation");
            return new();
        }
        public void ReleasePresentationSurface(FrameworkElement surface) { }
        public void CancelPending() { }
        public Task<PlaybackBackendOpened> OpenAsync(string sourcePath, CancellationToken token)
        {
            PipelineAtOpen = ColorCalls.LastOrDefault().Pipeline;
            BypassAtOpen = ColorCalls.LastOrDefault().Bypass;
            OpenPresentationOperations.Add("open");
            var audioStreams = hasAudio ? new[] { new MediaAudioStreamInfo(0, null, null, 2, true) } : [];
            var source = new MediaPlaybackSourceInfo(sourcePath, duration ?? TimeSpan.FromSeconds(60), TimeSpan.Zero, 1920, 1080, audioStreams, hasAudio ? 0 : null, false);
            return Task.FromResult(new PlaybackBackendOpened(source, new(TimeSpan.Zero)));
        }
        public Task CloseAsync(CancellationToken token) => Task.CompletedTask;
        public Task PlayAsync(CancellationToken token) { PlayCallCount++; return Task.CompletedTask; }
        public Task PauseAsync(CancellationToken token) { PauseCallCount++; return Task.CompletedTask; }
        public Task<MediaPresentationTimestamp> SeekAsync(TimeSpan position, CancellationToken token)
        {
            OpenPresentationOperations.Add("seek");
            SeekPositions.Add(position);
            return Task.FromResult(new MediaPresentationTimestamp(position));
        }
        public Task<MediaPresentationTimestamp> StepForwardAsync(CancellationToken token) => Task.FromResult(new MediaPresentationTimestamp(TimeSpan.Zero));
        public Task<MediaPresentationTimestamp> StepBackwardAsync(CancellationToken token)
        {
            Operations.Add("backward");
            return Task.FromResult(new MediaPresentationTimestamp(TimeSpan.Zero));
        }
        public Task<MediaDecodedFrame> GetFrameAsync(TimeSpan position, CancellationToken token) => throw new NotSupportedException();
        public Task<MediaDecodedFrame> CapturePresentedFrameAsync(CancellationToken token)
        {
            Operations.Add("capture");
            return Task.FromResult(new MediaDecodedFrame(new(TimeSpan.Zero), 1, 1, 4, [0, 0, 0, 255]));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeLutLibrary(IReadOnlyDictionary<Guid, string>? paths = null,
        Task? initialization = null) : ILutLibraryCache
    {
        public int RefreshCount { get; private set; }
        private LutLibrarySnapshot Library(string folder) => new(folder, (paths ?? new Dictionary<Guid, string>()).Select(item =>
                new ManagedLutResource(item.Key, Path.GetFileNameWithoutExtension(item.Value), Path.GetFileName(item.Value),
                    item.Key.ToString("N"), LutDimension.ThreeDimensional, 2, LutResourceAvailability.Available, item.Value)).ToArray(), []);
        public Task InitializeAsync(string cameraFolder, string creativeFolder, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LutLibrarySnapshot> RefreshAsync(ColorLutStage stage, string folder, CancellationToken cancellationToken = default)
        { RefreshCount++; return Task.FromResult(Library(folder)); }
        public Task WaitUntilInitializedAsync(CancellationToken cancellationToken = default) =>
            (initialization ?? Task.CompletedTask).WaitAsync(cancellationToken);
        public LutLibrarySnapshot Snapshot(ColorLutStage stage) => Library(Path.GetTempPath());
        public ManagedLutResource? Get(ColorLutStage stage, Guid lutId) => Snapshot(stage).Resources.FirstOrDefault(x => x.LutId == lutId);
        public string ResolvePath(ColorLutStage stage, Guid lutId) => paths![lutId];
        public Task<CubeLutData> GetRuntimeAsync(ColorLutStage stage, Guid lutId,
            CancellationToken cancellationToken = default) => Task.FromResult(CubeLutData.Load(paths![lutId]));
    }

    private sealed class FakeColorStore : IAssetColorStore
    {
        private readonly Func<Guid, AssetColorIntent>? _provider;
        private readonly AssetColorIntent? _restored;
        public FakeColorStore() { }
        public FakeColorStore(AssetColorIntent restored) => _restored = restored;
        public FakeColorStore(Func<Guid, AssetColorIntent> provider) => _provider = provider;
        public int SetCount { get; private set; }
        public Task<AssetColorIntent> GetAsync(Guid assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_provider?.Invoke(assetId) ?? (_restored is null
                ? new AssetColorIntent(assetId, null, null, "none") : _restored with { AssetId = assetId }));
        public Task<IReadOnlyDictionary<Guid, AssetColorIntent>> GetAsync(IReadOnlyCollection<Guid> assetIds,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<Guid, AssetColorIntent>>(
                assetIds.ToDictionary(id => id, id => new AssetColorIntent(id, null, null, "none")));
        public Task SetStageAsync(IReadOnlyCollection<Guid> assetIds, ColorLutStage stage, Guid? lutId,
            CancellationToken cancellationToken = default) { SetCount++; return Task.CompletedTask; }
        public Task SetAsync(IReadOnlyCollection<ColorAssignmentChange> changes,
            CancellationToken cancellationToken = default) { SetCount++; return Task.CompletedTask; }
    }

    private sealed class DynamicLutLibrary(
        Func<string, CancellationToken, Task<LutLibrarySnapshot>> refresh,
        IReadOnlyDictionary<Guid, string> paths) : ILutLibrary
    {
        public Task<LutLibrarySnapshot> RefreshAsync(string folder, CancellationToken cancellationToken = default) =>
            refresh(folder, cancellationToken);
        public Task<ManagedLutResource?> GetAsync(Guid lutId, string folder, CancellationToken cancellationToken = default) =>
            Task.FromResult<ManagedLutResource?>(null);
        public Task<string> ResolvePathAsync(Guid lutId, string folder, CancellationToken cancellationToken = default) =>
            Task.FromResult(paths[lutId]);
    }

    private static ManagedLutResource Resource(Guid id, string name, string path) =>
        new(id, name, Path.GetFileName(path), id.ToString("N"), LutDimension.ThreeDimensional, 2,
            LutResourceAvailability.Available, path);

    private static string WriteIdentityCube(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, "LUT_3D_SIZE 2\n0 0 0\n1 0 0\n0 1 0\n1 1 0\n0 0 1\n1 0 1\n0 1 1\n1 1 1\n");
        return path;
    }

    private sealed class FakeRangeStore(MediaRange? restored) : IMediaRangeStore
    {
        public Guid? RestoredAssetId { get; private set; }
        public int SaveCount { get; private set; }
        public MediaRange? SavedRange { get; private set; }

        public Task<MediaRange?> RestoreAsync(Guid assetId, CancellationToken cancellationToken = default)
        {
            RestoredAssetId = assetId;
            return Task.FromResult(restored);
        }

        public Task SaveAsync(Guid assetId, MediaRange? range, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            SavedRange = range;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScreengrabService : IFrameScreengrabService
    {
        public MediaDecodedFrame? SavedFrame { get; private set; }

        public Task<FrameScreengrabResult> SaveAsync(string sourcePath, MediaDecodedFrame frame,
            CancellationToken cancellationToken = default)
        {
            SavedFrame = frame;
            return Task.FromResult(new FrameScreengrabResult(
                Path.Combine(Path.GetTempPath(), "screengrabs", "frame.png"),
                frame.Timestamp, frame.Width, frame.Height));
        }
    }

    private sealed class FakeFolderLauncher : IFolderLauncher
    {
        public string? OpenedDirectory { get; private set; }
        public void Open(string directory) => OpenedDirectory = directory;
    }
}
