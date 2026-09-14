using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LightflowStudio;

public partial class PremiereIntegrationWindow : Window
{
    private readonly PremiereBridge _bridge;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private PremiereConnection _installation = new(PremiereConnectionState.ConnectionProblem, "Checking Adobe installation…");

    internal PremiereIntegrationWindow(PremiereBridge bridge)
    {
        InitializeComponent();
        _bridge = bridge;
        PairingPath.Text = bridge.PairingDirectory;
        _timer.Tick += (_, _) => RefreshConnection();
        Loaded += async (_, _) => { _timer.Start(); await InspectAsync(); RefreshConnection(); };
        Closed += (_, _) => _timer.Stop();
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }

    private async Task InspectAsync() { _installation = await PremiereInstallation.InspectAsync(); RefreshConnection(); }
    private void RefreshConnection()
    {
        var live = _bridge.Connection;
        var connection = PremiereSendState.WithInstallation(live, _installation);
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
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await InspectAsync();
    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        try { await _bridge.RotatePairingAsync(); ResultText.Text = "Connection reset. Choose Connect to Lightflow in the companion and select the connection folder again."; RefreshConnection(); }
        catch (Exception error) { ResultText.Text = error.Message; }
    }
    private void Install_Click(object sender, RoutedEventArgs e)
    {
        var package = Path.Combine(AppContext.BaseDirectory, "PremiereCompanion", "LightflowStudio.ccx");
        if (!File.Exists(package)) { ResultText.Text = "The companion package is missing. Rebuild or reinstall Lightflow's release package."; return; }
        try { Process.Start(new ProcessStartInfo(package) { UseShellExecute = true }); }
        catch (Exception error) { ResultText.Text = $"Creative Cloud could not open the companion package: {error.Message}"; }
    }
}
