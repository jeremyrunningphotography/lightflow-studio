using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LightflowStudio;

public partial class PremiereIntegrationWindow : Window
{
    private readonly PremiereBridge _bridge;
    private readonly PremiereJobs _jobs;
    private readonly IReadOnlyList<PremiereSource> _sources;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private PremiereConnection _installation = new(PremiereConnectionState.ConnectionProblem, "Checking Adobe installation…");
    private string? _destination;

    internal PremiereIntegrationWindow(PremiereBridge bridge, PremiereJobs jobs, IReadOnlyList<PremiereSource> sources)
    {
        InitializeComponent();
        _bridge = bridge; _jobs = jobs; _sources = sources;
        PairingPath.Text = bridge.PairingDirectory;
        SelectionText.Text = sources.Count == 0 ? "Select source media in the Browser, then choose Send to Premiere Pro."
            : $"{sources.Count} Catalog source(s) selected.";
        _timer.Tick += (_, _) => RefreshConnection();
        Loaded += async (_, _) => { _timer.Start(); await InspectAsync(); RefreshConnection(); };
        Closed += (_, _) => _timer.Stop();
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }

    private async Task InspectAsync() { _installation = await PremiereInstallation.InspectAsync(); RefreshConnection(); }
    private void RefreshConnection()
    {
        var live = _bridge.Connection;
        var connection = live.State == PremiereConnectionState.Ready ? _installation : live;
        ConnectionText.Text = connection.State switch
        {
            PremiereConnectionState.PremiereNotInstalled => "Premiere not installed",
            PremiereConnectionState.CompanionNotInstalled => "Companion not installed",
            PremiereConnectionState.UpdateRequired => "Update required",
            PremiereConnectionState.ConnectionProblem => "Connection problem",
            PremiereConnectionState.Connected => connection.Message,
            _ => "Ready"
        };
        GuidanceText.Text = connection.State == PremiereConnectionState.Connected ? "Authenticated companion connection is healthy." : connection.Message;
        var hello = connection.Companion;
        ProjectText.Text = hello is null ? "" : $"Project: {hello.Project?.Name ?? "No active project"}\nCompanion: {hello.CompanionVersion}";
        var destination = hello?.Project is { } project ? PremiereProtocol.DestinationId(project) : null;
        var selectedBin = (Bins.SelectedItem as PremiereBin)?.Id;
        var bins = hello?.Bins ?? [];
        if (_destination != destination || !bins.SequenceEqual((Bins.ItemsSource as IReadOnlyList<PremiereBin>) ?? []))
        {
            Bins.ItemsSource = bins;
            Bins.SelectedItem = _destination == destination ? bins.FirstOrDefault(bin => bin.Id == selectedBin) : null;
            if (Bins.SelectedItem is null && bins.Count > 0) Bins.SelectedIndex = 0;
            _destination = destination;
        }
        SendButton.IsEnabled = connection.State == PremiereConnectionState.Connected && hello?.Project is not null
            && _sources.Count > 0 && Bins.SelectedItem is not null;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await InspectAsync();
    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        try { await _bridge.RotatePairingAsync(); ResultText.Text = "Pairing rotated. Select this folder again in the Premiere companion."; }
        catch (Exception error) { ResultText.Text = error.Message; }
    }
    private void Install_Click(object sender, RoutedEventArgs e)
    {
        var package = Path.Combine(AppContext.BaseDirectory, "PremiereCompanion", "LightflowStudio.ccx");
        if (!File.Exists(package)) { ResultText.Text = "The companion package is missing. Rebuild or reinstall Lightflow's release package."; return; }
        try { Process.Start(new ProcessStartInfo(package) { UseShellExecute = true }); }
        catch (Exception error) { ResultText.Text = $"Creative Cloud could not open the companion package: {error.Message}"; }
    }
    private void Send_Click(object sender, RoutedEventArgs e)
    {
        var hello = _bridge.Connection.Companion;
        if (hello?.Project is not { } project || Bins.SelectedItem is not PremiereBin bin) return;
        var createName = string.IsNullOrWhiteSpace(NewBinName.Text) ? null : NewBinName.Text.Trim();
        try
        {
            _jobs.Enqueue(project, bin.Id, createName, _sources);
            Close();
        }
        catch (Exception error) { ResultText.Text = error.Message; }
    }
}
