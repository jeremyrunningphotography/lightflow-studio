using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using Lightflow.Application;
using Lightflow.Domain;
using Lightflow.Platform.MacOS;
using LightflowStudio;
using Xunit;

namespace Lightflow.Platform.MacOS.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("macos")]
[Collection("Native storage descriptor ownership")]
public sealed class MacBrowserDiscoveryTests
{
    private static string Fixture() => Path.Combine(Environment.GetEnvironmentVariable("LIGHTFLOW_TEST_ROOT")
        ?? Path.Combine(AppContext.BaseDirectory, "BrowserFixtures"), "lf027-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MountedVolumeNamespaceNeverReusesStartupAncestor()
    {
        var mounts = new[] { "/", "/Volumes/Field", "/Volumes/Field Backup" };
        Assert.Equal("/", MacBrowserMounts.Anchor("/Users/task/Media", mounts));
        Assert.Equal("/Volumes/Field", MacBrowserMounts.Anchor("/Volumes/Field/Media", mounts));
        Assert.Equal("/Volumes/Field Backup", MacBrowserMounts.Anchor("/Volumes/Field Backup/Media", mounts));
        Assert.Equal("/", MacBrowserMounts.Anchor("/Volumes/Fields/Media", mounts));
    }

    [Fact]
    public async Task MediaAncestorModeNeverChangesCatalogStorageAdmission()
    {
        var parent = Fixture(); var owned = Path.Combine(parent, "OwnedCatalog"); Directory.CreateDirectory(owned);
        var media = new StorageAssessmentRequest(Guid.NewGuid(), 0, StorageRole.MediaSource, StorageOperation.Read, parent);
        using var conservative = new MacStorageLocationAssessor([owned]);
        using var browser = new MacStorageLocationAssessor([owned], allowMediaSourceAncestor: true);
        Assert.NotEqual(StorageLocationDecision.Eligible, StorageLocationPolicy.Evaluate(media, await conservative.AssessAsync(media), DateTimeOffset.UtcNow).Decision);
        Assert.Equal(StorageLocationDecision.Eligible, StorageLocationPolicy.Evaluate(media, await browser.AssessAsync(media), DateTimeOffset.UtcNow).Decision);
        var active = new StorageAssessmentRequest(Guid.NewGuid(), 0, StorageRole.ActiveCatalog, StorageOperation.Open, parent);
        Assert.NotEqual(StorageLocationDecision.Eligible, StorageLocationPolicy.Evaluate(active, await browser.AssessAsync(active), DateTimeOffset.UtcNow).Decision);
        var inside = media with { OperationId = Guid.NewGuid(), RequestedLocation = owned };
        Assert.NotEqual(StorageLocationDecision.Eligible, StorageLocationPolicy.Evaluate(inside, await browser.AssessAsync(inside), DateTimeOffset.UtcNow).Decision);
    }

    [Fact]
    public async Task NativeDiscoveryCommitsWithoutPreviewsPreservesAuthoredStateAndReopens()
    {
        var data = Fixture(); var media = Path.Combine(data, "Media");
        Directory.CreateDirectory(Path.Combine(data, "Catalog"));
        Directory.CreateDirectory(Path.Combine(media, "Nested"));
        await File.WriteAllTextAsync(Path.Combine(media, "photo.JPG"), "original");
        await File.WriteAllTextAsync(Path.Combine(media, "notes.txt"), "unsupported");
        await File.WriteAllTextAsync(Path.Combine(media, "Nested", "clip.mov"), "nested");
        Guid catalog, root, asset, collection;
        await using (var shell = new MacShellCatalogSession(data))
        {
            Assert.True((await shell.CreateAsync(Path.Combine(data, "Catalog"))).IsReady);
            var first = await shell.DiscoverBrowserFolderAsync(media, Guid.Empty, "");
            Assert.Equal(MediaFolderEnumerationStatus.Succeeded, first.Status);
            asset = Assert.Single(first.Assets).AssetId; root = first.RootId;
            Assert.True(await shell.IsBrowserSourceAvailableAsync(root, asset));
            catalog = (await shell.ReadBrowserCatalogAsync()).CatalogId;
            var again = await shell.DiscoverBrowserFolderAsync(media, Guid.Empty, "");
            Assert.Equal(asset, Assert.Single(again.Assets).AssetId);
            await shell.SetBrowserRecursiveAsync(root, first.RelativeFolder, true);
            var recursive = await shell.DiscoverBrowserFolderAsync(media, root, first.RelativeFolder);
            Assert.True(recursive.Recursive); Assert.Equal(2, recursive.Assets.Count);
            var nested = recursive.Assets.Single(a => a.RelativePath.EndsWith("clip.mov"));
            var nestedFolder = Path.Combine(media, "Nested"); var permissions = File.GetUnixFileMode(nestedFolder);
            try
            {
                File.SetUnixFileMode(nestedFolder, UnixFileMode.None);
                var partial = await shell.DiscoverBrowserFolderAsync(media, root, first.RelativeFolder);
                Assert.DoesNotContain(partial.Assets, a => a.AssetId == nested.AssetId);
                Assert.False(string.IsNullOrWhiteSpace(partial.Diagnostic));
                Assert.Equal(MediaAssetSourceStatus.Available, (await shell.ReadBrowserCatalogAsync(root, first.RelativeFolder, recursive: true)).Assets.Single(a => a.AssetId == nested.AssetId).SourceStatus);
            }
            finally { File.SetUnixFileMode(nestedFolder, permissions); }
            await shell.SetBrowserRecursiveAsync(root, first.RelativeFolder, false);
            Assert.Single((await shell.DiscoverBrowserFolderAsync(media, root, first.RelativeFolder)).Assets);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shell.DiscoverBrowserFolderAsync(media, root, first.RelativeFolder, canceled.Token));
            await using var competitor = new MacShellCatalogSession(data);
            Assert.False((await competitor.StartAsync()).IsReady);
        }
        var opened = await new CatalogLifecycle(new MacCatalogLifecycleEffects(data)).StartAsync(allowFirstCreation: false);
        await using (var owner = opened.Coordinator!)
        {
            Assert.Equal(catalog, owner.CatalogSession.Identity.CatalogId);
            await new CatalogAssetClassificationStore(() => owner.Session).UpdateAsync(asset, prior => prior with { Rating = 5 });
            var collections = new CatalogCollectionOrganizationStore(() => owner.Session);
            collection = (await collections.CreateCollectionAsync("Preserved LF027")).CollectionId;
            await collections.AddMembershipAsync(collection, asset);
            Assert.False(File.Exists(owner.Locations.PreviewsDatabasePath));
        }
        await File.WriteAllTextAsync(Path.Combine(media, "photo.JPG"), "changed source, retained authored metadata");
        await using (var shell = new MacShellCatalogSession(data))
        {
            Assert.True((await shell.StartAsync()).IsReady);
            Assert.Equal(asset, Assert.Single((await shell.DiscoverBrowserFolderAsync(media, root, "")).Assets).AssetId);
            File.Move(Path.Combine(media, "photo.JPG"), Path.Combine(media, "renamed.jpg"));
            var renamed = await shell.DiscoverBrowserFolderAsync(media, root, "");
            Assert.NotEqual(asset, Assert.Single(renamed.Assets).AssetId);
            var preserved = await shell.ReadBrowserCatalogAsync(root, renamed.RelativeFolder);
            Assert.Equal(MediaAssetSourceStatus.Missing, preserved.Assets.Single(a => a.AssetId == asset).SourceStatus);
        }
        var reopened = await new CatalogLifecycle(new MacCatalogLifecycleEffects(data)).StartAsync(allowFirstCreation: false);
        await using (var owner = reopened.Coordinator!)
        {
            Assert.Equal(5, (await new CatalogAssetClassificationStore(() => owner.Session).GetAsync([asset]))[asset].Rating);
            Assert.Equal(asset, Assert.Single(await new CatalogCollectionOrganizationStore(() => owner.Session).ListMembershipsAsync(collection)).AssetId);
            Assert.Equal(root, (await new CatalogMediaAssetRepository(() => owner.Session).GetAsync(asset))!.RootId);
            Assert.False(File.Exists(owner.Locations.PreviewsDatabasePath));
        }
    }

    [Fact]
    public async Task ApplicationPackagesAreOpaqueWithoutRelaxingLinkRefusal()
    {
        var data = Fixture(); var media = Path.Combine(data, "Media");
        var package = Path.Combine(media, "Example.APP");
        var contents = Path.Combine(package, "Contents", "Resources");
        Directory.CreateDirectory(Path.Combine(data, "Catalog"));
        Directory.CreateDirectory(contents);
        await File.WriteAllTextAsync(Path.Combine(contents, "internal.png"), "package resource");
        await File.WriteAllTextAsync(Path.Combine(media, "photo.jpg"), "outside");
        Directory.CreateSymbolicLink(Path.Combine(contents, "loop"), package);
        var locations = new MacBrowserLocationProvider();
        var listing = await locations.GetFoldersAsync(media, default, true);
        Assert.Empty(listing.Folders);
        Assert.Equal(MediaFolderEnumerationStatus.FolderUnavailable,
            (await locations.GetFoldersAsync(contents, default, true)).Status);
        await using var shell = new MacShellCatalogSession(data);
        Assert.True((await shell.CreateAsync(Path.Combine(data, "Catalog"))).IsReady);
        var first = await shell.DiscoverBrowserFolderAsync(media, Guid.Empty, "");
        await shell.SetBrowserRecursiveAsync(first.RootId, first.RelativeFolder, true);
        var scan = await shell.DiscoverBrowserFolderAsync(media, first.RootId, first.RelativeFolder);
        Assert.Null(scan.Diagnostic); // Internal links were never enumerated.
        Assert.Equal("photo.jpg", Path.GetFileName(Assert.Single(scan.Assets).RelativePath));
        await Assert.ThrowsAsync<IOException>(() => shell.DiscoverBrowserFolderAsync(contents, Guid.Empty, ""));
        // A link masquerading as a package is still a link, never an exemption from safety.
        Directory.CreateSymbolicLink(Path.Combine(media, "Linked.app"), package);
        await Assert.ThrowsAsync<IOException>(() => shell.DiscoverBrowserFolderAsync(media, first.RootId, first.RelativeFolder));
        Assert.Equal(MediaAssetSourceStatus.Available,
            Assert.Single((await shell.ReadBrowserCatalogAsync(first.RootId, first.RelativeFolder, true)).Assets).SourceStatus);
    }

    [Fact]
    public async Task PersistenceRevisitAndStageCostsAreRecordedWithoutGui()
    {
        var evidenceRoot = Environment.GetEnvironmentVariable("LIGHTFLOW_BROWSER_QUALIFICATION_ROOT");
        var data = evidenceRoot is null ? Fixture() : Path.Combine(evidenceRoot, "Acceptance");
        // Never overwrite an existing acceptance Catalog or claim reused evidence is a fresh discovery.
        Assert.False(Directory.Exists(data));
        var existing = Path.Combine(data, "Media", "Existing");
        var newlyDiscovered = Path.Combine(data, "Media", "New");
        Directory.CreateDirectory(Path.Combine(data, "Catalog"));
        Directory.CreateDirectory(existing); Directory.CreateDirectory(newlyDiscovered);
        for (var i = 0; i < 24; i++)
            await File.WriteAllTextAsync(Path.Combine(existing, $"photo-{i:00}.jpg"), $"fixture {i}");
        await File.WriteAllTextAsync(Path.Combine(newlyDiscovered, "new.jpg"), "new source");
        var activities = new ConcurrentQueue<(string Name, double Milliseconds)>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BrowserPerformance.Source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Enqueue((activity.OperationName, activity.Duration.TotalMilliseconds))
        };
        ActivitySource.AddActivityListener(listener);
        var phases = new List<object>();
        async Task<T> Measure<T>(string name, Func<Task<T>> action)
        {
            while (activities.TryDequeue(out _)) { }
            var clock = Stopwatch.StartNew();
            var result = await action();
            phases.Add(new { phase = name, elapsedMs = clock.Elapsed.TotalMilliseconds,
                stages = activities.GroupBy(a => a.Name).Select(g => new { name = g.Key, count = g.Count(), totalMs = g.Sum(a => a.Milliseconds) }).ToArray() });
            return result;
        }
        Guid catalogId, rootId, assetId, collectionId;
        string folder;
        Guid newId;
        await using (var shell = new MacShellCatalogSession(data))
        {
            Assert.True((await shell.CreateAsync(Path.Combine(data, "Catalog"))).IsReady);
            var first = await Measure("existing-first-discovery", () => shell.DiscoverBrowserFolderAsync(existing, Guid.Empty, ""));
            Assert.Equal(24, first.Assets.Count);
            rootId = first.RootId; folder = first.RelativeFolder; assetId = first.Assets[0].AssetId;
            catalogId = (await shell.ReadBrowserCatalogAsync()).CatalogId;
        }
        var started = await new CatalogLifecycle(new MacCatalogLifecycleEffects(data)).StartAsync(allowFirstCreation: false);
        await using (var owner = started.Coordinator!)
        {
            await new CatalogAssetClassificationStore(() => owner.Session).UpdateAsync(assetId, prior => prior with { Rating = 4 });
            var store = new CatalogCollectionOrganizationStore(() => owner.Session);
            collectionId = (await store.CreateCollectionAsync("LF027 persistence")).CollectionId;
            await store.AddMembershipAsync(collectionId, assetId);
        }
        await using (var shell = new MacShellCatalogSession(data))
        {
            Assert.True((await Measure("reopen", shell.StartAsync)).IsReady);
            var read = await Measure("existing-catalog-read", () => shell.ReadBrowserCatalogAsync(rootId, folder));
            Assert.Equal(catalogId, read.CatalogId); Assert.Contains(read.Assets, a => a.AssetId == assetId);
            Assert.DoesNotContain(activities, a => a.Name is "mac.discovery" or "mac.filesystem.enumeration" or "mac.source.fingerprint");
            var revisit = await Measure("existing-navigation-revalidation", () => shell.DiscoverBrowserFolderAsync(existing, rootId, folder));
            Assert.Contains(revisit.Assets, a => a.AssetId == assetId);
            var discovered = await Measure("new-first-discovery", () => shell.DiscoverBrowserFolderAsync(newlyDiscovered, Guid.Empty, ""));
            newId = Assert.Single(discovered.Assets).AssetId;
            var repeated = await Measure("new-repeat-discovery", () => shell.DiscoverBrowserFolderAsync(newlyDiscovered, Guid.Empty, ""));
            Assert.Equal(newId, Assert.Single(repeated.Assets).AssetId);
        }
        await using (var shell = new MacShellCatalogSession(data))
        {
            Assert.True((await shell.StartAsync()).IsReady);
            var read = await Measure("new-reopened-catalog-read", () => shell.ReadBrowserCatalogAsync(rootId, Path.GetRelativePath("/", newlyDiscovered)));
            Assert.Equal(newId, Assert.Single(read.Assets).AssetId);
            Assert.DoesNotContain(activities, a => a.Name is "mac.discovery" or "mac.filesystem.enumeration" or "mac.source.fingerprint");
        }
        var reopened = await new CatalogLifecycle(new MacCatalogLifecycleEffects(data)).StartAsync(allowFirstCreation: false);
        await using (var owner = reopened.Coordinator!)
        {
            Assert.Equal(catalogId, owner.CatalogSession.Identity.CatalogId);
            Assert.Equal(4, (await new CatalogAssetClassificationStore(() => owner.Session).GetAsync([assetId]))[assetId].Rating);
            Assert.Equal(assetId, Assert.Single(await new CatalogCollectionOrganizationStore(() => owner.Session).ListMembershipsAsync(collectionId)).AssetId);
            Assert.Equal(rootId, (await new CatalogMediaAssetRepository(() => owner.Session).GetAsync(newId))!.RootId);
            Assert.False(File.Exists(owner.Locations.PreviewsDatabasePath));
        }
        var report = new { dataRoot = data, existing, newlyDiscovered, catalogId, rootId, assetId, newId, collectionId, rating = 4,
            phases, limitation = "Catalog-only reads do not scan. Explicit navigation/Refresh revalidates the requested scope using the unchanged shared Windows workflow; no persistent scan cache or whole-volume traversal." };
        var output = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(evidenceRoot ?? data, "persistence-timing.json"), output);
    }

    [Fact]
    public async Task NativeLinksRefuseMissingInferenceAndOwnedStorageIsExcluded()
    {
        var data = Fixture(); var media = Path.Combine(data, "Media");
        Directory.CreateDirectory(Path.Combine(data, "Catalog")); Directory.CreateDirectory(media);
        await File.WriteAllTextAsync(Path.Combine(media, "keep.jpg"), "keep");
        await using var shell = new MacShellCatalogSession(data);
        Assert.True((await shell.CreateAsync(Path.Combine(data, "Catalog"))).IsReady);
        var first = await shell.DiscoverBrowserFolderAsync(media, Guid.Empty, "");
        var asset = Assert.Single(first.Assets);
        File.CreateSymbolicLink(Path.Combine(media, "link.jpg"), Path.Combine(media, "keep.jpg"));
        File.Delete(Path.Combine(media, "keep.jpg"));
        await Assert.ThrowsAsync<IOException>(() => shell.DiscoverBrowserFolderAsync(media, first.RootId, first.RelativeFolder));
        var retained = await shell.ReadBrowserCatalogAsync(first.RootId, first.RelativeFolder);
        Assert.Equal(MediaAssetSourceStatus.Available, Assert.Single(retained.Assets).SourceStatus);
        Assert.Empty((await shell.DiscoverBrowserFolderAsync(Path.Combine(data, "Catalog"), Guid.Empty, "")).Assets);
    }
}
