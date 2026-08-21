using Xunit;

namespace LightflowStudio.Tests;

/// <summary>
/// Source-text regression coverage for the Back button's icon/text vertical alignment, mirroring the
/// technique #109/#126 already use (see BrowserQueryRegressionTests/BrowserStatusBarRegressionTests) for
/// layout details that pixel measurement can't reliably verify in this environment (RenderTargetBitmap
/// produces blank output under the disconnected-RDP-session rendering limitation noted elsewhere in
/// docs/ARCHITECTURE.md). Pins the structural fix — both the icon glyph and "Back" text explicitly centered —
/// rather than a brittle pixel assertion.
/// </summary>
public sealed class PlayerViewerHostAlignmentRegressionTests
{
    [Fact]
    public void BackButtonContent_CentersBothTheIconGlyphAndTheTextIndependently()
    {
        var source = Source();
        var buttonStart = source.IndexOf("x:Name=\"BackButton\"", StringComparison.Ordinal);
        Assert.True(buttonStart >= 0, "BackButton not found");
        var buttonEnd = source.IndexOf("</Button>", buttonStart, StringComparison.Ordinal);
        var body = source[buttonStart..buttonEnd];

        // A horizontal StackPanel top-aligns its children by default; the icon font glyph's own line-height
        // differs from the plain-text run's, so each child needs its own explicit VerticalAlignment="Center"
        // rather than relying on the StackPanel (whose own VerticalAlignment only affects its position within
        // the Button's content area, not how it arranges its own children) to align them as one control.
        var glyphIndex = body.IndexOf("Text=\"&#xE72B;\"", StringComparison.Ordinal);
        Assert.True(glyphIndex >= 0, "Back arrow glyph TextBlock not found inside BackButton");
        var backTextIndex = body.IndexOf("Text=\"Back\"", StringComparison.Ordinal);
        Assert.True(backTextIndex >= 0, "\"Back\" text TextBlock not found inside BackButton");

        var glyphElementStart = body.LastIndexOf("<TextBlock", glyphIndex, StringComparison.Ordinal);
        var glyphElement = body[glyphElementStart..(body.IndexOf('>', glyphIndex) + 1)];
        var backTextElementStart = body.LastIndexOf("<TextBlock", backTextIndex, StringComparison.Ordinal);
        var backTextElement = body[backTextElementStart..(body.IndexOf('>', backTextIndex) + 1)];

        Assert.Contains("VerticalAlignment=\"Center\"", glyphElement);
        Assert.Contains("VerticalAlignment=\"Center\"", backTextElement);
    }

    private static string Source() =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "PlayerViewerHost.xaml"));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the Lightflow Studio repository root.");
    }
}
