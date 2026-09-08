using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class InspectorDescriptionViewTests
{
    [Fact]
    public Task RealInspectorBindings_MixedSetClearMultiline_RefreshVisibilityAndNarrowLayout() => StaDispatcher.RunAsync(async () =>
    {
        TestWpfApplication.EnsureLoaded();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var store = new TestDescriptionStore { Values = new() { [a] = new(a, "First", Notes: "common"), [b] = new(b, "Second", Notes: "common") } };
        using var view = new MediaInspectorView();
        var requests = new List<DescriptionConfirmation>();
        view.ConfirmDescriptions = request => { requests.Add(request); return true; };
        view.Initialize(() => new MediaInspectorService(null, new Classifications(), Path.GetTempPath()), store);
        var window = new Window { Content = view, Width = 320, Height = 760, Left = -32000, Top = -32000,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        try
        {
            window.Show();
            InspectorAsset[] selection = [new(a, "a.jpg", "a.jpg", MediaPresentationKind.Image), new(b, "b.jpg", "b.jpg", MediaPresentationKind.Image)];
            view.SetContext(selection, false);
            await view.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            view.UpdateLayout();
            var editor = Assert.IsType<InspectorDescriptionEditor>(view.DescriptionSection.DataContext);
            Assert.Equal(5, editor.Fields.Count);
            Assert.Empty(Descendants<ComboBox>(view.DescriptionSection));
            var text = Descendants<TextBox>(view.DescriptionSection).ToArray();
            Assert.Equal(5, text.Length);
            Assert.False(text[0].IsReadOnly);
            text[0].Focus(); text[0].SelectAll();
            Assert.False(editor.HasDraft);
            await editor.ApplyAsync(); Assert.Empty(requests); Assert.Null(store.AppliedPatch);
            text[0].Text = "作者 🎬";
            Assert.True(text[1].AcceptsReturn); Assert.False(text[0].AcceptsReturn);
            text[1].Text = "First line\r\n第二行";
            text[2].Text = "";
            Assert.True(editor.CanApply);
            view.SetContext(selection, false, force: true);
            await view.RefreshAsync();
            Assert.Equal("作者 🎬", editor.Fields[0].Text);
            view.Visibility = Visibility.Collapsed; view.Visibility = Visibility.Visible;
            Assert.True(editor.HasDraft);
            var apply = Descendants<Button>(view.DescriptionSection).Single(button => Equals(button.Content, "Apply to 2 assets"));
            Assert.True(apply.IsEnabled);
            apply.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(Assert.Single(requests).IsApply);
            Assert.Equal("作者 🎬", store.AppliedPatch!.Values[AssetDescriptionField.Title]);
            Assert.Equal("First line\r\n第二行", store.AppliedPatch.Values[AssetDescriptionField.Description]);
            Assert.Null(store.AppliedPatch.Values[AssetDescriptionField.Notes]);
            Assert.False(editor.HasDraft);
            Assert.Contains("Saved", view.DescriptionStatus.Text);
            Assert.DoesNotContain(view.DescriptionStatus, Descendants<TextBlock>(view.DescriptionSection));
            foreach (var width in new[] { 260d, 600d })
            {
                window.Width = width; view.UpdateLayout();
                var editors = Descendants<TextBox>(view.DescriptionSection).ToArray();
                foreach (var control in editors)
                {
                    control.Text = control.AcceptsReturn ? "Top line\r\nNext line" : "Agj 作者 Title with a long line " + new string('W', 50);
                    control.UpdateLayout();
                    var bounds = control.TransformToAncestor(view).TransformBounds(new Rect(control.RenderSize));
                    Assert.True(bounds.Left >= 0 && bounds.Right <= view.ActualWidth + 1, $"Editor clips horizontally: {bounds}");
                    Assert.Equal(VerticalAlignment.Top, control.VerticalContentAlignment);
                    var content = Assert.IsType<ScrollViewer>(control.Template.FindName("PART_ContentHost", control));
                    var firstCharacter = control.GetRectFromCharacterIndex(0);
                    var hostBounds = content.TransformToAncestor(control).TransformBounds(new Rect(content.RenderSize));
                    Assert.True(firstCharacter.Top >= hostBounds.Top - 1 && firstCharacter.Bottom <= hostBounds.Bottom + 1,
                        $"Text is vertically clipped: {firstCharacter} inside {hostBounds}");
                    Assert.InRange(firstCharacter.Top - hostBounds.Top, 0, control.Padding.Top + 1);
                    if (!control.AcceptsReturn)
                    {
                        Assert.True(double.IsNaN(control.Height));
                        Assert.Equal(TextWrapping.NoWrap, control.TextWrapping);
                        control.Focus(); control.CaretIndex = control.Text.Length;
                        control.ScrollToHorizontalOffset(control.ExtentWidth); control.UpdateLayout();
                        Assert.True(control.HorizontalOffset > 0);
                    }
                }
            }
        }
        finally { window.Close(); }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private sealed class Classifications : IAssetClassificationStore
    {
        public Task<IReadOnlyDictionary<Guid, AssetClassification>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, AssetClassification>>(ids.ToDictionary(id => id, AssetClassification.Empty));
        public Task SaveAsync(AssetClassification value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
