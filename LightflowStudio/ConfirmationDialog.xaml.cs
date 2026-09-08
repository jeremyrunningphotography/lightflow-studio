using System.Windows;

namespace LightflowStudio;

public partial class ConfirmationDialog : Window
{
    private InspectorDescriptionEditor? _transitionEditor;
    private bool _saving;
    public ConfirmationDialog(string title, string heading, string message, string? detail, string confirmLabel)
    {
        InitializeComponent();
        Title = title;
        HeadingText.Text = heading;
        MessageText.Text = message;
        DetailText.Text = detail ?? "";
        DetailSurface.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.Content = confirmLabel;
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }

    public ConfirmationDialog(string title, string heading, string message, string? detail, string confirmLabel,
        string cancelLabel) : this(title, heading, message, detail, confirmLabel) => CancelButton.Content = cancelLabel;

    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_transitionEditor is null) { DialogResult = true; Close(); return; }
        _saving = true;
        CancelButton.IsEnabled = DiscardButton.IsEnabled = ConfirmButton.IsEnabled = false;
        MessageText.Text = "Saving changes before continuing…";
        var saved = await _transitionEditor.ResolveTransitionAsync(DescriptionTransitionChoice.Apply);
        _saving = false;
        // A failed save returns to the original context, where the draft and failure remain visible.
        DialogResult = saved;
        Close();
    }

    private async void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (_transitionEditor is null) return;
        DialogResult = await _transitionEditor.ResolveTransitionAsync(DescriptionTransitionChoice.Discard);
        Close();
    }

    internal static bool ConfirmTransition(Window owner, InspectorDescriptionEditor editor)
    {
        var request = editor.PendingChanges;
        var dialog = new ConfirmationDialog("Unapplied descriptive edits", "Save changes before continuing?",
            $"You have unapplied edits for {request.AssetCount:N0} selected asset(s). Apply them, discard them, or cancel to keep editing.",
            string.Join(", ", request.Fields), "Apply changes")
        { Owner = owner, Width = 620, _transitionEditor = editor };
        dialog.DiscardButton.Visibility = Visibility.Visible;
        dialog.ConfirmButton.IsDefault = false;
        dialog.CancelButton.IsDefault = true;
        dialog.Closing += (_, e) => { if (dialog._saving) e.Cancel = true; };
        return dialog.ShowDialog() == true;
    }

    internal static bool Confirm(Window owner, string title, string heading, string message, string? detail,
        string confirmLabel) => new ConfirmationDialog(title, heading, message, detail, confirmLabel) { Owner = owner }.ShowDialog() == true;

    internal static bool Confirm(Window owner, string title, string heading, string message, string? detail,
        string confirmLabel, string cancelLabel) =>
        new ConfirmationDialog(title, heading, message, detail, confirmLabel, cancelLabel) { Owner = owner }.ShowDialog() == true;
}
