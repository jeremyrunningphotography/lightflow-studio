using System.Windows;
using System.Windows.Controls.Primitives;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class VisualIndexSamplingTests
{
    [Theory]
    [InlineData(0.001, 25, 48)]
    [InlineData(0.1, 25, 48)]
    [InlineData(12, 29.97002997, 12)]
    [InlineData(3600, 60, 24)]
    [InlineData(86400, 24, 48)]
    public void PlansAreBoundedDeterministicDistinctAndCoverSource(double seconds, double rate, int count)
    {
        var duration = TimeSpan.FromSeconds(seconds);
        var plan = VisualIndexSampling.Plan(duration, rate, count);
        Assert.Equal(plan, VisualIndexSampling.Plan(duration, rate, count));
        Assert.InRange(plan.Count, 1, count);
        Assert.Equal(TimeSpan.Zero, plan[0]);
        Assert.Equal(plan.Count, plan.Distinct().Count());
        Assert.Equal(plan.Order(), plan);
        Assert.All(plan, p => Assert.InRange(p.Ticks, 0, duration.Ticks - 1));
        var step = (long)Math.Ceiling(TimeSpan.TicksPerSecond / rate);
        Assert.True(duration.Ticks - plan[^1].Ticks <= 2 * step + 10);
        Assert.All(plan, p =>
        {
            Assert.Equal(0, p.Ticks % 10); // FFmpeg's microsecond seek precision.
            var exactFrame = Math.Round(p.TotalSeconds * rate) / rate;
            Assert.InRange(exactFrame - p.TotalSeconds, -0.0000001, 0.0000011);
        });
    }
    [Theory]
    [InlineData(43.370667, 2599)]
    [InlineData(46.890667, 2810)]
    [InlineData(2.0125, 120)]
    public void FractionalCadenceDoesNotAccumulateRoundingBeyondLastFrame(double containerSeconds, int frames)
    {
        const double rate = 60000d / 1001;
        var lastTimestamp = (frames - 1) / rate;
        foreach (var count in VisualIndexSampling.Counts)
        {
            var last = VisualIndexSampling.Plan(TimeSpan.FromSeconds(containerSeconds), rate, count)[^1];
            Assert.InRange(lastTimestamp - last.TotalSeconds, 0, 0.0000011);
        }
    }
    [Fact]
    public void UnknownDurationAndInvalidCountAreSafeAndChangingDurationReplans()
    {
        Assert.Empty(VisualIndexSampling.Plan(null, 0, 24));
        Assert.Empty(VisualIndexSampling.Plan(TimeSpan.Zero, 0, 24));
        Assert.Empty(VisualIndexSampling.Plan(TimeSpan.FromTicks(-1), 0, 24));
        Assert.Equal(24, VisualIndexSampling.Plan(TimeSpan.FromSeconds(60), double.NaN, -1).Count);
        Assert.Single(VisualIndexSampling.Plan(TimeSpan.FromTicks(1), double.PositiveInfinity, 48));
        Assert.NotEqual(VisualIndexSampling.Plan(TimeSpan.FromSeconds(60), 25, 24)[^1],
            VisualIndexSampling.Plan(TimeSpan.FromSeconds(120), 25, 24)[^1]);
        Assert.Equal(48, VisualIndexSampling.Plan(TimeSpan.MaxValue, 60, 48).Count);
    }
    [Theory]
    [InlineData(220, 1)] [InlineData(280, 1)] [InlineData(340, 2)] [InlineData(580, 3)]
    public void GridAdaptsToRightPanel(double width, int columns) => Assert.Equal(columns, VisualIndexSampling.Columns(width));
    [Fact]
    public void WorkspaceCountAndSurfaceRoundTripWithoutCatalogState()
    {
        var root = Path.Combine(Path.GetTempPath(), "visual-index-workspace-" + Guid.NewGuid());
        try
        {
            var service = new WorkspaceStateService(Path.Combine(root, "workspace.json"));
            service.SetVisualIndexCount(48); service.SetRightPanel(440, true, "visual-index"); service.Save();
            var state = WorkspaceStateStore.Load(Path.Combine(root, "workspace.json"));
            Assert.Equal(48, state.Layout!.VisualIndexCount); Assert.Equal("visual-index", state.Layout.RightPanelActiveSurface);
            Assert.Equal(24, WorkspaceState.Normalize(state with { Layout = state.Layout with { VisualIndexCount = 999 } }).Layout!.VisualIndexCount);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

[Collection("STA dispatcher tests")]
public sealed class VisualIndexProjectionTests
{
    [Fact]
    public async Task CachedLaterFramesPublishBeforeMissingFirstFrameAndFailureEndsGenerating()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "visual-index-cache-" + Guid.NewGuid() + ".jpg");
            try
            {
                var image = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96,
                    System.Windows.Media.PixelFormats.Bgr24, null, new byte[] { 10, 20, 30 }, 3);
                var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using (var output = File.Create(path)) encoder.Save(output);
                var frames = new CachedFrames(path);
                using var model = new VisualIndexModel(frames);
                var id = Guid.NewGuid();
                model.SetContext(id, TimeSpan.FromSeconds(60), 25, 12, true);
                await frames.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal("Generating 11 of 12…", model.Status);
                Assert.All(model.Cards.Skip(1), card => Assert.NotNull(card.Frame));
                frames.Release.SetResult();
                await model.Pending;
                Assert.DoesNotContain("Generating", model.Status);
                Assert.Contains("unavailable", model.Status);
                Assert.Equal(1, frames.Misses);
                model.SetContext(id, TimeSpan.FromSeconds(60), 25, 24, true);
                await model.Pending;
                Assert.Equal(2, frames.Misses);
                Assert.All(frames.Priorities, p => Assert.Equal(ThumbnailPriority.Visible, p));
                var before = model.Cards;
                model.SetContext(id, TimeSpan.FromSeconds(60), 25, 24, false, revision: 1);
                Assert.NotSame(before, model.Cards);
                Assert.All(model.Cards, card => Assert.Null(card.Frame));
                Assert.Equal(2, frames.Misses);
            }
            finally { File.Delete(path); }
        });
    }

    private sealed class CachedFrames(string path) : IPositionFrameService
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Misses;
        internal readonly List<ThumbnailPriority> Priorities = [];
        public Task<MediaAssetResolution?> PrepareAsync(Guid id, CancellationToken token) => Task.FromResult<MediaAssetResolution?>(null);
        public Task<string?> FindCachedAsync(PositionFrameContext context, TimeSpan position, CancellationToken token) =>
            Task.FromResult(position == TimeSpan.Zero ? null : path);
        public Task<string?> GetAsync(MediaAssetResolution? source, TimeSpan position, CancellationToken token) => throw new NotSupportedException();
        public async Task<string?> GetAsync(PositionFrameContext context, TimeSpan position, ThumbnailPriority priority, CancellationToken token)
        {
            Interlocked.Increment(ref Misses); Priorities.Add(priority);
            Entered.TrySetResult(); await Release.Task.WaitAsync(token); return null;
        }
        public void Dispose() { }
    }
    [Fact]
    public async Task FramesPublishProgressivelyOffDispatcherWithoutReplacingOrScrollingCards()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "visual-index-progress-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, "frame.jpg");
                var image = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96,
                    System.Windows.Media.PixelFormats.Bgr24, null, new byte[] { 10, 20, 30 }, 3);
                var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                using (var output = File.Create(path)) encoder.Save(output);
                var frames = new ProgressiveFrames(path);
                using var model = new VisualIndexModel(frames);
                var id = Guid.NewGuid();
                var dispatcherThread = Environment.CurrentManagedThreadId;
                var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                model.ProgressChanged += (_, _) => { if (model.Cards.FirstOrDefault()?.Frame is not null) published.TrySetResult(); };
                model.SetContext(id, TimeSpan.FromSeconds(60), 25, 12, true);
                var cards = model.Cards;
                await frames.SecondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.NotEqual(dispatcherThread, frames.WorkerThread);
                Assert.NotNull(cards[0].Frame); Assert.True(cards[0].Frame!.IsFrozen);
                Assert.Equal("Generating…", cards[1].Status);
                Assert.Same(cards, model.Cards);
                Assert.Equal("Generating 1 of 12…", model.Status);
                frames.Release.SetResult(); await model.Pending;
                Assert.Equal("", model.Status);
                Assert.All(cards, card => Assert.NotNull(card.Frame));
                model.SetContext(id, TimeSpan.FromSeconds(60), 25, 12, false);
                Assert.Same(cards, model.Cards); Assert.NotNull(cards[0].Frame);
            }
            finally { Directory.Delete(root, true); }
        });
    }

    [Fact]
    public async Task ProgressivePublicationKeepsCardsStableAndRejectsCanceledAssetAndDensityWork()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            var frames = new DelayedFrames();
            using var model = new VisualIndexModel(frames);
            var first = Guid.NewGuid(); var second = Guid.NewGuid();
            model.SetContext(first, TimeSpan.FromSeconds(60), 25, 12, true);
            var oldCards = model.Cards; var pending = model.Pending;
            await frames.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(oldCards, c => Assert.Equal("Generating…", c.Status));
            model.SetContext(second, TimeSpan.FromSeconds(90), 25, 48, true);
            Assert.True(frames.FirstToken.IsCancellationRequested);
            frames.Release.SetResult();
            await pending; await model.Pending;
            Assert.All(oldCards, c => Assert.Equal("Generating…", c.Status));
            Assert.Equal(48, model.Cards.Count); Assert.All(model.Cards, c => Assert.Equal("Unavailable", c.Status));
            var stable = model.Cards;
            model.UpdatePosition(stable[10].Position);
            Assert.Same(stable, model.Cards); Assert.True(stable[10].IsCurrent); Assert.Single(stable, c => c.IsCurrent);
            model.SetContext(second, TimeSpan.FromSeconds(90), 25, 48, true);
            Assert.Same(stable, model.Cards);
            model.SetContext(second, TimeSpan.FromSeconds(90), 25, 24, true);
            await model.Pending; Assert.Equal(24, model.Cards.Count);
            model.SetContext(null, null, 0, 24, false); Assert.Empty(model.Cards);
        });
    }
    [Fact]
    public async Task HiddenPanelDoesNoWorkAndUnknownDurationRecovers()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            var frames = new EmptyFrames(); using var model = new VisualIndexModel(frames);
            var id = Guid.NewGuid();
            model.SetContext(id, null, 0, 24, false); Assert.Empty(model.Cards); Assert.Equal(0, frames.Prepares);
            model.SetContext(id, TimeSpan.FromSeconds(1), 25, 24, true); await model.Pending;
            Assert.Equal(1, frames.Prepares); Assert.Equal(24, frames.Reads);
            model.SetContext(id, TimeSpan.FromSeconds(2), 25, 12, true); await model.Pending;
            Assert.Equal(12, model.Cards.Count);
            model.SetContext(id, TimeSpan.FromSeconds(2), double.NaN, 12, true); await model.Pending;
            var cards = model.Cards;
            model.SetContext(id, TimeSpan.FromSeconds(2), double.NaN, 12, true);
            Assert.Same(cards, model.Cards);
        });
    }
    [Fact]
    public async Task ResponsiveViewMeasuresWithoutAWindowAndRegistersInSharedPanel()
    {
        await StaDispatcher.RunAsync(() =>
        {
            TestWpfApplication.EnsureLoaded();
            using var model = new VisualIndexModel(new EmptyFrames());
            var view = new VisualIndexView(); view.Initialize(model, 24);
            model.SetContext(Guid.NewGuid(), TimeSpan.FromSeconds(60), 25, 24, false);
            var pixels = new byte[160 * 90 * 3];
            for (var i = 0; i < pixels.Length; i += 3) { pixels[i] = 110; pixels[i + 1] = (byte)((i / 480) + 50); pixels[i + 2] = 45; }
            var image = System.Windows.Media.Imaging.BitmapSource.Create(160, 90, 96, 96,
                System.Windows.Media.PixelFormats.Bgr24, null, pixels, 480); image.Freeze();
            foreach (var card in model.Cards.Take(5)) card.Publish(image);
            model.Cards[5].Publish(null);
            var panel = new ContextualRightPanel(); panel.AddSurface("inspector", "Inspector", new System.Windows.Controls.Border());
            panel.AddSurface("visual-index", "Visual Index", view); panel.SelectSurface("visual-index");
            foreach (var width in new[] { 280d, 360, 600 })
            {
                panel.Measure(new Size(width, 700)); panel.Arrange(new Rect(0, 0, width, 700)); panel.UpdateLayout();
                Assert.Equal(24, view.Frames.Items.Count);
                Assert.True(view.DesiredSize.Width <= width);
                if (Environment.GetEnvironmentVariable("LIGHTFLOW_VISUAL_INDEX_CAPTURE") is { } capture)
                {
                    Directory.CreateDirectory(capture);
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width, 700, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(panel);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(capture, $"visual-index-{width}.png")); encoder.Save(output);
                }
            }
            panel.SetSurfaceAvailable("visual-index", false); Assert.Equal("inspector", panel.ActiveSurface);
            panel.SetSurfaceAvailable("visual-index", true); Assert.Equal("visual-index", panel.ActiveSurface);
            return Task.CompletedTask;
        });
    }
    internal sealed class EmptyFrames : IPositionFrameService
    {
        public int Prepares, Reads;
        public Task<MediaAssetResolution?> PrepareAsync(Guid id, CancellationToken token) { Interlocked.Increment(ref Prepares); return Task.FromResult<MediaAssetResolution?>(null); }
        public Task<string?> GetAsync(MediaAssetResolution? source, TimeSpan position, CancellationToken token) { Interlocked.Increment(ref Reads); return Task.FromResult<string?>(null); }
        public void Dispose() { }
    }
    private sealed class ProgressiveFrames(string path) : IPositionFrameService
    {
        public TaskCompletionSource SecondEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int WorkerThread;
        public Task<MediaAssetResolution?> PrepareAsync(Guid id, CancellationToken token) => Task.FromResult<MediaAssetResolution?>(null);
        public async Task<string?> GetAsync(MediaAssetResolution? source, TimeSpan position, CancellationToken token)
        {
            WorkerThread = Environment.CurrentManagedThreadId;
            if (position != TimeSpan.Zero) { SecondEntered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return path;
        }
        public void Dispose() { }
    }
    private sealed class DelayedFrames : IPositionFrameService
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken FirstToken;
        private int _reads;
        public Task<MediaAssetResolution?> PrepareAsync(Guid id, CancellationToken token) => Task.FromResult<MediaAssetResolution?>(null);
        public async Task<string?> GetAsync(MediaAssetResolution? source, TimeSpan position, CancellationToken token)
        {
            if (Interlocked.Increment(ref _reads) == 1) { FirstToken = token; Entered.SetResult(); }
            await Release.Task;
            return null;
        }
        public void Dispose() { }
    }
}

public sealed partial class PlayerViewerHostLeaseTests
{
    [Fact]
    public async Task VisualIndexSeekReturnsSpaceToPlayerAndRegenerateRetiresCurrentCards()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, new FakeRangeStore(null));
            host.InitializeVisualIndex(new VisualIndexProjectionTests.EmptyFrames(), () => null, 24);
            var outside = new System.Windows.Controls.Button { Content = "Index focus" };
            var layout = new System.Windows.Controls.StackPanel(); layout.Children.Add(host); layout.Children.Add(outside);
            var window = new Window { Content = layout, Width = 640, Height = 480, Left = -32000, ShowInTaskbar = false };
            window.Show();
            try
            {
                var asset = ReviewAsset("index.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                outside.Focus();
                var card = host.VisualIndexContent.Frames.Items.Cast<VisualIndexCard>().ElementAt(7);
                await host.SeekVisualIndexAsync(card);
                Assert.Same(host, System.Windows.Input.FocusManager.GetFocusedElement(window));
                Assert.Equal(card.Position, backend.SeekPositions[^1]);
                host.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Space)
                    { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                await WaitUntilAsync(() => backend.PlayCallCount == 1, "Space to play after Visual Index seek");
                Guid? regenerated = null;
                host.RegenerateVisualIndexRequested = (id, _) => { regenerated = id; return Task.CompletedTask; };
                host.VisualIndexContent.RegenerateButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await WaitUntilAsync(() => regenerated == asset.AssetId, "Visual Index regenerate action");
                Assert.DoesNotContain(card, host.VisualIndexContent.Frames.Items.Cast<VisualIndexCard>());
                var seeks = backend.SeekPositions.Count;
                await host.SeekVisualIndexAsync(card);
                Assert.Equal(seeks, backend.SeekPositions.Count);
            }
            finally { await host.CloseAsync(); window.Content = null; window.Close(); }
        });
    }

    [Fact]
    public async Task VisualIndexSeeksExactPositionThroughExistingPlayerAndRejectsOldCard()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend(); var ranges = new FakeRangeStore(null);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, ranges);
            host.InitializeVisualIndex(new VisualIndexProjectionTests.EmptyFrames(), () => null, 24);
            var asset = ReviewAsset("index.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
            var card = host.VisualIndexContent.Frames.Items.Cast<VisualIndexCard>().ElementAt(7);
            await host.SeekVisualIndexAsync(card);
            Assert.Equal(card.Position, backend.SeekPositions[^1]); Assert.Equal(0, ranges.SaveCount);
            await host.OpenAsync(ReviewAsset("next.mp4"), ReviewPath(asset));
            var count = backend.SeekPositions.Count; await host.SeekVisualIndexAsync(card);
            Assert.Equal(count, backend.SeekPositions.Count);
            await host.CloseAsync(); Assert.Empty(host.VisualIndexContent.Frames.Items);
        });
    }
}
