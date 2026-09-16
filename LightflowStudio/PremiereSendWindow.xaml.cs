using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LightflowStudio;

public partial class PremiereSendWindow : Window
{
    private readonly PremiereBridge _bridge;
    private readonly PremiereJobs _jobs;
    private readonly IReadOnlyList<PremiereSource> _sources;
    private readonly IReadOnlyList<PremierePlannedSubclip> _subclips;
    private readonly PremiereSendModel _media;
    private readonly PremiereSendState _state = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _refreshing;
    internal PremiereSendWindow(PremiereBridge bridge, PremiereJobs jobs, IReadOnlyList<PremiereSource> sources,
        IReadOnlyList<PremierePlannedSubclip>? subclips = null)
    {
        InitializeComponent();
        _bridge = bridge; _jobs = jobs; _sources = sources; _subclips = subclips ?? [];
        _media = new PremiereSendModel(sources, _subclips);
        SourceMediaRadio.IsChecked = true;
        SyncMedia();
        _timer.Tick += (_, _) => RefreshConnection();
        Loaded += (_, _) =>
        {
            // Keep the larger default useful on smaller displays; the outer dialog remains scrollable.
            MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 24);
            Height = Math.Min(Height, MaxHeight);
            RefreshConnection(); _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }
    private void RefreshConnection()
    {
        var live = _bridge.Connection;
        _refreshing = true;
        try
        {
            if (_state.Refresh(live)) NewBinName.Clear();
            if (!_state.Bins.SequenceEqual((Bins.ItemsSource as IReadOnlyList<PremiereBin>) ?? []))
                Bins.ItemsSource = _state.Bins;
            Bins.SelectedItem = _state.Bins.FirstOrDefault(bin => bin.Id == _state.SelectedBinId);
            var presentation = PremiereSendState.Present(live);
            Bins.IsEnabled = _state.Bins.Count > 0 && presentation.IsActionable;
            NewBinName.IsEnabled = _state.DestinationId is not null && presentation.IsActionable;
            var ready = presentation.Readiness is PremiereSendReadiness.DestinationRequired or PremiereSendReadiness.Ready;
            ConnectionText.Text = presentation.Badge;
            ConnectionBadge.Background = (System.Windows.Media.Brush)FindResource(ready ? "ReadyBadgeBackgroundBrush" : "PausedBadgeBackgroundBrush");
            ConnectionBadge.BorderBrush = (System.Windows.Media.Brush)FindResource(ready ? "ReadyBadgeBorderBrush" : "PausedBadgeBorderBrush");
            ProjectRow.Visibility = live.State == PremiereConnectionState.Connected ? Visibility.Visible : Visibility.Collapsed;
            ProjectText.Text = presentation.IsProjectMissing ? "No active project" : live.Companion?.Project?.Name ?? "";
            ProjectText.Foreground = (System.Windows.Media.Brush)FindResource(presentation.IsProjectMissing ? "RedBrush" : "TextBrush");
            CompanionText.Text = live.State == PremiereConnectionState.Connected && live.Companion is { } hello
                ? $"Premiere Pro {hello.HostVersion} · Companion {hello.CompanionVersion}" : "";
            ConnectionMessageText.Text = presentation.Guidance;
            DestinationMessageText.Text = _state.Message;
            SyncMedia();
            RangeMessageText.Text = _media.Mode == PremiereSendMode.Sources && _media.HasRangeIssue
                ? "One or more review ranges are too short for Premiere and will be sent as full sources."
                : "";
            SendButton.IsEnabled = _media.Items.Count > 0 && _state.CanSend(live);
        }
        finally { _refreshing = false; }
    }
    private void Bins_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        _state.SelectedBinId = (Bins.SelectedItem as PremiereBin)?.Id;
        RefreshConnection();
    }
    private void Settings_Click(object sender, RoutedEventArgs e) =>
        new PremiereIntegrationWindow(_bridge) { Owner = this }.ShowDialog();
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshConnection();
    private void RangeUse_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshing || sender is not System.Windows.Controls.CheckBox { DataContext: PremiereSendItem item } check) return;
        _media.SetUseRange(item.Index, check.IsChecked == true);
        SyncMedia();
    }
    private void GlobalUseRanges_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshing || GlobalUseRangesCheck.IsChecked is not { } use) return;
        _media.SetGlobalUseRanges(use);
        SyncMedia();
    }
    private void RepresentationMode_Changed(object sender, RoutedEventArgs e)
    {
        _media.SelectMode(SubclipsRadio.IsChecked == true ? PremiereSendMode.Subclips : PremiereSendMode.Sources);
        SyncMedia();
    }
    internal static bool ShouldTransferWheelToDialog(double scrollableHeight, double verticalOffset, int delta) =>
        scrollableHeight <= 0 || (delta > 0 && verticalOffset <= 0) || (delta < 0 && verticalOffset >= scrollableHeight);
    private void SourcesScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!ShouldTransferWheelToDialog(SourcesScroll.ScrollableHeight, SourcesScroll.VerticalOffset, e.Delta)) return;
        e.Handled = true;
        DialogScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = DialogScroll
        });
    }
    private void SyncMedia()
    {
        var items = _media.Items;
        var fallbackCount = _media.PlannedSubclips.Count(item => item.Projection.IsSourceFallback);
        var nativeCount = _media.PlannedSubclips.Count - fallbackCount;
        MediaHeading.Text = _media.Mode == PremiereSendMode.Subclips
            ? $"Media being sent · {PremiereGrammar.Mixed(nativeCount, fallbackCount)}"
            : $"Media being sent · {PremiereGrammar.Count(_sources.Count, "source")}";
        RepresentationHelpText.Text = _media.Mode == PremiereSendMode.Subclips
            ? "Saved Subclips are sent as native Premiere Subclips. A selected source with no saved Subclips is sent as the whole source."
            : "Each selected Browser source is sent once. Saved In/Out points can be included where available.";
        Sources.ItemsSource = items;
        GlobalUseRangesCheck.Visibility = _media.Mode == PremiereSendMode.Sources ? Visibility.Visible : Visibility.Collapsed;
        GlobalUseRangesCheck.IsEnabled = _media.Mode == PremiereSendMode.Sources && items.Any(item => item.HasRange);
        GlobalUseRangesCheck.IsChecked = _media.GlobalUseRangeState;
        System.Windows.Automation.AutomationProperties.SetName(SourcesScroll,
            _media.Mode == PremiereSendMode.Subclips
                ? $"Media being sent, {PremiereGrammar.Mixed(nativeCount, fallbackCount)}"
                : $"Media being sent, {PremiereGrammar.Count(_sources.Count, "source")}");
    }
    private void Send_Click(object sender, RoutedEventArgs e)
    {
        var live = _bridge.Connection;
        if (!_state.CanSend(live)) { RefreshConnection(); ResultText.Text = "The destination changed or is unavailable. Review the current project and choose a bin again."; return; }
        try
        {
            var binName = string.IsNullOrWhiteSpace(NewBinName.Text) ? null : NewBinName.Text.Trim();
            if (_media.Mode == PremiereSendMode.Subclips)
                _jobs.EnqueueSubclips(live.Companion!.Project!, _state.SelectedBinId!, binName, _media.PlannedSubclips);
            else _jobs.Enqueue(live.Companion!.Project!, _state.SelectedBinId!, binName, _media.PlannedSources);
            Close();
        }
        catch (Exception error) { ResultText.Text = error.Message; }
    }
}
