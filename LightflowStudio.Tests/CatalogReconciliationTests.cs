using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class CatalogReconciliationTests : IAsyncLifetime
{
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), $"lightflow-reconcile-{Guid.NewGuid():N}");
    private string _media = null!;
    private LightflowStorageCoordinator _coordinator = null!;
    private MediaRootInfo _root = null!;

    [Fact]
    public async Task FirstAndRepeatedRefreshDistinguishNewAndUnchangedSupportedMedia()
    {
        Write("photo.jpg", "photo");
        Write("clip.mp4", "video");
        Write("notes.txt", "ignored");

        var first = await Refresh();
        var second = await Refresh();

        Assert.True(first.Succeeded, first.Diagnostic);
        Assert.Equal(2, first.NewCount);
        Assert.Equal(1, first.UnsupportedCount);
        Assert.Equal(0, first.MissingCount);
        Assert.True(second.Succeeded, second.Diagnostic);
        Assert.Equal(2, second.UnchangedCount);
        Assert.Equal(0, second.NewCount);
        Assert.Equal(first.Items.Select(item => item.AssetId).Order(),
            second.Items.Select(item => item.AssetId).Order());
        Assert.Equal(2, (await _coordinator.MediaAssets.ListAsync()).Count);
    }

    [Fact]
    public async Task ChangedAndNewFilesPreserveExistingIdentityAtSameLocation()
    {
        var path = Write("clip.mp4", "first");
        var initial = await Refresh();
        var originalId = Assert.Single(initial.Items).AssetId;
        await File.WriteAllTextAsync(path, "a changed and larger source");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(2));
        Write("new.mov", "new");

        var refreshed = await Refresh();

        Assert.Equal(1, refreshed.ChangedCount);
        Assert.Equal(1, refreshed.NewCount);
        Assert.Equal(originalId, refreshed.Items.Single(item => item.RelativePath == "clip.mp4").AssetId);
        Assert.Equal(originalId, (await _coordinator.MediaAssets.FindAsync(_root.RootId, "clip.mp4"))!.Asset.AssetId);
    }

    [Fact]
    public async Task VersionedFingerprintDetectsChangeWhenSizeAndTimestampArePreserved()
    {
        var path = Write("clip.mp4", "AAAA");
        var initial = await Refresh();
        var originalId = Assert.Single(initial.Items).AssetId;
        var originalWrite = File.GetLastWriteTimeUtc(path);
        await File.WriteAllTextAsync(path, "BBBB");
        File.SetLastWriteTimeUtc(path, originalWrite);

        var refreshed = await Refresh();

        Assert.Equal(1, refreshed.ChangedCount);
        Assert.Equal(originalId, Assert.Single(refreshed.Items).AssetId);
    }

    [Fact]
    public async Task PresentUnsupportedFileIsNotCreatedOrMisreportedAsMissing()
    {
        Write("notes.txt", "present but unsupported");
        var existing = await _coordinator.MediaAssets.CreateAsync(_root.RootId, "notes.txt", "unknown");

        var result = await Refresh();
        var persisted = await _coordinator.MediaAssets.GetAsync(existing.Asset!.Asset.AssetId);

        Assert.True(result.Succeeded, result.Diagnostic);
        Assert.Equal(1, result.UnsupportedCount);
        Assert.Empty(result.Items);
        Assert.Equal(MediaAssetSourceStatus.Available, persisted!.Asset.SourceStatus);
    }

    [Fact]
    public async Task SuccessfulAuthoritativeRefreshMarksOnlyTrueMissingDirectChildren()
    {
        var removed = Write("removed.mp4", "removed");
        Write("kept.mp4", "kept");
        Write("sub/child.mp4", "child");
        await Refresh();
        await _coordinator.CatalogReconciliation.ReconcileAsync(new(_root.RootId, "sub"));
        File.Delete(removed);

        var result = await Refresh();
        var assets = await _coordinator.MediaAssets.ListAsync();

        Assert.True(result.Succeeded, result.Diagnostic);
        Assert.Equal(1, result.MissingCount);
        Assert.Equal(MediaAssetSourceStatus.Missing,
            assets.Single(asset => asset.RelativePath == "removed.mp4").SourceStatus);
        Assert.Equal(MediaAssetSourceStatus.Available,
            assets.Single(asset => asset.RelativePath == "kept.mp4").SourceStatus);
        Assert.Equal(MediaAssetSourceStatus.Available,
            assets.Single(asset => asset.RelativePath == "sub/child.mp4").SourceStatus);
    }

    [Theory]
    [InlineData("RootUnavailable", "RootUnavailable")]
    [InlineData("Failed", "EnumerationFailed")]
    public async Task NonAuthoritativeEnumerationNeverMarksExistingAssetsMissing(
        string enumerationStatus, string expectedStatus)
    {
        Write("clip.mp4", "source");
        await Refresh();
        var fake = new FixedEnumerator(new(Enum.Parse<MediaFolderEnumerationStatus>(enumerationStatus), "", [], "offline"));
        var service = new CatalogReconciliationService(fake, _coordinator.MediaAssets);

        var result = await service.ReconcileAsync(new(_root.RootId));

        Assert.Equal(Enum.Parse<CatalogReconciliationStatus>(expectedStatus), result.Status);
        Assert.Equal(MediaAssetSourceStatus.Available,
            Assert.Single(await _coordinator.MediaAssets.ListAsync()).SourceStatus);
    }

    [Fact]
    public async Task CancellationBeforeAuthoritativeCompletionDoesNotMarkUnseenAssetsMissing()
    {
        Write("clip.mp4", "source");
        await Refresh();
        var service = new CatalogReconciliationService(new CanceledEnumerator(), _coordinator.MediaAssets);

        var result = await service.ReconcileAsync(new(_root.RootId));

        Assert.Equal(CatalogReconciliationStatus.Canceled, result.Status);
        Assert.Equal(MediaAssetSourceStatus.Available,
            Assert.Single(await _coordinator.MediaAssets.ListAsync()).SourceStatus);
    }

    [Fact]
    public async Task ReconciliationPersistsAcrossCatalogReopen()
    {
        Write("clip.mp4", "source");
        var first = await Refresh();
        var assetId = Assert.Single(first.Items).AssetId;
        await _coordinator.DisposeAsync();
        _coordinator = (await LightflowStorageCoordinator.StartAsync(Path.Combine(_temporary, "app"))).Coordinator!;

        var repeated = await _coordinator.CatalogReconciliation.ReconcileAsync(new(_root.RootId));

        Assert.Equal(1, repeated.UnchangedCount);
        Assert.Equal(assetId, Assert.Single(repeated.Items).AssetId);
    }

    [Fact]
    public async Task LargeDirectoryReconcilesDeterministicallyWithoutDuplicateAssets()
    {
        const int count = 400;
        for (var index = 0; index < count; index++) Write($"clip-{index:D4}.mp4", index.ToString());

        var first = await Refresh();
        var second = await Refresh();

        Assert.Equal(count, first.NewCount);
        Assert.Equal(count, second.UnchangedCount);
        Assert.Equal(count, (await _coordinator.MediaAssets.ListAsync()).Count);
        Assert.Equal(first.Items.OrderBy(item => item.RelativePath).Select(item => item.AssetId),
            second.Items.OrderBy(item => item.RelativePath).Select(item => item.AssetId));
    }

    public async Task InitializeAsync()
    {
        _media = Directory.CreateDirectory(Path.Combine(_temporary, "media")).FullName;
        _coordinator = (await LightflowStorageCoordinator.StartAsync(Path.Combine(_temporary, "app"))).Coordinator!;
        _root = (await _coordinator.MediaRoots.CreateAsync("Media", _media)).Root!;
    }

    public async Task DisposeAsync()
    {
        await _coordinator.DisposeAsync();
        try { Directory.Delete(_temporary, recursive: true); } catch { }
    }

    private Task<CatalogReconciliationResult> Refresh() =>
        _coordinator.CatalogReconciliation.ReconcileAsync(new(_root.RootId));

    private string Write(string relativePath, string contents)
    {
        var path = MediaPathSemantics.ResolveContained(_media, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private sealed class FixedEnumerator(MediaFolderEnumerationResult result) : IMediaFolderEnumerator
    {
        public Task<MediaFolderEnumerationResult> EnumerateAsync(MediaFolderEnumerationRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class CanceledEnumerator : IMediaFolderEnumerator
    {
        public Task<MediaFolderEnumerationResult> EnumerateAsync(MediaFolderEnumerationRequest request,
            CancellationToken cancellationToken = default) => Task.FromCanceled<MediaFolderEnumerationResult>(
                new CancellationToken(canceled: true));
    }
}
