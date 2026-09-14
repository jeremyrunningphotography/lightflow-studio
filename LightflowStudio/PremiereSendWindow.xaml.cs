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
        SelectionText.Text = $"{sources.Count} Catalog source(s) selected.";
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
            ContextText.Text = live.State == PremiereConnectionState.Connected && live.Companion is { } hello
                ? $"{live.Message}\nProject: {hello.Project?.Name ?? "No active project"}\nCompanion: {hello.CompanionVersion}" : "Premiere companion disconnected";
            StateText.Text = _state.Message;
            SendButton.IsEnabled = _sources.Count > 0 && _state.CanSend(live);
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
    private void Send_Click(object sender, RoutedEventArgs e)
    {
        var live = _bridge.Connection;
        if (!_state.CanSend(live)) { RefreshConnection(); ResultText.Text = "The destination changed or is unavailable. Review the current project and choose a bin again."; return; }
        try
        {
            _jobs.Enqueue(live.Companion!.Project!, _state.SelectedBinId!,
                string.IsNullOrWhiteSpace(NewBinName.Text) ? null : NewBinName.Text.Trim(), _sources);
            Close();
        }
        catch (Exception error) { ResultText.Text = error.Message; }
    }
}
