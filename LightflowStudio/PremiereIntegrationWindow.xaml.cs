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
    private bool _checkingInstallation = true;

    internal PremiereIntegrationWindow(PremiereBridge bridge)
    {
        InitializeComponent();
        _bridge = bridge;
        RefreshConnection();
        _timer.Tick += (_, _) => RefreshConnection();
        Loaded += async (_, _) => { _timer.Start(); await InspectAsync(); RefreshConnection(); };
        Closed += (_, _) => _timer.Stop();
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }

    private async Task InspectAsync()
    {
        _checkingInstallation = true;
        RefreshConnection();
        try
        {
            await _bridge.StartAsync();
            _installation = await PremiereInstallation.InspectAsync();
            ResultText.Text = "";
        }
        catch (Exception error) { ResultText.Text = $"Connection unavailable: {error.Message}"; }
        finally { _checkingInstallation = false; }
        RefreshConnection();
    }
    private void RefreshConnection()
    {
        var live = _bridge.Connection;
        if (_checkingInstallation && live.State == PremiereConnectionState.Ready)
        {
            ConnectionText.Text = "Checking installation…";
            GuidanceText.Text = "Checking Adobe installation and companion version.";
            InstallButton.Visibility = Visibility.Collapsed;
            ProjectText.Text = "";
            return;
        }
        var connection = PremiereSendState.WithInstallation(live, _installation);
        ConnectionText.Text = connection.State switch
        {
            PremiereConnectionState.PremiereNotInstalled => "Premiere not installed",
            PremiereConnectionState.CompanionNotInstalled => "Companion not installed",
            PremiereConnectionState.UpdateRequired => "Update required",
            PremiereConnectionState.ConnectionProblem => "Connection problem",
            PremiereConnectionState.Connected => connection.Message,
            _ => "Companion installed — not connected"
        };
        GuidanceText.Text = connection.State == PremiereConnectionState.Connected ? "Authenticated companion connection is healthy." : connection.Message;
        InstallButton.Visibility = connection.State is PremiereConnectionState.CompanionNotInstalled or PremiereConnectionState.UpdateRequired
            ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.Content = connection.State == PremiereConnectionState.UpdateRequired ? "Update Companion…" : "Install Companion…";
        var hello = connection.Companion;
        ProjectText.Text = hello is null ? "" : $"Project: {hello.Project?.Name ?? "No active project"}\nCompanion: {hello.CompanionVersion}";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await InspectAsync();
    private void CopySetup_Click(object sender, RoutedEventArgs e)
    {
        try { System.Windows.Clipboard.SetText(_bridge.PairingDirectory); ResultText.Text = "Setup location copied. Paste it into Adobe's folder picker address bar, press Enter, then select the folder."; }
        catch (Exception) { ResultText.Text = "The clipboard is busy. Try Copy Setup Location again."; }
    }
    private void Done_Click(object sender, RoutedEventArgs e) => Close();
    private async void Pair_Click(object sender, RoutedEventArgs e)
    {
        try { await _bridge.RotatePairingAsync(); ResultText.Text = "Connection reset. The companion will reconnect automatically using its remembered access. If paused, choose Resume Connection in the companion."; RefreshConnection(); }
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
