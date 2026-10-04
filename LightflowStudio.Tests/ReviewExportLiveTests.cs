using Lightflow.Actions;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    [Fact]
    public async Task AllFiveSemanticExportEntriesUseTheRealOwnedModalAndExistingMaterializers()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded();
            var directory = Directory.CreateTempSubdirectory("lightflow-352-export-").FullName;
            var startup = await LightflowStorageCoordinator.StartAsync(Path.Combine(directory, "profile")); var storage = startup.Coordinator!;
            storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
            var media = Directory.CreateDirectory(Path.Combine(directory, "media")).FullName;
            var root = (await storage.MediaRoots.CreateAsync("Export semantics", media)).Root!;
            await File.WriteAllBytesAsync(Path.Combine(media, "clip.mp4"), new byte[128]);
            var asset = (await storage.MediaAssets.CreateAsync(root.RootId, "clip.mp4", "video")).Asset!.Asset;
            var range = new MediaRange(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
            await storage.MediaRanges.SaveAsync(asset.AssetId, range);
            await storage.Subclips.CreateAsync(asset.AssetId, range);
            var secondRange = new MediaRange(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20));
            await storage.Subclips.CreateAsync(asset.AssetId, secondRange);
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, storage.MediaRanges, storage.Subclips);
            var window = new MainWindow(storage, startup.Status, startup.Diagnostic) { ShowActivated = false, Left = -32000, Opacity = 0 };
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            try {
                window.Show(); Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)));
                var grid = (BrowserGridModel)typeof(MainWindow).GetField("_browserGrid", flags)!.GetValue(window)!;
                var entries = new[] { new MediaFolderEntry(root.RootId, "clip.mp4", "CLIP.MP4", "clip.mp4", false,
                    new(MediaTypeCategory.Video), asset.FileSizeBytes, DateTimeOffset.UtcNow, AssetId: asset.AssetId) };
                grid.Populate(entries); grid.SelectSingle(0);
                Assert.True(window.ShellActions.Eligibility(ReviewShellActions.Export, window.ShellActionTarget, new ExportEntryArguments(ExportEntry.BrowserSubclips)).Available);
                Assert.False(window.ShellActions.Eligibility(ReviewShellActions.Export, window.ShellActionTarget, new ExportEntryArguments(ExportEntry.BrowserVideos)).Available);
                typeof(MainWindow).GetField("_lastLoadedBrowserState", flags)!.SetValue(window,
                    new BrowserFolderState(new(root.RootId, "Export semantics", media, ""), BrowserFolderStatus.Ready, entries, null, false, false, false));
                var playerAsset = new PlayerViewerAsset(root.RootId, "clip.mp4", "CLIP.MP4", "clip.mp4", MediaPresentationKind.Video, asset.AssetId);
                await host.OpenAsync(playerAsset, new(root.RootId, "clip.mp4", "CLIP.MP4", Path.Combine(media, "clip.mp4"), MediaRootAvailability.Online, true));
                host.SubclipsList.SelectedItem = host.SubclipsList.Items[1];
                typeof(MainWindow).GetField("_playerViewerHost", flags)!.SetValue(window, host);
                foreach (var entry in Enum.GetValues<ExportEntry>()) {
                    var player = entry is not (ExportEntry.BrowserVideos or ExportEntry.BrowserSubclips);
                    typeof(MainWindow).GetField("_browserPresentation", flags)!.SetValue(window, player ? BrowserPresentationMode.PlayerViewer : BrowserPresentationMode.Grid);
                    Exception? failure = null; var opened = false;
                    EventHandler tick = async (_, _) => {
                        var dialog = window.OwnedWindows.OfType<ExportDialog>().FirstOrDefault(); if (dialog is null) return;
                        timer.Stop(); opened = true;
                        try {
                            Assert.Same(window, dialog.Owner);
                            if (entry == ExportEntry.PlayerVideo) Assert.False(host.ExportButton.IsEnabled);
                            if (entry == ExportEntry.PlayerSelectedSubclips) Assert.False(host.ExportSelectedSubclipsMenuItem.IsEnabled);
                            if (entry == ExportEntry.PlayerAllSubclips) Assert.False(host.ExportAllSubclipsMenuItem.IsEnabled);
                            var model = (ExportDialogModel)typeof(ExportDialog).GetField("_model", flags)!.GetValue(dialog)!;
                            var subclips = entry is ExportEntry.BrowserSubclips or ExportEntry.PlayerSelectedSubclips or ExportEntry.PlayerAllSubclips;
                            Assert.Equal(subclips, model.IsSubclipExport);
                            Assert.All(model.Inputs, input => Assert.Equal(asset.AssetId, input.AssetId));
                            Assert.Equal(entry is ExportEntry.BrowserSubclips or ExportEntry.PlayerAllSubclips ? 2 : 1, model.Inputs.Count);
                            if (!subclips) Assert.Equal(range, model.Inputs[0].InitialTrim);
                            if (entry == ExportEntry.PlayerSelectedSubclips) Assert.Equal(secondRange, model.Inputs[0].InitialTrim);
                            var blocked = await window.ShellActions.InvokeAsync(new(ReviewShellActions.Export, new ExportEntryArguments(entry), SemanticController, Guid.NewGuid(), window.ShellActionTarget));
                            Assert.Equal(ActionUnavailableReason.ModalInteraction, blocked.Reason);
                        } catch (Exception error) { failure = error; }
                        finally { dialog.DialogResult = false; }
                    };
                    timer.Tick += tick; timer.Start();
                    var before = window.ShellActionTarget;
                    var result = await window.ShellActions.InvokeAsync(new(ReviewShellActions.Export, new ExportEntryArguments(entry), SemanticController, Guid.NewGuid(), before));
                    timer.Stop(); timer.Tick -= tick;
                    Assert.Null(failure); Assert.True(opened); Assert.Equal(ActionOutcome.Completed, result.Outcome);
                    Assert.Equal(before, window.ShellActionTarget); Assert.Single(grid.SelectedAssetIdsInBrowserOrder);
                    if (entry == ExportEntry.PlayerVideo) Assert.True(host.ExportButton.IsEnabled);
                    if (entry == ExportEntry.PlayerSelectedSubclips) Assert.True(host.ExportSelectedSubclipsMenuItem.IsEnabled);
                    if (entry == ExportEntry.PlayerAllSubclips) Assert.True(host.ExportAllSubclipsMenuItem.IsEnabled);
                    Assert.Equal(range, await storage.MediaRanges.RestoreAsync(asset.AssetId));
                }
            } finally {
                timer.Stop(); await host.CloseAsync(); window.Close(); await storage.DisposeAsync();
                try { Directory.Delete(directory, true); } catch (IOException) { }
            }
        });
    }
}
