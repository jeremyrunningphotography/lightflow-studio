using Lightflow.Application;
using Lightflow.Domain;
using Lightflow.Platform.MacOS;
using LightflowStudio;
using Xunit;

namespace Lightflow.Platform.MacOS.Tests;

[Collection("Native storage descriptor ownership")]
public sealed class MacBrowserDiscoveryTests
{
    private static string Fixture() => Path.Combine(Environment.GetEnvironmentVariable("LIGHTFLOW_TEST_ROOT")
        ?? Path.Combine(AppContext.BaseDirectory, "BrowserFixtures"), "lf027-" + Guid.NewGuid().ToString("N"));

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
            Assert.Equal(2, renamed.Assets.Count);
            Assert.Equal(MediaAssetSourceStatus.Missing, renamed.Assets.Single(a => a.AssetId == asset).SourceStatus);
            Assert.NotEqual(asset, renamed.Assets.Single(a => a.RelativePath.EndsWith("renamed.jpg")).AssetId);
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
