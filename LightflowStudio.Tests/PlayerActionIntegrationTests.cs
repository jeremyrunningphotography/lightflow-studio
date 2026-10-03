using Lightflow.Actions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InputKey = System.Windows.Input.Key;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    private static readonly ActionInputSource SemanticController = new("acceptance-controller", ActionInputKind.Controller);
    private static ActionInvocation SemanticCall(PlayerViewerHost host, string id, ActionArguments? arguments = null,
        ActionPhase phase = ActionPhase.Invoke, Guid? identity = null) =>
        new(id, arguments ?? NoActionArguments.Instance, SemanticController, identity ?? Guid.NewGuid(), host.ActionTarget, phase);

    [Fact]
    public async Task SemanticKeyboard_NativeSurfaceUsesSameRepeatAndOwnershipPolicy()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator); var asset = ReviewAsset("native-input.mp4");
            await host.OpenAsync(asset, ReviewPath(asset));
            using var input = new PlayerSurfaceInput(new Border(), () => { }, () => { }, (_, _) => { }, _ => { },
                (_, _) => false, host.TryHandleShortcutKeyUp, repeatAwareKey: (key, owner, repeat) => host.TryHandleShortcut(key, owner, ModifierKeys.None, repeat));
            Assert.True(input.HandleKeyDown(InputKey.Space, host, false));
            await WaitUntilAsync(() => backend.PlayCallCount == 1, "native toggle");
            Assert.True(input.HandleKeyDown(InputKey.Space, host, true)); Assert.Equal(1, backend.PlayCallCount); Assert.Equal(0, backend.PauseCallCount);
            Assert.True(input.HandleKeyDown(InputKey.Space, new Slider(), true));
            Assert.False(input.HandleKeyDown(InputKey.Right, new Slider(), false));
            Assert.True(input.HandleKeyDown(System.Windows.Input.Key.Right, host, true));
            await WaitUntilAsync(() => backend.Operations.Contains("forward"), "native repeat step");
            await host.CloseAsync();
        });
    }

    [Fact]
    public async Task SemanticPlayer_TransportKeyboardAndControllerShareLeaseAndRangeBehavior()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded();
            var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, new FakeRangeStore(new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10))));
            var window = CreateSubclipWindow(host); window.ShowActivated = false; window.Left = -32000; window.Show();
            try {
                var asset = ReviewAsset("semantic-transport.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                host.PlayPauseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await WaitUntilAsync(() => backend.PlayCallCount == 1, "transport action");
                Assert.True(host.TryHandleShortcut(InputKey.Space, host, ModifierKeys.None));
                await WaitUntilAsync(() => backend.PauseCallCount == 1, "keyboard pause");
                var result = await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.PlayPause));
                Assert.Equal(ActionOutcome.Completed, result.Outcome); Assert.Equal(2, backend.PlayCallCount);
                Assert.Single(backend.SeekPositions); Assert.Equal(TimeSpan.FromSeconds(5), backend.SeekPositions[0]);
                for (var i = 0; i < 50; i++) Assert.True(host.TryHandleShortcut(InputKey.Space, host, ModifierKeys.None, isRepeat: true));
                Assert.Equal(2, backend.PlayCallCount); Assert.Equal(1, backend.PauseCallCount);
                await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.PlayPause)); Assert.Equal(2, backend.PauseCallCount);
            } finally { await host.CloseAsync(); window.Close(); }
        });
    }

    [Theory]
    [InlineData("text")] [InlineData("multiline")] [InlineData("slider")] [InlineData("selector")] [InlineData("dropdown")] [InlineData("button")]
    public async Task SemanticKeyboard_LocalControlsRetainSpaceAndEditorsRetainColor(string kind)
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator); var asset = ReviewAsset("ownership.mp4");
            await host.OpenAsync(asset, ReviewPath(asset));
            DependencyObject owner = kind switch { "text" => new TextBox(), "multiline" => new TextBox { AcceptsReturn = true },
                "slider" => new Slider(), "selector" => new ListBox(), "dropdown" => new ComboBox { IsEditable = true }, _ => new Button() };
            Assert.Equal(kind == "slider", host.TryHandleShortcut(InputKey.Space, owner, ModifierKeys.None)); Assert.Equal(kind == "slider" ? 1 : 0, backend.PlayCallCount);
            if (kind != "button") Assert.False(host.TryHandleShortcut(InputKey.C, owner, ModifierKeys.None));
            await host.CloseAsync();
        });
    }

    [Fact]
    public async Task SemanticPlayer_EligibilityDoesNotDependOnSliderEnabledAndRejectsInactiveOrModalContext()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator); var asset = ReviewAsset("eligibility.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
            host.PositionSlider.IsEnabled = false;
            Assert.Equal(ActionOutcome.Completed, (await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.PlayPause))).Outcome);
            host.ActionPresentationActive = () => false;
            Assert.Equal(ActionUnavailableReason.InactivePresentation, (await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.PlayPause))).Reason);
            host.ActionPresentationActive = () => true; host.IsEnabled = false;
            Assert.Equal(ActionUnavailableReason.ModalInteraction, (await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.StepFrame, new FrameStepArguments(1)))).Reason);
            host.IsEnabled = true; await host.CloseAsync();
            Assert.Equal(ActionUnavailableReason.NoPlayer, (await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.PlayPause))).Reason);
        });
    }

    [Fact]
    public async Task SemanticPlayer_FrameRequestsUseBoundedQueueAndRetainedPresentation()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new FakeBackend { BeforeStep = () => { started.TrySetResult(); return release.Task; } };
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator); var window = CreateSubclipWindow(host); window.ShowActivated = false; window.Left = -32000; window.Show();
            try {
                var asset = ReviewAsset("controller-queue.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                var first = host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.StepFrame, new FrameStepArguments(1)));
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var pending = Enumerable.Range(0,100).Select(_ => host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.StepFrame, new FrameStepArguments(1)))).ToList();
                pending.AddRange(Enumerable.Range(0,10).Select(_ => host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.StepFrame, new FrameStepArguments(-1)))));
                release.SetResult(); var results = await Task.WhenAll(pending.Prepend(first)).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.All(results, result => Assert.Equal(ActionOutcome.Completed, result.Outcome));
                Assert.Equal(11, backend.Operations.Count(x => x == "forward")); Assert.DoesNotContain("backward", backend.Operations); Assert.Equal(0, backend.PlayCallCount);
                Assert.Equal(ActionOutcome.Completed, (await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.StepFrame, new FrameStepArguments(-1)))).Outcome);
                Assert.Equal(Visibility.Visible, host.SteppedFrameSurface.Visibility); Assert.NotNull(host.SteppedFrameSurface.Source);
                Assert.Equal(new[] { "capture", "backward", "capture" }, backend.Operations.TakeLast(3));
            } finally { release.TrySetResult(); await host.CloseAsync(); window.Close(); }
        });
    }

    [Fact]
    public async Task SemanticPlayer_SourceReplacementCancelsPendingBatchBeforeNewSourceSteps()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new FakeBackend { BeforeStep = () => { started.TrySetResult(); return release.Task; } };
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator); var old = ReviewAsset("old.mp4"); await host.OpenAsync(old, ReviewPath(old));
            try {
                var request = SemanticCall(host, PlayerActions.StepFrame, new FrameStepArguments(1));
                var first = host.SemanticActions.InvokeAsync(request); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var queued = host.SemanticActions.InvokeAsync(request with { InvocationId = Guid.NewGuid() });
                var next = ReviewAsset("new.mp4"); var replacing = host.OpenAsync(next, ReviewPath(next));
                Assert.Equal(ActionOutcome.Superseded, (await first.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
                Assert.Equal(ActionOutcome.Superseded, (await queued.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
                release.SetResult(); await replacing.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(ActionOutcome.Superseded, (await host.SemanticActions.InvokeAsync(request)).Outcome);
                Assert.Equal(1, backend.Operations.Count(x => x == "forward")); Assert.Equal(next, host.CurrentAsset);
            } finally { release.TrySetResult(); await host.CloseAsync(); }
        });
    }

    [Theory]
    [InlineData("end")] [InlineData("cancel")] [InlineData("disconnect")] [InlineData("focus")] [InlineData("deactivate")] [InlineData("modal")] [InlineData("modal-loop")] [InlineData("replace")]
    public async Task SemanticColor_RecomputesCurrentAssignmentAndLifecycleNeverWritesCatalog(string ending)
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded();
            var folder = Directory.CreateDirectory(Path.Combine("artifacts", "345", "color-" + Guid.NewGuid().ToString("N")));
            var backend = new FakeBackend(); var colors = new FakeColorStore(); var camera = Guid.NewGuid(); var creative = Guid.NewGuid();
            var cache = new FakeLutLibrary(new Dictionary<Guid,string> { [camera] = WriteIdentityCube(folder.FullName,"camera.cube"), [creative] = WriteIdentityCube(folder.FullName,"creative.cube") });
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, lutCache: cache, assetColors: colors, cameraLutFolder: () => folder.FullName, creativeLutFolder: () => folder.FullName);
            var window = CreateSubclipWindow(host); window.ShowActivated = false; window.Left = -32000; window.Show();
            var modal = false;
            try {
                var asset = ReviewAsset("color.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                await WaitUntilAsync(() => host.CameraLutCombo.Items.Count >= 3, "Color choices");
                host.CameraLutCombo.SelectedIndex = 1;
                await WaitUntilAsync(() => colors.SetCount == 1 && !backend.ColorCalls[^1].Bypass, "initial assignment");
                var begin = SemanticCall(host, PlayerActions.ColorBypass, phase: ActionPhase.Begin);
                if (ending == "focus") Assert.True(host.TryHandleShortcut(InputKey.C, host, ModifierKeys.None));
                else Assert.Equal(ActionOutcome.Completed, (await host.SemanticActions.InvokeAsync(begin)).Outcome);
                Assert.True(backend.ColorCalls[^1].Bypass); Assert.Equal(1, colors.SetCount);
                host.CameraLutCombo.SelectedIndex = 2;
                await WaitUntilAsync(() => colors.SetCount == 2, "current assignment while bypassed");
                var current = backend.ColorCalls[^1].Pipeline; Assert.True(backend.ColorCalls[^1].Bypass);
                switch (ending) {
                    case "end": await host.SemanticActions.InvokeAsync(begin with { Phase = ActionPhase.End }); break;
                    case "cancel": await host.SemanticActions.InvokeAsync(begin with { Phase = ActionPhase.Cancel }); break;
                    case "disconnect": host.SemanticActions.CancelSource(SemanticController); break;
                    case "focus": host.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, host, null) { RoutedEvent = UIElement.LostKeyboardFocusEvent }); break;
                    case "deactivate": typeof(PlayerViewerHost).GetMethod("ActionWindowDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(host, new object?[] { window, EventArgs.Empty }); break;
                    case "modal": window.IsEnabled = false; break;
                    case "modal-loop": System.Windows.Interop.ComponentDispatcher.PushModal(); modal = true;
                        Assert.Equal(ActionUnavailableReason.ModalInteraction, (await host.SemanticActions.InvokeAsync(SemanticCall(host, PlayerActions.PlayPause))).Reason); break;
                    case "replace": var next = ReviewAsset("replacement.mp4"); await host.OpenAsync(next, ReviewPath(next)); break;
                }
                Assert.Equal(2, colors.SetCount);
                if (ending != "replace") { Assert.False(backend.ColorCalls[^1].Bypass); Assert.Same(current, backend.ColorCalls[^1].Pipeline); }
                else { Assert.False(host.TryHandleShortcutKeyUp(InputKey.C)); Assert.Equal(ActionOutcome.NoChange, (await host.SemanticActions.InvokeAsync(begin with { Phase = ActionPhase.End })).Outcome); }
            } finally { if (modal) System.Windows.Interop.ComponentDispatcher.PopModal(); window.IsEnabled = true; await host.CloseAsync(); window.Close(); folder.Delete(true); }
        });
    }
}
