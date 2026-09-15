using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace LightflowStudio;

internal static partial class PremiereInstallation
{
    public static string InstallerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
        @"Adobe\Adobe Desktop Common\RemoteComponents\UPI\UnifiedPluginInstallerAgent\UnifiedPluginInstallerAgent.exe");

    public static async Task<PremiereConnection> InspectAsync()
    {
        if (!File.Exists(InstallerPath)) return new(PremiereConnectionState.ConnectionProblem,
            "Adobe plugin installer is unavailable. Install or repair Creative Cloud Desktop, then refresh.");
        try
        {
            using var process = new Process { StartInfo = new(InstallerPath) { UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
            process.StartInfo.ArgumentList.Add("/list");
            process.StartInfo.ArgumentList.Add("all");
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { process.Kill(); throw; }
            var text = await output.ConfigureAwait(false);
            if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(await error.ConfigureAwait(false)))
                return new(PremiereConnectionState.ConnectionProblem, "Adobe could not read installed plugins. Refresh or repair Creative Cloud Desktop.");
            return ParseInventory(text);
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or OperationCanceledException)
        { return new(PremiereConnectionState.ConnectionProblem, "Adobe installation detection failed. Refresh or check Creative Cloud Desktop."); }
    }

    internal static PremiereConnection ParseInventory(string inventory)
    {
        var host = HostSection().Match(inventory);
        if (!host.Success) return inventory.Contains("extension installed for", StringComparison.Ordinal)
            || inventory.Contains("extensions installed for", StringComparison.Ordinal)
            ? new(PremiereConnectionState.PremiereNotInstalled, "Premiere Pro is not installed. Install Premiere Pro 26.5 or later.")
            : new(PremiereConnectionState.ConnectionProblem, "Adobe returned an unrecognized plugin inventory. Refresh installation status.");
        if (!PremiereProtocol.SupportedHost(host.Groups[1].Value))
            return new(PremiereConnectionState.UpdateRequired, "Update Premiere Pro to 26.5 or later.");
        var plugin = CompanionRow().Match(host.Groups[2].Value);
        if (!plugin.Success) return new(PremiereConnectionState.CompanionNotInstalled,
            "Install the companion, then in Premiere choose Window > UXP Plugins > Lightflow Studio Companion.");
        if (!Version.TryParse(plugin.Groups[2].Value, out var companionVersion)
            || companionVersion < Version.Parse(PremiereProtocol.CompanionVersion))
            return new(PremiereConnectionState.UpdateRequired, "Install the companion version included with this Lightflow release.");
        return new(PremiereConnectionState.Ready, plugin.Groups[1].Value == "Enabled"
            ? "In Premiere, choose Window > UXP Plugins > Lightflow Studio Companion. Complete its one-time setup; later connections are automatic."
            : "Companion installed but disabled. Enable it in Creative Cloud Desktop.");
    }

    [GeneratedRegex(@"\d+ extensions? installed for Premiere Pro \(ver ([\d.]+)\)\s*([\s\S]*?)(?=\r?\n\d+ extensions? installed for|$)")]
    private static partial Regex HostSection();
    [GeneratedRegex(@"^\s*(Enabled|Disabled)\s+(?:com\.lightflowstudio\.premiere|Lightflow Studio Companion)\s+(\d+\.\d+\.\d+)\s*$", RegexOptions.Multiline)]
    private static partial Regex CompanionRow();
}
