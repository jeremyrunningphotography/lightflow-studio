using Lightflow.Actions;
using System.Windows.Controls;
using System.Windows.Input;
using Xunit;
using InputKey = System.Windows.Input.Key;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    private static IMediaPlaybackService ReviewPlayback(PlayerViewerHost host) => (IMediaPlaybackService)typeof(PlayerViewerHost)
        .GetField("_service", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host)!;
    [Fact]
    public async Task ReviewActions_ControllerUsesExactTimestampCrossBoundaryPolicyAndKeyboardParity()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend(); var ranges = new FakeRangeStore(null);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, ranges); var asset = ReviewAsset("boundaries.mp4");
            try {
                await host.OpenAsync(asset, ReviewPath(asset));
                await ReviewPlayback(host).SeekAsync(TimeSpan.FromTicks(123456789));
                host.PositionSlider.IsEnabled = false; // Eligibility is source state, never control state.
                Assert.Equal(ActionOutcome.Completed, (await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.SetBoundary, new SetBoundaryArguments(WorkingRangeBoundary.In)))).Outcome);
                Assert.Equal(TimeSpan.FromTicks(123456789), ranges.SavedRange!.In);
                await ReviewPlayback(host).SeekAsync(TimeSpan.FromSeconds(20));
                await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.SetBoundary, new SetBoundaryArguments(WorkingRangeBoundary.Out)));
                Assert.Equal(TimeSpan.FromSeconds(20), ranges.SavedRange.Out);
                await ReviewPlayback(host).SeekAsync(TimeSpan.FromSeconds(25));
                Assert.True(host.TryHandleShortcut(InputKey.I, host, ModifierKeys.None));
                Assert.Equal(TimeSpan.FromSeconds(25), ranges.SavedRange.In); Assert.Null(ranges.SavedRange.Out);
                await ReviewPlayback(host).SeekAsync(TimeSpan.FromSeconds(10));
                Assert.True(host.TryHandleShortcut(InputKey.O, host, ModifierKeys.None));
                Assert.Null(ranges.SavedRange.In); Assert.Equal(TimeSpan.FromSeconds(10), ranges.SavedRange.Out);
                var count = ranges.SaveCount;
                Assert.True(host.TryHandleShortcut(InputKey.O, host, ModifierKeys.None, isRepeat: true));
                Assert.Equal(count, ranges.SaveCount);
                Assert.False(host.TryHandleShortcut(InputKey.I, new TextBox(), ModifierKeys.None));
            } finally { await host.CloseAsync(); }
        });
    }

    [Fact]
    public async Task ReviewActions_NativeSurfacePassesRepeatStateForAllCreationAndBoundaryKeys()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend(); var ranges = new FakeRangeStore(null);
            var clips = new FakeSubclipService(); var markers = new FakeMarkers();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, ranges, clips, markers: markers);
            using var input = new PlayerSurfaceInput(new System.Windows.Controls.Border(), () => { }, () => { }, (_, _) => { }, _ => { },
                (_, _) => false, host.TryHandleShortcutKeyUp, repeatAwareKey: (key, owner, repeat) => host.TryHandleShortcut(key, owner, ModifierKeys.None, repeat));
            try {
                var asset = ReviewAsset("native-review.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                await ReviewPlayback(host).SeekAsync(TimeSpan.FromSeconds(7));
                Assert.True(input.HandleKeyDown(InputKey.I, host, false));
                await ReviewPlayback(host).SeekAsync(TimeSpan.FromSeconds(20));
                Assert.True(input.HandleKeyDown(InputKey.O, host, false));
                Assert.True(input.HandleKeyDown(InputKey.S, host, false));
                Assert.True(input.HandleKeyDown(InputKey.M, host, false));
                Assert.Equal(2, ranges.SaveCount); Assert.Equal(1, clips.CreateCount); Assert.Single(markers.Items);
                for (var i = 0; i < 20; i++)
                    foreach (var key in new[] { InputKey.I, InputKey.O, InputKey.S, InputKey.M }) Assert.True(input.HandleKeyDown(key, host, true));
                Assert.Equal(2, ranges.SaveCount); Assert.Equal(1, clips.CreateCount); Assert.Single(markers.Items);
            } finally { await host.CloseAsync(); }
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReviewActions_ControllerCreatesRestoredPartialOrFullRangeAndRevealsDuplicate(bool partial)
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend();
            var range = new MediaRange(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(7), partial ? null : TimeSpan.FromSeconds(20));
            var clips = new FakeSubclipService();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, new FakeRangeStore(range), clips); var asset = ReviewAsset("clips.mp4");
            var window = CreateSubclipWindow(host); window.ShowActivated = false; window.Left = -32000; window.Show();
            var reveals = 0; host.SubclipsRevealRequested += (_, _) => reveals++;
            try {
                await host.OpenAsync(asset, ReviewPath(asset));
                Assert.Equal(ActionOutcome.Completed, (await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.CreateSubclip))).Outcome);
                Assert.Equal(TimeSpan.FromSeconds(7), clips.Range!.In); Assert.Equal(TimeSpan.FromSeconds(partial ? 60 : 20), clips.Range.Out);
                var id = Assert.Single(clips.Items).SubclipId;
                Assert.Equal(ActionOutcome.NoChange, (await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.CreateSubclip))).Outcome);
                Assert.Equal(id, Assert.Single(clips.Items).SubclipId); Assert.Equal(2, reveals);
                Assert.Equal(id, ((SubclipPanelItem)host.SubclipsList.SelectedItem).SubclipId);
                Assert.True(host.TryHandleShortcut(InputKey.S, host, ModifierKeys.None, isRepeat: true));
                Assert.Equal(2, clips.CreateCount);
                Assert.True(host.TryHandleShortcut(InputKey.S, host, ModifierKeys.None)); Assert.Equal(3, clips.CreateCount);
                Assert.False(host.TryHandleShortcut(InputKey.S, new TextBox(), ModifierKeys.None));
            } finally { await host.CloseAsync(); window.Close(); }
        });
    }

    [Fact]
    public async Task ReviewActions_ControllerTraversesCapturedSetNoWrapAndHonorsDirtyGuard()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, new FakeRangeStore(new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(7), null)));
            var assets = new[] { ReviewAsset("first.mp4"), ReviewAsset("second.mp4") };
            var review = new PlayerReviewSet(assets.Select(a => new PlayerReviewItem(a, null)).ToArray(), assets[0].AssetId, true);
            host.SetReviewSet(review, (a, _) => Task.FromResult(ReviewPath(a)));
            async Task<ActionResult> Move(TraversalDirection direction) => await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.TraverseReview, new TraverseArguments(direction)));
            try {
                await host.OpenAsync(assets[0], ReviewPath(assets[0]));
                Assert.Equal(ActionOutcome.NoChange, (await Move(TraversalDirection.Previous)).Outcome);
                host.ContextChanging = () => false;
                Assert.Equal(ActionOutcome.NoChange, (await Move(TraversalDirection.Next)).Outcome);
                Assert.Equal(0, review.CurrentIndex); Assert.Equal(assets[0], host.CurrentAsset);
                host.ContextChanging = () => true;
                Assert.Equal(ActionOutcome.Completed, (await Move(TraversalDirection.Next)).Outcome);
                Assert.Equal(assets[1], host.CurrentAsset); Assert.Equal(TimeSpan.FromSeconds(7), backend.SeekPositions[^1]);
                Assert.True(review.IsSelectionSubset); Assert.Same(review, host.ReviewSet);
                Assert.Equal(ActionOutcome.NoChange, (await Move(TraversalDirection.Next)).Outcome);
                Assert.Equal(ActionOutcome.Completed, (await Move(TraversalDirection.Previous)).Outcome);
                Assert.True(host.TryHandleShortcut(InputKey.Right, host, ModifierKeys.Control));
                await WaitUntilAsync(() => host.CurrentAsset == assets[1] && host.PositionSlider.IsEnabled, "semantic keyboard review");
                Assert.True(host.TryHandleShortcut(InputKey.Left, host, ModifierKeys.Control));
                await WaitUntilAsync(() => host.CurrentAsset == assets[0] && host.PositionSlider.IsEnabled, "semantic previous review");
            } finally { host.ContextChanging = () => true; await host.CloseAsync(); }
        });
    }

    [Fact]
    public async Task ReviewActions_DelayedReviewCannotReplaceNewSourceAndRepeatedTraversalIsBounded()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            var assets = new[] { ReviewAsset("first.mp4"), ReviewAsset("pending.mp4"), ReviewAsset("replacement.mp4") };
            var entered = new TaskCompletionSource(); var release = new TaskCompletionSource<MediaPathResolution>();
            host.SetReviewSet(new(assets.Select(a => new PlayerReviewItem(a, null)).ToArray(), assets[0].AssetId),
                (a, _) => { entered.TrySetResult(); return release.Task; });
            try {
                await host.OpenAsync(assets[0], ReviewPath(assets[0]));
                var pending = host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.TraverseReview, new TraverseArguments(TraversalDirection.Next)));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(ActionOutcome.Busy, (await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.TraverseReview, new TraverseArguments(TraversalDirection.Next)))).Outcome);
                await host.OpenAsync(assets[2], ReviewPath(assets[2]));
                release.TrySetResult(ReviewPath(assets[1]));
                Assert.Equal(ActionOutcome.Superseded, (await pending).Outcome); Assert.Equal(assets[2], host.CurrentAsset);
            } finally { release.TrySetResult(ReviewPath(assets[1])); await host.CloseAsync(); }
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReviewActions_ControllerCanLeaveUnavailableVideoOrStillMember(bool image)
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator);
            var first = ReviewAsset("offline.mp4") with { Kind = image ? MediaPresentationKind.Image : MediaPresentationKind.Video };
            var next = ReviewAsset("online.mp4");
            host.SetReviewSet(new([new(first, null), new(next, null)], first.AssetId), (a, _) => Task.FromResult(ReviewPath(a)));
            try {
                await host.OpenAsync(first, new(first.RootId, first.RelativePath, first.Key, null, MediaRootAvailability.Unavailable, false));
                Assert.False(host.SemanticActions.Eligibility(PlayerActions.PlayPause, host.ActionTarget).Available);
                Assert.Equal(ActionOutcome.Completed, (await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.TraverseReview, new TraverseArguments(TraversalDirection.Next)))).Outcome);
                Assert.Equal(next, host.CurrentAsset);
            } finally { await host.CloseAsync(); }
        });
    }

    [Fact]
    public async Task ReviewActions_ControllerMarkerIdentityTimestampNoWrapAndKeyboardParity()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend(); var markers = new FakeMarkers();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, markers: markers); var asset = ReviewAsset("markers.mp4");
            async Task<ActionResult> Move(TraversalDirection direction) => await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.NavigateMarker, new TraverseArguments(direction)));
            try {
                await host.OpenAsync(asset, ReviewPath(asset));
                Assert.Equal(ActionOutcome.NoChange, (await Move(TraversalDirection.Next)).Outcome);
                await ReviewPlayback(host).SeekAsync(TimeSpan.FromTicks(123456789));
                Assert.Equal(ActionOutcome.Completed, (await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.AddMarker))).Outcome);
                var marker = Assert.Single(markers.Items); Assert.Equal(TimeSpan.FromTicks(123456789), marker.Position); Assert.Equal(marker.MarkerId, host.SelectedMarkerId);
                Assert.Equal(ActionOutcome.NoChange, (await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.AddMarker))).Outcome);
                await ReviewPlayback(host).SeekAsync(TimeSpan.FromSeconds(30));
                Assert.True(host.TryHandleShortcut(InputKey.M, host, ModifierKeys.None, isRepeat: true)); Assert.Single(markers.Items);
                Assert.True(host.TryHandleShortcut(InputKey.M, host, ModifierKeys.None)); Assert.Equal(2, markers.Items.Count);
                Assert.Equal(ActionOutcome.NoChange, (await Move(TraversalDirection.Next)).Outcome);
                Assert.Equal(ActionOutcome.Completed, (await Move(TraversalDirection.Previous)).Outcome); Assert.Equal(marker.MarkerId, host.SelectedMarkerId);
                Assert.Equal(ActionOutcome.NoChange, (await Move(TraversalDirection.Previous)).Outcome);
                Assert.True(host.TryHandleShortcut(InputKey.Right, host, ModifierKeys.Alt));
                await WaitUntilAsync(() => backend.SeekPositions[^1] == TimeSpan.FromSeconds(30), "semantic next marker");
                Assert.True(host.TryHandleShortcut(InputKey.Left, host, ModifierKeys.Alt));
                await WaitUntilAsync(() => backend.SeekPositions[^1] == marker.Position, "semantic previous marker");
                Assert.False(host.TryHandleShortcut(InputKey.M, new TextBox(), ModifierKeys.None));
                Assert.False(host.TryHandleShortcut(InputKey.Right, new TextBox(), ModifierKeys.Alt));
            } finally { await host.CloseAsync(); }
        });
    }

    [Theory]
    [InlineData(PlayerActions.SetBoundary)] [InlineData(PlayerActions.TraverseReview)]
    [InlineData(PlayerActions.CreateSubclip)] [InlineData(PlayerActions.AddMarker)] [InlineData(PlayerActions.NavigateMarker)]
    public async Task ReviewActions_StaleControllerTargetCannotMutateReplacement(string id)
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend(); var ranges = new FakeRangeStore(null); var clips = new FakeSubclipService(); var markers = new FakeMarkers();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, ranges, clips, markers: markers);
            try {
                var asset = ReviewAsset("old.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                ActionArguments args = id == PlayerActions.SetBoundary ? new SetBoundaryArguments(WorkingRangeBoundary.In)
                    : id is PlayerActions.TraverseReview or PlayerActions.NavigateMarker ? new TraverseArguments(TraversalDirection.Next) : NoActionArguments.Instance;
                var call = SemanticCall(host, id, args);
                var replacement = ReviewAsset("new.mp4"); await host.OpenAsync(replacement, ReviewPath(replacement));
                Assert.Equal(ActionOutcome.Superseded, (await host.SemanticActions.InvokeAsync(call)).Outcome);
                Assert.Equal(0, ranges.SaveCount); Assert.Equal(0, clips.CreateCount); Assert.Empty(markers.Items); Assert.Equal(replacement, host.CurrentAsset);
                await host.CloseAsync();
                Assert.Equal(ActionOutcome.Ineligible, (await host.SemanticActions.InvokeAsync(SemanticCall(host, id, args))).Outcome);
            } finally { await host.CloseAsync(); }
        });
    }

    [Theory]
    [InlineData(PlayerActions.SetBoundary)] [InlineData(PlayerActions.CreateSubclip)] [InlineData(PlayerActions.AddMarker)]
    public async Task ReviewActions_AsyncMutationCompletionCannotPublishIntoReplacement(string id)
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend();
            var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
            Task Gate() { entered.TrySetResult(); return release.Task; }
            var ranges = new FakeRangeStore(new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(7), null));
            var clips = new FakeSubclipService(); var markers = new FakeMarkers();
            if (id == PlayerActions.SetBoundary) ranges.BeforeSave = Gate;
            if (id == PlayerActions.CreateSubclip) clips.BeforeCreate = Gate;
            if (id == PlayerActions.AddMarker) markers.BeforeCreate = Gate;
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, ranges, clips, markers: markers);
            var reveals = 0; host.SubclipsRevealRequested += (_, _) => reveals++;
            try {
                var old = ReviewAsset("old-mutation.mp4"); await host.OpenAsync(old, ReviewPath(old));
                await ReviewPlayback(host).SeekAsync(TimeSpan.FromSeconds(25));
                var pending = host.SemanticActions.InvokeAsync(SemanticCall(host, id, id == PlayerActions.SetBoundary ? new SetBoundaryArguments(WorkingRangeBoundary.In) : null));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(ActionOutcome.Busy, (await host.SemanticActions.InvokeAsync(SemanticCall(host, id, id == PlayerActions.SetBoundary ? new SetBoundaryArguments(WorkingRangeBoundary.Out) : null))).Outcome);
                var replacement = ReviewAsset("new-mutation.mp4"); await host.OpenAsync(replacement, ReviewPath(replacement));
                release.TrySetResult();
                Assert.Equal(ActionOutcome.Superseded, (await pending).Outcome);
                Assert.Equal(replacement, host.CurrentAsset); Assert.Equal(0, reveals); Assert.Empty(host.CurrentMarkers);
                Assert.Null(host.SelectedMarkerId);
                // The submitted service mutation remains owned by its captured asset; replacement range is restored.
                var range = (MediaRange?)typeof(PlayerViewerHost).GetField("_reviewRange", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host);
                Assert.Equal(TimeSpan.FromSeconds(7), range!.In);
                Assert.All(clips.Items, clip => Assert.Equal(old.AssetId, clip.AssetId));
                Assert.All(markers.Items, marker => Assert.Equal(old.AssetId, marker.AssetId));
            } finally { release.TrySetResult(); await host.CloseAsync(); }
        });
    }
}
