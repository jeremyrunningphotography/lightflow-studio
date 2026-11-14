using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

public sealed partial class PlayerViewerHostLeaseTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task SpeedPopupClose_RestoresPlayerArrowsAndButtonsAtEveryRate(int index)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            await using var f = await FocusFixture.CreateAsync();
            f.Host.SpeedChoice.Focus();
            f.Host.SpeedChoice.IsDropDownOpen = true;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Task.Delay(100);
            f.Host.SpeedChoice.SelectedIndex = index;
            Assert.True(f.Host.SpeedChoice.IsDropDownOpen);
            f.Host.SpeedChoice.IsDropDownOpen = false;
            await DrainFocusAsync(f.Host);
            await WaitUntilAsync(() => ReferenceEquals(f.Host, Keyboard.FocusedElement), "Player receives closed speed-popup focus");
            Assert.Same(f.Host, Keyboard.FocusedElement);
            await Task.Delay(200); // popup fade/teardown must not recapture focus afterward
            await WaitUntilAsync(() => ReferenceEquals(f.Host, Keyboard.FocusedElement), "Player receives closed speed-popup focus");
            Assert.Same(f.Host, Keyboard.FocusedElement);
            Assert.Equal(PlaybackReviewOptions.Speeds[index], f.Backend.Options.Speed);
            FocusKey(System.Windows.Input.Key.Right);
            await f.IdleAsync();
            FocusKey(System.Windows.Input.Key.Left);
            await f.IdleAsync();
            f.Host.NextFrameButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await f.IdleAsync();
            f.Host.PreviousFrameButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await f.IdleAsync();
            Assert.Equal(new[] { "forward", "backward", "forward", "backward" },
                f.Backend.Operations.Where(x => x is "forward" or "backward"));
        });
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task SpeedPopup_KeyboardEnterAndEscapeKeepNativeSelectionAndResumePlayer(bool confirm)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            await using var f = await FocusFixture.CreateAsync();
            var back = 0; f.Host.BackRequested += (_, _) => back++;
            f.Host.SpeedChoice.Focus();
            f.Host.SpeedChoice.IsDropDownOpen = true;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Task.Delay(100);
            var initial = f.Host.SpeedChoice.SelectedIndex;
            var popup = (Popup)f.Host.SpeedChoice.Template.FindName("PART_Popup", f.Host.SpeedChoice);
            popup.Child.IsHitTestVisible = false; // exclude ambient pointer hover from keyboard-only input
            Assert.True(((ComboBoxItem)f.Host.SpeedChoice.ItemContainerGenerator.ContainerFromIndex(initial)).Focus());
            var operations = f.Backend.Operations.Count;
            FocusKey(System.Windows.Input.Key.Up);
            Assert.True(f.Host.SpeedChoice.IsDropDownOpen);
            Assert.Equal(operations, f.Backend.Operations.Count);
            var highlighted = Assert.IsType<ComboBoxItem>(Keyboard.FocusedElement);
            var highlightedIndex = f.Host.SpeedChoice.ItemContainerGenerator.IndexFromContainer(highlighted);
            Assert.NotEqual(initial, highlightedIndex);
            FocusKey(confirm ? System.Windows.Input.Key.Enter : System.Windows.Input.Key.Escape);
            await DrainFocusAsync(f.Host);
            Assert.False(f.Host.SpeedChoice.IsDropDownOpen);
            Assert.Equal(0, back);
            Assert.Equal(confirm ? highlightedIndex : initial, f.Host.SpeedChoice.SelectedIndex);
            Assert.Equal(PlaybackReviewOptions.Speeds[confirm ? highlightedIndex : initial], f.Backend.Options.Speed);
            await WaitUntilAsync(() => ReferenceEquals(f.Host, Keyboard.FocusedElement), "Player receives closed speed-popup focus");
            Assert.Same(f.Host, Keyboard.FocusedElement);
        });
    }

    [Fact]
    public async Task ClosedSpeedKeyboardSelection_HandsOffOnlyItsOwnFocus()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            await using var f = await FocusFixture.CreateAsync();
            f.Host.SpeedChoice.Focus();
            FocusKey(System.Windows.Input.Key.Up);
            await DrainFocusAsync(f.Host);
            Assert.Equal(2, f.Host.SpeedChoice.SelectedIndex);
            await WaitUntilAsync(() => ReferenceEquals(f.Host, Keyboard.FocusedElement), "Player receives closed speed-popup focus");
            Assert.Same(f.Host, Keyboard.FocusedElement);
            f.Text.Focus();
            f.Host.SpeedChoice.SelectedIndex = 1; // programmatic change cannot steal editing focus
            await DrainFocusAsync(f.Host);
            Assert.Same(f.Text, Keyboard.FocusedElement);
        });
    }

    [Fact]
    public async Task ReopenedSpeedPopup_KeepsOwnershipDespitePendingCloseHandoff()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            await using var f = await FocusFixture.CreateAsync();
            f.Host.SpeedChoice.Focus();
            f.Host.SpeedChoice.IsDropDownOpen = true;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Task.Delay(100);
            f.Host.SpeedChoice.IsDropDownOpen = false;
            f.Host.SpeedChoice.IsDropDownOpen = true;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Task.Delay(100);
            await DrainFocusAsync(f.Host);
            Assert.True(f.Host.SpeedChoice.IsDropDownOpen);
            Assert.True(f.Host.SpeedChoice.IsKeyboardFocusWithin);
            Assert.False(f.Host.TryHandleShortcut(System.Windows.Input.Key.Escape, f.Host.SpeedChoice, ModifierKeys.None));
            f.Host.SpeedChoice.IsDropDownOpen = false;
            await DrainFocusAsync(f.Host);
            await WaitUntilAsync(() => ReferenceEquals(f.Host, Keyboard.FocusedElement), "Player receives closed speed-popup focus");
            Assert.Same(f.Host, Keyboard.FocusedElement);
        });
    }

    [Theory]
    [InlineData("background")] [InlineData("preview")] [InlineData("header")]
    public async Task NeutralPlayerPress_ReleasesStaleSpeedFocus(string target)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            await using var f = await FocusFixture.CreateAsync();
            f.Host.SpeedChoice.Focus();
            UIElement surface = target switch
            {
                "background" => f.Host.TransportBar,
                "header" => f.Host.PlayerHeader,
                _ => f.Host.MediaSurfaceHost
            };
            FocusPress(surface);
            Assert.False(f.Host.SpeedChoice.IsKeyboardFocusWithin);
            Assert.True(f.Host.IsKeyboardFocusWithin);
            FocusKey(System.Windows.Input.Key.Right);
            await f.IdleAsync();
            Assert.Contains("forward", f.Backend.Operations);
        });
    }

    [Theory]
    [InlineData("text")] [InlineData("slider")] [InlineData("dropdown")]
    [InlineData("browser")] [InlineData("jobs")] [InlineData("subclips")]
    public async Task DeferredSpeedHandoff_DoesNotOverrideAnotherInputOwner(string owner)
    {
        await StaDispatcher.RunAsync(async () =>
        {
            await using var f = await FocusFixture.CreateAsync();
            var target = owner switch
            {
                "text" => (Control)f.Text,
                "slider" => f.Host.PositionSlider,
                "dropdown" => f.Host.ZoomChoice,
                "subclips" => f.Host.SubclipsList,
                "jobs" => f.Jobs,
                _ => f.Browser
            };
            f.Host.SpeedChoice.Focus();
            f.Host.SpeedChoice.IsDropDownOpen = true;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Task.Delay(100);
            f.Host.SpeedChoice.SelectedIndex = 4;
            f.Host.SpeedChoice.IsDropDownOpen = false;
            target.Focus();
            Assert.Same(target, Keyboard.FocusedElement);
            await DrainFocusAsync(f.Host);
            Assert.Same(target, Keyboard.FocusedElement);
            if (owner is "slider" or "dropdown")
            {
                FocusPress(target);
                Assert.Same(target, Keyboard.FocusedElement);
            }
            Assert.False(f.Host.TryHandleShortcut(System.Windows.Input.Key.Left, target, ModifierKeys.None));
            FocusPress(f.Host.TransportBar);
            Assert.True(f.Host.IsKeyboardFocused);
        });
    }

    [Fact]
    public async Task SpeedHandoff_DoesNotActivateAnotherWindowOrSurviveMediaClose()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            await using var f = await FocusFixture.CreateAsync();
            f.Host.SpeedChoice.Focus();
            f.Host.SpeedChoice.IsDropDownOpen = true;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Task.Delay(100);
            f.Host.SpeedChoice.IsDropDownOpen = false;
            var editor = new TextBox();
            var dialog = new Window { Content = editor, Width = 200, Height = 100, Left = 100,
                ShowInTaskbar = false, Owner = f.Window };
            dialog.Show(); dialog.Activate();
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Task.Delay(100);
            editor.Focus();
            try { await DrainFocusAsync(f.Host); Assert.Same(editor, Keyboard.FocusedElement); }
            finally { dialog.Close(); }
            f.Window.Activate(); f.Host.SpeedChoice.Focus();
            f.Host.SpeedChoice.IsDropDownOpen = true;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Task.Delay(100);
            f.Host.SpeedChoice.IsDropDownOpen = false;
            await f.Host.CloseAsync();
            f.Text.Focus();
            await DrainFocusAsync(f.Host);
            Assert.Same(f.Text, Keyboard.FocusedElement);
        });
    }

    [Fact]
    public async Task RepeatedSpeedChangesAndSourceReplacement_DoNotRestoreObsoleteFocus()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            await using var f = await FocusFixture.CreateAsync();
            for (var i = 0; i < 12; i++)
            {
                f.Host.SpeedChoice.Focus(); f.Host.SpeedChoice.IsDropDownOpen = true;
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                await Task.Delay(100);
                f.Host.SpeedChoice.SelectedIndex = i % 6;
                f.Host.SpeedChoice.IsDropDownOpen = false;
                await DrainFocusAsync(f.Host);
                await WaitUntilAsync(() => ReferenceEquals(f.Host, Keyboard.FocusedElement), "Player receives closed speed-popup focus");
                Assert.Same(f.Host, Keyboard.FocusedElement);
            }
            f.Host.SpeedChoice.Focus(); f.Host.SpeedChoice.IsDropDownOpen = true;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Task.Delay(100);
            f.Host.SpeedChoice.IsDropDownOpen = false;
            await f.OpenAsync("replacement.mp4");
            f.Text.Focus(); await DrainFocusAsync(f.Host);
            Assert.Same(f.Text, Keyboard.FocusedElement);
            Assert.False(f.Host.SpeedChoice.IsDropDownOpen);
        });
    }

    [Fact]
    public async Task SourceReplacement_ClosesOpenSpeedPopupAndDiscardsItsSelectionTransaction()
    {
        await StaDispatcher.RunAsync(async () =>
        {
            await using var f = await FocusFixture.CreateAsync();
            f.Host.SpeedChoice.Focus(); f.Host.SpeedChoice.IsDropDownOpen = true;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Task.Delay(100);
            f.Host.SpeedChoice.SelectedIndex = 4;
            await f.OpenAsync("new-source.mp4");
            f.Text.Focus();
            await DrainFocusAsync(f.Host);
            Assert.False(f.Host.SpeedChoice.IsDropDownOpen);
            Assert.Equal(3, f.Host.SpeedChoice.SelectedIndex);
            Assert.Equal(1, f.Backend.Options.Speed);
            Assert.Same(f.Text, Keyboard.FocusedElement);
        });
    }

    private static async Task DrainFocusAsync(PlayerViewerHost host)
    {
        if (!host.SpeedChoice.IsDropDownOpen && host.SpeedChoice.Template?.FindName("PART_Popup", host.SpeedChoice) is Popup { Child: Visual child })
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (PresentationSource.FromVisual(child) is not null)
            {
                Assert.True(DateTime.UtcNow < deadline, "Speed popup did not finish closing.");
                await Task.Delay(10);
            }
        }
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    private static void FocusPress(UIElement target) => target.RaiseEvent(
        new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseDownEvent });

    private static void FocusKey(Key key)
    {
        var target = Assert.IsAssignableFrom<UIElement>(Keyboard.FocusedElement);
        var source = PresentationSource.FromVisual(target);
        Assert.NotNull(source);
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled)
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
                { RoutedEvent = Keyboard.KeyDownEvent });
    }

    private sealed class FocusFixture : IAsyncDisposable
    {
        internal FakeBackend Backend { get; } = new();
        internal MediaPlaybackCoordinator Coordinator { get; }
        internal PlayerViewerHost Host { get; }
        internal Window Window { get; }
        internal TextBox Text { get; } = new() { Width = 120 };
        internal ListBox Browser { get; } = new() { Width = 100, Height = 50 };
        internal ListBox Jobs { get; } = new() { Width = 100, Height = 50 };
        private FocusFixture()
        {
            Coordinator = new(() => new MediaPlaybackService(Backend));
            Host = new(Coordinator);
            var root = new DockPanel();
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            controls.Children.Add(Text); controls.Children.Add(Browser); controls.Children.Add(Jobs);
            DockPanel.SetDock(controls, Dock.Bottom); root.Children.Add(controls);
            Host.SubclipsContent.Height = 100;
            DockPanel.SetDock(Host.SubclipsContent, Dock.Bottom); root.Children.Add(Host.SubclipsContent);
            root.Children.Add(Host);
            Window = new() { Content = root, Width = 1200, Height = 900, Left = 100, ShowInTaskbar = false };
        }
        internal static async Task<FocusFixture> CreateAsync()
        {
            TestWpfApplication.EnsureLoaded();
            var f = new FocusFixture(); f.Window.Show(); f.Window.Activate();
            await f.OpenAsync("focus.mp4"); f.Window.Activate(); f.Window.UpdateLayout();
            Assert.True(f.Host.SpeedChoice.Focus());
            return f;
        }
        internal async Task OpenAsync(string name)
        {
            var asset = new PlayerViewerAsset(Guid.NewGuid(), name, name, name, MediaPresentationKind.Video);
            await Host.OpenAsync(asset, new(asset.RootId, asset.RelativePath, asset.Key,
                Path.GetFullPath(name), MediaRootAvailability.Online, true));
        }
        internal Task IdleAsync() => ((FrameStepQueue)typeof(PlayerViewerHost).GetField("_frameStepQueue",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Host)!).WaitUntilIdleAsync();
        public async ValueTask DisposeAsync()
        { Host.SpeedChoice.IsDropDownOpen = false; await DrainFocusAsync(Host); await Host.CloseAsync(); Window.Close(); await Coordinator.DisposeAsync(); }
    }
}

