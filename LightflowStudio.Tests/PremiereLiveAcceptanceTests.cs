using System.IO;
using System.Text.Json;
using Xunit;

namespace LightflowStudio.Tests;

/// <summary>Opt-in acceptance driver for the production bridge and installed companion, using an isolated Catalog.</summary>
public sealed class PremiereLiveFactAttribute : FactAttribute
{
    public PremiereLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LIGHTFLOW_PREMIERE_ACCEPTANCE") is null)
            Skip = "Requires the installed companion and an explicitly selected disposable Premiere project.";
    }
}

[Collection("Premiere bridge")]
public sealed class PremiereLiveAcceptanceTests
{
    private sealed class Machine : IMachineIdentityProvider { public string GetMachineId() => "premiere-acceptance"; }

    [PremiereLiveFact]
    public async Task InstalledCompanion_ProductionBridgeAcceptance()
    {
        var directory = Path.GetFullPath(Environment.GetEnvironmentVariable("LIGHTFLOW_PREMIERE_ACCEPTANCE")!);
        var expectedProject = Environment.GetEnvironmentVariable("LIGHTFLOW_PREMIERE_PROJECT")
            ?? throw new InvalidOperationException("Explicit disposable project path required.");
        var sourcePath = Path.Combine(directory, "media", "source.mov");
        Assert.True(File.Exists(sourcePath));
        var locations = LightflowStorageLocations.Create(directory);
        var database = new CatalogDatabaseService(locations);
        var opened = File.Exists(locations.CatalogDatabasePath) ? await database.OpenExistingAsync() : await database.CreateNewAsync();
        await using var session = opened.Session!;
        Assert.NotNull(session);
        var roots = new MediaRootService(() => session, new Machine(), new MediaRootFileSystem());
        var root = (await roots.ListAsync()).SingleOrDefault();
        if (root is null)
        {
            var created = await roots.CreateAsync("Premiere acceptance", Path.GetDirectoryName(sourcePath)!);
            Assert.True(created.Succeeded, created.Diagnostic);
            root = created.Root!;
        }
        var assets = new MediaAssetService(new CatalogMediaAssetRepository(() => session), roots, new SampledSourceFingerprintService());
        var asset = (await assets.FindAsync(root.RootId, "source.mov"))?.Asset;
        if (asset is null)
        {
            var created = await assets.CreateAsync(root.RootId, "source.mov", "video");
            Assert.True(created.Asset is not null, created.Diagnostic);
            asset = created.Asset.Asset;
        }
        var source = new PremiereSource(asset.AssetId, sourcePath, asset.FileSizeBytes.ToString(), asset.LastWriteUtcTicks.ToString());
        var journal = new CatalogPremiereHandoffs(() => session);
        await using var bridge = new PremiereBridge(journal, Path.Combine(Path.GetTempPath(), "Lightflow-Premiere-acceptance-pairing"));
        await bridge.StartAsync();
        var deadline = DateTimeOffset.UtcNow.AddMinutes(20);
        var controlPath = Path.Combine(directory, "action.txt");
        var evidence = Path.Combine(directory, "evidence.jsonl");
        while (DateTimeOffset.UtcNow < deadline)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "connection.json"), JsonSerializer.Serialize(bridge.Connection, PremiereProtocol.Json));
            if (File.Exists(controlPath))
            {
                var action = (await File.ReadAllTextAsync(controlPath)).Trim();
                File.Delete(controlPath);
                if (action == "stop") return;
                if (action == "rotate") await bridge.RotatePairingAsync();
                else if (action == "send")
                {
                    var hello = bridge.Connection.Companion;
                    Assert.NotNull(hello?.Project);
                    Assert.Equal(PremiereProtocol.PathKey(expectedProject), PremiereProtocol.PathKey(hello.Project.Path));
                    var command = await journal.PrepareAsync(hello.Project, hello.Bins[0].Id, "Lightflow 257 acceptance", source);
                    var receipt = await bridge.SendAsync(command, CancellationToken.None);
                    await File.AppendAllTextAsync(evidence, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow,
                        connection = bridge.Connection, command, receipt }, PremiereProtocol.Json) + Environment.NewLine);
                }
                else if (action != "snapshot") throw new InvalidOperationException("Unknown acceptance action.");
            }
            await Task.Delay(1000);
        }
        throw new TimeoutException("Acceptance driver expired without a stop command.");
    }
}
