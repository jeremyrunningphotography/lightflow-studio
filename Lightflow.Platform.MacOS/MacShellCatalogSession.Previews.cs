using LightflowStudio;
namespace Lightflow.Platform.MacOS;

public sealed partial class MacShellCatalogSession
{
    private CatalogPreviewStoreService? _previews;
    private SharedDerivedWorkScheduler? _previewScheduler;
    private PreviewOperationCoordinator? _previewOperations;
    private MacPreviewColorSource? _previewColors;
    private async Task EnsurePreviewsAsync(CatalogLifecycle owner, CancellationToken token)
    {
        if (_previews is not null || thumbnailCodec is null) return;
        var guard = new MacCatalogNativeEffects(DataRoot);
        // Cache must remain beneath this host's admitted local data root. No completion-peer evidence is fabricated.
        if (!MacCatalogNativeEffects.Contains(DataRoot, owner.Locations.PreviewsDirectory)) throw new IOException("Preview storage must remain local to this data root.");
        var store = new CatalogPreviewStoreService(owner.Locations, guard: path => { guard.GuardAccess(path); return path; });
        try
        {
            await store.InitializeAsync(token).ConfigureAwait(false);
            var effects = MediaEffects(owner);
            var assets = new MediaAssetService(new CatalogMediaAssetRepository(() => owner.Session),
                new CatalogMediaRootService(() => owner.Session, new MachineIdentityProvider(Path.Combine(DataRoot, "machine-id")), new MediaFiles(), effects), new NativeFingerprints(effects));
            var renderer = new MacThumbnailRenderer(thumbnailCodec, thumbnailObserved);
            var colors = new MacPreviewColorSource(owner, guard);
            var preferred = new CatalogPreferredPreviewFrameStore(() => owner.Session);
            var operations = new PreviewOperationCoordinator();
            var metadata = new DerivedMediaMetadataService(assets, store, renderer);
            var thumbnails = new SharedThumbnailGenerationService(assets, store, owner.Locations, renderer, metadata, operations: operations,
                colorSource: colors, validate: renderer.Validate, preferredFrames: preferred, pathGuard: path => { guard.GuardAccess(path); return path; });
            _previewOperations = operations; _previews = store; _previewColors = colors;
            _previewScheduler = new(assets, store, metadata, thumbnails, ownsGenerators: true, operations: operations, colors: colors,
                artifactExists: relative => File.Exists(store.ResolveArtifactPath(relative)), preferredFrames: preferred, excludedPath: effects.IsProtected,
                supportsThumbnail: type => type is "image" or "raw-image" or "video");
        }
        catch { _previews = null; _previewColors = null; _previewOperations?.Dispose(); _previewOperations = null; await store.DisposeAsync(); throw; }
    }
    public Task<IDerivedWorkBatch?> DemandThumbnailsAsync(IReadOnlyList<Guid> ids, DerivedWorkPriority priority, CancellationToken token) =>
        BorrowAsync<IDerivedWorkBatch?>(async owner =>
        {
            using var timing = ThumbnailPerformance.Measure("request.create");
            timing?.SetTag("count", ids.Count); timing?.SetTag("priority", priority.ToString());
            if (ids.Count > 192) throw new ArgumentException("Thumbnail demand must be viewport bounded.", nameof(ids));
            await EnsurePreviewsAsync(owner, token).ConfigureAwait(false);
            if (_previewScheduler is null) return null;
            var assets = await new CatalogMediaAssetRepository(() => owner.Session).GetManyAsync(ids.ToArray(), token).ConfigureAwait(false);
            var reconciliation = new CatalogReconciliationResult(CatalogReconciliationStatus.Succeeded, Guid.Empty, "",
                assets.Values.Select(x => new CatalogReconciliationItem(x.AssetId, x.RelativePath, x.SourceStatus == MediaAssetSourceStatus.Missing ? CatalogReconciliationItemStatus.Missing : CatalogReconciliationItemStatus.Unchanged)).ToArray());
            var demand = _previewScheduler.TrySchedule(reconciliation, priority, token);
            return demand.Batch;
        }, token);
    public Task<IReadOnlyDictionary<Guid, BrowserThumbnail>> ReadThumbnailsAsync(IReadOnlyList<Guid> ids, CancellationToken token) =>
        BorrowAsync<IReadOnlyDictionary<Guid, BrowserThumbnail>>(async owner =>
        {
            using var timing = ThumbnailPerformance.Measure("cache.project");
            timing?.SetTag("count", ids.Count);
            if (ids.Count > 192) throw new ArgumentException("Thumbnail reads must be viewport bounded.", nameof(ids));
            if (_previews is null) return new Dictionary<Guid, BrowserThumbnail>();
            var records = await _previews.GetManyAsync(ids.ToArray(), token).ConfigureAwait(false);
            var assets = await new CatalogMediaAssetRepository(() => owner.Session).GetManyAsync(ids.ToArray(), token).ConfigureAwait(false);
            var preferred = new CatalogPreferredPreviewFrameStore(() => owner.Session);
            var rotations = await new CatalogAssetVideoRotationStore(() => owner.Session).GetAsync(ids.ToArray(), token).ConfigureAwait(false);
            var result = new Dictionary<Guid, BrowserThumbnail>();
            foreach (var (id, record) in records)
            {
                string? path = null;
                var sameSource = assets.TryGetValue(id, out var asset) && BrowserPreviewReuse.Matches(asset with { SourceStatus = MediaAssetSourceStatus.Available }, record);
                var color = await _previewColors!.IdentityAsync(id, token).ConfigureAwait(false);
                var visual = SharedThumbnailGenerationService.BrowserVisualIdentity(color, await preferred.GetAsync(id, token).ConfigureAwait(false));
                var sameVisual = string.Equals(record.ThumbnailVisualIdentity ?? PreviewVisualIdentity.Original, visual, StringComparison.Ordinal);
                if (sameSource && sameVisual && BrowserPreviewReuse.HasRetainedThumbnail(record, _previews) && record.ThumbnailRelativePath is { } relative)
                { var resolved = _previews.ResolveArtifactPath(relative); if (File.Exists(resolved)) path = resolved; }
                var state = !sameSource || !sameVisual ? "Loading" : record.ThumbnailState == PreviewComponentState.Failed ? record.ThumbnailFailureReason switch
                { PreviewFailureReason.CodecUnavailable => "Unsupported", PreviewFailureReason.DependencyUnavailable => "LUT unavailable", _ => "Preview failed" }
                    : asset?.SourceStatus == MediaAssetSourceStatus.Missing || record.SourceAvailability is PreviewSourceAvailability.Missing or PreviewSourceAvailability.Unavailable ? "Unavailable" : path is null ? "Loading" : "Ready";
                result[id] = new(id, path, state, rotations.TryGetValue(id, out var rotation) ? rotation.Rotation.Degrees : 0);
            }
            return result;
        }, token);
    private async Task RetirePreviewsAsync()
    {
        var scheduler = _previewScheduler; _previewScheduler = null;
        if (scheduler is not null) await scheduler.DisposeAsync().ConfigureAwait(false);
        var previews = _previews; _previews = null;
        if (previews is not null) await previews.DisposeAsync().ConfigureAwait(false);
        _previewColors = null; _previewOperations?.Dispose(); _previewOperations = null;
    }
}
