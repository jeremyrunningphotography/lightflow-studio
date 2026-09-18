using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LightflowStudio;

internal sealed class InspectorMarkerCard(TimelineMarker marker) : INotifyPropertyChanged
{
    public TimelineMarker Marker { get; private set; } = marker;
    private string _draft = marker.Name;
    public string DraftName { get => _draft; set { _draft = value; Changed(nameof(DraftName)); } }
    private bool _isEditing;
    public bool IsEditing { get => _isEditing; set { _isEditing = value; Changed(nameof(IsEditing)); } }
    public void CancelRename() { DraftName = Marker.Name; IsEditing = false; }
    public bool IsDirty => DraftName != Marker.Name;
    public bool CanEdit { get; private set; } = true;
    public string Error { get; private set; } = "";
    public BitmapSource? Thumbnail { get; private set; }
    public string ThumbnailStatus { get; private set; } = "Loading marker frame…";
    public void SetThumbnail(BitmapSource? thumbnail)
    { Thumbnail = thumbnail; ThumbnailStatus = thumbnail is null ? "Marker frame unavailable" : "Marker frame"; Changed(nameof(Thumbnail)); Changed(nameof(ThumbnailStatus)); }
    public void SetBusy(bool busy, string error = "") { CanEdit = !busy; Error = error; Changed(nameof(CanEdit)); Changed(nameof(Error)); }
    public void Update(TimelineMarker value) { Marker = value; DraftName = value.Name; Changed(nameof(Marker)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string name) => PropertyChanged?.Invoke(this, new(name));
}

public partial class MediaInspectorView
{
    private IMarkerService? _markerCatalog;
    private IMarkerThumbnailService? _markerThumbnails;
    internal event EventHandler<Guid>? MarkersChanged;
    internal IReadOnlyList<InspectorMarkerCard> MarkerCards { get; private set; } = [];
    internal void InitializeMarkers(IMarkerService catalog, IMarkerThumbnailService thumbnails)
    { _markerCatalog = catalog; _markerThumbnails = thumbnails; }

    internal void PresentMarkers(IReadOnlyList<TimelineMarker> markers, CancellationToken token)
    {
        var generation = _generation;
        var existing = MarkerCards.ToDictionary(c => c.Marker.MarkerId);
        MarkerCards = markers.Select(marker =>
        {
            if (!existing.TryGetValue(marker.MarkerId, out var card)) return new InspectorMarkerCard(marker);
            if (!card.IsEditing && !card.IsDirty && card.CanEdit) card.Update(marker);
            return card;
        }).ToArray();
        InspectorMarkers.ItemsSource = MarkerCards;
        foreach (var card in MarkerCards) _ = LoadMarkerThumbnailAsync(card, generation, token);
    }
    private async Task LoadMarkerThumbnailAsync(InspectorMarkerCard card, long generation, CancellationToken token)
    {
        try
        {
            var path = _markerThumbnails is null ? null : await _markerThumbnails.GetAsync(card.Marker, token);
            var bitmap = path is null ? null : await Task.Run(() => PlayerViewerHost.DecodeImage(path), token);
            if (generation == _generation && !token.IsCancellationRequested) card.SetThumbnail(bitmap);
        }
        catch (OperationCanceledException) { }
        catch { if (generation == _generation) card.SetThumbnail(null); }
    }
    private async void InspectorMarkerCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Edit/thumbnail buttons and text editing own their input; the remaining card surface seeks.
        for (var current = e.OriginalSource as DependencyObject; current is not null && current != sender;
             current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase) return;
        if (_playerContext && sender is FrameworkElement { DataContext: InspectorMarkerCard card } && SeekMarker is not null)
        { e.Handled = true; await SeekMarker(card.Marker); }
    }
    private async void InspectorMarker_Click(object sender, RoutedEventArgs e)
    {
        if (_playerContext && sender is FrameworkElement { DataContext: InspectorMarkerCard card } && SeekMarker is not null)
            await SeekMarker(card.Marker);
    }
    internal async Task SaveMarkerNameAsync(InspectorMarkerCard card)
    {
        if (_markerCatalog is null || !card.CanEdit || !card.IsEditing || !IsCurrentMarker(card)) return;
        if (string.IsNullOrWhiteSpace(card.DraftName) || string.Equals(card.DraftName.Trim(), card.Marker.Name, StringComparison.Ordinal))
        { card.CancelRename(); return; }
        var marker = card.Marker; var name = card.DraftName;
        card.SetBusy(true);
        try
        {
            await _markerCatalog.RenameAsync(marker.MarkerId, marker.Revision, name);
            var updated = (await _markerCatalog.ListAsync(marker.AssetId)).FirstOrDefault(m => m.MarkerId == marker.MarkerId);
            if (updated is not null) card.Update(updated);
            card.IsEditing = false;
            MarkersChanged?.Invoke(this, marker.AssetId);
        }
        catch (MarkerConcurrencyException error)
        {
            card.CancelRename();
            await RefreshAsync();
            card.SetBusy(false, error.Message);
            StatusText.Text = error.Message;
        }
        catch (Exception error) { card.SetBusy(false, error.Message); return; }
        finally { if (!card.CanEdit) card.SetBusy(false); }
    }
    internal async Task ClearInspectorMarkerAsync(InspectorMarkerCard card)
    {
        if (_markerCatalog is null || !card.CanEdit || !IsCurrentMarker(card)) return;
        card.SetBusy(true);
        try
        {
            await _markerCatalog.DeleteAsync(card.Marker.MarkerId, card.Marker.Revision);
            MarkersChanged?.Invoke(this, card.Marker.AssetId);
            await RefreshAsync();
        }
        catch (Exception error) { card.SetBusy(false, error.Message); }
    }
    private bool IsCurrentMarker(InspectorMarkerCard card) => MarkerCards.Contains(card) && _context.Any(a => a.AssetId == card.Marker.AssetId);
    private async void InspectorMarkerName_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: InspectorMarkerCard card }) return;
        if (e.Key == Key.Enter) { e.Handled = true; await SaveMarkerNameAsync(card); }
        else if (e.Key == Key.Escape) { e.Handled = true; card.CancelRename(); }
    }
    private async void InspectorMarkerName_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    { if (sender is FrameworkElement { DataContext: InspectorMarkerCard card }) await SaveMarkerNameAsync(card); }
    private void InspectorMarkerRename_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: InspectorMarkerCard card } button)
            BeginMarkerRename(card, (DependencyObject)button.Parent);
    }
    internal void BeginMarkerRename(InspectorMarkerCard card, DependencyObject owner)
    {
        if (!card.CanEdit || !IsCurrentMarker(card)) return;
        card.DraftName = card.Marker.Name;
        card.SetBusy(false);
        card.IsEditing = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (card.IsEditing && IsCurrentMarker(card) && FindMarkerEditor(owner) is { } editor)
            { editor.Focus(); editor.SelectAll(); }
        }, System.Windows.Threading.DispatcherPriority.Input);
    }
    private void InspectorMarker_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: InspectorMarkerCard card } owner) return;
        owner.ContextMenu = MarkerMenus.Create(owner, () =>
        {
            BeginMarkerRename(card, owner);
        }, () => ClearInspectorMarkerAsync(card));
        // A name editor otherwise supplies its own stock text menu. The entire card shares marker actions.
        e.Handled = true;
        owner.ContextMenu.PlacementTarget = owner;
        owner.ContextMenu.IsOpen = true;
    }
    private static System.Windows.Controls.TextBox? FindMarkerEditor(DependencyObject owner)
    {
        if (owner is System.Windows.Controls.TextBox editor) return editor;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(owner); i++)
            if (FindMarkerEditor(VisualTreeHelper.GetChild(owner, i)) is { } child) return child;
        return null;
    }
}
