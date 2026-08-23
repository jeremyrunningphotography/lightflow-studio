using Xunit;

namespace LightflowStudio.Tests;

public sealed class EncodingCapabilityHandoffTests
{
    [Fact]
    public async Task Materialize_rejects_an_empty_invocation()
    {
        var result = await new EncodingCapabilityHandoff(new FakeAssets(), new FakeRoots(), new FakeRanges())
            .MaterializeAsync(new CapabilityInvocation("video.encode", []));

        Assert.False(result.Succeeded);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task Materialize_preserves_invocation_order_and_snapshots_ranges()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var range = new MediaRange(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(9));
        var assets = new FakeAssets(
            Resolution(first, "C:\\media\\z.mov"),
            Resolution(second, "D:\\remapped\\a.mp4"));
        var ranges = new FakeRanges(new Dictionary<Guid, MediaRange?> { [first] = range });

        var result = await new EncodingCapabilityHandoff(assets, new FakeRoots(), ranges).MaterializeAsync(
            new CapabilityInvocation("video.encode", [second, first]));

        Assert.True(result.Succeeded);
        Assert.Equal([second, first], result.Inputs.Select(input => input.AssetId));
        Assert.Null(result.Inputs[0].InitialTrim);
        Assert.Equal(range, result.Inputs[1].InitialTrim);
        Assert.NotSame(range, result.Inputs[1].InitialTrim);
    }

    [Fact]
    public async Task Materialize_rejects_the_entire_mixed_or_offline_selection()
    {
        var video = Guid.NewGuid();
        var image = Guid.NewGuid();
        var offline = Guid.NewGuid();
        var assets = new FakeAssets(
            Resolution(video, "C:\\media\\one.mov"),
            Resolution(image, "C:\\media\\still.jpg", "still-image"),
            Resolution(offline, null, availability: MediaRootAvailability.Unavailable));

        var result = await new EncodingCapabilityHandoff(assets, new FakeRoots(), new FakeRanges()).MaterializeAsync(
            new CapabilityInvocation("video.encode", [video, image, offline]));

        Assert.False(result.Succeeded);
        Assert.Empty(result.Inputs);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, error => error.Contains("not supported", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("offline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Materialized_trim_does_not_follow_later_catalog_changes()
    {
        var id = Guid.NewGuid();
        var ranges = new FakeRanges(new Dictionary<Guid, MediaRange?>
        {
            [id] = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2), null)
        });
        var handoff = new EncodingCapabilityHandoff(new FakeAssets(Resolution(id, "C:\\media\\one.mxf")),
            new FakeRoots(), ranges);

        var result = await handoff.MaterializeAsync(new CapabilityInvocation("video.encode", [id]));
        await ranges.SaveAsync(id, new MediaRange(TimeSpan.FromSeconds(30), null, TimeSpan.FromSeconds(12)));

        Assert.Equal(TimeSpan.FromSeconds(2), result.Inputs.Single().InitialTrim!.In);
        Assert.Null(result.Inputs.Single().InitialTrim!.Out);
    }

    [Fact]
    public async Task Materialize_copies_full_in_only_out_only_and_bounded_range_semantics()
    {
        var full = Guid.NewGuid();
        var inOnly = Guid.NewGuid();
        var outOnly = Guid.NewGuid();
        var bounded = Guid.NewGuid();
        var duration = TimeSpan.FromSeconds(40);
        var assets = new FakeAssets(
            Resolution(full, "C:\\media\\full.mov"), Resolution(inOnly, "C:\\media\\in.mov"),
            Resolution(outOnly, "C:\\media\\out.mov"), Resolution(bounded, "C:\\media\\bounded.mov"));
        var ranges = new FakeRanges(new Dictionary<Guid, MediaRange?>
        {
            [inOnly] = new(duration, TimeSpan.FromSeconds(5), null),
            [outOnly] = new(duration, null, TimeSpan.FromSeconds(30)),
            [bounded] = new(duration, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30))
        });

        var result = await new EncodingCapabilityHandoff(assets, new FakeRoots(), ranges).MaterializeAsync(
            new CapabilityInvocation("video.encode", [full, inOnly, outOnly, bounded]));

        Assert.Null(result.Inputs[0].InitialTrim);
        Assert.Equal((TimeSpan.FromSeconds(5), duration),
            (result.Inputs[1].InitialTrim!.EffectiveIn, result.Inputs[1].InitialTrim!.EffectiveOut));
        Assert.Equal((TimeSpan.Zero, TimeSpan.FromSeconds(30)),
            (result.Inputs[2].InitialTrim!.EffectiveIn, result.Inputs[2].InitialTrim!.EffectiveOut));
        Assert.Equal((TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)),
            (result.Inputs[3].InitialTrim!.EffectiveIn, result.Inputs[3].InitialTrim!.EffectiveOut));
    }

    [Fact]
    public async Task Materialize_reresolves_originating_folder_through_current_root_mapping()
    {
        var rootId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var assets = new FakeAssets(Resolution(assetId, "D:\\remapped\\shoot\\clip.mov", rootId: rootId));
        var roots = new FakeRoots("D:\\remapped\\shoot");
        var invocation = new CapabilityInvocation("video.encode", [assetId],
            new CapabilitySourceContext(rootId, "shoot"));

        var result = await new EncodingCapabilityHandoff(assets, roots, new FakeRanges())
            .MaterializeAsync(invocation);

        Assert.True(result.Succeeded);
        Assert.Equal("D:\\remapped\\shoot", result.InputFolder);
        Assert.Equal((rootId, "shoot"), roots.LastResolution);
    }

    private static MediaAssetResolution Resolution(Guid id, string? path, string type = "video",
        MediaRootAvailability availability = MediaRootAvailability.Online, Guid? rootId = null)
    {
        var relative = path is null ? $"{id:D}.mov" : Path.GetFileName(path);
        var asset = new MediaAsset(id, rootId ?? Guid.NewGuid(), relative, relative.ToUpperInvariant(), type, 123, 456,
            null, MediaAssetSourceStatus.Available, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        return new(asset, availability, path, path is not null, path is null ? "The mapped root is unavailable." : null);
    }

    private sealed class FakeRoots(string? resolvedFolder = null) : IMediaRootService
    {
        public (Guid RootId, string RelativePath)? LastResolution { get; private set; }
        public Task<MediaPathResolution> ResolveAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastResolution = (rootId, relativePath);
            return Task.FromResult(new MediaPathResolution(rootId, relativePath,
                MediaPathSemantics.RelativePathKey(relativePath), resolvedFolder, MediaRootAvailability.Online,
                resolvedFolder is not null));
        }
        public Task<IReadOnlyList<MediaRootInfo>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootInfo?> GetAsync(Guid rootId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> CreateAsync(string displayName, string physicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RenameAsync(Guid rootId, string displayName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootChangeResult> RemapAsync(Guid rootId, string physicalPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRanges(Dictionary<Guid, MediaRange?>? values = null) : IMediaRangeStore
    {
        private readonly Dictionary<Guid, MediaRange?> _values = values ?? [];
        public Task<MediaRange?> RestoreAsync(Guid assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(assetId));
        public Task SaveAsync(Guid assetId, MediaRange? range, CancellationToken cancellationToken = default)
        {
            _values[assetId] = range;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAssets(params MediaAssetResolution[] values) : IMediaAssetService
    {
        private readonly Dictionary<Guid, MediaAssetResolution> _values = values.ToDictionary(value => value.Asset.AssetId);
        public Task<MediaAssetResolution?> GetAsync(Guid assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(assetId));
        public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaAsset>>(_values.Values.Select(value => value.Asset).ToArray());
        public Task<MediaAssetOperationResult> CreateAsync(Guid rootId, string relativePath, string mediaType = "unknown", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetResolution?> FindAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetOperationResult> ObserveAsync(Guid assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
