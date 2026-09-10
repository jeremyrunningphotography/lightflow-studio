using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LightflowStudio;

public partial class PlayerViewerHost
{
    private PlayerReviewSet? _reviewSet;
    private Func<PlayerViewerAsset, CancellationToken, Task<MediaPathResolution>>? _reviewResolver;
    private CancellationTokenSource? _reviewRequest;
    private bool _syncingFilmstrip;
    private bool _filmstripVisible = true;
    internal PlayerReviewSet? ReviewSet => _reviewSet;
    internal event EventHandler? FilmstripVisibilityChanged;
    internal bool FilmstripVisible
    {
        get => _filmstripVisible;
        set
        {
            _filmstripVisible = value;
            Filmstrip.Visibility = value && !IsFullscreen ? Visibility.Visible : Visibility.Collapsed;
            FilmstripToggle.Content = value ? "Hide filmstrip" : "Show filmstrip";
            if (value) RevealReviewItem();
            FilmstripVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void SetReviewSet(PlayerReviewSet reviewSet,
        Func<PlayerViewerAsset, CancellationToken, Task<MediaPathResolution>> resolver)
    {
        CancelReviewRequest();
        _reviewSet = reviewSet;
        _reviewResolver = resolver;
        _syncingFilmstrip = true;
        Filmstrip.ItemsSource = reviewSet.Items;
        _syncingFilmstrip = false;
        SyncReviewNavigation();
    }

    private void CancelReviewRequest()
    {
        _reviewRequest?.Cancel();
        _reviewRequest?.Dispose();
        _reviewRequest = null;
    }

    internal Task TraverseReviewAsync(int direction)
    {
        if (_reviewSet is not { } review || (direction < 0 ? !review.CanPrevious : !review.CanNext))
            return Task.CompletedTask;
        return SelectReviewAssetAsync(review.Items[review.CurrentIndex + Math.Sign(direction)].Asset.AssetId!.Value);
    }

    internal async Task SelectReviewAssetAsync(Guid assetId)
    {
        if (_reviewSet is not { } review || _reviewResolver is not { } resolve ||
            ContextChanging?.Invoke() == false || !review.Select(assetId)) { SyncReviewNavigation(); return; }
        CancelReviewRequest();
        var request = _reviewRequest = new CancellationTokenSource();
        var token = request.Token;
        // Invalidate a still-opening source before even awaiting destination path resolution.
        _sourceOpenCts?.Cancel();
        ++_generation;
        SyncReviewNavigation();
        var asset = review.Items[review.CurrentIndex].Asset;
        try
        {
            await PauseIfPlayingAsync();
            token.ThrowIfCancellationRequested();
            MediaPathResolution resolution;
            try { resolution = await resolve(asset, token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            { resolution = new(asset.RootId, asset.RelativePath, asset.Key, null, MediaRootAvailability.Unavailable, false, exception.Message); }
            token.ThrowIfCancellationRequested();
            await OpenAsync(asset, resolution, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!token.IsCancellationRequested) SetStatus(exception.Message); }
    }

    private void SyncReviewNavigation()
    {
        PreviousAssetButton.IsEnabled = _reviewSet?.CanPrevious == true;
        NextAssetButton.IsEnabled = _reviewSet?.CanNext == true;
        _syncingFilmstrip = true;
        Filmstrip.SelectedIndex = _reviewSet?.CurrentIndex ?? -1;
        _syncingFilmstrip = false;
        ReviewPositionText.Text = _reviewSet is { CurrentIndex: >= 0 } review
            ? $"{review.CurrentIndex + 1} / {review.Items.Count}" : "";
        RevealReviewItem();
    }

    private void RevealReviewItem()
    {
        // One coalesced layout callback, including rapid traversal and show-after-collapse.
        if (_revealOperation is { Status: DispatcherOperationStatus.Pending }) _revealOperation.Abort();
        _revealOperation = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (FilmstripVisible && !IsFullscreen && Filmstrip.SelectedItem is { } item) Filmstrip.ScrollIntoView(item);
        }));
    }
    private DispatcherOperation? _revealOperation;
    private void PreviousAsset_Click(object sender, RoutedEventArgs e) => _ = TraverseReviewAsync(-1);
    private void NextAsset_Click(object sender, RoutedEventArgs e) => _ = TraverseReviewAsync(1);
    private void FilmstripToggle_Click(object sender, RoutedEventArgs e) => FilmstripVisible = !FilmstripVisible;
    private void Filmstrip_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingFilmstrip && Filmstrip.SelectedItem is PlayerReviewItem { Asset.AssetId: Guid id })
            _ = SelectReviewAssetAsync(id);
    }
    private void Filmstrip_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindFilmstripScroller(Filmstrip) is not { } scroll) return;
        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - Math.Sign(e.Delta) * 3);
        e.Handled = true;
    }
    private static ScrollViewer? FindFilmstripScroller(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindFilmstripScroller(System.Windows.Media.VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
}
