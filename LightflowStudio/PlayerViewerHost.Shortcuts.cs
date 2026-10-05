using Lightflow.Actions;
using System.Windows;
using System.Windows.Input;

namespace LightflowStudio;

public partial class PlayerViewerHost
{
    internal KeyboardShortcutResolver Shortcuts { get; set; } = new(new(), ShortcutPlatform.Windows);
    private Key? _keyboardColorKey;
    internal Func<BindableCommand, bool, bool>? DispatchShellShortcut { get; set; }
    private bool TryResolvePlayerShortcut(Key key, ModifierKeys modifiers, bool repeat)
    {
        if (!ActionContext.InteractionAvailable || !ActionContext.PlayerPresented) return false;
        // An active/cancelled hold keeps ownership across modifier changes. A fresh
        // nonrepeat press can recover when release occurred outside our window.
        if (_keyboardColorKey == key) {
            if (repeat || _keyboardColorSession is not null) return true;
            _keyboardColorKey = null;
        }
        var gesture = WindowsKeyboardShortcuts.Translate(key, modifiers);
        if (gesture is null) return false;
        var command = Shortcuts.Resolve(gesture, ShortcutContext.Player);
        if (command is null) return false;
        if (repeat && command.Action.Repeat != ActionRepeatPolicy.BoundedRelative) return true;
        if (command.Action.Repeat == ActionRepeatPolicy.Session) {
            if (_keyboardColorKey is not null) return true;
            if (!_actions.Eligibility(command.Action.Id, ActionTarget).Available) return false;
            _keyboardColorKey = key;
            _keyboardColorSession = Guid.NewGuid();
            _ = DispatchKeyboardAsync(new(command.Action.Id, command.Arguments, KeyboardActionSource, _keyboardColorSession.Value, ActionTarget, ActionPhase.Begin));
            return true;
        }
        if (command.Arguments is SetRatingArguments rating) {
            if (_currentAsset?.AssetId is null) return false;
            _ = SetRatingAsync(rating.Rating, rating.ToggleCurrent); return true;
        }
        if (command.Arguments is StepFlagArguments flag) {
            if (_currentAsset?.AssetId is null) return false;
            _ = StepFlagAsync((int)flag.Direction); return true;
        }
        if (PlayerActions.Descriptors.Any(a => a.Id == command.Action.Id)) {
            DispatchReviewKeyboard(command.Action.Id, command.Arguments, repeat);
            if (command.Action.Id is PlayerActions.SetBoundary or PlayerActions.CreateSubclip) Focus();
            return true;
        }
        if (ReviewPresentationActions.Descriptors.Any(a => a.Id == command.Action.Id)) {
            _ = DispatchPresentationKeyboardAsync(command, repeat); return true;
        }
        return DispatchShellShortcut?.Invoke(command, repeat) ?? false;
    }
    private async Task DispatchPresentationKeyboardAsync(BindableCommand command, bool repeat)
    {
        var invocation = new ActionInvocation(command.Action.Id, command.Arguments, KeyboardActionSource, Guid.NewGuid(), ActionTarget, IsRepeat: repeat);
        PresentActionResult(invocation, await _presentationActions.InvokeAsync(invocation));
    }
}
