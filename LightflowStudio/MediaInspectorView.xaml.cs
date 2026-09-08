using System.Windows;
using System.Windows.Controls;

namespace LightflowStudio;

public partial class MediaInspectorView : System.Windows.Controls.UserControl, IDisposable
{
    private Func<MediaInspectorService>? _service;
    private IReadOnlyList<InspectorAsset> _context = [];
    private IReadOnlyList<InspectorAsset> _displayedContext = [];
    private bool _playerContext;
    private CancellationTokenSource? _hydration;
    private long _generation;
    private InspectorSnapshot? _snapshot;
    private bool _reading;
    private bool _refreshAgain;
    private InspectorDescriptionEditor? _descriptions;
    internal Func<DescriptionConfirmation, bool>? ConfirmDescriptions { get; set; }
    internal event EventHandler? OpenPlayerRequested;
    internal Func<Task>? OpenFolder { get; set; }
    internal bool IsPlayerContext => _playerContext;

    public MediaInspectorView() => InitializeComponent();
    internal void Initialize(Func<MediaInspectorService> service, IAssetDescriptionStore descriptions)
    {
        _service = service;
        _descriptions = new(descriptions, request => ConfirmDescriptions?.Invoke(request) ?? ConfirmDescriptionChanges(request));
        DescriptionSection.DataContext = _descriptions;
    }
    internal void SetContext(IReadOnlyList<InspectorAsset> context, bool player, bool force = false)
    {
        if (player == _playerContext && context.SequenceEqual(_context))
        {
            if (!force) return;
            // Derived-work notifications must not continuously cancel a large, unchanged selection.
            if (_reading) { _refreshAgain = true; return; }
        }
        _context = context;
        _playerContext = player;
        if (_descriptions is not null) _ = _descriptions.SetContextAsync(context.Select(a => a.AssetId), player);
        _ = RefreshAsync();
    }
    internal Task RefreshAsync() => HydrateAsync();

    private async Task HydrateAsync()
    {
        var generation = ++_generation;
        _reading = false;
        _refreshAgain = false;
        _hydration?.Cancel();
        _hydration?.Dispose();
        _hydration = new();
        var token = _hydration.Token;
        _snapshot = null;
        if (!_context.Select(a => (a.AssetId, a.RelativePath, a.Kind))
            .SequenceEqual(_displayedContext.Select(a => (a.AssetId, a.RelativePath, a.Kind))))
        {
            PreviewImage.Source = null;
            FieldGroups.ItemsSource = null;
        }
        _displayedContext = _context;
        PreviewStatus.Text = "";
        OpenPlayerButton.Visibility = !_playerContext && _context.Count == 1 && _context[0].Kind == MediaPresentationKind.Video
            ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = _context.Count == 1 ? _context[0].Name : _context.Count > 1 ? $"{_context.Count:N0} selected assets" : "Select media to inspect";
        StatusText.Text = _context.Count == 0 ? "Select one or more Browser assets, or open media in Player." : "";
        if (_context.Count == 0 || !IsVisible || _service is null) return;
        _reading = true;
        _ = ShowSlowLoadingAsync(generation, token);
        try
        {
            // A short cancellable debounce prevents a rapid key-repeat from queueing store reads.
            await Task.Delay(100, token);
            var snapshot = await _service().ReadAsync(_context, token);
            if (generation != _generation || token.IsCancellationRequested) return;
            _snapshot = snapshot;
            TitleText.Text = snapshot.Title;
            StatusText.Text = snapshot.Status;
            FieldGroups.ItemsSource = snapshot.Fields.GroupBy(f => f.Group).ToArray();
            if (_context.Count == 1)
            {
                PreviewStatus.Text = snapshot.PreviewPath is null ? "Cached Preview unavailable or pending." : "";
                if (snapshot.PreviewPath is { } path)
                {
                    var bitmap = await Task.Run(() => PlayerViewerHost.DecodeImage(path), token);
                    if (generation != _generation || token.IsCancellationRequested) return;
                    PreviewImage.Source = bitmap;
                    PreviewStatus.Text = "";
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation != _generation) return;
            if (_snapshot is null) StatusText.Text = $"Inspection unavailable: {exception.Message}";
            else PreviewStatus.Text = "Cached Preview unavailable.";
        }
        finally
        {
            if (generation == _generation)
            {
                _reading = false;
                if (_refreshAgain && IsVisible) _ = RefreshAsync();
            }
        }
    }
    private async Task ShowSlowLoadingAsync(long generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(750, token);
            if (generation == _generation && _reading && _snapshot is null) StatusText.Text = "Loading metadata…";
        }
        catch (OperationCanceledException) { }
    }
    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var generation = _generation;
        try { if (OpenFolder is not null) await OpenFolder(); }
        catch (Exception exception)
        {
            if (generation == _generation) StatusText.Text = $"Could not open folder: {exception.Message}";
        }
    }
    private void OpenPlayer_Click(object sender, RoutedEventArgs e) => OpenPlayerRequested?.Invoke(this, EventArgs.Empty);
    private async void ApplyDescriptions_Click(object sender, RoutedEventArgs e)
    { if (_descriptions is not null) await _descriptions.ApplyAsync(); }
    private async void ReloadDescriptions_Click(object sender, RoutedEventArgs e)
    { if (_descriptions is not null) await _descriptions.ReloadAsync(); }
    private bool ConfirmDescriptionChanges(DescriptionConfirmation request) => ConfirmationDialog.Confirm(
        Window.GetWindow(this), request.IsApply ? "Apply descriptions" : "Reload descriptions",
        request.IsApply ? "Apply descriptive changes?" : "Discard unapplied edits?",
        request.IsApply ? $"Change descriptive Catalog data for {request.AssetCount:N0} selected asset(s)."
            : "Reloading Catalog values will discard your unapplied descriptive edits.",
        string.Join(", ", request.Fields), request.IsApply ? "Apply changes" : "Discard and reload", "Keep editing");
    private void Inspector_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => _ = RefreshAsync();
    public void Dispose()
    {
        _descriptions?.Dispose();
        ++_generation;
        _hydration?.Cancel();
        _hydration?.Dispose();
        _hydration = null;
    }
}
