using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;
using InputKey = System.Windows.Input.Key;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    // Routed mouse intent plus normal WPF class-focus equivalent: no global input injection or cursor movement.
    private static void BeginReviewMouse(Control control)
    {
        control.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        Assert.True(control.Focus());
    }
    private static void ReviewMouseClick(ButtonBase button)
    {
        BeginReviewMouse(button);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
    private static async Task VerifyReviewRangeKeys(PlayerViewerHost host, Window window, FakeBackend backend, FakeRangeStore ranges, FakeSubclipService clips)
    {
        host.PositionSlider.Value = 10000;
        await WaitUntilAsync(() => backend.SeekPositions.Contains(TimeSpan.FromSeconds(10)), "range In position");
        Assert.True(RaisePlayerKey(Keyboard.FocusedElement as UIElement ?? host, window, InputKey.I).Handled);
        await WaitUntilAsync(() => ranges.SavedRange?.In == TimeSpan.FromSeconds(10), "I saves In");
        host.PositionSlider.Value = 20000;
        await WaitUntilAsync(() => backend.SeekPositions.Contains(TimeSpan.FromSeconds(20)), "range Out position");
        Assert.True(RaisePlayerKey(Keyboard.FocusedElement as UIElement ?? host, window, InputKey.O).Handled);
        await WaitUntilAsync(() => ranges.SavedRange?.Out == TimeSpan.FromSeconds(20), "O saves Out");
        var before = clips.CreateCount;
        Assert.True(RaisePlayerKey(Keyboard.FocusedElement as UIElement ?? host, window, InputKey.S).Handled);
        await WaitUntilAsync(() => clips.CreateCount == before + 1, "S creates Subclip");
        Assert.Equal(TimeSpan.FromSeconds(10), clips.Range?.In); Assert.Equal(TimeSpan.FromSeconds(20), clips.Range?.Out);
        Assert.Same(host, Keyboard.FocusedElement);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReviewFocus_MouseZoomCloseReturnsToPlayerWithOrWithoutSelection(bool change)
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var ranges = new FakeRangeStore(null); var clips = new FakeSubclipService();
            var host = new PlayerViewerHost(coordinator, ranges, clips); var window = CreateSubclipWindow(host);
            window.ShowActivated = false; window.Left = -32000; window.Show();
            try {
                var asset = ReviewAsset("zoom-focus.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                BeginReviewMouse(host.ZoomChoice); host.ZoomChoice.IsDropDownOpen = true;
                // WPF queues the initial popup-item focus at Send priority on opening.
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.False(RaisePlayerKey(host.ZoomChoice, window, InputKey.Right).Handled);
                Assert.False(RaisePlayerKey(host.ZoomChoice, window, InputKey.Space).Handled);
                if (change) {
                    var item = (ComboBoxItem)host.ZoomChoice.ItemContainerGenerator.ContainerFromIndex(2);
                    BeginReviewMouse(item); host.ZoomChoice.SelectedIndex = 2;
                }
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                host.ZoomChoice.DropDownClosed += (_, _) => closed.TrySetResult();
                host.ZoomChoice.IsDropDownOpen = false; await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Same(host, Keyboard.FocusedElement);
                Assert.True(RaisePlayerKey(host, window, InputKey.Space).Handled);
                await WaitUntilAsync(() => backend.PlayCallCount == 1, "review after Zoom");
                Assert.True(RaisePlayerKey(host, window, InputKey.Right).Handled);
                await WaitUntilAsync(() => backend.Operations.Contains("forward"), "step after Zoom");
                Assert.True(RaisePlayerKey(host, window, InputKey.Left).Handled);
                await WaitUntilAsync(() => backend.Operations.Contains("backward"), "previous after Zoom");
                await VerifyReviewRangeKeys(host, window, backend, ranges, clips);
            } finally { await host.CloseAsync(); window.Close(); }
        });
    }

    [Theory]
    [InlineData("in")] [InlineData("out")] [InlineData("play")] [InlineData("previous")] [InlineData("next")]
    public async Task ReviewFocus_MouseButtonsCompleteButKeyboardActivationStaysLocal(string name)
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend(); var ranges = new FakeRangeStore(null);
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var clips = new FakeSubclipService();
            var host = new PlayerViewerHost(coordinator, ranges, clips); var window = CreateSubclipWindow(host);
            window.ShowActivated = false; window.Left = -32000; window.Show();
            try {
                var asset = ReviewAsset("button-focus.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                var button = name switch { "in" => host.SetInButton, "out" => host.SetOutButton, "play" => host.PlayPauseButton,
                    "previous" => host.PreviousFrameButton, _ => host.NextFrameButton };
                ReviewMouseClick(button); Assert.Same(host, Keyboard.FocusedElement);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var toggles = backend.PlayCallCount + backend.PauseCallCount; var saves = ranges.SaveCount;
                Assert.True(RaisePlayerKey(host, window, InputKey.Space).Handled);
                await WaitUntilAsync(() => backend.PlayCallCount + backend.PauseCallCount == toggles + 1, "Space after mouse button");
                Assert.Equal(saves, ranges.SaveCount);
                await VerifyReviewRangeKeys(host, window, backend, ranges, clips);
                // Keyboard/assistive focus is deliberate and retains activation keys and the existing Tab stop.
                Assert.True(button.IsTabStop); Assert.True(button.Focus());
                Assert.False(RaisePlayerKey(button, window, InputKey.Space).Handled);
                Assert.False(RaisePlayerKey(button, window, InputKey.Enter).Handled);
                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.Same(button, Keyboard.FocusedElement);
                Assert.True(RaisePlayerKey(button, window, InputKey.I).Handled);
                Assert.Same(host, Keyboard.FocusedElement);
            } finally { await host.CloseAsync(); window.Close(); }
        });
    }

    [Fact]
    public async Task ReviewFocus_LegacySubclipCreationEndsIncidentalSetInFocus()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend(); var clips = new FakeSubclipService();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, new FakeRangeStore(new(TimeSpan.FromSeconds(60), TimeSpan.Zero, TimeSpan.FromSeconds(10))), clips);
            var window = CreateSubclipWindow(host); window.ShowActivated = false; window.Left = -32000; window.Show();
            try {
                var asset = ReviewAsset("subclip-focus.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                host.SetInButton.Focus(); Assert.True(RaisePlayerKey(host.SetInButton, window, InputKey.S).Handled);
                await WaitUntilAsync(() => clips.CreateCount == 1, "S creates Subclip"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Same(host, Keyboard.FocusedElement);
                Assert.True(RaisePlayerKey(host, window, InputKey.Space).Handled);
                await WaitUntilAsync(() => backend.PlayCallCount == 1, "Space after S");
                Assert.Equal(1, clips.CreateCount);
                PanelFor(window).Visibility = Visibility.Visible; window.UpdateLayout();
                ReviewMouseClick(host.AddSubclipButton);
                await WaitUntilAsync(() => clips.CreateCount == 2, "mouse Subclip creation");
                Assert.Same(host, Keyboard.FocusedElement);
                var toggles = backend.PlayCallCount + backend.PauseCallCount;
                Assert.True(RaisePlayerKey(host, window, InputKey.Space).Handled);
                await WaitUntilAsync(() => backend.PlayCallCount + backend.PauseCallCount == toggles + 1, "Space after mouse Subclip");
            } finally { await host.CloseAsync(); window.Close(); }
        });
    }

    [Theory]
    [InlineData("success")] [InlineData("failure")] [InlineData("moved-focus")] [InlineData("cancelled")] [InlineData("replaced-context")]
    public async Task ReviewFocus_InspectorApplyReturnsOnlyAfterSuccessfulOwnedCompletion(string outcome)
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded();
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
            var root = Path.Combine(directory!.FullName, "artifacts", "349", "tests", "shell-" + Guid.NewGuid().ToString("N"));
            var startup = await LightflowStorageCoordinator.StartAsync(root); var storage = startup.Coordinator!;
            storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
            var backend = new FakeBackend(); await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var ranges = new FakeRangeStore(null); var clips = new FakeSubclipService();
            var host = new PlayerViewerHost(coordinator, ranges, clips);
            var window = new MainWindow(storage, startup.Status, startup.Diagnostic) { ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var store = new TestDescriptionStore { SaveStarted = new(), SaveRelease = new(), Failure = outcome == "failure" ? new IOException("test failure") : null };
            try {
                window.Show(); await WaitUntilAsync(() => window.BrowserFolderTree.Items.Count > 0, "shell startup");
                typeof(MainWindow).GetField("_playerViewerHost", flags)!.SetValue(window, host); window.BrowserPlayerHost.Content = host;
                typeof(MainWindow).GetMethod("SetBrowserPresentationMode", flags)!.Invoke(window, [BrowserPresentationMode.PlayerViewer]);
                typeof(MainWindow).GetMethod("SetRightPanelOpen", flags)!.Invoke(window, [true]);
                var asset = ReviewAsset("inspector-focus.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                var inspector = (MediaInspectorView)typeof(MainWindow).GetField("_inspector", flags)!.GetValue(window)!;
                inspector.ConfirmTransition = _ => false;
                inspector.ConfirmDescriptions = _ => false;
                inspector.Initialize(() => new MediaInspectorService(storage.Previews, storage.AssetClassifications, storage.Locations.PreviewsDirectory), store);
                InspectorAsset[] context = outcome == "cancelled"
                    ? [new(asset.AssetId, asset.Name, asset.RelativePath, asset.Kind), new(Guid.NewGuid(), "other.mp4", "other.mp4", asset.Kind)]
                    : [new(asset.AssetId, asset.Name, asset.RelativePath, asset.Kind)];
                inspector.SetContext(context, true);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var editor = (InspectorDescriptionEditor)inspector.DescriptionSection.DataContext;
                var text = ReviewDescendants<TextBox>(inspector.DescriptionSection).First(); text.Focus(); text.Text = "preserve my draft"; text.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                foreach (var key in new[] { InputKey.Space, InputKey.Left, InputKey.Right, InputKey.I, InputKey.O, InputKey.S, InputKey.M, InputKey.C })
                    Assert.False(RaisePlayerKey(text, window, key).Handled);
                Assert.True(editor.HasDraft); Assert.Equal(0, backend.PlayCallCount);
                Assert.False(inspector.TryLeaveContext());
                var apply = ReviewDescendants<Button>(inspector.DescriptionSection).Single(b => Equals(b.Content, editor.ApplyLabel));
                ReviewMouseClick(apply);
                if (outcome == "cancelled") {
                    Assert.True(editor.HasDraft); Assert.True(inspector.IsKeyboardFocusWithin); Assert.Null(store.AppliedPatch); return;
                }
                await store.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(inspector.IsKeyboardFocusWithin); Assert.False(inspector.TryLeaveContext());
                if (outcome == "moved-focus") { host.ZoomChoice.Focus(); Assert.Same(host.ZoomChoice, Keyboard.FocusedElement); }
                if (outcome == "replaced-context") {
                    // An obsolete async completion cannot end a replacement Inspector editing session.
                    inspector.Initialize(() => new MediaInspectorService(storage.Previews, storage.AssetClassifications, storage.Locations.PreviewsDirectory), new TestDescriptionStore());
                    inspector.SetContext([new(Guid.NewGuid(), "replacement.mp4", "replacement.mp4", asset.Kind)], true);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                    var replacement = ReviewDescendants<TextBox>(inspector.DescriptionSection).First();
                    replacement.Focus(); replacement.Text = "new context draft"; replacement.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                }
                store.SaveRelease.SetResult();
                await WaitUntilAsync(() => editor.CanLeaveContext, "Apply completion"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                if (outcome == "success") {
                    Assert.False(editor.HasDraft); Assert.Same(host, Keyboard.FocusedElement);
                    foreach (var key in new[] { InputKey.Space, InputKey.Left, InputKey.Right })
                        Assert.True(RaisePlayerKey(host, window, key).Handled);
                    await VerifyReviewRangeKeys(host, window, backend, ranges, clips);
                } else if (outcome == "failure") { Assert.True(editor.HasDraft); Assert.True(inspector.IsKeyboardFocusWithin); }
                else if (outcome == "replaced-context") { Assert.True(((InspectorDescriptionEditor)inspector.DescriptionSection.DataContext).HasDraft); Assert.True(inspector.IsKeyboardFocusWithin); }
                else { Assert.False(editor.HasDraft); Assert.Same(host.ZoomChoice, Keyboard.FocusedElement); }
            } finally {
                store.SaveRelease.TrySetResult(); await host.CloseAsync();
                // Assertions above deliberately reject draft transitions. Teardown explicitly consents to leave.
                ((MediaInspectorView)typeof(MainWindow).GetField("_inspector", flags)!.GetValue(window)!).ConfirmTransition = _ => true;
                window.Close(); await storage.DisposeAsync();
                Assert.DoesNotContain(window, System.Windows.Application.Current.Windows.Cast<Window>());
            }
        });
    }

    [Fact]
    public async Task ReviewFocus_PerKeyOwnershipPreservesLocalNavigationAndNonconflictingCommands()
    {
        await StaDispatcher.RunAsync(() => {
            var player = new Border(); var filmstrip = new ListBox();
            Assert.True(PlayerKeyboardOwnership.Owns(InputKey.Left, ModifierKeys.None, new Slider(), player, filmstrip));
            Assert.False(PlayerKeyboardOwnership.Owns(InputKey.Space, ModifierKeys.None, new Slider(), player, filmstrip));
            foreach (var key in new[] { InputKey.Space, InputKey.Left, InputKey.I, InputKey.O, InputKey.S, InputKey.M, InputKey.C }) {
                Assert.True(PlayerKeyboardOwnership.Owns(key, ModifierKeys.None, new TextBox { AcceptsReturn = true }, player, filmstrip));
                Assert.True(PlayerKeyboardOwnership.Owns(key, ModifierKeys.None, new ComboBox { IsEditable = true }, player, filmstrip));
                Assert.True(PlayerKeyboardOwnership.Owns(key, ModifierKeys.None, new MenuItem(), player, filmstrip));
            }
            Assert.True(PlayerKeyboardOwnership.Owns(InputKey.Space, ModifierKeys.None, new Button(), player, filmstrip));
            Assert.False(PlayerKeyboardOwnership.Owns(InputKey.I, ModifierKeys.None, new Button(), player, filmstrip));
            Assert.False(PlayerKeyboardOwnership.Owns(InputKey.Right, ModifierKeys.None, filmstrip, player, filmstrip));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReviewFocus_KeyboardTraversalAndFullscreenKeepTheirContracts(bool fullscreen)
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded(); var backend = new FakeBackend(); var ranges = new FakeRangeStore(null); var clips = new FakeSubclipService();
            await using var coordinator = new MediaPlaybackCoordinator(() => new MediaPlaybackService(backend));
            var host = new PlayerViewerHost(coordinator, ranges, clips); var window = CreateSubclipWindow(host);
            window.ShowActivated = false; window.Left = -32000; window.Show();
            try {
                var asset = ReviewAsset("keyboard-focus.mp4"); await host.OpenAsync(asset, ReviewPath(asset));
                if (fullscreen) { host.ToggleFullscreen(); Assert.True(host.IsFullscreen); await VerifyReviewRangeKeys(host, window, backend, ranges, clips); host.ExitFullscreen(); }
                host.Focus();
                bool TabTo(Control target) {
                    for (var i = 0; i < 100; i++) {
                        if (ReferenceEquals(Keyboard.FocusedElement, target)) return true;
                        Assert.True((Keyboard.FocusedElement as UIElement ?? host).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                    }
                    return false;
                }
                Assert.True(TabTo(host.SetInButton)); var before = ranges.SaveCount;
                var space = RaisePlayerKey(host.SetInButton, window, InputKey.Space); Assert.False(space.Handled);
                space.RoutedEvent = Keyboard.KeyDownEvent; host.SetInButton.RaiseEvent(space);
                host.SetInButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, InputKey.Space) { RoutedEvent = Keyboard.KeyUpEvent });
                Assert.Equal(before + 1, ranges.SaveCount); Assert.Same(host.SetInButton, Keyboard.FocusedElement);
                Assert.True(TabTo(host.ZoomChoice)); Assert.True(host.ZoomChoice.IsTabStop);
                Assert.False(RaisePlayerKey(host.ZoomChoice, window, InputKey.Right).Handled);
                var index = host.ZoomChoice.SelectedIndex;
                host.ZoomChoice.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, InputKey.Right) { RoutedEvent = Keyboard.KeyDownEvent });
                Assert.NotEqual(index, host.ZoomChoice.SelectedIndex);
                host.ZoomChoice.IsDropDownOpen = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                host.ZoomChoice.DropDownClosed += (_, _) => closed.TrySetResult();
                host.ZoomChoice.IsDropDownOpen = false; await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Same(host.ZoomChoice, Keyboard.FocusedElement);
            } finally { await host.CloseAsync(); window.Close(); }
        });
    }
    private static IEnumerable<T> ReviewDescendants<T>(DependencyObject root) where T : DependencyObject {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root,i); if (child is T value) yield return value; foreach (var nested in ReviewDescendants<T>(child)) yield return nested; }
    }
}
