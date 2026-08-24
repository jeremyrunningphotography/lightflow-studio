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
        var physicalRoot = Directory.CreateTempSubdirectory("lightflow-handoff-map-").FullName;
        var physicalFolder = Directory.CreateDirectory(Path.Combine(physicalRoot, "shoot")).FullName;
        try
        {
            var rootId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var assets = new FakeAssets(Resolution(assetId, Path.Combine(physicalFolder, "clip.mov"), rootId: rootId));
            var roots = new FakeRoots(physicalFolder, rootId, physicalRoot);
            var invocation = new CapabilityInvocation("video.encode", [assetId],
                new CapabilitySourceContext(rootId, "shoot"));

            var result = await new EncodingCapabilityHandoff(assets, roots, new FakeRanges())
                .MaterializeAsync(invocation);

            Assert.True(result.Succeeded);
            Assert.Equal(physicalFolder, result.InputFolder);
            Assert.Equal(rootId, roots.LastRootLookup);
        }
        finally { Directory.Delete(physicalRoot, true); }
    }

    [Fact]
    public async Task Materialize_uses_directory_semantics_for_ordinary_and_recursive_browser_scopes()
    {
        var root = Directory.CreateTempSubdirectory("lightflow-handoff-root-").FullName;
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root, "shoot", "day-one")).FullName;
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var assets = new FakeAssets(Resolution(first, Path.Combine(root, "shoot", "one.mov")),
                Resolution(second, Path.Combine(nested, "two.mov")));
            var rootId = assets.RootIdFor(first);
            assets.SetRootId(second, rootId);
            var roots = new FakeRoots(Path.Combine(root, "shoot"), rootId, root);
            var handoff = new EncodingCapabilityHandoff(assets, roots, new FakeRanges());

            var ordinary = await handoff.MaterializeAsync(new CapabilityInvocation("video.encode", [first],
                new CapabilitySourceContext(rootId, "shoot")));
            var recursive = await handoff.MaterializeAsync(new CapabilityInvocation("video.encode", [first, second],
                new CapabilitySourceContext(rootId, "shoot")));

            Assert.True(ordinary.Succeeded);
            Assert.True(recursive.Succeeded);
            Assert.Equal(Path.Combine(root, "shoot"), recursive.InputFolder);
            Assert.True(recursive.IncludeSubfolders);
            Assert.Equal(["one.mov", Path.Combine("day-one", "two.mov")], recursive.Inputs.Select(input =>
                Path.GetRelativePath(recursive.InputFolder!, input.SourcePath)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Materialize_snapshots_heterogeneous_color_once_and_derives_current_active_semantics()
    {
        var cameraOnly = Guid.NewGuid();
        var creativeOnly = Guid.NewGuid();
        var disabledBoth = Guid.NewGuid();
        var none = Guid.NewGuid();
        var camera = Resource(ColorLutStage.Camera, 'a', "Camera A");
        var creative = Resource(ColorLutStage.Creative, 'b', "Creative B");
        var colors = new FakeColors(new Dictionary<Guid, AssetColorIntent>
        {
            [cameraOnly] = Intent(cameraOnly, true, CameraReference(camera)),
            [creativeOnly] = Intent(creativeOnly, true, creative: CameraReference(creative)),
            [disabledBoth] = Intent(disabledBoth, false, CameraReference(camera), CameraReference(creative)),
            [none] = Intent(none, true)
        });
        var assets = new FakeAssets(
            Resolution(cameraOnly, "C:\\media\\camera.mov"),
            Resolution(creativeOnly, "C:\\media\\creative.mov"),
            Resolution(disabledBoth, "C:\\media\\disabled.mov"),
            Resolution(none, "C:\\media\\none.mov"));
        var snapshots = new FakeResourceStore();
        var handoff = new EncodingCapabilityHandoff(assets, new FakeRoots(), new FakeRanges(), colors,
            new FakeLutCache(camera, creative), snapshots);

        var result = await handoff.MaterializeAsync(new CapabilityInvocation("video.encode",
            [cameraOnly, creativeOnly, disabledBoth, none]));
        colors.Values[cameraOnly] = Intent(cameraOnly, false, creative: CameraReference(creative));

        Assert.True(result.Succeeded);
        Assert.Equal(ColorLutStage.Camera, result.Inputs[0].AssignedColor!.OrderedPipeline.Single().Stage);
        Assert.Equal(ColorLutStage.Creative, result.Inputs[1].AssignedColor!.OrderedPipeline.Single().Stage);
        Assert.True(result.Inputs[2].AssignedColor!.ColorEnabled);
        Assert.Equal([ColorLutStage.Camera, ColorLutStage.Creative],
            result.Inputs[2].AssignedColor!.OrderedPipeline.Select(value => value.Stage));
        Assert.False(result.Inputs[3].AssignedColor!.ColorEnabled);
        Assert.Empty(result.Inputs[3].AssignedColor!.OrderedPipeline);
        Assert.True(result.Inputs[0].AssignedColor!.ColorEnabled);
        Assert.Equal(4, snapshots.Count);
        Assert.All(colors.ReadCounts.Values, count => Assert.Equal(1, count));
    }

    private static ManagedLutResource Resource(ColorLutStage stage, char value, string name)
    {
        var hash = new string(value, 64);
        return new(Guid.NewGuid(), name, name + ".cube", hash, LutDimension.ThreeDimensional, 2,
            LutResourceAvailability.Available, $"C:\\luts\\{name}.cube");
    }

    private static ColorLutReference CameraReference(ManagedLutResource resource) =>
        new(resource.LutId, resource.DisplayName, resource.ContentSha256, LutResourceAvailability.Available);

    private static AssetColorIntent Intent(Guid assetId, bool enabled, ColorLutReference? camera = null,
        ColorLutReference? creative = null) => new(assetId, camera, creative, Guid.NewGuid().ToString("N"), enabled);

    private static MediaAssetResolution Resolution(Guid id, string? path, string type = "video",
        MediaRootAvailability availability = MediaRootAvailability.Online, Guid? rootId = null)
    {
        var relative = path is null ? $"{id:D}.mov" : Path.GetFileName(path);
        var asset = new MediaAsset(id, rootId ?? Guid.NewGuid(), relative, relative.ToUpperInvariant(), type, 123, 456,
            null, MediaAssetSourceStatus.Available, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        return new(asset, availability, path, path is not null, path is null ? "The mapped root is unavailable." : null);
    }

    private sealed class FakeRoots(string? resolvedFolder = null, Guid? knownRootId = null, string? rootPath = null) : IMediaRootService
    {
        public (Guid RootId, string RelativePath)? LastResolution { get; private set; }
        public Guid? LastRootLookup { get; private set; }
        public Task<MediaPathResolution> ResolveAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastResolution = (rootId, relativePath);
            return Task.FromResult(new MediaPathResolution(rootId, relativePath,
                MediaPathSemantics.RelativePathKey(relativePath), resolvedFolder, MediaRootAvailability.Online,
                resolvedFolder is not null));
        }
        public Task<IReadOnlyList<MediaRootInfo>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaRootInfo?> GetAsync(Guid rootId, CancellationToken cancellationToken = default)
        {
            LastRootLookup = rootId;
            var path = rootPath ?? (resolvedFolder is null ? null : Directory.GetParent(resolvedFolder)?.FullName);
            return Task.FromResult<MediaRootInfo?>(knownRootId is null || knownRootId == rootId
                ? new MediaRootInfo(rootId, "Root", path, path is null ? MediaRootAvailability.Unavailable : MediaRootAvailability.Online)
                : null);
        }
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
        public Guid RootIdFor(Guid assetId) => _values[assetId].Asset.RootId;
        public void SetRootId(Guid assetId, Guid rootId)
        {
            var value = _values[assetId];
            _values[assetId] = value with { Asset = value.Asset with { RootId = rootId } };
        }
        public Task<MediaAssetResolution?> GetAsync(Guid assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(assetId));
        public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MediaAsset>>(_values.Values.Select(value => value.Asset).ToArray());
        public Task<MediaAssetOperationResult> CreateAsync(Guid rootId, string relativePath, string mediaType = "unknown", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetResolution?> FindAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MediaAssetOperationResult> ObserveAsync(Guid assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeColors(Dictionary<Guid, AssetColorIntent> values) : IAssetColorStore
    {
        public Dictionary<Guid, AssetColorIntent> Values { get; } = values;
        public Dictionary<Guid, int> ReadCounts { get; } = [];
        public Task<AssetColorIntent> GetAsync(Guid assetId, CancellationToken cancellationToken = default)
        { ReadCounts[assetId] = ReadCounts.GetValueOrDefault(assetId) + 1; return Task.FromResult(Values[assetId]); }
        public Task<IReadOnlyDictionary<Guid, AssetColorIntent>> GetAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, AssetColorIntent>>(assetIds.ToDictionary(id => id, id => Values[id]));
        public Task SetStageAsync(IReadOnlyCollection<Guid> assetIds, ColorLutStage stage, Guid? lutId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetAsync(IReadOnlyCollection<ColorAssignmentChange> changes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeLutCache(params ManagedLutResource[] resources) : ILutLibraryCache
    {
        public LutLibrarySnapshot Snapshot(ColorLutStage stage) => new("", resources.Where(resource =>
            (stage == ColorLutStage.Camera && resource.DisplayName.StartsWith("Camera"))
            || (stage == ColorLutStage.Creative && resource.DisplayName.StartsWith("Creative"))).ToArray(), []);
        public ManagedLutResource? Get(ColorLutStage stage, Guid lutId) => Snapshot(stage).Resources.FirstOrDefault(value => value.LutId == lutId);
        public string ResolvePath(ColorLutStage stage, Guid lutId) => Get(stage, lutId)!.FilePath!;
        public Task<CubeLutData> GetRuntimeAsync(ColorLutStage stage, Guid lutId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task InitializeAsync(string cameraFolder, string creativeFolder, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LutLibrarySnapshot> RefreshAsync(ColorLutStage stage, string folder, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot(stage));
    }

    private sealed class FakeResourceStore : IEncodingLutResourceStore
    {
        public int Count { get; private set; }
        public Task<MaterializedLutResource> SnapshotAsync(ColorLutStage stage, ManagedLutResource resource, CancellationToken cancellationToken = default)
        { Count++; return Task.FromResult(new MaterializedLutResource(resource.LutId, stage, resource.DisplayName,
            resource.ContentSha256, $"{resource.ContentSha256[..2]}/{resource.ContentSha256}.cube")); }
        public string Resolve(MaterializedLutResource resource) => resource.ResourceKey;
    }
}
