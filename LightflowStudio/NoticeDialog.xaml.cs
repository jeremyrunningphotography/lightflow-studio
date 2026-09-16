using System.Windows;

namespace LightflowStudio;

public partial class NoticeDialog : Window
{
    public NoticeDialog(string title, string heading, string message)
    {
        InitializeComponent(); Title = title; HeadingText.Text = heading; MessageText.Text = message;
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }
    private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    internal static bool OfferAction(Window owner, string title, string heading, string message, string actionLabel)
    {
        var dialog = new NoticeDialog(title, heading, message) { Owner = owner };
        dialog.ActionButton.Content = actionLabel;
        dialog.ActionButton.Style = (Style)dialog.FindResource("PrimaryButton");
        dialog.CancelButton.Visibility = Visibility.Visible;
        return dialog.ShowDialog() == true;
    }
    internal static void Show(Window owner, string title, string heading, string message) =>
        new NoticeDialog(title, heading, message) { Owner = owner }.ShowDialog();
}
