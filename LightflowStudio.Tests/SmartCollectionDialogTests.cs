using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class SmartCollectionDialogTests
{
    [Fact]
    public Task PresetCustomAndDurationOperatorsRemainSemanticAndSavedStateAlternativesStayExplicit() => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var rate = new BrowserFilterRowEditor(BrowserFilterField.FrameRate, [], [], _ => true, () => { }, () => { });
        var ratePanel = (StackPanel)((ContentControl)rate.Children[2]).Content;
        var rateLine = ratePanel.Children.OfType<Grid>().First();
        var selector = rateLine.Children.OfType<ComboBox>().Single();
        selector.SelectedIndex = 4; Assert.Equal(29.97, Assert.Single(rate.Predicates).NumberValue);
        selector.SelectedIndex = selector.Items.Count - 1;
        var custom = rateLine.Children.OfType<Grid>().Single();
        Assert.Equal(Visibility.Visible, custom.Visibility); Assert.Equal(Visibility.Collapsed, selector.Visibility);
        custom.Children.OfType<TextBox>().Single().Text = "120";
        Assert.Equal(120, Assert.Single(rate.Predicates).NumberValue);
        custom.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(Visibility.Visible, selector.Visibility); Assert.Equal(Visibility.Collapsed, custom.Visibility);
        Assert.Equal(120, Assert.Single(rate.Predicates).NumberValue);

        var duration = new BrowserFilterRowEditor(BrowserFilterField.Duration,
            [BrowserFilterPredicate.ForMinimum(BrowserFilterField.Duration, 30)], [], _ => true, () => { }, () => { });
        var durationPanel = (StackPanel)((ContentControl)duration.Children[2]).Content;
        var durationLine = (Grid)durationPanel.Children.OfType<StackPanel>().First().Children[0];
        var comparison = durationLine.Children.OfType<ComboBox>().First(); Assert.Equal(2, comparison.Items.Count);
        comparison.SelectedIndex = 1;
        Assert.Equal(BrowserNumberComparison.LessThanOrEqual, Assert.Single(duration.Predicates).Comparison);
        durationLine.Children.OfType<TextBox>().Single().Text = "01:15";
        Assert.Equal(75, Assert.Single(duration.Predicates).NumberValue);

        var saved = new[] { BrowserFilterPredicate.ForState(BrowserFilterField.SubclipState, true), BrowserFilterPredicate.ForState(BrowserFilterField.SubclipState, false) };
        var state = new BrowserFilterRowEditor(BrowserFilterField.SubclipState, saved, [], _ => true, () => { }, () => { });
        var stateLines = ((StackPanel)((ContentControl)state.Children[1]).Content).Children.OfType<StackPanel>().ToArray();
        Assert.Equal(2, stateLines.Length); Assert.Equal(saved, state.Predicates);
        Assert.All(stateLines, line => Assert.Equal(2, line.Children.OfType<ComboBox>().Single().Items.Count));
        Assert.Contains(stateLines[1].Children.OfType<TextBlock>(), label => label.Text == "or" && label.Visibility == Visibility.Visible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task StructuredInputsWorkWithoutObservedValuesAndStateHasNoThirdControl() => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var resolution = new BrowserFilterRowEditor(BrowserFilterField.Resolution, [], [], _ => true, () => { }, () => { });
        var value = (StackPanel)((ContentControl)resolution.Children[2]).Content;
        var presetLine = value.Children.OfType<Grid>().First();
        var preset = presetLine.Children.OfType<ComboBox>().Single();
        Assert.True(preset.Items.Count >= 7);
        preset.SelectedIndex = preset.Items.Count - 1; // Custom replaces the preset selector in the same value area.
        Assert.Equal(Visibility.Collapsed, preset.Visibility);
        var boxes = presetLine.Children.OfType<Grid>().Single().Children.OfType<TextBox>().ToArray();
        boxes[0].Text = "3840"; boxes[1].Text = "2160";
        Assert.True(resolution.IsValid); Assert.Equal(BrowserFilterPredicate.ForResolution(3840, 2160), Assert.Single(resolution.Predicates));
        boxes[0].Text = "invalid"; resolution.RefreshValues([]); Assert.False(resolution.IsValid); Assert.Equal("invalid", boxes[0].Text);
        boxes[0].Text = "1920"; Assert.True(resolution.IsValid);
        var fieldChoices = (ComboBox)resolution.Children[0];
        Assert.DoesNotContain(fieldChoices.Items.Cast<BrowserFilterDescriptor>(), d => d.Field == BrowserFilterField.Camera);

        var state = new BrowserFilterRowEditor(BrowserFilterField.ReviewRangeState,
            [BrowserFilterPredicate.ForState(BrowserFilterField.ReviewRangeState, true)], [], _ => true, () => { }, () => { });
        Assert.Equal(Visibility.Collapsed, state.Children[2].Visibility);
        var operations = (StackPanel)((ContentControl)state.Children[1]).Content;
        var operation = operations.Children.OfType<StackPanel>().Single().Children.OfType<ComboBox>().Single();
        operation.SelectedIndex = 1; Assert.False(Assert.Single(state.Predicates).BooleanValue);
        Assert.Equal(2, operation.Items.Count);
        Assert.Equal(2, Grid.GetColumnSpan(state.Children[1]));
        return Task.CompletedTask;
    });

    [Fact]
    public Task ValueEditorGalleryUsesDarkControlsAndNormalTextHintAlignment() => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var query = new BrowserQueryIntent { Filters = [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.StillImage),
            BrowserFilterPredicate.ForMediaType(MediaTypeCategory.RawImage), BrowserFilterPredicate.ForResolution(3840, 2160),
            BrowserFilterPredicate.ForFrameRate(29.97), BrowserFilterPredicate.ForMinimum(BrowserFilterField.Duration, 30),
            BrowserFilterPredicate.ForState(BrowserFilterField.ReviewRangeState, true), BrowserFilterPredicate.ForText(BrowserFilterField.FileOrPath, "stabilized")] };
        var dialog = new SmartCollectionDialog(new NoLocations(), [], [new(null, "Top level")], [], "Editor examples", null,
            new(SmartCollectionSourceKind.Folder, Guid.NewGuid(), "Media"), query, true);
        var content = (FrameworkElement)dialog.Content;
        content.Measure(new Size(742, double.PositiveInfinity)); content.Arrange(new Rect(new Point(), new Size(742, content.DesiredSize.Height))); content.UpdateLayout();
        var rows = ((StackPanel)dialog.FindName("FilterRows")).Children.OfType<BrowserFilterRowEditor>().ToArray();
        var text = (TextBox)((ContentControl)rows.Last().Children[2]).Content;
        text.Text = ""; text.ApplyTemplate(); content.UpdateLayout();
        var hint = (TextBlock)text.Template.FindName("InputHint", text);
        Assert.Equal(text.Padding, hint.Margin);
        Assert.Equal(text.VerticalContentAlignment, hint.VerticalAlignment);
        Assert.Equal("Text to match", hint.Text);
        Assert.Equal(((TextBox)dialog.FindName("NameText")).Style, text.Style);
        var mediaHost = (Grid)((ContentControl)rows[0].Children[2]).Content;
        var toggle = mediaHost.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Single();
        Assert.Same(Application.Current.FindResource("FilterValueToggleStyle"), toggle.Style);
        Assert.All(rows, row => Assert.Equal(24, row.Children.OfType<Button>().Single().Width));
        Render(content, "smart-value-editor-gallery.png");
        // Render the popup content without opening a native popup/window.
        var popup = mediaHost.Children.OfType<System.Windows.Controls.Primitives.Popup>().Single();
        var popupContent = (FrameworkElement)popup.Child; popup.Child = null;
        popupContent.Measure(new Size(280, double.PositiveInfinity)); popupContent.Arrange(new Rect(new Point(), new Size(280, popupContent.DesiredSize.Height))); popupContent.UpdateLayout();
        Render(popupContent, "smart-value-popup.png");
        return Task.CompletedTask;
    });

    private static void Render(FrameworkElement content, string filename)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Root(), ".task-notes", filename)); encoder.Save(stream);
    }
    [Fact]
    public Task BlankCreationAndInlineAlternativesStayEditable() => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var blank = new SmartCollectionDialog(new NoLocations(), [], [new(null, "Top level")], [], "", null, null, new(), false);
        var rows = (StackPanel)blank.FindName("FilterRows");
        Assert.Empty(rows.Children); Assert.Empty(blank.Query.Filters);
        ((Button)blank.FindName("AddFilterButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var row = Assert.IsType<BrowserFilterRowEditor>(Assert.Single(rows.Children.Cast<object>()));
        Assert.False(row.IsValid); Assert.Empty(blank.Query.Filters);
        row.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(rows.Children);

        var query = BrowserQueryIntent.Capture(new() { SearchText = "ceremony", Filters =
            [BrowserFilterPredicate.ForMediaType(MediaTypeCategory.StillImage), BrowserFilterPredicate.ForMediaType(MediaTypeCategory.RawImage),
                BrowserFilterPredicate.ForText(BrowserFilterField.Flag, "Picked")] });
        var dialog = new SmartCollectionDialog(new NoLocations(), [], [new(null, "Top level")], [], "Picks", null, null, query, true);
        var savedRows = ((StackPanel)dialog.FindName("FilterRows")).Children.OfType<BrowserFilterRowEditor>().ToArray();
        Assert.Equal(3, savedRows.Length); Assert.Equal(2, savedRows[0].Predicates.Count);
        Assert.All(savedRows, r => Assert.True(r.IsValid));
        var host = (ContentControl)savedRows[0].Children[2];
        var choices = (Grid)host.Content;
        var popup = choices.Children.OfType<System.Windows.Controls.Primitives.Popup>().Single();
        var checks = (StackPanel)((ScrollViewer)((Border)popup.Child).Child).Content;
        var raw = checks.Children.OfType<CheckBox>().Single(c => (string)c.Content == "RAW");
        raw.IsChecked = false; raw.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        Assert.Single(savedRows[0].Predicates);
        Assert.DoesNotContain(dialog.Query.Filters, p => p.MediaTypeValue == MediaTypeCategory.RawImage);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DateAlternativesRenderAndEditWithoutLosingSavedRanges() => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var dates = new[] { BrowserFilterPredicate.ForDateRange(new(2026, 1, 1), new(2026, 1, 31)),
            BrowserFilterPredicate.ForDateRange(new(2026, 3, 1), new(2026, 3, 31)) };
        var row = new BrowserFilterRowEditor(BrowserFilterField.CaptureDate, dates, [], _ => true, () => { }, () => { });
        var host = new Border { Background = (Brush)Application.Current.FindResource("CardBrush"), Padding = new Thickness(12), Child = row };
        host.Measure(new Size(700, double.PositiveInfinity)); host.Arrange(new Rect(new Point(), new Size(700, host.DesiredSize.Height))); host.UpdateLayout();
        Assert.Equal(dates, row.Predicates);
        var panel = (StackPanel)((ContentControl)row.Children[2]).Content;
        var firstRange = panel.Children.OfType<StackPanel>().First();
        firstRange.Children.OfType<DatePicker>().First().SelectedDate = new(2026, 1, 2);
        Assert.Equal(new DateTime(2026, 1, 2), row.Predicates[0].DateFrom);
        Assert.Equal(dates[1], row.Predicates[1]);
        var bitmap = new RenderTargetBitmap(700, (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Root(), ".task-notes", "smart-date-rows.png")); encoder.Save(stream);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DialogPrepopulationAndOffscreenLayoutUseSharedResourcesWithoutOpeningAWindow() => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var root = Guid.NewGuid(); var parent = Guid.NewGuid(); var collection = Guid.NewGuid();
        var source = new SmartCollectionSource(SmartCollectionSourceKind.Folder, root, "Projects/2026", IncludeSubfolders: true);
        var query = new BrowserQueryIntent { SearchText = "portrait", Filters = [BrowserFilterPredicate.ForRating(BrowserNumberComparison.GreaterThanOrEqual, 4)] };
        var dialog = new SmartCollectionDialog(new NoLocations(), [], [new(null, "Top level"), new(parent, "Portfolio")],
            [(collection, "Favorites")], "Portrait picks", parent, source, query, true);
        Assert.Equal(source, dialog.Source); Assert.Equal(parent, dialog.ParentSetId);
        Assert.Equal(query.Serialize(), dialog.Query.Serialize());
        Assert.Equal("Portrait picks", dialog.CollectionName);
        var content = (FrameworkElement)dialog.Content;
        content.Measure(new Size(742, double.PositiveInfinity));
        content.Arrange(new Rect(new Point(), new Size(742, content.DesiredSize.Height))); content.UpdateLayout();
        var match = VisualChildren((ContentControl)dialog.FindName("MatchHost")).OfType<ComboBox>().Single();
        match.SelectedValue = BrowserMatchMode.Any;
        Assert.Equal(BrowserMatchMode.Any, dialog.Query.MatchMode);
        match.SelectedValue = BrowserMatchMode.All;
        Assert.InRange(content.ActualHeight, 300, 780);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var output = Path.Combine(Root(), ".task-notes", "smart-collection-dialog.png"); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var stream = File.Create(output)) encoder.Save(stream);
        var type = (ComboBox)dialog.FindName("SourceKindCombo"); type.SelectedIndex = 1;
        var choices = (ComboBox)dialog.FindName("SourceCollectionCombo"); choices.SelectedIndex = 0;
        Assert.Equal(collection, dialog.Source!.CollectionId); Assert.False(dialog.Source.IncludeSubfolders);
        type.SelectedIndex = -1; Assert.Null(dialog.Source);
        Assert.Equal(Visibility.Collapsed, ((StackPanel)dialog.FindName("FolderPanel")).Visibility);
        return Task.CompletedTask;
    });

    [Fact]
    public void CreationEntryPointsAndDistinctIconArePresentInExistingBrowserSurfaces()
    {
        var document = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml"));
        var x = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var creations = document.Descendants().Where(e => (string?)e.Attribute("Click") == "BrowserCreateSmartCollection_Click").ToArray();
        Assert.Contains(creations, e => e.Name.LocalName == "Button");
        Assert.Contains(creations, e => e.Name.LocalName == "MenuItem");
        var blankCreations = document.Descendants().Where(e => (string?)e.Attribute("Click") == "BrowserNewSmartCollection_Click").ToArray();
        Assert.Contains(blankCreations, e => e.Name.LocalName == "Button");
        Assert.Contains(blankCreations, e => (string?)e.Attribute(x + "Name") == "BrowserSetNewSmartMenu");
        Assert.Contains(document.Descendants(), e => (string?)e.Attribute(x + "Name") == "BrowserSetNewCollectionMenu" &&
            (string?)e.Attribute("Click") == "BrowserNewCollection_Click");
        Assert.Contains(document.Descendants(), e => (string?)e.Attribute("Binding") == "{Binding IsSmartCollection}");
        var grid = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "BrowserGridRows");
        Assert.Equal("True", (string?)grid.Attribute("VirtualizingPanel.IsVirtualizing"));
    }

    private sealed class NoLocations : IBrowserLocationResolver
    {
        public Task<BrowserLocationResolution> ResolveAsync(string absoluteFolder, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in VisualChildren(child)) yield return descendant;
        }
    }
    private static string Root()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "AGENTS.md"))) path = path.Parent;
        return path?.FullName ?? throw new InvalidOperationException("Repository not found.");
    }
}
