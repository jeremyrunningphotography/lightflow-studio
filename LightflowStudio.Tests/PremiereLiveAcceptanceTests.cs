using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
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
        var fixtureRoot = Environment.GetEnvironmentVariable("LIGHTFLOW_PREMIERE_ACCEPTANCE")!;
        var expectedProject = Environment.GetEnvironmentVariable("LIGHTFLOW_PREMIERE_PROJECT")
            ?? throw new InvalidOperationException("Explicit disposable project path required.");
        var isolation = PremiereAcceptanceIsolation.Validate(fixtureRoot, expectedProject, Path.Combine(fixtureRoot, "media", "source.mov"));
        var sourcePath = isolation.MediaPath;
        var directory = isolation.RunDirectory;
        var locations = isolation.Locations;
        ApplicationDataProfile.Initialize(locations);
        var database = new CatalogDatabaseService(locations);
        var opened = await database.CreateNewAsync();
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
        var markers = new CatalogMarkerService(() => session);
        foreach (var (ticks, name) in new[] { (99999999L, "Before In"), (100000000L, "At In"),
            (120000001L, ""), (199999999L, "Before Out"), (200000000L, "At exclusive Out") })
        {
            var created = (await markers.CreateAsync(asset.AssetId, TimeSpan.FromTicks(ticks))).Marker;
            if (name.Length > 0) await markers.RenameAsync(created.MarkerId, created.Revision, name);
        }
        var subclips = new CatalogSubclipService(() => session);
        await subclips.CreateAsync(asset.AssetId, new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)));
        await subclips.CreateAsync(asset.AssetId, new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(21)));
        var journal = new CatalogPremiereHandoffs(() => session);
        var bridge = new PremiereBridge(journal, isolation.PairingDirectory);
        try
        {
            await bridge.StartAsync();
            var jobs = new PremiereJobs(journal, bridge);
            Console.WriteLine($"Acceptance run: {directory}; pair with: {isolation.PairingDirectory}");
            var deadline = DateTimeOffset.UtcNow.AddMinutes(20);
            var controlPath = Path.Combine(directory, "action.txt");
            var evidence = Path.Combine(directory, "evidence.jsonl");
            async Task EvidenceAsync(string stage)
            {
                isolation.Revalidate(isolation.ProjectPath);
                using var media = File.OpenRead(sourcePath);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(media));
                await File.AppendAllTextAsync(evidence, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, stage,
                    sourceSha256 = hash, adjacentXmp = Directory.GetFiles(Path.GetDirectoryName(sourcePath)!, "*.xmp"),
                    connection = bridge.Connection, handoffs = await journal.ListAsync(), jobs = jobs.Jobs }, PremiereProtocol.Json) + Environment.NewLine);
            }
            await EvidenceAsync("initial-fixtures");
            while (DateTimeOffset.UtcNow < deadline)
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "connection.json"), JsonSerializer.Serialize(bridge.Connection, PremiereProtocol.Json));
                if (File.Exists(controlPath))
                {
                    var action = (await File.ReadAllTextAsync(controlPath)).Trim();
                    File.Delete(controlPath);
                    if (action == "stop") { await EvidenceAsync("stopped"); return; }
                    if (action == "rotate") await bridge.RotatePairingAsync();
                    else if (action == "rename")
                    {
                        var marker = (await markers.ListAsync(asset.AssetId)).Single(m => m.Position.Ticks == 120000001);
                        await markers.RenameAsync(marker.MarkerId, marker.Revision, "Renamed in Lightflow");
                        await EvidenceAsync("renamed-authoritative-marker");
                    }
                    else if (action is "send" or "send-subclips")
                    {
                        var hello = bridge.Connection.Companion;
                        Assert.NotNull(hello?.Project);
                        isolation.Revalidate(hello.Project.Path);
                        await EvidenceAsync("before-" + action);
                        if (action == "send") jobs.Enqueue(hello.Project, hello.Bins[0].Id, "Lightflow 259 acceptance", [source]);
                        else jobs.EnqueueSubclips(hello.Project, hello.Bins[0].Id, "Lightflow 259 acceptance",
                            PremiereSendPlanning.Subclips([source], new Dictionary<Guid, IReadOnlyList<Subclip>>
                            { [asset.AssetId] = await subclips.ListAsync(asset.AssetId) }));
                        var jobId = jobs.Jobs.Last().JobId;
                        while (jobs.Jobs.Single(j => j.JobId == jobId).State is JobState.Queued or JobState.Running)
                        {
                            if (DateTimeOffset.UtcNow >= deadline) { jobs.Cancel(jobId); throw new TimeoutException("Live acceptance expired during a handoff."); }
                            // Production bridge/companion guards revalidate project on each command; this also
                            // catches a switch outside the disposable fixture before any subsequent dispatch.
                            if (bridge.Connection.Companion?.Project is { } active)
                            {
                                try { isolation.Revalidate(active.Path); }
                                catch { jobs.Cancel(jobId); throw; }
                            }
                            await Task.Delay(100);
                        }
                        await EvidenceAsync("after-" + action);
                    }
                    else if (action == "snapshot") await EvidenceAsync("snapshot");
                    else throw new InvalidOperationException("Unknown acceptance action.");
                }
                await Task.Delay(1000);
            }
            throw new TimeoutException("Acceptance driver expired without a stop command.");
        }
        finally
        {
            await bridge.DisposeAsync();
            ApplicationDataProfile.RequireContained(directory, isolation.PairingDirectory);
            var pairing = Path.Combine(isolation.PairingDirectory, "lightflow-pairing.json");
            if (File.Exists(pairing)) File.Delete(pairing);
        }
    }
}
