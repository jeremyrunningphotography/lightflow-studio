using System.Windows;
using System.Windows.Controls;

namespace LightflowStudio;

public partial class MediaInspectorView : System.Windows.Controls.UserControl, IDisposable
{
    private Func<MediaInspectorService>? _service;
    private IReadOnlyList<InspectorAsset> _context = [];
    private bool _playerContext;
    private CancellationTokenSource? _hydration;
    private long _generation;
    private InspectorSnapshot? _snapshot;
    private bool _reading;
    private bool _refreshAgain;
    internal event EventHandler? OpenPlayerRequested;

    public MediaInspectorView() => InitializeComponent();
    internal void Initialize(Func<MediaInspectorService> service) => _service = service;
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
        PreviewImage.Source = null;
        PreviewStatus.Text = "";
        FieldGroups.ItemsSource = null;
        RawRows.ItemsSource = null;
        RawStatus.Text = "";
        ContextText.Text = _playerContext ? "PLAYER" : "BROWSER SELECTION";
        OpenPlayerButton.Visibility = !_playerContext && _context.Count == 1 && _context[0].Kind == MediaPresentationKind.Video
            ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = _context.Count == 1 ? _context[0].Name : _context.Count > 1 ? $"{_context.Count:N0} selected assets" : "Select media to inspect";
        StatusText.Text = _context.Count == 0 ? "Select one or more Browser assets, or open media in Player." : "Loading metadata…";
        if (_context.Count == 0 || !IsVisible || _service is null) return;
        _reading = true;
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
            ApplyRawSearch();
            if (_context.Count == 1)
            {
                PreviewStatus.Text = _playerContext ? "Media is presented in Player." : "Cached Preview unavailable or pending.";
                if (!_playerContext && snapshot.PreviewPath is { } path)
                {
                    var bitmap = await Task.Run(() => PlayerViewerHost.DecodeImage(path), token);
                    if (generation != _generation || token.IsCancellationRequested) return;
                    PreviewImage.Source = bitmap;
                    PreviewStatus.Text = "Cached Preview";
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
    private void ApplyRawSearch()
    {
        if (_snapshot is null) return;
        var matches = MediaInspectorService.SearchRaw(_snapshot.Raw, RawSearch.Text.Trim());
        RawRows.ItemsSource = matches;
        RawStatus.Text = _context.Count > 1 ? "Select one asset to search its raw provider snapshot." :
            _snapshot.Raw.Count == 0 ? "No raw provider snapshot available." :
            $"{matches.Count:N0} of {_snapshot.Raw.Count:N0} fields · provider / namespace path" +
            (_snapshot.RawTruncated ? " · first 10,000 fields shown" : "");
    }
    private void RawSearch_Changed(object sender, TextChangedEventArgs e) => ApplyRawSearch();
    private void OpenPlayer_Click(object sender, RoutedEventArgs e) => OpenPlayerRequested?.Invoke(this, EventArgs.Empty);
    private void Inspector_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => _ = RefreshAsync();
    public void Dispose()
    {
        ++_generation;
        _hydration?.Cancel();
        _hydration?.Dispose();
        _hydration = null;
    }
}
