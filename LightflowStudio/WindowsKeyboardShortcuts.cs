using Lightflow.Actions;
using System.Windows.Input;

namespace LightflowStudio;

/// <summary>The only WPF-to-neutral key translation. IME/dead/modifier/numpad input is deliberately ineligible.</summary>
internal static class WindowsKeyboardShortcuts
{
    internal static KeyboardGesture? Translate(Key key, ModifierKeys modifiers)
    {
        string? logical = key >= Key.A && key <= Key.Z ? key.ToString()
            : key >= Key.D0 && key <= Key.D9 ? ((int)key - (int)Key.D0).ToString()
            : key >= Key.F1 && key <= Key.F24 ? key.ToString()
            : key switch { Key.Space => "Space", Key.Left => "Left", Key.Right => "Right", Key.Up => "Up", Key.Down => "Down",
                Key.Home => "Home", Key.End => "End", Key.PageUp => "PageUp", Key.PageDown => "PageDown", Key.Enter => "Enter",
                Key.Escape => "Escape", Key.Delete => "Delete", Key.Tab => "Tab", _ => null };
        if (logical is null) return null;
        var neutral = ShortcutModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) neutral |= ShortcutModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt)) neutral |= ShortcutModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift)) neutral |= ShortcutModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) neutral |= ShortcutModifiers.Meta;
        return new(logical, neutral);
    }
}
