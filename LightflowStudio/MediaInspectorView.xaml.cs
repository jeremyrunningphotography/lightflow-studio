using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
    private bool _transitionPending;
    private long _applyFocusRevision;
    internal event EventHandler? BrowserFocusRequested;
    internal Func<DescriptionConfirmation, bool>? ConfirmDescriptions { get; set; }
    internal Func<InspectorDescriptionEditor, bool>? ConfirmTransition { get; set; }
    internal bool TryLeaveContext()
    {
        if (_descriptions is null) return true;
        if (_transitionPending || !_descriptions.CanLeaveContext) return false;
        if (!_descriptions.HasDraft) return true;
        _transitionPending = true;
        try { return ConfirmTransition?.Invoke(_descriptions)
            ?? ConfirmationDialog.ConfirmTransition(Window.GetWindow(this), _descriptions); }
        finally { _transitionPending = false; }
    }
    internal event EventHandler? OpenPlayerRequested;
    internal Func<TimelineMarker, Task>? SeekMarker { get; set; }
    internal Func<Task>? OpenFolder { get; set; }
    internal bool IsPlayerContext => _playerContext;
    internal IDisposable? SuspendEditing() => _descriptions?.SuspendEditing();

    public MediaInspectorView() => InitializeComponent();

    private void Inspector_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!e.Handled && IsNeutralPointerSource(e.OriginalSource as DependencyObject, Mouse.Captured))
        {
            // A neutral focus sink, not a new save or selection action. PropertyChanged
            // description drafts and marker LostKeyboardFocus commits keep their existing lifecycle.
            Focus();
        }
    }

    internal bool IsNeutralPointerSource(DependencyObject? source, IInputElement? captured)
    {
        if (captured is not null) return false;
        for (var node = source; node is not null; node =
            node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, this))
                return true;
            if (node is System.Windows.Documents.Hyperlink or System.Windows.Controls.Primitives.Selector or
                System.Windows.Controls.Primitives.MenuBase or System.Windows.Controls.TreeView ||
                node is FrameworkElement { DataContext: InspectorMarkerCard } ||
                node is System.Windows.Controls.Control and not ScrollViewer and not ItemsControl ||
                node is UIElement { Focusable: true } and not ScrollViewer and not ItemsControl) return false;
        }
        return false;
    }
    internal void Initialize(Func<MediaInspectorService> service, IAssetDescriptionStore descriptions)
    {
        ++_applyFocusRevision;
        _service = service;
        _descriptions?.Dispose();
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
        if (_descriptions?.HasDraft == true &&
            (player != _playerContext || !context.Select(a => a.AssetId).ToHashSet().SetEquals(_context.Select(a => a.AssetId))))
            return; // A missed caller guard must never silently destroy a draft.
        if (player != _playerContext || !context.Select(a => a.AssetId).ToHashSet().SetEquals(_context.Select(a => a.AssetId)))
            ++_applyFocusRevision;
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
        InspectorMarkers.ItemsSource = null;
        MarkerSection.Visibility = Visibility.Collapsed;
        if (!_context.Select(a => (a.AssetId, a.RelativePath, a.Kind))
            .SequenceEqual(_displayedContext.Select(a => (a.AssetId, a.RelativePath, a.Kind))))
        {
            PreviewImage.Source = null;
            FieldGroups.ItemsSource = null;
        }
        _displayedContext = _context;
        PreviewImage.AssetId = _context.Count == 1 ? _context[0].AssetId : null;
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
            PresentMarkers(snapshot.Markers, token);
            MarkerSection.Visibility = _playerContext && snapshot.Markers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
    {
        var editor = _descriptions;
        if (editor?.CanApply != true) return;
        var revision = ++_applyFocusRevision;
        var owner = Window.GetWindow(this);
        // The Apply button becomes disabled while saving. Keep a stable Inspector focus
        // anchor, rather than mistaking that automatic focus loss for user navigation.
        var mayReturn = IsKeyboardFocusWithin && Focus() && ReferenceEquals(Keyboard.FocusedElement, this);
        var save = editor.ApplyAsync(); // Includes authoritative validation and confirmation.
        mayReturn &= ReferenceEquals(Keyboard.FocusedElement, this);
        KeyboardFocusChangedEventHandler changed = (_, args) =>
        {
            if (!ReferenceEquals(args.NewFocus, this)) mayReturn = false;
        };
        MouseButtonEventHandler pointer = (_, _) => mayReturn = false;
        EventHandler deactivated = (_, _) => mayReturn = false;
        owner?.AddHandler(Keyboard.LostKeyboardFocusEvent, changed, true);
        owner?.AddHandler(Mouse.PreviewMouseDownEvent, pointer, true);
        if (owner is not null) owner.Deactivated += deactivated;
        try
        {
            if (await save && mayReturn && revision == _applyFocusRevision &&
                ReferenceEquals(editor, _descriptions) && IsVisible && owner?.IsActive == true &&
                ReferenceEquals(Keyboard.FocusedElement, this))
                BrowserFocusRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            owner?.RemoveHandler(Keyboard.LostKeyboardFocusEvent, changed);
            owner?.RemoveHandler(Mouse.PreviewMouseDownEvent, pointer);
            if (owner is not null) owner.Deactivated -= deactivated;
        }
    }
    private async void ReloadDescriptions_Click(object sender, RoutedEventArgs e)
    { if (_descriptions is not null) await _descriptions.ReloadAsync(); }
    private bool ConfirmDescriptionChanges(DescriptionConfirmation request) => ConfirmationDialog.Confirm(
        Window.GetWindow(this), request.IsApply ? "Apply descriptions" : "Reload descriptions",
        request.IsApply ? "Apply descriptive changes?" : "Discard unapplied edits?",
        request.IsApply ? $"Change descriptive Catalog data for {request.AssetCount:N0} selected asset(s)."
            : "Reloading Catalog values will discard your unapplied descriptive edits.",
        string.Join(", ", request.Fields), request.IsApply ? "Apply changes" : "Discard and reload", "Keep editing");
    private void Inspector_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false) ++_applyFocusRevision;
        _ = RefreshAsync();
    }
    public void Dispose()
    {
        ++_applyFocusRevision;
        _descriptions?.Dispose();
        _markerThumbnails?.Dispose();
        ++_generation;
        _hydration?.Cancel();
        _hydration?.Dispose();
        _hydration = null;
    }
}
