using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using LightflowStudio;
using Lightflow.Application;
using Xunit;
using Xunit.Abstractions;

namespace LightflowStudio.Tests;

public sealed class CatalogOwnershipProcessTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(ClosedCatalogTransferState.Activating)]
    [InlineData(ClosedCatalogTransferState.ActivatedPendingConfiguration)]
    public async Task InterruptedIndependentMove_RestartBlocksUnresolvedOwner_RetainsRecoverableCatalog(ClosedCatalogTransferState boundary)
    {
        ValidationPresentation.RequireNoninteractive();
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var root = Path.Combine(workspace, "artifacts", "lf012-process-interruption", Guid.NewGuid().ToString("N"));
        var inner = Path.Combine(root, "interrupted"); Directory.CreateDirectory(inner);
        var destination = Path.Combine(inner, "moved");
        var info = new ProcessStartInfo(Path.Combine(workspace, "LightflowStudio", "bin", "Release", "net8.0-windows", "LightflowStudio.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--data-root", inner, CatalogOwnershipVerifier.Switch,
            "--ownership-report", Path.Combine(inner, "final.json"), "--ownership-release", Path.Combine(inner, "release"),
            "--ownership-move", destination, "--ownership-pause-state", boundary.ToString(),
            "--ownership-paused", Path.Combine(inner, "paused.json") }) info.ArgumentList.Add(arg);
        using var child = Process.Start(info)!;
        var processHandle = child.SafeHandle;
        output.WriteLine("{0:O} parent pid={1} start={2:O}; child pid={3} start={4:O}",
            DateTimeOffset.UtcNow, Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime(), child.Id, child.StartTime.ToUniversalTime());
        CatalogOwnershipVerifier.Report paused;
        try
        {
            paused = await Report(inner, "paused", child);
            Assert.Equal(boundary.ToString(), paused.Diagnostic);
            Assert.NotEqual(Environment.ProcessId, paused.ProcessId);
            var journalPath = Directory.GetFiles(Path.Combine(inner, "Catalog Transfers"), "*.journal").Single();
            JournalLockDiagnostics.Record(output, "paused child before termination", journalPath, root);
            // Terminate only this explicitly test-owned synthetic child, simulating power loss.
            output.WriteLine("{0:O} kill requested child={1}", DateTimeOffset.UtcNow, child.Id);
            child.Kill(entireProcessTree: true); await child.WaitForExitAsync();
            output.WriteLine("Native termination signal after managed wait: {0}", WaitForSingleObject(processHandle, 0));
            // HasExited/WaitForExitAsync can observe the termination exit code before
            // Windows signals the process and finishes closing its file handles.
            // Wait on the retained process object, never poll/retry the journal.
            Assert.Equal(0u, await Task.Run(() => WaitForSingleObject(processHandle, 10_000)));
            Assert.Equal(0u, WaitForSingleObject(processHandle, 0));
            output.WriteLine("{0:O} wait complete child={1} exit={2:O} code={3}", DateTimeOffset.UtcNow, child.Id, child.ExitTime.ToUniversalTime(), child.ExitCode);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            Assert.Equal(0u, await Task.Run(() => WaitForSingleObject(processHandle, 10_000)));
        }
        var profile = ApplicationDataProfile.Resolve(["--data-root", inner]);
        output.WriteLine("{0:O} first startup journal read boundary", DateTimeOffset.UtcNow);
        var restart = await LightflowStorageCoordinator.StartAsync(profile: profile, allowFirstCreation: false);
        JournalLockDiagnostics.Record(output, "after first startup read", Directory.GetFiles(ClosedCatalogTransferJournal.DirectoryPath(profile), "*.journal").Single(), root);
        output.WriteLine("Interrupted boundary: {0}; child pid: {1}; restart status: {2}; reason: {3}",
            boundary, paused.ProcessId, restart.Status, restart.RecoveryReason);
        output.WriteLine("Full restart diagnostic: {0}", restart.Diagnostic?.Replace(root, "[fixture]"));
        if (restart.Diagnostic?.Contains("interrupted Catalog transfer", StringComparison.OrdinalIgnoreCase) != true)
        {
            // Collect evidence only after refusal. Never retry startup or change its result.
            try
            {
                var journal = Directory.GetFiles(ClosedCatalogTransferJournal.DirectoryPath(profile), "*.journal").Single();
                output.WriteLine("Retained journal: {0}; bytes: {1}", journal.Replace(root, "[fixture]"), new FileInfo(journal).Length);
                var observed = ClosedCatalogTransferJournal.Read(journal);
                output.WriteLine("Diagnostic read only: {0} records; final state {1}; intent {2}",
                    observed.Count, observed.LastOrDefault()?.State, observed.LastOrDefault()?.IsIntent);
            }
            catch (Exception diagnosticError) { output.WriteLine("Diagnostic journal read: HRESULT=0x{0:X8}; native={1}; {2}", diagnosticError.HResult, diagnosticError.HResult & 0xffff, diagnosticError.ToString().Replace(root, "[fixture]")); }
        }
        Assert.False(restart.IsReady);
        Assert.Equal(CatalogRecoveryReason.PendingRecovery, restart.RecoveryReason);
        Assert.Contains("interrupted Catalog transfer", restart.Diagnostic!, StringComparison.OrdinalIgnoreCase);
        await using (var unavailable = restart.Coordinator!)
        {
            Assert.False(unavailable.CatalogAvailable);
            await Assert.ThrowsAsync<InvalidOperationException>(() => unavailable.Collections.CreateCollectionAsync("Blocked callback"));
            var refusedRestore = await unavailable.RestoreCatalogAsync(Path.Combine(inner, "unselected-backup.db"));
            Assert.False(refusedRestore.Succeeded);
            Assert.Contains("interrupted Catalog transfer", refusedRestore.Diagnostic!, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var database in new[] { profile.CatalogDatabasePath, Path.Combine(destination, LightflowStorageLocations.CatalogFileName) })
        {
            Assert.True(File.Exists(database));
            using var released = StartupStoreEvidence.Begin(WindowsCatalogEvidenceEffects.Instance, database, "Catalog", false);
            Assert.NotNull(released); // Native process death released both ownership handles.
        }
        var records = ClosedCatalogTransferJournal.Read(Directory.GetFiles(ClosedCatalogTransferJournal.DirectoryPath(profile), "*.journal").Single());
        Assert.True(ClosedCatalogJournalFormat.RequiresRecovery(records));
        Assert.Contains(records, r => r.State == boundary && r.IsIntent);
        // A receiving isolated profile can explicitly validate/open the retained published unit.
        var receiving = ApplicationDataProfile.Resolve(["--data-root", root]);
        var recovered = await LightflowStorageCoordinator.StartAsync(profile: receiving, allowFirstCreation: false,
            existingCatalogDirectory: destination, selectedCatalogId: paused.CatalogId);
        Assert.True(recovered.IsReady, recovered.Diagnostic);
        await using var owner = recovered.Coordinator!;
        Assert.Equal(paused.CatalogId, owner.CatalogSession.Identity.CatalogId);
        Assert.Equal(destination, owner.Locations.CatalogDirectory);
        Assert.Single(await owner.Collections.ListCollectionsAsync());
        Assert.False(File.Exists(receiving.CatalogDatabasePath)); // No blank replacement.
        Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(WindowsCatalogEvidenceEffects.Instance, owner.CatalogSession.DatabasePath, "Catalog", false));
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task IndependentProcesses_RefuseSecondOwner_ReleaseAfterShutdown(bool move, bool explicitSelection)
    {
        ValidationPresentation.RequireNoninteractive();
        var workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var root = Path.Combine(workspace, "artifacts", "lf012-process-ownership", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var firstRoot = Path.Combine(root, "inner");
        var first = Launch(firstRoot, "first", move ? Path.Combine(firstRoot, "moved") : null);
        try
        {
            var firstReport = await Report(firstRoot, "first", first);
            Assert.True(firstReport.Ready, firstReport.Diagnostic);
            Assert.NotEqual(Environment.ProcessId, firstReport.ProcessId);
            var outer = ApplicationDataProfile.Resolve(["--data-root", root]);
            ApplicationDataProfile.Initialize(outer);
            AppSettingsStore.Save(outer.SettingsPath, new AppSettings { CatalogDirectory = firstReport.Directory,
                CatalogId = firstReport.CatalogId, CameraLutFolder = "", CreativeLutFolder = "" }, isolated: true);
            using (var competing = Launch(root, "competing", selected: explicitSelection ? firstReport : null))
            {
                var refused = await Report(root, "competing", competing);
                Assert.False(refused.Ready);
                Assert.Contains("ownership", refused.Diagnostic!, StringComparison.OrdinalIgnoreCase);
                Assert.NotEqual(firstReport.ProcessId, refused.ProcessId);
                await competing.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(0, competing.ExitCode);
            }
            await File.WriteAllTextAsync(Path.Combine(firstRoot, "first.release"), "release");
            await first.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, first.ExitCode);
            using var next = Launch(root, "next", selected: explicitSelection ? firstReport : null);
            try
            {
                var reopened = await Report(root, "next", next);
                Assert.True(reopened.Ready, reopened.Diagnostic);
                Assert.Equal(firstReport.CatalogId, reopened.CatalogId);
                await File.WriteAllTextAsync(Path.Combine(root, "next.release"), "release");
                await next.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(0, next.ExitCode);
            }
            finally { if (!next.HasExited) { next.Kill(entireProcessTree: true); await next.WaitForExitAsync(); } }
        }
        finally
        {
            if (!first.HasExited) { first.Kill(entireProcessTree: true); await first.WaitForExitAsync(); }
            first.Dispose();
        }

        Process Launch(string profile, string name, string? destination = null, CatalogOwnershipVerifier.Report? selected = null)
        {
            Directory.CreateDirectory(profile);
            var info = new ProcessStartInfo(Path.Combine(workspace, "LightflowStudio", "bin", "Release", "net8.0-windows", "LightflowStudio.exe"))
                { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "--data-root", profile, CatalogOwnershipVerifier.Switch,
                "--ownership-report", Path.Combine(profile, name + ".json"), "--ownership-release", Path.Combine(profile, name + ".release") })
                info.ArgumentList.Add(arg);
            if (destination is not null) { info.ArgumentList.Add("--ownership-move"); info.ArgumentList.Add(destination); }
            if (selected is not null)
            {
                info.ArgumentList.Add("--ownership-selection"); info.ArgumentList.Add(selected.Directory!);
                info.ArgumentList.Add("--ownership-selected-id"); info.ArgumentList.Add(selected.CatalogId!.Value.ToString("D"));
            }
            return Process.Start(info)!;
        }
    }
    private static async Task<CatalogOwnershipVerifier.Report> Report(string root, string name, Process process)
    {
        var path = Path.Combine(root, name + ".json");
        var timer = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            Assert.False(process.HasExited, "Ownership child exited before reporting; exit=" + (process.HasExited ? process.ExitCode : -1));
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30), "Ownership child did not report before timeout.");
            await Task.Delay(50);
        }
        return JsonSerializer.Deserialize<CatalogOwnershipVerifier.Report>(await File.ReadAllTextAsync(path))!;
    }
}
