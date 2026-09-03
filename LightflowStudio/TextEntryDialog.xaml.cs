using System.Windows;

namespace LightflowStudio;

public partial class TextEntryDialog : Window
{
    public TextEntryDialog(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueText.Text = initialValue;
        Loaded += (_, _) => { ValueText.Focus(); ValueText.SelectAll(); };
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }

    public string Value => ValueText.Text.Trim();
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0) return;
        DialogResult = true;
        Close();
    }

    internal static string? Prompt(Window owner, string title, string prompt, string initialValue = "")
    {
        var dialog = new TextEntryDialog(title, prompt, initialValue) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }
}
