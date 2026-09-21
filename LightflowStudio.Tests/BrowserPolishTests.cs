using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class BrowserPolishTests
{
    [Theory]
    [InlineData(Orientation.Vertical, 1)]
    [InlineData(Orientation.Horizontal, 1)]
    [InlineData(Orientation.Vertical, 1.5)]
    [InlineData(Orientation.Horizontal, 2)]
    public Task SharedScrollbar_MinimumAndProportionalThumbPreserveFullDragRange(Orientation orientation, double scale) => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var vertical = orientation == Orientation.Vertical;
        var bar = new ScrollBar { Orientation = orientation, Minimum = 0, Maximum = 1000000,
            ViewportSize = 100, Width = vertical ? 12 : 300, Height = vertical ? 300 : 12,
            Style = (Style)Application.Current.FindResource(typeof(ScrollBar)), LayoutTransform = new ScaleTransform(scale, scale) };
        Layout(bar, 400 * scale, 400 * scale);
        var track = (Track)bar.Template.FindName("PART_Track", bar);
        Assert.Equal(28, vertical ? track.Thumb.ActualHeight : track.Thumb.ActualWidth, 4);
        var travel = 300 - 28;
        Assert.Equal(bar.Maximum, Math.Abs(track.ValueFromDistance(vertical ? 0 : travel, vertical ? travel : 0)), 3);
        bar.Value = bar.Maximum;
        bar.UpdateLayout();
        Assert.Equal(bar.Maximum, track.Value);
        bar.Maximum = 100;
        bar.Value = 0;
        Layout(bar, 400 * scale, 400 * scale);
        Assert.Equal(150, vertical ? track.Thumb.ActualHeight : track.Thumb.ActualWidth, 4);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public Task BrowserSearch_ContentHostFitsTextAndCaretAtToolbarHeight(double scale) => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var source = XDocument.Load(Path.Combine(Repository(), "LightflowStudio", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var search = source.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "BrowserSearchBox");
        var height = double.Parse(source.Descendants().Single(element => (string?)element.Attribute(x + "Key") == "BrowserRow2ControlHeight").Value,
            System.Globalization.CultureInfo.InvariantCulture);
        var box = new TextBox { Text = "AgjpQ 012345", Width = 145, Padding = (Thickness)new ThicknessConverter().ConvertFromString(search.Attribute("Padding")!.Value)!,
            Margin = new Thickness(0), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.FindResource(typeof(TextBox)) };
        var host = new Border { Height = height, Width = 190, BorderThickness = new Thickness(1), Child = box,
            LayoutTransform = new ScaleTransform(scale, scale) };
        Layout(host, 250 * scale, height * scale);
        var content = (ScrollViewer)box.Template.FindName("PART_ContentHost", box);
        Assert.True(content.ViewportHeight >= box.FontSize, $"Viewport {content.ViewportHeight}, font {box.FontSize}");
        var caret = box.GetRectFromCharacterIndex(2);
        Assert.True(caret.Height >= box.FontSize);
        Assert.True(caret.Bottom <= box.ActualHeight + 0.5);
        box.SelectAll();
        Assert.Equal(box.Text.Length, box.SelectionLength);
        var size = box.RenderSize;
        box.Text = "";
        Layout(host, 250 * scale, height * scale);
        Assert.Equal(size.Height, box.ActualHeight);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DropTarget_ChangesVisibleChromeWithoutSelectionOrGeometryChanges() => StaDispatcher.RunAsync(() =>
    {
        TestWpfApplication.EnsureLoaded();
        var node = new BrowserTreeNode("Folder", @"C:\Folder");
        var item = new TreeViewItem { Header = new TextBlock { Text = "Folder" }, DataContext = node,
            Style = (Style)Application.Current.FindResource("BrowserTreeItemStyle") };
        item.SetBinding(TreeDropPresentation.IsValidProperty, new System.Windows.Data.Binding(nameof(BrowserTreeNode.IsFileDropTarget)));
        Layout(item, 250, 100);
        var chrome = (Border)item.Template.FindName("HeaderChrome", item);
        var size = chrome.RenderSize;
        var normal = chrome.Background.ToString();
        node.IsFileDropTarget = true;
        Layout(item, 250, 100);
        Assert.True(TreeDropPresentation.GetIsValid(item));
        Assert.Same(item, chrome.TemplatedParent);
        Assert.NotEqual(normal, chrome.Background.ToString());
        Assert.False(item.IsSelected);
        Assert.Equal(size, chrome.RenderSize);
        node.IsFileDropTarget = false;
        Layout(item, 250, 100);
        Assert.Equal(normal, chrome.Background.ToString());
        item.IsSelected = true;
        Layout(item, 250, 100);
        var selected = chrome.Background.ToString();
        node.IsFileDropTarget = true;
        Layout(item, 250, 100);
        Assert.NotEqual(selected, chrome.Background.ToString());
        Assert.True(item.IsSelected);
        Assert.Equal(size, chrome.RenderSize);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("decoder raw stderr /secret/path", (int)PreviewFailureReason.Unknown)]
    [InlineData("Output file is empty", (int)PreviewFailureReason.NoVideoFrame)]
    [InlineData("Permission denied", (int)PreviewFailureReason.SourceUnreadable)]
    [InlineData("Decoder not found", (int)PreviewFailureReason.CodecUnavailable)]
    public void DecoderClassification_OnlyReturnsAllowlistedPresentation(string diagnostic, int expectedValue)
    {
        var expected = (PreviewFailureReason)expectedValue;
        Assert.Equal(expected, PreviewFailure.ClassifyDecoder(diagnostic));
        Assert.DoesNotContain(diagnostic, PreviewFailure.Message(expected), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Preview could not be generated.", PreviewFailure.Message((PreviewFailureReason)999));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void DeletePreflight_InspectsEntireSelectionAndKeepsItIntact(int unavailable)
    {
        var sources = Enumerable.Range(0, 3).Select(index => new FileOperationSource(null, $@"C:\media\{index}.mp4")).ToArray();
        var calls = 0;
        var result = DeletePreflight.Inspect(sources, _ => calls++ >= unavailable);
        Assert.Equal(3, calls);
        Assert.Equal(unavailable, result.UnrecoverableCount);
        Assert.Equal(unavailable > 0, result.RequiresPermanentDelete);
        Assert.Equal(sources, result.Sources);
    }

    [Fact]
    public void ShellGuard_ProbeAlwaysVetoesMutationAndExecutionVetoesPermanentDelete()
    {
        var probe = new RecycleProgressSink(true);
        Assert.True(probe.PreDeleteItem(0x80, IntPtr.Zero) < 0);
        Assert.True(probe.RecycleProposed);
        var execute = new RecycleProgressSink(false);
        Assert.True(execute.PreDeleteItem(0, IntPtr.Zero) < 0);
        Assert.False(execute.RecycleProposed);
        Assert.Equal(0, execute.PreDeleteItem(0x80, IntPtr.Zero));
        execute.PostDeleteItem(0x80, IntPtr.Zero, 0, IntPtr.Zero);
        Assert.False(execute.Completed);
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(20, false, false)]
    [InlineData(1, true, false)]
    [InlineData(20, true, false)]
    [InlineData(1, true, true)]
    public async Task PermanentFallback_OnlyExecutesCompleteSelectionAfterExplicitConfirmation(int count, bool accept, bool explicitPermanent)
    {
        var sources = Enumerable.Range(0, count).Select(index => new FileOperationSource(null, $@"C:\media\{index}.mp4")).ToArray();
        var inspected = 0;
        var executions = 0;
        await BrowserDeleteOperation.RunAsync(sources, explicitPermanent,
            _ => inspected++ != 0, dialog =>
            {
                Assert.Equal(explicitPermanent ? 0 : count, inspected);
                Assert.Contains("permanent", dialog.Action, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Recycle Bin", dialog.Warning);
                if (!explicitPermanent) Assert.Contains($"ALL {count}", dialog.Warning);
                Assert.Equal(0, executions);
                return accept;
            }, (kind, captured) =>
            {
                executions++;
                Assert.Equal(FileOperationKind.PermanentDelete, kind);
                Assert.Equal(sources, captured);
                var plan = FileOperationPlanner.Plan(kind, captured, null);
                Assert.Equal(count > 8 ? FileOperationExecution.Job : FileOperationExecution.Direct, plan.Execution);
                return Task.CompletedTask;
            });
        Assert.Equal(accept ? 1 : 0, executions);
    }

    [Fact]
    public async Task RecyclableSelection_KeepsNormalDeleteAndWaitsForConfirmation()
    {
        var executed = false;
        await BrowserDeleteOperation.RunAsync([new(null, @"C:\media\clip.mp4")], false, _ => true,
            dialog => { Assert.Equal("Move to Recycle Bin", dialog.Action); Assert.False(executed); return true; },
            (kind, _) => { Assert.Equal(FileOperationKind.Recycle, kind); executed = true; return Task.CompletedTask; });
        Assert.True(executed);
    }

    [Fact]
    public void NativeShellPreflight_LeavesLocalFileAndFolderUntouched()
    {
        var directory = Path.Combine(Repository(), "artifacts", "polish-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "probe.txt");
        File.WriteAllText(file, "untouched");
        try
        {
            Assert.True(WindowsRecycleOperation.Probe(file));
            Assert.True(WindowsRecycleOperation.Probe(directory));
            Assert.Equal("untouched", File.ReadAllText(file));
            Assert.True(Directory.Exists(directory));
            Assert.False(WindowsRecycleCapability.CanRecycle(@"\\unavailable.invalid\share\file.mov"));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
    }
    private static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        throw new InvalidOperationException("Repository not found.");
    }
}
