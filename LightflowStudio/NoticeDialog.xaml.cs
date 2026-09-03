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
    internal static void Show(Window owner, string title, string heading, string message) =>
        new NoticeDialog(title, heading, message) { Owner = owner }.ShowDialog();
}
