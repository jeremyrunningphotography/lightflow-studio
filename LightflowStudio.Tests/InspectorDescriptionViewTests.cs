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
        var store = new TestDescriptionStore { Values = new() { [a] = new(a, "First"), [b] = new(b, "Second") } };
        using var view = new MediaInspectorView();
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
            var combos = Descendants<ComboBox>(view.DescriptionSection).ToArray();
            var text = Descendants<TextBox>(view.DescriptionSection).ToArray();
            Assert.Equal(5, combos.Length); Assert.Equal(5, text.Length);
            Assert.All(combos, combo => Assert.Equal(3, combo.Items.Count));
            Assert.True(text[0].IsReadOnly); Assert.Equal("", text[0].Text);
            combos[0].SelectedValue = DescriptionEditOperation.Set;
            Assert.False(text[0].IsReadOnly);
            text[0].Text = "作者 🎬";
            combos[1].SelectedValue = DescriptionEditOperation.Set;
            Assert.True(text[1].AcceptsReturn); Assert.False(text[0].AcceptsReturn);
            text[1].Text = "First line\r\n第二行";
            combos[2].SelectedValue = DescriptionEditOperation.Clear;
            Assert.True(editor.CanApply);
            view.SetContext(selection, false, force: true);
            await view.RefreshAsync();
            Assert.Equal("作者 🎬", editor.Fields[0].Text);
            view.Visibility = Visibility.Collapsed; view.Visibility = Visibility.Visible;
            Assert.True(editor.HasDraft);
            var apply = Descendants<Button>(view.DescriptionSection).Single(button => Equals(button.Content, "Apply to 2 assets"));
            Assert.True(apply.IsEnabled);
            apply.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("作者 🎬", store.AppliedPatch!.Values[AssetDescriptionField.Title]);
            Assert.Equal("First line\r\n第二行", store.AppliedPatch.Values[AssetDescriptionField.Description]);
            Assert.Null(store.AppliedPatch.Values[AssetDescriptionField.Notes]);
            Assert.False(editor.HasDraft);
            window.Width = 260; view.UpdateLayout();
            foreach (var control in Descendants<ComboBox>(view.DescriptionSection).Cast<FrameworkElement>().Concat(Descendants<TextBox>(view.DescriptionSection)))
            {
                var bounds = control.TransformToAncestor(view).TransformBounds(new Rect(control.RenderSize));
                Assert.True(bounds.Left >= 0 && bounds.Right <= view.ActualWidth + 1, $"Editor clips horizontally: {bounds}");
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
