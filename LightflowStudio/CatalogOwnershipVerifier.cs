using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Lightflow.Application;

namespace LightflowStudio;

// Explicit private-desktop process tooling, never an ordinary launch path.
internal static class CatalogOwnershipVerifier
{
    internal const string Switch = "--verify-catalog-ownership";
    internal sealed record Report(bool Ready, string? Diagnostic, Guid? CatalogId, string? Directory, int ProcessId);
    internal static async Task<bool> RunAsync(string[] args)
    {
        ValidationPresentation.RequireNoninteractive();
        var profile = LightflowStorageLocations.Current;
        if (!profile.IsIsolated) throw new InvalidOperationException("Ownership verification requires --data-root.");
        string Argument(string name)
        {
            var index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 == args.Length) throw new ArgumentException("Missing " + name);
            var path = Path.GetFullPath(args[index + 1]);
            ApplicationDataProfile.RequireContained(profile.ApplicationDataDirectory, path);
            return path;
        }
        var reportPath = Argument("--ownership-report");
        var release = Argument("--ownership-release");
        LightflowStorageCoordinator? owner = null;
        try
        {
            using var instance = new WindowsApplicationInstanceCoordinator(ApplicationDataProfile.InstanceIdentity(profile));
            if (instance.StartOrSignal(ApplicationLaunchRequest.Current(args)).Status != ApplicationInstanceStatus.Primary)
                throw new IOException("The verification profile already has an application instance owner.");
            var selection = Array.IndexOf(args, "--ownership-selection") >= 0 ? Argument("--ownership-selection") : null;
            var identityIndex = Array.IndexOf(args, "--ownership-selected-id");
            Guid? selectedId = identityIndex >= 0 && identityIndex + 1 < args.Length ? Guid.Parse(args[identityIndex + 1]) : null;
            var startup = await LightflowStorageCoordinator.StartAsync(profile: profile,
                existingCatalogDirectory: selection, selectedCatalogId: selectedId);
            owner = startup.Coordinator;
            var diagnostic = startup.Diagnostic;
            var ready = startup.IsReady;
            if (ready && Array.IndexOf(args, "--ownership-move") >= 0)
            {
                await owner!.Collections.CreateCollectionAsync("Authored before interrupted transfer");
                var pauseIndex = Array.IndexOf(args, "--ownership-pause-state");
                IProgress<ClosedCatalogTransferState>? progress = pauseIndex >= 0
                    ? new PauseAtState(Enum.Parse<ClosedCatalogTransferState>(args[pauseIndex + 1]),
                        Argument("--ownership-paused"), owner) : null;
                var moved = await owner.RelocateCatalogAsync(Argument("--ownership-move"), progress: progress);
                ready = moved.Succeeded; diagnostic = moved.Diagnostic;
            }
            if (ready) await owner!.Collections.CreateCollectionAsync("Independent process " + Environment.ProcessId);
            var report = new Report(ready, diagnostic, owner?.Settings.CatalogId, owner?.Locations.CatalogDirectory, Environment.ProcessId);
            var temporary = reportPath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(report));
            File.Move(temporary, reportPath);
            if (!ready) return true; // Refusal is an observed result, not a tooling failure.
            var timeout = Stopwatch.StartNew();
            while (!File.Exists(release))
            {
                if (timeout.Elapsed > TimeSpan.FromSeconds(40)) throw new TimeoutException("Ownership process was not released.");
                await Task.Delay(50);
            }
            return true;
        }
        finally { if (owner is not null) await owner.DisposeAsync(); }
    }

    // A test-owned child is killed after durable intent; no alternate storage lifecycle.
    private sealed class PauseAtState(ClosedCatalogTransferState expected, string path,
        LightflowStorageCoordinator owner) : IProgress<ClosedCatalogTransferState>
    {
        public void Report(ClosedCatalogTransferState state)
        {
            if (state != expected) return;
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Report(false, state.ToString(),
                owner.Settings.CatalogId, owner.Locations.CatalogDirectory, Environment.ProcessId)));
            File.Move(temporary, path);
            // Bounded fail-safe if the parent disappears without killing this owned child.
            using var pause = new ManualResetEventSlim(false);
            if (!pause.Wait(TimeSpan.FromSeconds(40)))
                throw new TimeoutException("Interrupted-transfer child was not terminated by its test owner.");
        }
    }
}
