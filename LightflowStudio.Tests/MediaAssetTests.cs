using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class MediaAssetTests : IDisposable
{
    private readonly string _temporary = Path.Combine(Path.GetTempPath(), $"lightflow-assets-{Guid.NewGuid():N}");

    [Fact]
    public async Task AssetId_PersistsAcrossCatalogReopen()
    {
        await using var fixture = await Fixture.CreateAsync(_temporary);
        var (root, _) = await fixture.AddRootWithFileAsync("Originals", "day/clip.mp4", "source");
        var created = await fixture.Assets.CreateAsync(root.RootId, "day/clip.mp4", "video");
        var assetId = created.Asset!.Asset.AssetId;
        await fixture.ReopenAsync();

        var reopened = await fixture.Assets.GetAsync(assetId);

        Assert.Equal(assetId, reopened!.Asset.AssetId);
        Assert.Equal("day/clip.mp4", reopened.Asset.RelativePath);
        Assert.Equal(MediaAssetSourceStatus.Available, reopened.Asset.SourceStatus);
    }

    [Fact]
    public async Task SameRelativePath_InDifferentRootsHasDifferentStableIdentity()
    {
        await using var fixture = await Fixture.CreateAsync(_temporary);
        var (first, _) = await fixture.AddRootWithFileAsync("First", "clip.mp4", "one");
        var (second, _) = await fixture.AddRootWithFileAsync("Second", "clip.mp4", "two");

        var firstAsset = await fixture.Assets.CreateAsync(first.RootId, "clip.mp4", "video");
        var secondAsset = await fixture.Assets.CreateAsync(second.RootId, "clip.mp4", "video");

        Assert.True(firstAsset.Succeeded); Assert.True(secondAsset.Succeeded);
        Assert.NotEqual(firstAsset.Asset!.Asset.AssetId, secondAsset.Asset!.Asset.AssetId);
    }

    [Fact]
    public async Task CreateWithNonexistentRootReturnsRootNotFoundWithoutCreatingAsset()
    {
        await using var fixture = await Fixture.CreateAsync(_temporary);

        var exception = await Record.ExceptionAsync(async () =>
        {
            var result = await fixture.Assets.CreateAsync(Guid.NewGuid(), "clip.mp4", "video");
            Assert.Equal(MediaAssetOperationStatus.RootNotFound, result.Status);
            Assert.Null(result.Asset);
        });

        Assert.Null(exception);
        Assert.Equal(0L, fixture.AssetCount());
    }

    [Fact]
    public async Task LogicalLookup_NormalizesSeparatorsAndCase_AndRejectsDuplicate()
    {
        await using var fixture = await Fixture.CreateAsync(_temporary);
        var (root, _) = await fixture.AddRootWithFileAsync("Originals", "Day One/Clip.mp4", "source");
        var created = await fixture.Assets.CreateAsync(root.RootId, @"Day One\Clip.mp4", "video");

        var found = await fixture.Assets.FindAsync(root.RootId, "day one/CLIP.MP4");
        var duplicate = await fixture.Assets.CreateAsync(root.RootId, "DAY ONE//clip.mp4", "video");

        Assert.Equal(created.Asset!.Asset.AssetId, found!.Asset.AssetId);
        Assert.Equal(MediaAssetOperationStatus.AlreadyExists, duplicate.Status);
        Assert.Equal(1L, fixture.AssetCount());
    }

    [Fact]
    public async Task ObservationChangesFactsAndFingerprintWithoutChangingAssetId()
    {
        await using var fixture = await Fixture.CreateAsync(_temporary);
        var (root, path) = await fixture.AddRootWithFileAsync("Originals", "clip.mp4", "first");
        var created = await fixture.Assets.CreateAsync(root.RootId, "clip.mp4", "video");
        var original = created.Asset!.Asset;
        await File.WriteAllTextAsync(path, "second version is larger");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        var observed = await fixture.Assets.ObserveAsync(original.AssetId);

        Assert.True(observed.Succeeded);
        Assert.Equal(original.AssetId, observed.Asset!.Asset.AssetId);
        Assert.NotEqual(original.FileSizeBytes, observed.Asset.Asset.FileSizeBytes);
        Assert.NotEqual(original.LastWriteUtcTicks, observed.Asset.Asset.LastWriteUtcTicks);
        Assert.NotEqual(original.Fingerprint, observed.Asset.Asset.Fingerprint);
        Assert.Equal(MediaAssetSourceStatus.Available, observed.Asset.Asset.SourceStatus);
        Assert.NotNull(observed.Asset.Asset.LastSeenUtc);
    }

    [Fact]
    public async Task TimestampOnlyObservationUpdatesFactsButKeepsFingerprintAndAssetId()
    {
        await using var fixture = await Fixture.CreateAsync(_temporary);
        var (root, path) = await fixture.AddRootWithFileAsync("Originals", "clip.mp4", "unchanged bytes");
        var created = await fixture.Assets.CreateAsync(root.RootId, "clip.mp4", "video");
        var original = created.Asset!.Asset;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(2));

        var observed = await fixture.Assets.ObserveAsync(original.AssetId);

        Assert.Equal(original.AssetId, observed.Asset!.Asset.AssetId);
        Assert.NotEqual(original.LastWriteUtcTicks, observed.Asset.Asset.LastWriteUtcTicks);
        Assert.Equal(original.Fingerprint, observed.Asset.Asset.Fingerprint);
    }

    [Fact]
    public async Task RootRemapChangesResolutionWithoutChangingAssetOrCatalogLocation()
    {
        await using var fixture = await Fixture.CreateAsync(_temporary);
        var (root, oldPath) = await fixture.AddRootWithFileAsync("Originals", "day/clip.mp4", "source");
        var created = await fixture.Assets.CreateAsync(root.RootId, "day/clip.mp4", "video");
        var newRoot = Directory.CreateDirectory(Path.Combine(_temporary, "remapped")).FullName;
        var newPath = Path.Combine(newRoot, "day", "clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.Copy(oldPath, newPath);
        Assert.True((await fixture.Roots.RemapAsync(root.RootId, newRoot)).Succeeded);

        var remapped = await fixture.Assets.GetAsync(created.Asset!.Asset.AssetId);

        Assert.Equal(created.Asset.Asset.AssetId, remapped!.Asset.AssetId);
        Assert.Equal(root.RootId, remapped.Asset.RootId);
        Assert.Equal("day/clip.mp4", remapped.Asset.RelativePath);
        Assert.Equal(newPath, remapped.PhysicalPath);
    }

    [Fact]
    public async Task MissingChildIsPersistedAsMissing_ButOfflineRootDoesNotChangeAssetStatus()
    {
        await using var fixture = await Fixture.CreateAsync(_temporary);
        var (root, path) = await fixture.AddRootWithFileAsync("Originals", "clip.mp4", "source");
        var created = await fixture.Assets.CreateAsync(root.RootId, "clip.mp4", "video");
        File.Delete(path);

        var missing = await fixture.Assets.ObserveAsync(created.Asset!.Asset.AssetId);
        Assert.Equal(MediaAssetOperationStatus.SourceMissing, missing.Status);
        Assert.Equal(MediaRootAvailability.Online, missing.Asset!.RootAvailability);
        Assert.Equal(MediaAssetSourceStatus.Missing, missing.Asset.Asset.SourceStatus);

        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        var offline = await fixture.Assets.ObserveAsync(created.Asset.Asset.AssetId);
        Assert.Equal(MediaAssetOperationStatus.RootUnavailable, offline.Status);
        Assert.Equal(MediaRootAvailability.Unavailable, offline.Asset!.RootAvailability);
        Assert.Equal(MediaAssetSourceStatus.Missing, offline.Asset.Asset.SourceStatus);
    }

    [Fact]
    public async Task OfflineRootDoesNotTurnPreviouslyAvailableAssetIntoMissing()
    {
        await using var fixture = await Fixture.CreateAsync(_temporary);
        var (root, path) = await fixture.AddRootWithFileAsync("Originals", "clip.mp4", "source");
        var created = await fixture.Assets.CreateAsync(root.RootId, "clip.mp4", "video");
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);

        var offline = await fixture.Assets.ObserveAsync(created.Asset!.Asset.AssetId);

        Assert.Equal(MediaAssetOperationStatus.RootUnavailable, offline.Status);
        Assert.Equal(MediaAssetSourceStatus.Available, offline.Asset!.Asset.SourceStatus);
    }

    [Fact]
    public async Task UnicodeAndLongRelativePathRoundTrips()
    {
        await using var fixture = await Fixture.CreateAsync(_temporary);
        var relative = $"撮影/{new string('a', 120)}/Été 🔥.mp4";
        var (root, _) = await fixture.AddRootWithFileAsync("Unicode", relative, "source");

        var created = await fixture.Assets.CreateAsync(root.RootId, relative, "video");
        var found = await fixture.Assets.FindAsync(root.RootId, relative.ToUpperInvariant());

        Assert.True(created.Succeeded);
        Assert.Equal(relative, created.Asset!.Asset.RelativePath);
        Assert.Equal(created.Asset.Asset.AssetId, found!.Asset.AssetId);
    }

    [Fact]
    public async Task FingerprintIsVersionedAndBoundedRatherThanFullFileHash()
    {
        Directory.CreateDirectory(_temporary);
        var path = Path.Combine(_temporary, "large.bin");
        await File.WriteAllBytesAsync(path, new byte[SampledSourceFingerprintService.SampleBytes * 4]);
        var service = new SampledSourceFingerprintService();
        var original = await service.CreateAsync(path);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.Position = SampledSourceFingerprintService.SampleBytes * 2;
            stream.WriteByte(42);
        }
        var middleChanged = await service.CreateAsync(path);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.Position = 0;
            stream.WriteByte(42);
        }
        var sampledChanged = await service.CreateAsync(path);

        Assert.Equal(SampledSourceFingerprintService.CurrentVersion, original.Version);
        Assert.Equal(original, middleChanged);
        Assert.NotEqual(original, sampledChanged);
    }

    [Fact]
    public async Task DuplicateRepositoryInsertRollsBackWithoutChangingOriginal()
    {
        await using var fixture = await Fixture.CreateAsync(_temporary);
        var (root, _) = await fixture.AddRootWithFileAsync("Originals", "clip.mp4", "source");
        var created = await fixture.Assets.CreateAsync(root.RootId, "clip.mp4", "video");
        var original = created.Asset!.Asset;
        var duplicate = original with { AssetId = Guid.NewGuid(), MediaType = "audio" };

        var result = await fixture.Repository.CreateAsync(duplicate);
        var persisted = await fixture.Repository.GetAsync(original.AssetId);

        Assert.Equal(MediaAssetOperationStatus.AlreadyExists, result);
        Assert.Equal(1L, fixture.AssetCount());
        Assert.Equal("video", persisted!.MediaType);
    }

    public void Dispose() { try { Directory.Delete(_temporary, recursive: true); } catch { } }

    private sealed class FixedMachine : IMachineIdentityProvider
    {
        public string GetMachineId() => "asset-test-machine";
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly LightflowStorageLocations _locations;
        private CatalogDatabaseSession _session;

        private Fixture(LightflowStorageLocations locations, CatalogDatabaseSession session)
        {
            _locations = locations;
            _session = session;
            RebuildServices();
        }

        public MediaRootService Roots { get; private set; } = null!;
        public CatalogMediaAssetRepository Repository { get; private set; } = null!;
        public MediaAssetService Assets { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync(string temporary)
        {
            var locations = LightflowStorageLocations.Create(Path.Combine(temporary, "app"));
            var opened = await new CatalogDatabaseService(locations).CreateNewAsync();
            return new(locations, opened.Session!);
        }

        public async Task<(MediaRootInfo Root, string FilePath)> AddRootWithFileAsync(
            string name, string relativePath, string contents)
        {
            var rootPath = Directory.CreateDirectory(Path.Combine(_locations.ApplicationDataDirectory,
                $"source-{Guid.NewGuid():N}")).FullName;
            var filePath = MediaPathSemantics.ResolveContained(rootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, contents);
            var root = await Roots.CreateAsync(name, rootPath);
            return (root.Root!, filePath);
        }

        public async Task ReopenAsync()
        {
            await _session.DisposeAsync();
            _session = (await new CatalogDatabaseService(_locations).OpenExistingAsync()).Session!;
            RebuildServices();
        }

        public long AssetCount()
        {
            using var connection = _session.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM MediaAssets;";
            return Convert.ToInt64(command.ExecuteScalar());
        }

        private void RebuildServices()
        {
            Roots = new MediaRootService(() => _session, new FixedMachine(), new MediaRootFileSystem());
            Repository = new CatalogMediaAssetRepository(() => _session);
            Assets = new MediaAssetService(Repository, Roots, new SampledSourceFingerprintService());
        }

        public ValueTask DisposeAsync() => _session.DisposeAsync();
    }
}
