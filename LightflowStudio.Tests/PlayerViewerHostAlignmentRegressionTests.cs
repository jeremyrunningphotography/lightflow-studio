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

    [Fact]
    public void VolumeSlider_HandlesClickToSetTheSameWayThePositionSliderDoes()
    {
        var source = Source();
        var sliderStart = source.IndexOf("x:Name=\"VolumeSlider\"", StringComparison.Ordinal);
        Assert.True(sliderStart >= 0, "VolumeSlider not found");
        var elementStart = source.LastIndexOf("<Slider", sliderStart, StringComparison.Ordinal);
        var elementEnd = source.IndexOf('>', sliderStart);
        var element = source[elementStart..elementEnd];

        // The PlaybackTimelineSlider style's own track RepeatButtons only nudge by DecreaseLarge/IncreaseLarge
        // on a click (ordinary WPF Slider default) — PreviewMouseLeftButtonDown is what actually makes a click
        // anywhere on the track jump straight to that position, exactly like PositionSlider already does.
        Assert.Contains("PreviewMouseLeftButtonDown=\"VolumeSlider_PreviewMouseLeftButtonDown\"", element);
        Assert.Contains("PlaybackTimelineSlider", element);
    }

    [Fact]
    public void MuteButton_ReservesAStableFootprintForEveryVisualState()
    {
        var source = Source();
        var start = source.IndexOf("x:Name=\"MuteButton\"", StringComparison.Ordinal);
        var end = source.IndexOf('>', start);
        var element = source[start..end];
        Assert.Contains("Width=\"34\"", element);
        Assert.Contains("Height=\"30\"", element);
        Assert.Contains("BorderThickness=\"1\"", element);
        Assert.Contains("FocusVisualStyle=\"{x:Null}\"", element);
    }

    [Fact]
    public void ArrowKeys_UseTheSharedStepPathAndPreserveSliderTextAndSelectorInteraction()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "PlayerViewerHost.xaml.cs"));
        Assert.Contains("case Key.Left when _service is not null && PositionSlider.IsEnabled:", source);
        Assert.Contains("case Key.Right when _service is not null && PositionSlider.IsEnabled:", source);
        Assert.Contains("IsArrowKeyOwnedByFocusedControl", source);
        Assert.Contains("TextBoxBase or System.Windows.Controls.Slider", source);
        Assert.Contains("System.Windows.Controls.Primitives.Selector", source);
    }

    [Fact]
    public void ReviewRange_DrawsBoundariesWithoutPaintingDarkOutsideBands()
    {
        var xaml = Source();
        var indicatorStart = xaml.IndexOf("x:Name=\"ReviewRangeIndicator\"", StringComparison.Ordinal);
        Assert.True(indicatorStart >= 0, "ReviewRangeIndicator not found");
        var elementStart = xaml.LastIndexOf("<local:TrimRangeIndicator", indicatorStart, StringComparison.Ordinal);
        var element = xaml[elementStart..xaml.IndexOf("/>", indicatorStart, StringComparison.Ordinal)];
        Assert.DoesNotContain("ShowBoundaries=", element);

        var rendering = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "TrimRangeIndicator.cs"));
        Assert.DoesNotContain("DrawRectangle", rendering);
        Assert.DoesNotContain("DimOutside", rendering);
        Assert.Contains("if (ShowBoundaries)", rendering);

        var behavior = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "PlayerViewerHost.xaml.cs"));
        Assert.Contains("ReviewRangeIndicator.ShowBoundaries = presentation.ShowBoundaries;", behavior);
    }

    [Fact]
    public void RangeControls_KeepSetActionsSlimAndRenderSavedTimesAsLightweightLinksWithCompactClearButtons()
    {
        var xaml = Source();
        Assert.Contains("x:Key=\"RangeSetButton\"", xaml);
        Assert.Contains("<Setter Property=\"Padding\" Value=\"8,3\"/>", xaml);
        Assert.Contains("x:Key=\"RangeTimestampLink\"", xaml);
        Assert.Contains("x:Key=\"RangeClearButton\"", xaml);
        Assert.Contains("<Setter Property=\"Width\" Value=\"18\"/>", xaml);
        Assert.Contains("<Setter Property=\"VerticalAlignment\" Value=\"Center\"/>", xaml);
        Assert.Contains("<Trigger Property=\"Tag\" Value=\"Active\">", xaml);
        Assert.Contains("x:Name=\"InTimeButton\" Style=\"{StaticResource RangeTimestampLink}\"", xaml);
        Assert.Contains("x:Name=\"OutTimeButton\" Style=\"{StaticResource RangeTimestampLink}\"", xaml);
        Assert.Contains("x:Name=\"ClearInButton\" Style=\"{StaticResource RangeClearButton}\"", xaml);
        Assert.Contains("x:Name=\"ClearOutButton\" Style=\"{StaticResource RangeClearButton}\"", xaml);

        var behavior = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "PlayerViewerHost.xaml.cs"));
        Assert.Contains("InTimeButton.Content = FormatTimestamp(rangeIn);", behavior);
        Assert.Contains("OutTimeButton.Content = FormatTimestamp(rangeOut);", behavior);
        Assert.DoesNotContain("InTimeButton.Content = $\"In ", behavior);
        Assert.DoesNotContain("OutTimeButton.Content = $\"Out ", behavior);
        Assert.Contains("SetInButton.Tag = hasIn ? \"Active\" : null;", behavior);
        Assert.Contains("SetOutButton.Tag = hasOut ? \"Active\" : null;", behavior);
    }

    [Fact]
    public void ScreengrabControl_IsAnAccessibleStableCameraActionWithPoliteFeedback()
    {
        var xaml = Source();
        Assert.Contains("x:Name=\"ScreengrabButton\" Width=\"34\" Height=\"30\"", xaml);
        Assert.Contains("ToolTip=\"Save full-resolution frame as PNG\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Save full-resolution screengrab\"", xaml);
        Assert.Contains("x:Name=\"ScreengrabFeedbackText\"", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        Assert.Contains("x:Name=\"ScreengrabSuccessButton\" Width=\"26\" Height=\"26\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Screengrab saved. Open screengrab folder\"", xaml);
        Assert.Contains("Click=\"ScreengrabSuccess_Click\" Foreground=\"{StaticResource OrangeBrush}\"", xaml);
        Assert.DoesNotContain("Click=\"ScreengrabSuccess_Click\" Foreground=\"{StaticResource SuccessBrush}\"", xaml);

        var behavior = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "PlayerViewerHost.xaml.cs"));
        Assert.DoesNotContain("SetScreengrabFeedback($\"Saved {Path.GetFileName(result.Path)}\")", behavior);
        Assert.Contains("ScreengrabSuccessButton.Visibility = Visibility.Visible;", behavior);
        Assert.Contains("_folderLauncher.Open(_lastScreengrabDirectory);", behavior);
    }

    [Fact]
    public void ColorSurface_UsesPersistedStageSelectorsWithoutAnIndependentToggle()
    {
        var xaml = Source();
        var behavior = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "LightflowStudio", "PlayerViewerHost.xaml.cs"));

        Assert.Contains("x:Name=\"CameraLutCombo\"", xaml);
        Assert.Contains("x:Name=\"CreativeLutCombo\"", xaml);
        Assert.DoesNotContain("ColorToggleButton", xaml);
        Assert.DoesNotContain("SetColorEnabledAsync", behavior);
        Assert.Contains("case Key.C when _service is not null && _colorActive", behavior);
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
