using System.Windows;
using System.Windows.Controls;
using Panel = System.Windows.Controls.Panel;

namespace LightflowStudio;

/// <summary>Temporarily hosts the same Player in the same window; no playback lease or source transition.</summary>
internal sealed class PlayerFullscreenPresentation : IDisposable
{
    private readonly Window _window;
    private readonly FrameworkElement _player;
    private readonly Action _restoreParent;
    private readonly Grid _root;
    private readonly ContentControl _layer;
    private readonly WindowStyle _style;
    private readonly ResizeMode _resize;
    private readonly WindowState _state;
    private bool _disposed;

    internal PlayerFullscreenPresentation(FrameworkElement player)
    {
        _player = player;
        _window = Window.GetWindow(player) ?? throw new InvalidOperationException("Player is not hosted in a window.");
        _root = _window.Content as Grid ?? throw new InvalidOperationException("Fullscreen requires a root Grid.");
        _style = _window.WindowStyle; _resize = _window.ResizeMode; _state = _window.WindowState;
        if (player.Parent is ContentControl contentHost)
        {
            _restoreParent = () => contentHost.Content = player;
            contentHost.Content = null;
        }
        else if (player.Parent is Panel panel)
        {
            var index = panel.Children.IndexOf(player);
            _restoreParent = () => panel.Children.Insert(index, player);
            panel.Children.Remove(player);
        }
        else throw new InvalidOperationException("Player requires a content or panel host.");
        // Keep the shell and its layout/loaded lifetime attached. Only the same Player moves
        // into a root-spanning presentation slot, with its own attached properties untouched.
        _layer = new ContentControl { Content = player,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch };
        Grid.SetRowSpan(_layer, Math.Max(1, _root.RowDefinitions.Count));
        Grid.SetColumnSpan(_layer, Math.Max(1, _root.ColumnDefinitions.Count));
        Panel.SetZIndex(_layer, int.MaxValue);
        _root.Children.Add(_layer);
        _window.WindowState = WindowState.Normal;
        _window.WindowStyle = WindowStyle.None;
        _window.ResizeMode = ResizeMode.NoResize;
        _window.WindowState = WindowState.Maximized;
        player.Focus();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _layer.Content = null;
        _root.Children.Remove(_layer);
        _window.WindowState = WindowState.Normal;
        _window.WindowStyle = _style; _window.ResizeMode = _resize;
        _window.WindowState = _state;
        _restoreParent();
        _player.Focus();
    }
}
