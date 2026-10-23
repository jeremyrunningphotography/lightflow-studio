using Lightflow.Application;
using LightflowStudio;

namespace Lightflow.Platform.MacOS;

/// <summary>Thin shell composition adapter; CatalogLifecycle retains admission, configuration and resource ownership.</summary>
public sealed class MacShellCatalogSession(string dataRoot) : IShellCatalogSession, IBrowserDiscoverySession
{
    public string DataRoot { get; } = Path.GetFullPath(dataRoot);
    private readonly SemaphoreSlim _requests = new(1, 1);
    private CatalogLifecycle? _owner;
    private ShellCatalogSnapshot _snapshot = new(StorageStartupStatus.FirstRunChoiceRequired);
    private bool _closed;
    private Task? _closeTask;
    private readonly object _closeLock = new();

    public Task<ShellCatalogSnapshot> StartAsync() => RequestAsync(async () =>
    {
        await RetireAsync().ConfigureAwait(false);
        return Accept(await NewLifecycle().StartAsync(allowFirstCreation: false).ConfigureAwait(false));
    });

    public Task<ShellCatalogSnapshot> CreateAsync(string preparedDirectory) => RequestAsync(async () =>
    {
        if (_snapshot.Reason == CatalogRecoveryReason.PendingRecovery) return _snapshot;
        // Native #425 deliberately refuses missing-to-created folder binding transitions.
        // Preparation is the user's explicit operation, not a new publication protocol in the shell.
        if (!Directory.Exists(preparedDirectory))
        {
            await RetireAsync().ConfigureAwait(false);
            return Failure(CatalogRecoveryReason.LocationRefused, "Select a prepared empty folder inside the isolated data folder.");
        }
        await RetireAsync().ConfigureAwait(false);
        var lifecycle = NewLifecycle();
        return Accept(await lifecycle.CreateCatalogAsync(preparedDirectory).ConfigureAwait(false));
    });

    public Task<ShellCatalogSnapshot> OpenAsync(string directory) => RequestAsync(async () =>
    {
        if (_snapshot.Reason == CatalogRecoveryReason.PendingRecovery) return _snapshot;
        // Preflight uses the shared owner and exact Catalog identity; never reads SQLite from presentation.
        if (_owner is null)
        {
            var initial = await NewLifecycle().StartAsync(allowFirstCreation: false).ConfigureAwait(false);
            Accept(initial);
        }
        if (_owner is null || _snapshot.Reason == CatalogRecoveryReason.PendingRecovery) return _snapshot;
        var selection = await _owner.InspectCatalogSelectionAsync(directory).ConfigureAwait(false);
        if (!selection.Succeeded)
        {
            // Failed selection must not leave an active but hidden writer. Retire through its owner.
            await RetireAsync().ConfigureAwait(false);
            return Failure(selection.RecoveryReason, selection.Diagnostic);
        }
        await RetireAsync().ConfigureAwait(false);
        return Accept(await NewLifecycle().StartAsync(allowFirstCreation: false,
            existingCatalogDirectory: selection.Directory, selectedCatalogId: selection.CatalogId,
            resumePreviousCatalogOnFailure: false).ConfigureAwait(false));
    });

    public Task<ShellCatalogSnapshot> VerifyAsync() => RequestAsync(() =>
    {
        if (_snapshot.IsReady && _owner?.VerifyWorkspaceReadiness() != true)
            _snapshot = new(StorageStartupStatus.CatalogUnavailable, CatalogRecoveryReason.LocationUnavailable,
                _owner?.RecoveryDiagnostic, _snapshot.Directory);
        return Task.FromResult(_snapshot);
    });

    private CatalogMediaRootReader Roots(CatalogLifecycle owner) => new(() => owner.Session,
        new MachineIdentityProvider(Path.Combine(DataRoot, "machine-id")), new MediaFiles());
    private MacBrowserMediaEffects MediaEffects(CatalogLifecycle owner) => new([
        owner.Locations.CatalogDirectory, owner.Locations.PreviewsDirectory, owner.Locations.TemporaryDirectory]);

    public Task<BrowserCatalogSnapshot> ReadBrowserCatalogAsync(Guid? rootId = null, string folder = "",
        bool recursive = false, CancellationToken cancellationToken = default) => BorrowAsync(async owner =>
    {
        using var timing = BrowserPerformance.Measure("mac.catalog.read");
        var roots = await Roots(owner).ListAsync(cancellationToken).ConfigureAwait(false);
        var assets = rootId is { } id
            ? await new CatalogMediaAssetRepository(() => owner.Session).ListScopeAsync(id, folder, recursive, cancellationToken).ConfigureAwait(false)
            : Array.Empty<MediaAsset>();
        return new BrowserCatalogSnapshot(owner.CatalogSession.Identity.CatalogId, roots, assets);
    }, cancellationToken);

    public Task<MediaFolderEnumerationResult> ReadBrowserFoldersAsync(Guid rootId, string folder, CancellationToken token = default) => BorrowAsync(owner =>
    {
        var effects = MediaEffects(owner);
        if (folder.Length > 0 && !PortablePathIdentityValidator.ValidateRelativePath(folder, token).IsSafe)
            throw new ArgumentException("This relative folder needs review.", nameof(folder));
        return new CatalogMediaFolderEnumerator(Roots(owner), MediaTypeRegistry.CreateDefault(), effects, effects)
            .EnumerateAsync(new(rootId, folder), token);
    }, token);

    public Task<BrowserFolderSnapshot> ReadBrowserFolderAsync(Guid rootId, string folder, CancellationToken token = default) => BorrowAsync(owner =>
    {
        var effects = MediaEffects(owner);
        var folders = new CatalogMediaFolderEnumerator(Roots(owner), MediaTypeRegistry.CreateDefault(), effects, effects);
        return new BrowserCatalogReader(new CatalogMediaAssetRepository(() => owner.Session),
            new CatalogBrowserRecursiveRootRepository(() => owner.Session), folders).ReadAsync(rootId, folder, token);
    }, token);

    public Task<bool> IsBrowserSourceAvailableAsync(Guid rootId, Guid assetId, CancellationToken token = default) => BorrowAsync(async owner =>
    {
        var asset = await new CatalogMediaAssetRepository(() => owner.Session).GetAsync(assetId, token).ConfigureAwait(false);
        var root = await Roots(owner).GetAsync(rootId, token).ConfigureAwait(false);
        return asset is not null && asset.RootId == rootId && root is not null &&
            await MediaEffects(owner).FileExistsAsync(root, asset.RelativePath, token).ConfigureAwait(false);
    }, token);

    public Task<BrowserFolderSnapshot> DiscoverBrowserFolderAsync(string? absolutePath, Guid rootId,
        string folder, CancellationToken token = default) => BorrowAsync(async owner =>
    {
        using var timing = BrowserPerformance.Measure("mac.discovery");
        if (absolutePath is not null) MacBrowserMediaEffects.RefuseApplicationPackage(absolutePath);
        var effects = MediaEffects(owner);
        var roots = new CatalogMediaRootService(() => owner.Session,
            new MachineIdentityProvider(Path.Combine(DataRoot, "machine-id")), new MediaFiles(), effects);
        if (absolutePath is not null)
        {
            using var mappingTiming = BrowserPerformance.Measure("mac.root.mapping");
            var path = effects.NormalizeRootPath(absolutePath);
            var anchor = effects.GetAnchor(path);
            var mappings = await roots.ListAsync(token);
            var mapped = BrowserLocationMapping.Find(path, mappings.Where(root => root.PhysicalPath is { } physical &&
                effects.GetAnchor(physical) == anchor), StringComparison.Ordinal);
            if (mapped is null)
            {
                // Establish identity only at a real mounted volume boundary. This does not scan the volume.
                // Do not migrate historical cross-volume records or silently create duplicate identities.
                var prior = BrowserLocationMapping.Find(path, mappings, StringComparison.Ordinal);
                if (prior is not null && (await new CatalogMediaAssetRepository(() => owner.Session)
                    .ListScopeAsync(prior.RootId, prior.RelativeFolder, true, token)).Count > 0)
                    throw new IOException("This volume has Catalog records under a different location mapping. Review its mapping before discovery.");
                var created = await roots.CreateBrowserAnchorAsync(anchor == "/" ? "Macintosh HD" : Path.GetFileName(anchor), anchor, token);
                if (!created.Succeeded) throw new IOException(created.Diagnostic);
                mapped = BrowserLocationMapping.Find(path, (await roots.ListAsync(token)).Where(root => root.PhysicalPath is { } physical && effects.GetAnchor(physical) == anchor), StringComparison.Ordinal)
                    ?? throw new IOException("The folder identity could not be established.");
            }
            rootId = mapped.RootId; folder = mapped.RelativeFolder;
        }
        var mediaTypes = MediaTypeRegistry.CreateDefault();
        var folders = new CatalogMediaFolderEnumerator(roots, mediaTypes, effects, effects, excludedPath: effects.IsProtected);
        var assets = new CatalogMediaAssetRepository(() => owner.Session);
        var observations = new MediaAssetService(assets, roots, new NativeFingerprints(effects));
        var reconciliation = new CatalogReconciliationService(folders, observations);
        var discovery = new MediaDiscoveryRefreshService(reconciliation, () => null);
        var recursiveRoots = new CatalogBrowserRecursiveRootRepository(() => owner.Session);
        var recursive = BrowserRecursiveRootLogic.IsEffectivelyRecursive(await recursiveRoots.ListAsync(token), rootId, folder);
        CatalogReconciliationResult result;
        IReadOnlyList<MediaFolderEntry> visible;
        MediaFolderEnumerationResult listing;
        if (recursive)
        {
            var scan = await new RecursiveMediaDiscoveryService(folders, discovery).DiscoverAsync(new(rootId, folder), enumerationToken: token);
            if (!scan.Succeeded || scan.Reconciliation is null) throw new IOException(scan.Diagnostic ?? "This folder could not be scanned.");
            result = scan.Reconciliation; visible = scan.MediaEntries;
            listing = await folders.EnumerateAsync(new(rootId, folder), token);
            if (!listing.Succeeded) throw new IOException(listing.Diagnostic ?? "This folder is unavailable.");
        }
        else
        {
            result = (await discovery.RefreshAsync(new(rootId, folder), cancellationToken: token)).Reconciliation;
            if (!result.Succeeded) throw new IOException(result.Diagnostic ?? "This folder could not be scanned.");
            listing = await folders.EnumerateAsync(new(rootId, folder), token);
            if (!listing.Succeeded) throw new IOException(listing.Diagnostic ?? "This folder is unavailable.");
            visible = listing.Entries;
        }
        token.ThrowIfCancellationRequested();
        // Windows' authoritative view comes from successfully scanned filesystem candidates.
        // Missing records and failed recursive descendants remain in Catalog, outside this visible set.
        var visiblePaths = visible.Where(entry => !entry.IsDirectory && entry.MediaType.IsKnown)
            .Select(entry => entry.RelativePathKey).ToHashSet(StringComparer.Ordinal);
        using var projectionTiming = BrowserPerformance.Measure("mac.catalog.projection");
        // Reuse the post-reconciliation listing for the tree projection. The old reader repeated
        // that same native enumeration once more; Catalog scope reads need no filesystem traversal.
        var content = await assets.ListScopeAsync(rootId, folder, recursive, token);
        return new BrowserFolderSnapshot(rootId, folder, recursive, listing.Status,
            listing.Entries.Where(entry => entry.IsDirectory).ToArray(),
            content.Where(asset => asset.SourceStatus != MediaAssetSourceStatus.Missing &&
                visiblePaths.Contains(asset.RelativePathKey) && mediaTypes.Classify(new(asset.RelativePath)).IsKnown).ToArray(),
            result.Diagnostic);

    }, token);

    public Task SetBrowserRecursiveAsync(Guid rootId, string folder, bool enabled, CancellationToken token = default) => BorrowAsync(async owner =>
    {
        var service = new BrowserRecursiveRootService(new CatalogBrowserRecursiveRootRepository(() => owner.Session));
        if (enabled) await service.EnableAsync(rootId, folder, token);
        else await service.DisableAsync(rootId, folder, token);
        return true;
    }, token);

    private sealed class NativeFingerprints(MacBrowserMediaEffects effects) : ISourceFingerprintService
    {
        public Task<SourceFingerprint> CreateAsync(string path, CancellationToken token = default) => effects.FingerprintAsync(path, token);
    }

    private async Task<T> BorrowAsync<T>(Func<CatalogLifecycle, Task<T>> read, CancellationToken token)
    {
        await _requests.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (!_snapshot.IsReady || _owner?.VerifyWorkspaceReadiness() != true)
                throw new InvalidOperationException("The Catalog is unavailable.");
            // Borrow the sole lifecycle session for the complete read. Catalog switching/close cannot retire it here.
            var result = await read(_owner).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!_owner.VerifyWorkspaceReadiness()) throw new InvalidOperationException("The Catalog is unavailable.");
            return result;
        }
        finally { _requests.Release(); }
    }
    private sealed class MediaFiles : IMediaRootFileSystem
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public bool FileExists(string path) => File.Exists(path);
    }

    private CatalogLifecycle NewLifecycle() => new(new MacCatalogLifecycleEffects(DataRoot));
    private ShellCatalogSnapshot Accept(CatalogStartupResult result)
    {
        _owner = result.Coordinator;
        return _snapshot = new(result.Status, result.RecoveryReason, result.Diagnostic,
            _owner?.Locations.CatalogDirectory,
            result.IsReady ? _owner!.CatalogSession.Identity.CatalogId : null,
            result.IsReady ? _owner!.CatalogSession.SchemaVersion : null);
    }
    private ShellCatalogSnapshot Failure(CatalogRecoveryReason reason, string? diagnostic) =>
        _snapshot = new(StorageStartupStatus.CatalogUnavailable, reason, diagnostic, _snapshot.Directory);

    private async Task<ShellCatalogSnapshot> RequestAsync(Func<Task<ShellCatalogSnapshot>> request)
    {
        await _requests.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            try { return await request().ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            {
                await RetireAsync().ConfigureAwait(false);
                return Failure(CatalogRecoveryReason.OperationFailed, error.Message);
            }
        }
        finally { _requests.Release(); }
    }
    private async Task RetireAsync()
    {
        var owner = _owner;
        _owner = null;
        if (owner is not null) await owner.DisposeAsync(Console.Error.WriteLine).ConfigureAwait(false);
    }
    public ValueTask DisposeAsync()
    {
        lock (_closeLock) return new(_closeTask ??= CloseAsync());
    }
    private async Task CloseAsync()
    {
        await _requests.WaitAsync().ConfigureAwait(false);
        try { _closed = true; await RetireAsync().ConfigureAwait(false); }
        finally { _requests.Release(); }
    }
}
