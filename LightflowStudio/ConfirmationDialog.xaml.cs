using System.Windows;

namespace LightflowStudio;

public partial class ConfirmationDialog : Window
{
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
    private void Confirm_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }

    internal static bool Confirm(Window owner, string title, string heading, string message, string? detail,
        string confirmLabel) => new ConfirmationDialog(title, heading, message, detail, confirmLabel) { Owner = owner }.ShowDialog() == true;

    internal static bool Confirm(Window owner, string title, string heading, string message, string? detail,
        string confirmLabel, string cancelLabel) =>
        new ConfirmationDialog(title, heading, message, detail, confirmLabel, cancelLabel) { Owner = owner }.ShowDialog() == true;
}
