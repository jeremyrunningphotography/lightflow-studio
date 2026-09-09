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
    private readonly object _content;
    private readonly WindowStyle _style;
    private readonly ResizeMode _resize;
    private readonly WindowState _state;
    private bool _disposed;

    internal PlayerFullscreenPresentation(FrameworkElement player)
    {
        _player = player;
        _window = Window.GetWindow(player) ?? throw new InvalidOperationException("Player is not hosted in a window.");
        _content = _window.Content;
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
        _window.Content = player;
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
        _window.Content = null;
        _restoreParent();
        _window.Content = _content;
        _window.WindowState = WindowState.Normal;
        _window.WindowStyle = _style; _window.ResizeMode = _resize;
        _window.WindowState = _state;
        _player.Focus();
    }
}
