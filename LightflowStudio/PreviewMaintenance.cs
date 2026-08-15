using System.IO;

namespace LightflowStudio;

internal interface IPreviewOperationCoordinator : IDisposable
{
    Task<IDisposable> EnterOperationAsync(CancellationToken cancellationToken = default);
    Task<IDisposable> EnterMaintenanceAsync(CancellationToken cancellationToken = default);
}

internal sealed class PreviewOperationCoordinator : IPreviewOperationCoordinator
{
    private readonly SemaphoreSlim _turnstile = new(1, 1);
    private readonly SemaphoreSlim _readerGate = new(1, 1);
    private readonly SemaphoreSlim _resource = new(1, 1);
    private int _readers;
    private bool _disposed;

    public async Task<IDisposable> EnterOperationAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _readerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_readers == 0) await _resource.WaitAsync(cancellationToken).ConfigureAwait(false);
                _readers++;
            }
            finally { _readerGate.Release(); }
        }
        finally { _turnstile.Release(); }
        return new Lease(ReleaseOperation);
    }

    public async Task<IDisposable> EnterMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _turnstile.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _resource.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch { _turnstile.Release(); throw; }
        return new Lease(() => { _resource.Release(); _turnstile.Release(); });
    }

    private void ReleaseOperation()
    {
        _readerGate.Wait();
        try
        {
            _readers--;
            if (_readers == 0) _resource.Release();
        }
        finally { _readerGate.Release(); }
    }

    public void Dispose()
    {
        _disposed = true;
        _turnstile.Dispose();
        _readerGate.Dispose();
        _resource.Dispose();
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

internal sealed record PreviewMaintenancePolicy(
    long QuotaBytes,
    TimeSpan StaleRetention,
    TimeSpan OrphanGrace)
{
    public static PreviewMaintenancePolicy FromSettings(AppSettings settings) =>
        new((long)settings.PreviewCacheQuotaGb * 1024 * 1024 * 1024, TimeSpan.FromDays(30), TimeSpan.FromHours(24));
}

internal sealed record PreviewUsage(
    long DatabaseBytes,
    long ThumbnailBytes,
    long StandardPreviewBytes,
    long TemporaryBytes,
    int RecordCount,
    int ArtifactCount,
    int OrphanCount)
{
    public long CacheBytes => ThumbnailBytes + StandardPreviewBytes + TemporaryBytes;
    public long TotalBytes => DatabaseBytes + CacheBytes;
}

internal sealed record PreviewMaintenanceResult(
    bool Succeeded,
    PreviewUsage? Usage = null,
    int FilesRemoved = 0,
    long BytesFreed = 0,
    string? Diagnostic = null);

internal sealed record PreviewRebuildProgress(int Completed, int Total, string? CurrentItem = null);
internal sealed record PreviewRebuildResult(bool Succeeded, int Rebuilt, int Skipped, int Failed,
    string? Diagnostic = null);

internal interface IPreviewMaintenanceService : IDisposable
{
    Task<PreviewUsage> GetUsageAsync(CancellationToken cancellationToken = default);
    Task<PreviewMaintenanceResult> CleanupAsync(PreviewMaintenancePolicy policy,
        CancellationToken cancellationToken = default);
    Task<PreviewMaintenanceResult> ClearAsync(CancellationToken cancellationToken = default);
    Task<PreviewRebuildResult> RebuildAsync(IProgress<PreviewRebuildProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

internal sealed class PreviewMaintenanceService : IPreviewMaintenanceService
{
    private readonly IPreviewStoreService _store;
    private readonly IMediaAssetService _assets;
    private readonly IDerivedMediaMetadataService _metadata;
    private readonly IThumbnailGenerationService _thumbnails;
    private readonly IPreviewOperationCoordinator _operations;
    private readonly ILightflowStorageLocations _locations;
    private readonly bool _ownsGenerators;

    public PreviewMaintenanceService(IPreviewStoreService store, IMediaAssetService assets,
        IDerivedMediaMetadataService metadata, IThumbnailGenerationService thumbnails,
        IPreviewOperationCoordinator operations, ILightflowStorageLocations locations, bool ownsGenerators = false)
    {
        _store = store;
        _assets = assets;
        _metadata = metadata;
        _thumbnails = thumbnails;
        _operations = operations;
        _locations = locations;
        _ownsGenerators = ownsGenerators;
    }

    public async Task<PreviewUsage> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        using var lease = await _operations.EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        var records = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => Measure(records, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public async Task<PreviewMaintenanceResult> CleanupAsync(PreviewMaintenancePolicy policy,
        CancellationToken cancellationToken = default)
    {
        if (policy.QuotaBytes < 0) throw new ArgumentOutOfRangeException(nameof(policy));
        using var lease = await _operations.EnterMaintenanceAsync(cancellationToken).ConfigureAwait(false);
        var records = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var removed = 0;
        long freed = 0;
        var now = DateTimeOffset.UtcNow;
        var referenced = ReferencedPaths(records);

        foreach (var file in EnumerateCacheFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (referenced.Contains(file.FullName) || file.LastWriteTimeUtc > now.UtcDateTime - policy.OrphanGrace) continue;
            if (TryDelete(file.FullName)) { removed++; freed += file.Length; }
        }

        foreach (var record in records.Where(record =>
                     record.SourceAvailability == PreviewSourceAvailability.Available &&
                     record.MetadataState is PreviewComponentState.Stale or PreviewComponentState.Failed &&
                     record.UpdatedUtc <= now - policy.StaleRetention))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _store.ClearMetadataAsync(record.AssetId, cancellationToken).ConfigureAwait(false);
        }

        foreach (var item in ArtifactEntries(records)
                     .Where(item => item.Record.SourceAvailability == PreviewSourceAvailability.Available &&
                         item.State is PreviewComponentState.Stale or PreviewComponentState.Failed &&
                         item.Record.UpdatedUtc <= now - policy.StaleRetention))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var removedArtifact = item.Path is null || !File.Exists(item.Path);
            if (item.Path is not null && File.Exists(item.Path))
            {
                var length = new FileInfo(item.Path).Length;
                if (TryDelete(item.Path)) { removed++; freed += length; removedArtifact = true; }
            }
            if (removedArtifact)
                await _store.ClearArtifactAsync(item.Record.AssetId, item.Kind, cancellationToken).ConfigureAwait(false);
        }

        records = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var usage = Measure(records, cancellationToken);
        if (usage.CacheBytes > policy.QuotaBytes)
        {
            foreach (var item in ArtifactEntries(records)
                         .Where(item => item.Record.SourceAvailability == PreviewSourceAvailability.Available &&
                             item.State == PreviewComponentState.Current && item.Path is not null && File.Exists(item.Path))
                         .OrderBy(item => File.GetLastWriteTimeUtc(item.Path!)).ThenBy(item => item.Record.UpdatedUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = new FileInfo(item.Path!).Length;
                if (!TryDelete(item.Path!)) continue;
                removed++;
                freed += length;
                await _store.ClearArtifactAsync(item.Record.AssetId, item.Kind, cancellationToken).ConfigureAwait(false);
                records = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
                usage = Measure(records, cancellationToken);
                if (usage.CacheBytes <= policy.QuotaBytes) break;
            }
        }

        records = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        usage = Measure(records, cancellationToken);
        var diagnostic = usage.CacheBytes > policy.QuotaBytes
            ? "The cache remains above quota because protected offline or recently-created Preview data was retained."
            : null;
        return new(true, usage, removed, freed, diagnostic);
    }

    public async Task<PreviewMaintenanceResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        using var lease = await _operations.EnterMaintenanceAsync(cancellationToken).ConfigureAwait(false);
        var before = Measure(await _store.ListAsync(cancellationToken).ConfigureAwait(false), cancellationToken);
        var staging = Path.Combine(_locations.PreviewsDirectory, $".lightflow-clearing-{Guid.NewGuid():N}");
        var moved = new List<(string Source, string Staged)>();
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(staging);
                MoveCacheDirectory(_locations.ThumbnailCacheDirectory, Path.Combine(staging, "thumbnails"), moved);
                MoveCacheDirectory(_locations.StandardPreviewCacheDirectory, Path.Combine(staging, "previews"), moved);
            }, cancellationToken).ConfigureAwait(false);
            await _store.ClearAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await Task.Run(() => RestoreMoved(moved), CancellationToken.None).ConfigureAwait(false);
            TryDeleteDirectory(staging);
            throw;
        }

        var stagingRemoved = await Task.Run(() => TryDeleteDirectory(staging), CancellationToken.None).ConfigureAwait(false);
        Directory.CreateDirectory(_locations.ThumbnailCacheDirectory);
        Directory.CreateDirectory(_locations.StandardPreviewCacheDirectory);
        var usage = Measure(await _store.ListAsync(CancellationToken.None).ConfigureAwait(false), CancellationToken.None);
        return new(true, usage, before.ArtifactCount,
            stagingRemoved ? Math.Max(0, before.CacheBytes - usage.CacheBytes) : 0,
            stagingRemoved ? null : $"Preview records were cleared, but temporary cleanup remains at {staging}.");
    }

    public async Task<PreviewRebuildResult> RebuildAsync(IProgress<PreviewRebuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await ClearAsync(cancellationToken).ConfigureAwait(false);
        var assets = await _assets.ListAsync(cancellationToken).ConfigureAwait(false);
        var rebuilt = 0;
        var skipped = 0;
        var failed = 0;
        for (var index = 0; index < assets.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asset = assets[index];
            progress?.Report(new(index, assets.Count, asset.RelativePath));
            try
            {
                var metadata = await _metadata.ProbeAsync(asset.AssetId, forceRefresh: true, cancellationToken)
                    .ConfigureAwait(false);
                var thumbnail = await _thumbnails.GenerateAsync(
                    new(asset.AssetId, ForceRefresh: true, Priority: ThumbnailPriority.Background), cancellationToken)
                    .ConfigureAwait(false);
                if (metadata.Status is DerivedMetadataStatus.RootUnavailable or DerivedMetadataStatus.SourceMissing ||
                    thumbnail.Status is ThumbnailGenerationStatus.RootUnavailable or ThumbnailGenerationStatus.SourceMissing)
                    skipped++;
                else if (metadata.Status == DerivedMetadataStatus.Unsupported &&
                         thumbnail.Status == ThumbnailGenerationStatus.Unsupported)
                    skipped++;
                else if (!metadata.Succeeded && metadata.Status != DerivedMetadataStatus.Unsupported ||
                         !thumbnail.Succeeded && thumbnail.Status != ThumbnailGenerationStatus.Unsupported)
                    failed++;
                else rebuilt++;
            }
            catch (OperationCanceledException) { throw; }
            catch { failed++; }
            progress?.Report(new(index + 1, assets.Count, asset.RelativePath));
        }
        return new(failed == 0, rebuilt, skipped, failed,
            failed == 0 ? null : $"{failed} asset(s) could not be rebuilt and can be retried later.");
    }

    private PreviewUsage Measure(IReadOnlyList<PreviewRecord> records, CancellationToken cancellationToken)
    {
        long database = 0;
        foreach (var path in new[] { _locations.PreviewsDatabasePath, _locations.PreviewsDatabasePath + "-wal", _locations.PreviewsDatabasePath + "-shm" })
            if (File.Exists(path)) database += new FileInfo(path).Length;
        long thumbnails = 0, previews = 0, temporary = 0;
        var files = EnumerateCacheFiles().ToArray();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.Name.Contains(".lightflow", StringComparison.OrdinalIgnoreCase)) temporary += file.Length;
            else if (IsWithin(_locations.ThumbnailCacheDirectory, file.FullName)) thumbnails += file.Length;
            else previews += file.Length;
        }
        var referenced = ReferencedPaths(records);
        return new(database, thumbnails, previews, temporary, records.Count, files.Length,
            files.Count(file => !referenced.Contains(file.FullName)));
    }

    private HashSet<string> ReferencedPaths(IEnumerable<PreviewRecord> records) =>
        ArtifactEntries(records).Where(item => item.Path is not null)
            .Select(item => item.Path!).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private IEnumerable<(PreviewRecord Record, PreviewArtifactKind Kind, PreviewComponentState State, string? Path)>
        ArtifactEntries(IEnumerable<PreviewRecord> records)
    {
        foreach (var record in records)
        {
            yield return (record, PreviewArtifactKind.Thumbnail, record.ThumbnailState,
                Resolve(record.ThumbnailRelativePath));
            yield return (record, PreviewArtifactKind.StandardPreview, record.StandardPreviewState,
                Resolve(record.StandardPreviewRelativePath));
        }
    }

    private string? Resolve(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        try { return MediaPathSemantics.ResolveContained(_locations.PreviewsDirectory, relative); }
        catch (ArgumentException) { return null; }
    }

    private IEnumerable<FileInfo> EnumerateCacheFiles()
    {
        foreach (var root in new[] { _locations.ThumbnailCacheDirectory, _locations.StandardPreviewCacheDirectory })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) yield return new(path);
        }
    }

    private static void MoveCacheDirectory(string source, string destination,
        ICollection<(string Source, string Staged)> moved)
    {
        if (!Directory.Exists(source)) return;
        Directory.Move(source, destination);
        moved.Add((source, destination));
    }

    private static void RestoreMoved(IEnumerable<(string Source, string Staged)> moved)
    {
        foreach (var item in moved.Reverse())
        {
            if (Directory.Exists(item.Source)) Directory.Delete(item.Source, recursive: true);
            if (Directory.Exists(item.Staged)) Directory.Move(item.Staged, item.Source);
        }
    }

    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool IsWithin(string directory, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return !Path.IsPathFullyQualified(relative) && relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    public void Dispose()
    {
        if (!_ownsGenerators) return;
        _thumbnails.Dispose();
        _metadata.Dispose();
    }
}
