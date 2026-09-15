using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LightflowStudio;

public partial class PremiereSendWindow : Window
{
    private readonly PremiereBridge _bridge;
    private readonly PremiereJobs _jobs;
    private readonly IReadOnlyList<PremiereSource> _sources;
    private readonly PremiereSendState _state = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _refreshing;
    internal PremiereSendWindow(PremiereBridge bridge, PremiereJobs jobs, IReadOnlyList<PremiereSource> sources)
    {
        InitializeComponent();
        _bridge = bridge; _jobs = jobs; _sources = sources;
        Sources.ItemsSource = sources;
        ApplyRangesCheck.IsChecked = PremiereSendPlanning.CanApplyRanges(sources);
        ApplyRangesCheck.IsEnabled = PremiereSendPlanning.CanApplyRanges(sources);
        _timer.Tick += (_, _) => RefreshConnection();
        Loaded += (_, _) => { RefreshConnection(); _timer.Start(); };
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
            Bins.IsEnabled = _state.Bins.Count > 0;
            NewBinName.IsEnabled = _state.DestinationId is not null;
            var connected = live.State == PremiereConnectionState.Connected;
            ConnectionText.Text = connected ? "Connected" : "Not connected";
            ConnectionBadge.Background = (System.Windows.Media.Brush)FindResource(connected ? "ReadyBadgeBackgroundBrush" : "PausedBadgeBackgroundBrush");
            ConnectionBadge.BorderBrush = (System.Windows.Media.Brush)FindResource(connected ? "ReadyBadgeBorderBrush" : "PausedBadgeBorderBrush");
            ProjectText.Text = connected ? $"Project: {live.Companion?.Project?.Name ?? "No active project"}" : "";
            CompanionText.Text = connected && live.Companion is { } hello
                ? $"Premiere Pro {hello.HostVersion} · Companion {hello.CompanionVersion}" : "";
            ConnectionMessageText.Text = live.Message;
            DestinationMessageText.Text = _state.Message;
            RangeMessageText.Text = PremiereSendPlanning.HasRangeIssue(_sources)
                ? ApplyRangesCheck.IsChecked == true
                    ? "One or more review ranges are too short for Premiere. Turn this off to send those files as full sources."
                    : "One or more review ranges are too short for Premiere. Those files will be sent as full sources."
                : ApplyRangesCheck.IsEnabled ? "Saved ranges apply only to matching source items; this does not create Subclips."
                : "No selected video has saved In/Out points.";
            SendButton.IsEnabled = _sources.Count > 0 && _state.CanSend(live)
                && !(ApplyRangesCheck.IsChecked == true && PremiereSendPlanning.HasRangeIssue(_sources));
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
    private void ApplyRanges_Changed(object sender, RoutedEventArgs e) => RefreshConnection();
    private void Send_Click(object sender, RoutedEventArgs e)
    {
        var live = _bridge.Connection;
        if (!_state.CanSend(live)) { RefreshConnection(); ResultText.Text = "The destination changed or is unavailable. Review the current project and choose a bin again."; return; }
        try
        {
            _jobs.Enqueue(live.Companion!.Project!, _state.SelectedBinId!,
                string.IsNullOrWhiteSpace(NewBinName.Text) ? null : NewBinName.Text.Trim(),
                PremiereSendPlanning.Sources(_sources, ApplyRangesCheck.IsChecked == true));
            Close();
        }
        catch (Exception error) { ResultText.Text = error.Message; }
    }
}
