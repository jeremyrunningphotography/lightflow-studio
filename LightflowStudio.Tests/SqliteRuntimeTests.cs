using LightflowStudio;
using Xunit;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace LightflowStudio.Tests;

public sealed class SqliteRuntimeTests
{
    [Theory]
    [InlineData("3.41.2", false)]
    [InlineData("3.50.1", false)]
    [InlineData("3.50.2", true)]
    [InlineData("3.53.3", true)]
    [InlineData("invalid", false)]
    public void RuntimeSecurityBoundary_RejectsAffectedVersions(string version, bool patched)
        => Assert.Equal(patched, CatalogPackageRuntimeVerifier.IsPatchedRuntime(version));

    [Fact]
    public async Task RealProvider_QualifiesCatalogPreviewAndLoadedNativeRuntime()
        => Assert.True(await CatalogPackageRuntimeVerifier.VerifyAsync());

    [Fact]
    public async Task PublishEvidence_AllowsNeutralFrameworkButRejectsStaleOrMissingSqliteGraph()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Directory.Build.props")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        var root = Path.Combine(Path.GetTempPath(), "lightflow-package-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var report = Path.Combine(root, "runtime.json");
            Assert.True(await CatalogPackageRuntimeVerifier.VerifyAsync(report));
            var nativePath = JsonNode.Parse(File.ReadAllText(report))!["NativePath"]!.GetValue<string>();
            var package = Path.Combine(root, "package");
            Directory.CreateDirectory(package);
            var graph = JsonNode.Parse(File.ReadAllText(Path.Combine(repository!.FullName, "LightflowStudio", "packages.lock.json")))!;
            graph["dependencies"]!["net8.0"] = new JsonObject { ["lightflow.actions"] = new JsonObject { ["type"] = "Project" } };
            var lockPath = Path.Combine(root, "publish.lock.json");

            async Task<(int ExitCode, string Errors)> CheckAsync()
            {
                File.WriteAllText(lockPath, graph.ToJsonString());
                var start = new ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                // A test host launched from PowerShell 7 can inherit that edition's
                // module path; let Windows PowerShell discover its own built-in modules.
                start.Environment.Remove("PSModulePath");
                foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                    Path.Combine(repository.FullName, "scripts", "Test-SqliteRuntimeEvidence.ps1"),
                    "-ReportPath", report, "-LockPath", lockPath, "-PackageDirectory", package,
                    "-ExtractionRoot", Path.GetDirectoryName(nativePath)! })
                    start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                await output;
                return (process.ExitCode, await errors);
            }

            var valid = await CheckAsync();
            Assert.True(valid.ExitCode == 0, valid.Errors);
            graph["dependencies"]!["net8.0-windows7.0"]!["SQLitePCLRaw.core"]!["resolved"] = "2.1.6";
            var stale = await CheckAsync();
            Assert.NotEqual(0, stale.ExitCode);
            Assert.Contains("Stale or unexpected SQLitePCLRaw component", stale.Errors);
            graph["dependencies"]!.AsObject().Remove("net8.0-windows7.0");
            var missing = await CheckAsync();
            Assert.NotEqual(0, missing.ExitCode);
            Assert.Contains("Publish lock has no complete managed SQLite graph", missing.Errors);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
