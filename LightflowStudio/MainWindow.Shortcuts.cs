using Lightflow.Actions;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LightflowStudio;

public partial class MainWindow
{
    private ShortcutProfile _shortcutProfile = new();
    private ShortcutProfile _shortcutDraft = new();
    private KeyboardShortcutResolver _shortcutResolver = new(new(), ShortcutPlatform.Windows);
    private bool _shortcutsChanged;
    private bool _shortcutsCanSave = true;
    private BindableCommand? _captureCommand;
    private KeyboardGesture? _captureGesture;
    private Key? _captureKeyRelease;
    private ShortcutRow? _captureRow;
    private string ShortcutPath => Path.Combine(Path.GetDirectoryName(_storage.Locations.SettingsPath)!, "keyboard-shortcuts.json");

    private void InitializeShortcuts()
    {
        var loaded = KeyboardShortcutStore.Load(ShortcutPath);
        _shortcutProfile = loaded.Profile;
        _shortcutDraft = _shortcutProfile.Copy();
        _shortcutsCanSave = loaded.CanSave;
        ApplyShortcutResolver();
        ShortcutMessage.Text = loaded.Diagnostic ?? "";
        RefreshShortcutRows();
        Deactivated += (_, _) => CancelShortcutCapture(false);
    }
    private void ApplyShortcutResolver()
    {
        _shortcutResolver = new(_shortcutProfile, ShortcutPlatform.Windows);
        if (_playerViewerHost is { } host) {
            host.CancelPlayerActionSessions();
            host.Shortcuts = _shortcutResolver;
        }
    }
    private bool TryHandleShellShortcut(System.Windows.Input.KeyEventArgs e)
    {
        if (MainTabs.SelectedIndex != 0 || !IsEnabled || System.Windows.Interop.ComponentDispatcher.IsThreadModal) return false;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var input = e.OriginalSource as DependencyObject;
        if (PlayerKeyboardOwnership.Owns(key, Keyboard.Modifiers, input, PlayerOwnsShortcutContext() ? _playerViewerHost! : BrowserGridRows,
            PlayerOwnsShortcutContext() ? _playerViewerHost!.Filmstrip : BrowserGridRows)) return false;
        var gesture = WindowsKeyboardShortcuts.Translate(key, Keyboard.Modifiers);
        var command = gesture is null ? null : _shortcutResolver.Resolve(gesture, PlayerOwnsShortcutContext() ? ShortcutContext.Player : ShortcutContext.Browser);
        return command is not null && ReviewShellActions.Descriptors.Any(a => a.Id == command.Action.Id) && DispatchConfiguredShell(command, e.IsRepeat);
    }
    private bool DispatchConfiguredShell(BindableCommand command, bool repeat)
    {
        if (repeat && command.Action.Repeat != ActionRepeatPolicy.BoundedRelative) return true;
        _ = DispatchShellActionAsync(command.Action.Id, command.Arguments, ReviewShellKeyboard, repeat);
        return true;
    }
    internal bool TryHandleConfiguredBrowserShortcut(Key key, ModifierKeys modifiers, DependencyObject? input, bool repeat = false)
    {
        if (!BrowserSemanticContext.Presented || !BrowserSemanticContext.InteractionAvailable || BrowserOwnsLocalKey(key, modifiers, input)) return false;
        var gesture = WindowsKeyboardShortcuts.Translate(key, modifiers);
        var command = gesture is null ? null : _shortcutResolver.Resolve(gesture, ShortcutContext.Browser);
        if (command is null || !BrowserActions.Descriptors.Any(a => a.Id == command.Action.Id)) return false;
        if (repeat && command.Action.Repeat != ActionRepeatPolicy.BoundedRelative) return true;
        var arguments = command.Arguments;
        if (arguments is NavigateSelectionArguments nav) {
            var columns = _browserGrid.Rows.FirstOrDefault()?.Tiles.Count ?? 1;
            var distance = command.Distance == NavigationDistance.Row ? columns : command.Distance == NavigationDistance.Page ?
                columns * Math.Max(1, (int)((FindBrowserGridScrollViewer()?.ViewportHeight ?? 380) /
                    (_browserLayoutMode == BrowserLayoutMode.Details ? BrowserDetails.RowHeight : 150))) : 1;
            arguments = nav with { Distance = Math.Clamp(distance, 1, 10000) };
        }
        _ = InvokeBrowserActionAsync(command.Action.Id, arguments, repeat, ActionInputKind.Keyboard);
        return true;
    }
    private void RefreshShortcutRows()
    {
        if (ShortcutRows is null) return;
        var sections = BuildShortcutSections(_shortcutDraft, ShortcutSearch.Text ?? "", _shortcutExpansion,
            _shortcutRowModels, _captureCommand?.Id, _shortcutProfile);
        ShortcutRows.ItemsSource = sections;
        ShortcutEmpty.Visibility = sections.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ShortcutSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshShortcutRows();
    private BindableCommand ShortcutFromButton(object sender) => KeyboardCommandCatalog.Commands.Single(c => c.Id == (string)((FrameworkElement)sender).Tag);
    private void ShortcutEdit_Click(object sender, RoutedEventArgs e)
    {
        CancelShortcutCapture(false);
        _captureCommand = ShortcutFromButton(sender);
        _captureRow = _shortcutRowModels[_captureCommand.Id];
        _captureGesture = null;
        _captureRow.Capture(true, message: "Press the new key combination. Esc cancels; modifier-only presses are ignored.");
        FocusShortcutControl(_captureCommand.Id, "ShortcutCancel");
    }
    private bool TryCaptureShortcut(System.Windows.Input.KeyEventArgs e)
    {
        if (_captureCommand is null) return false;
        if (MainTabs.SelectedIndex != ShellDestinationSelection.Index(ShellDestination.Settings) || SettingsShortcutsPage.Visibility != Visibility.Visible) {
            CancelShortcutCapture(); return false;
        }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { e.Handled = true; CancelShortcutCapture(); return true; }
        // Tab leaves recording and keeps normal focus navigation; a valid candidate leaves
        // recording so Use Shortcut/Cancel remain operable by keyboard and assistive tools.
        if (key == Key.Tab && _captureGesture is null) {
            var id = _captureCommand.Id;
            CancelShortcutCapture(false);
            ShortcutRows.UpdateLayout();
            FindShortcutControl(id, "ShortcutEdit")?.Focus();
            return false;
        }
        if (_captureGesture is not null) {
            if (_captureKeyRelease == key) { e.Handled = true; return true; }
            return false;
        }
        e.Handled = true;
        if (e.IsRepeat) return true;
        var gesture = WindowsKeyboardShortcuts.Translate(key, Keyboard.Modifiers);
        if (gesture is null) return true;
        var error = new KeyboardShortcutResolver(_shortcutDraft, ShortcutPlatform.Windows).Validate(_captureCommand, gesture);
        _captureGesture = error is null ? gesture : null;
        _captureKeyRelease = key;
        _captureRow!.Capture(true, gesture.Display(ShortcutPlatform.Windows), error ?? "Select Use Shortcut to stage this change.", error is null);
        if (error is null) FocusShortcutControl(_captureCommand.Id, "ShortcutApply");
        return true;
    }
    private void ShortcutApply_Click(object sender, RoutedEventArgs e)
    {
        if (_captureCommand is null || _captureGesture is null) return;
        _shortcutDraft.Set(_captureCommand, _captureGesture, ShortcutPlatform.Windows);
        ChangedShortcuts();
        CancelShortcutCapture();
    }
    private void ShortcutCancel_Click(object sender, RoutedEventArgs e) => CancelShortcutCapture();
    private void CancelShortcutCapture(bool restoreFocus = true)
    {
        ++_shortcutFocusVersion;
        if (_captureCommand is null) return;
        var id = _captureCommand.Id;
        _captureRow?.Capture(false); _captureRow = null;
        _captureCommand = null; _captureGesture = null;
        _captureKeyRelease = null;
        if (restoreFocus) FocusShortcutControl(id, "ShortcutEdit");
    }
    private void ShortcutClear_Click(object sender, RoutedEventArgs e)
    {
        var command = ShortcutFromButton(sender);
        if (_captureCommand?.Id == command.Id) CancelShortcutCapture(false);
        _shortcutDraft.Set(command, null, ShortcutPlatform.Windows);
        ChangedShortcuts();
        FocusShortcutControl(command.Id, "ShortcutEdit");
    }
    private void ShortcutReset_Click(object sender, RoutedEventArgs e)
    {
        var command = ShortcutFromButton(sender);
        var current = _shortcutDraft.Copy(); current.Reset(command.Id);
        var resolver = new KeyboardShortcutResolver(current, ShortcutPlatform.Windows);
        if (command.Default(ShortcutPlatform.Windows) is { } gesture && resolver.Validate(command, gesture) is { } error) {
            CancelShortcutCapture(false);
            _captureCommand = command; _captureRow = _shortcutRowModels[command.Id]; _captureGesture = null;
            _captureRow.Capture(true, gesture.Display(ShortcutPlatform.Windows), error + " Unassign or reset the conflicting command first.");
            FocusShortcutControl(command.Id, "ShortcutCancel"); return;
        }
        if (_captureCommand?.Id == command.Id) CancelShortcutCapture(false);
        _shortcutDraft = current; ChangedShortcuts();
        FocusShortcutControl(command.Id, "ShortcutEdit");
    }
    private void ShortcutResetAll_Click(object sender, RoutedEventArgs e) => ResetAllShortcuts();
    private void ResetAllShortcuts() { _shortcutDraft.ResetAll(); CancelShortcutCapture(); ChangedShortcuts(); }
    private void ChangedShortcuts()
    {
        _shortcutsChanged = true;
        // Update existing objects: rebuilding ItemsSource would discard row focus/scroll anchors.
        UpdateShortcutModels(_shortcutDraft, _shortcutProfile, _shortcutRowModels);
    }
    private bool SaveShortcuts()
    {
        if (!_shortcutsChanged) return true;
        if (!_shortcutsCanSave) { SettingsMessage.Text = "The shortcut file uses a newer schema. It has been retained; shortcut changes cannot be saved."; return false; }
        var resolver = new KeyboardShortcutResolver(_shortcutDraft, ShortcutPlatform.Windows);
        foreach (var info in resolver.Query()) if (info.Current is { } gesture && resolver.Validate(info.Command, gesture) is { } error) {
            SettingsMessage.Text = $"{info.Label}: {error}"; return false;
        }
        try { KeyboardShortcutStore.Save(ShortcutPath, _shortcutDraft); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { SettingsMessage.Text = $"Shortcuts could not be saved: {error.Message}"; return false; }
        _shortcutProfile = _shortcutDraft.Copy(); ApplyShortcutResolver(); _shortcutsChanged = false;
        UpdateShortcutModels(_shortcutDraft, _shortcutProfile, _shortcutRowModels);
        CancelShortcutCapture(); ShortcutMessage.Text = "Keyboard shortcuts saved.";
        return true;
    }
}
