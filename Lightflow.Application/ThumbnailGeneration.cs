namespace LightflowStudio;

public enum ThumbnailGenerationStatus
{
    Succeeded,
    Current,
    SourceChanged,
    AssetNotFound,
    RootUnavailable,
    SourceMissing,
    Unsupported,
    GeneratorUnavailable,
    InvalidOutput,
    Deferred,
    Failed
}

public enum ThumbnailPriority { Background, Normal, Visible }

public sealed record ThumbnailRequest(Guid AssetId, bool ForceRefresh = false,
    ThumbnailPriority Priority = ThumbnailPriority.Normal);

public sealed record ThumbnailGenerationResult(ThumbnailGenerationStatus Status,
    string? ThumbnailPath = null, string? Diagnostic = null,
    PreviewFailureReason FailureReason = PreviewFailureReason.Unknown)
{
    public bool Succeeded => Status is ThumbnailGenerationStatus.Succeeded or ThumbnailGenerationStatus.Current;
}

public sealed record ThumbnailRenderResult(ThumbnailGenerationStatus Status, string? Diagnostic = null,
    PreviewFailureReason FailureReason = PreviewFailureReason.Unknown);
public sealed record ThumbnailColorRender(string VisualIdentity, IReadOnlyList<string> OrderedLutPaths)
{
    public static ThumbnailColorRender Original { get; } = new(PreviewVisualIdentity.Original, []);
}

public sealed record PreviewRegenerationCompleted(Guid AssetId, ThumbnailGenerationResult Result);
public enum PreviewRegenerationMode { EnsureCurrent, Force }

public sealed class AssetPreviewGenerationGate
{
    private sealed class Entry { public SemaphoreSlim Semaphore { get; } = new(1, 1); public int Users { get; set; } }
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Entry> _entries = [];

    public async Task<IDisposable> EnterAsync(Guid assetId, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(assetId, out entry!)) _entries.Add(assetId, entry = new());
            entry.Users++;
        }
        try { await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch { ReleaseUser(assetId, entry, ownsSemaphore: false); throw; }
        return new Lease(this, assetId, entry);
    }

    private void ReleaseUser(Guid assetId, Entry entry, bool ownsSemaphore)
    {
        if (ownsSemaphore) entry.Semaphore.Release();
        lock (_sync)
        {
            entry.Users--;
            if (entry.Users == 0 && _entries.TryGetValue(assetId, out var current) && ReferenceEquals(current, entry))
                _entries.Remove(assetId);
        }
    }

    private sealed class Lease(AssetPreviewGenerationGate owner, Guid assetId, Entry entry) : IDisposable
    {
        private AssetPreviewGenerationGate? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseUser(assetId, entry, ownsSemaphore: true);
    }
}

public static class PreviewRegenerationBatch
{
    public static async Task<IReadOnlyList<ThumbnailGenerationResult>> RunAsync(IReadOnlyList<Guid> assetIds,
        Func<Guid, CancellationToken, Task<ThumbnailGenerationResult>> generate,
        IProgress<PreviewRegenerationCompleted>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<ThumbnailGenerationResult>(assetIds.Count);
        foreach (var assetId in assetIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await generate(assetId, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            progress?.Report(new(assetId, result));
        }
        return results;
    }
}

public sealed record ThumbnailGenerationActivityChanged(Guid AssetId, bool IsGenerating);
public interface IThumbnailGenerationActivity
{
    event EventHandler<ThumbnailGenerationActivityChanged>? Changed;
    IDisposable Begin(Guid assetId);
    bool IsGenerating(Guid assetId);
}

public sealed class ThumbnailGenerationActivity : IThumbnailGenerationActivity
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, int> _active = [];
    public event EventHandler<ThumbnailGenerationActivityChanged>? Changed;
    public bool IsGenerating(Guid assetId) { lock (_sync) return _active.ContainsKey(assetId); }
    public IDisposable Begin(Guid assetId)
    {
        lock (_sync)
        {
            _active.TryGetValue(assetId, out var count);
            _active[assetId] = count + 1;
            if (count == 0) Changed?.Invoke(this, new(assetId, true));
        }
        return new Lease(this, assetId);
    }
    private void End(Guid assetId)
    {
        lock (_sync)
        {
            if (!_active.TryGetValue(assetId, out var count)) return;
            if (count > 1) _active[assetId] = count - 1;
            else { _active.Remove(assetId); Changed?.Invoke(this, new(assetId, false)); }
        }
    }
    private sealed class Lease(ThumbnailGenerationActivity owner, Guid assetId) : IDisposable
    {
        private ThumbnailGenerationActivity? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.End(assetId);
    }
}

public interface IThumbnailRenderer
{
    Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
        string destinationPath, CancellationToken cancellationToken = default);
    Task<ThumbnailRenderResult> RenderAsync(string sourcePath, string mediaType, TimeSpan videoPosition,
        string destinationPath, ThumbnailColorRender color, CancellationToken cancellationToken = default) =>
        RenderAsync(sourcePath, mediaType, videoPosition, destinationPath, cancellationToken);
}

public interface IThumbnailGenerationService : IDisposable
{
    Task<ThumbnailGenerationResult> GenerateAsync(ThumbnailRequest request,
        CancellationToken cancellationToken = default);
}

public class SharedThumbnailGenerationService : IThumbnailGenerationService
{
    public const int CurrentGeneratorVersion = 1;
    private readonly TimeProvider _time;
    private readonly IMediaAssetService _assets;
    private readonly IPreviewStoreService _previews;
    private readonly ILightflowStorageLocations _locations;
    private readonly IThumbnailRenderer _renderer;
    private readonly IDerivedMediaMetadataService? _metadata;
    private readonly IDisposable? _ownedDependency;
    private readonly PriorityAsyncGate _gate;
    private readonly IPreviewOperationCoordinator? _operations;
    private readonly IThumbnailColorSource? _colorSource;
    private readonly Func<string, bool> _validate;
    private readonly Action<string>? _log;
    private readonly Func<string, string> _pathGuard;
    private readonly IThumbnailGenerationActivity? _activity;
    private readonly IPreferredPreviewFrameStore? _preferredFrames;

    public SharedThumbnailGenerationService(IMediaAssetService assets, IPreviewStoreService previews,
        ILightflowStorageLocations locations, IThumbnailRenderer renderer,
        IDerivedMediaMetadataService? metadata = null, int maximumConcurrency = 2,
        IDisposable? ownedDependency = null, IPreviewOperationCoordinator? operations = null,
        IThumbnailColorSource? colorSource = null, Func<string, bool>? validate = null, Action<string>? log = null,
        IThumbnailGenerationActivity? activity = null, IPreferredPreviewFrameStore? preferredFrames = null, TimeProvider? time = null, Func<string, string>? pathGuard = null)
    {
        _time = time ?? TimeProvider.System;
        _assets = assets;
        _previews = previews;
        _locations = locations;
        _renderer = renderer;
        _metadata = metadata;
        _ownedDependency = ownedDependency;
        _gate = new(maximumConcurrency);
        _operations = operations;
        _colorSource = colorSource;
        _validate = validate ?? throw new ArgumentNullException(nameof(validate));
        _log = log;
        _pathGuard = pathGuard ?? (path => path);
        _activity = activity;
        _preferredFrames = preferredFrames;
    }

    public async Task<ThumbnailGenerationResult> GenerateAsync(ThumbnailRequest request,
        CancellationToken cancellationToken = default)
    {
        using var timing = ThumbnailPerformance.Measure("generation", request.AssetId);
        var result = await GenerateCoreAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && result.Status != ThumbnailGenerationStatus.Deferred && result.Diagnostic is { Length: > 0 } diagnostic)
            _log?.Invoke($"Preview {request.AssetId}: {diagnostic}");
        return result;
    }

    private async Task<ThumbnailGenerationResult> GenerateCoreAsync(ThumbnailRequest request,
        CancellationToken cancellationToken)
    {
        using var operationLease = _operations is null ? null :
            await _operations.EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        // Freshness and Color are intentionally resolved after this wait. A queued ensure-current request may
        // have been satisfied by overlapping work, or its committed Color may have changed while it waited.
        using var assetLease = await PreviewRetryPolicy.ThumbnailGate(_previews).EnterAsync(request.AssetId, cancellationToken).ConfigureAwait(false);
        using var lease = await _gate.EnterAsync(request.Priority, cancellationToken).ConfigureAwait(false);
        var observed = await _assets.ObserveAsync(request.AssetId, cancellationToken).ConfigureAwait(false);
        if (observed.Status == MediaAssetOperationStatus.NotFound)
            return new(ThumbnailGenerationStatus.AssetNotFound, Diagnostic: observed.Diagnostic);
        if (observed.Status == MediaAssetOperationStatus.RootUnavailable)
            return await RetainOfflineAsync(request.AssetId, PreviewSourceAvailability.Unavailable,
                ThumbnailGenerationStatus.RootUnavailable, observed.Diagnostic, cancellationToken).ConfigureAwait(false);
        if (observed.Status == MediaAssetOperationStatus.SourceMissing)
            return await RetainOfflineAsync(request.AssetId, PreviewSourceAvailability.Missing,
                ThumbnailGenerationStatus.SourceMissing, observed.Diagnostic, cancellationToken).ConfigureAwait(false);
        if (!observed.Succeeded || observed.Asset?.PhysicalPath is null || observed.Asset.Asset.Fingerprint is null)
            return new(ThumbnailGenerationStatus.Failed,
                Diagnostic: observed.Diagnostic ?? "The media source could not be observed.");

        var asset = observed.Asset.Asset;
        var source = SourceIdentity(asset);
        var preview = await _previews.ObserveSourceAsync(request.AssetId, source, cancellationToken).ConfigureAwait(false);
        var preferredFrame = _preferredFrames is null ? null
            : await _preferredFrames.GetAsync(request.AssetId, cancellationToken).ConfigureAwait(false);
        ThumbnailColorRender color;
        try { color = await ResolveColorAsync(request.AssetId, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or KeyNotFoundException)
        {
            var failedIdentity = await CurrentVisualIdentityAsync(request.AssetId, cancellationToken).ConfigureAwait(false);
            var failedVisual = BrowserVisualIdentity(failedIdentity, preferredFrame);
            if (!request.ForceRefresh && PreviewRetryPolicy.ThumbnailDeferred(preview, failedVisual, _time.GetUtcNow()))
                return Deferred(preview);
            await RecordFailureAsync(request.AssetId, preview, failedVisual, cancellationToken,
                PreviewFailureReason.DependencyUnavailable).ConfigureAwait(false);
            return new(ThumbnailGenerationStatus.Failed, ResolveExisting(preview.ThumbnailRelativePath), exception.Message);
        }
        var visualIdentity = BrowserVisualIdentity(color.VisualIdentity, preferredFrame);
        if (!request.ForceRefresh && preview.ThumbnailState == PreviewComponentState.Current &&
            preview.ThumbnailGeneratorVersion == CurrentGeneratorVersion &&
            string.Equals(preview.ThumbnailVisualIdentity ?? PreviewVisualIdentity.Original, visualIdentity, StringComparison.Ordinal) &&
            ResolveExisting(preview.ThumbnailRelativePath) is { } existing && _validate(existing))
            return new(ThumbnailGenerationStatus.Current, existing);

        // A newly available LUT is a changed dependency, even if committed Color identity is unchanged.
        if (!request.ForceRefresh && preview.ThumbnailFailureReason != PreviewFailureReason.DependencyUnavailable &&
            PreviewRetryPolicy.ThumbnailDeferred(preview, visualIdentity, _time.GetUtcNow()))
            return Deferred(preview);

        using var activity = _activity?.Begin(request.AssetId);
        var finalPath = _previews.GetArtifactPath(request.AssetId, PreviewArtifactKind.Thumbnail,
            CurrentGeneratorVersion, source, "jpg", visualIdentity);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        var temporaryPath = finalPath + $".{Guid.NewGuid():N}.lightflow";
        var stage = "rendering";
        try
        {
            TimeSpan position;
            using (var posterTiming = ThumbnailPerformance.Measure("poster.policy", request.AssetId))
            {
                position = preferredFrame?.Position ?? await RepresentativePositionAsync(request.AssetId, asset.MediaType, cancellationToken).ConfigureAwait(false);
                posterTiming?.SetTag("requested.seconds", position.TotalSeconds);
            }
            ThumbnailRenderResult rendered;
            using (ThumbnailPerformance.Measure("renderer", request.AssetId))
                rendered = await _renderer.RenderAsync(observed.Asset.PhysicalPath, asset.MediaType, position, temporaryPath, color, cancellationToken).ConfigureAwait(false);
            if (rendered.Status != ThumbnailGenerationStatus.Succeeded)
            {
                await RecordFailureAsync(request.AssetId, preview, visualIdentity, cancellationToken, rendered.FailureReason).ConfigureAwait(false);
                return new(rendered.Status, ResolveExisting(preview.ThumbnailRelativePath), rendered.Diagnostic, rendered.FailureReason);
            }
            if (!_validate(temporaryPath))
            {
                await RecordFailureAsync(request.AssetId, preview, visualIdentity, cancellationToken,
                    asset.MediaType == "video" ? PreviewFailureReason.NoVideoFrame : PreviewFailureReason.Unknown).ConfigureAwait(false);
                return new(ThumbnailGenerationStatus.InvalidOutput, ResolveExisting(preview.ThumbnailRelativePath),
                    "The generated Preview was not a valid image.",
                    asset.MediaType == "video" ? PreviewFailureReason.NoVideoFrame : PreviewFailureReason.Unknown);
            }

            var verified = await _assets.ObserveAsync(request.AssetId, cancellationToken).ConfigureAwait(false);
            if (verified.Status == MediaAssetOperationStatus.RootUnavailable)
                return await RetainOfflineAsync(request.AssetId, PreviewSourceAvailability.Unavailable,
                    ThumbnailGenerationStatus.RootUnavailable, verified.Diagnostic, cancellationToken).ConfigureAwait(false);
            if (verified.Status == MediaAssetOperationStatus.SourceMissing)
                return await RetainOfflineAsync(request.AssetId, PreviewSourceAvailability.Missing,
                    ThumbnailGenerationStatus.SourceMissing, verified.Diagnostic, cancellationToken).ConfigureAwait(false);
            if (verified.Status == MediaAssetOperationStatus.NotFound)
                return new(ThumbnailGenerationStatus.AssetNotFound, Diagnostic: verified.Diagnostic);
            if (!verified.Succeeded || verified.Asset?.Asset.Fingerprint is null)
                return new(ThumbnailGenerationStatus.Failed, ResolveExisting(preview.ThumbnailRelativePath),
                    verified.Diagnostic ?? "The source could not be verified after Preview generation.");

            var verifiedSource = SourceIdentity(verified.Asset.Asset);
            await _previews.ObserveSourceAsync(request.AssetId, verifiedSource, cancellationToken).ConfigureAwait(false);
            if (!SameSourceIdentity(source, verifiedSource))
                return new(ThumbnailGenerationStatus.SourceChanged, ResolveExisting(preview.ThumbnailRelativePath),
                    "The source changed while its Preview was being generated. Retry after the file is stable.");

            var verifiedVisualIdentity = await CurrentBrowserVisualIdentityAsync(request.AssetId, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(visualIdentity, verifiedVisualIdentity, StringComparison.Ordinal))
                return new(ThumbnailGenerationStatus.SourceChanged, ResolveExisting(preview.ThumbnailRelativePath),
                    "Preview frame or Color changed while its Preview was being generated. Newer work will replace it.");

            using var cacheTiming = ThumbnailPerformance.Measure("cache.publish", request.AssetId);
            stage = "artifact promotion";
            File.Move(temporaryPath, finalPath, overwrite: true);
            if (!string.Equals(visualIdentity,
                    await CurrentBrowserVisualIdentityAsync(request.AssetId, cancellationToken).ConfigureAwait(false),
                    StringComparison.Ordinal))
            {
                try { File.Delete(finalPath); } catch { }
                return new(ThumbnailGenerationStatus.SourceChanged, ResolveExisting(preview.ThumbnailRelativePath),
                    "Preview frame or Color changed before its Preview could be committed. Newer work will replace it.");
            }
            stage = "artifact persistence";
            var relative = Path.GetRelativePath(_locations.PreviewsDirectory, finalPath).Replace('\\', '/');
            await _previews.SetArtifactAsync(request.AssetId, PreviewArtifactKind.Thumbnail,
                new(CurrentGeneratorVersion, PreviewComponentState.Current, relative, VisualIdentity: visualIdentity, ExpectedSource: source), cancellationToken).ConfigureAwait(false);
            return new(ThumbnailGenerationStatus.Succeeded, finalPath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await RecordFailureAsync(request.AssetId, preview, visualIdentity, CancellationToken.None).ConfigureAwait(false);
            return new(ThumbnailGenerationStatus.Failed, ResolveExisting(preview.ThumbnailRelativePath),
                $"Preview {stage} failed ({exception.GetType().Name}): {exception.Message}");
        }
        finally { try { File.Delete(temporaryPath); } catch { } }
    }

    private ThumbnailGenerationResult Deferred(PreviewRecord preview) => new(ThumbnailGenerationStatus.Deferred,
        ResolveExisting(preview.ThumbnailRelativePath), "Preview retry is deferred after the previous failure.", preview.ThumbnailFailureReason);

    private async Task<TimeSpan> RepresentativePositionAsync(Guid assetId, string mediaType,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase) || _metadata is null)
            return TimeSpan.FromSeconds(1);
        var result = await _metadata.ProbeAsync(assetId, cancellationToken: cancellationToken).ConfigureAwait(false);
        var duration = result.Metadata?.DurationSeconds;
        if (duration is null or <= 0) return TimeSpan.FromSeconds(1);
        return ThumbnailPosterPolicy.Position(duration);
    }

    private async Task RecordFailureAsync(Guid assetId, PreviewRecord preview, string? visualIdentity, CancellationToken cancellationToken,
        PreviewFailureReason failureReason = PreviewFailureReason.Unknown)
    {
        var observed = await _assets.ObserveAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (!observed.Succeeded || observed.Asset?.Asset.Fingerprint is null) return;
        var source = SourceIdentity(observed.Asset.Asset);
        await _previews.ObserveSourceAsync(assetId, source, cancellationToken).ConfigureAwait(false);
        if (!SameSourceIdentity(source, preview.Source)) return;
        await _previews.SetArtifactAsync(assetId, PreviewArtifactKind.Thumbnail,
            new(CurrentGeneratorVersion, PreviewComponentState.Failed, preview.ThumbnailRelativePath, VisualIdentity: visualIdentity,
                FailureReason: failureReason, ExpectedSource: preview.Source), cancellationToken).ConfigureAwait(false);
    }

    private Task<ThumbnailColorRender> ResolveColorAsync(Guid assetId, CancellationToken token) =>
        _colorSource?.ResolveAsync(assetId, token) ?? Task.FromResult(ThumbnailColorRender.Original);
    private Task<string> CurrentVisualIdentityAsync(Guid assetId, CancellationToken token) =>
        _colorSource?.IdentityAsync(assetId, token) ?? Task.FromResult(PreviewVisualIdentity.Original);

    private async Task<string> CurrentBrowserVisualIdentityAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var colorIdentity = await CurrentVisualIdentityAsync(assetId, cancellationToken).ConfigureAwait(false);
        var preferred = _preferredFrames is null ? null
            : await _preferredFrames.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        return BrowserVisualIdentity(colorIdentity, preferred);
    }

    public static string BrowserVisualIdentity(string colorIdentity, PreferredPreviewFrame? preferred) =>
        preferred is null ? colorIdentity : $"{colorIdentity};preferred-frame={preferred.Position.Ticks};revision={preferred.Revision}";

    private async Task<ThumbnailGenerationResult> RetainOfflineAsync(Guid assetId,
        PreviewSourceAvailability availability, ThumbnailGenerationStatus status, string? diagnostic,
        CancellationToken cancellationToken)
    {
        var existing = await _previews.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            await _previews.SetSourceAvailabilityAsync(assetId, availability, cancellationToken).ConfigureAwait(false);
        return new(status, ResolveExisting(existing?.ThumbnailRelativePath), diagnostic);
    }

    private string? ResolveExisting(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        try
        {
            var path = _pathGuard(PreviewCachePaths.ResolveContained(_locations.PreviewsDirectory, relativePath));
            return File.Exists(path) ? path : null;
        }
        catch (ArgumentException) { return null; }
    }

    private static PreviewSourceIdentity SourceIdentity(MediaAsset asset) => new(asset.FileSizeBytes,
        asset.LastWriteUtcTicks, asset.Fingerprint!.Version, asset.Fingerprint.Value);
    private static bool SameSourceIdentity(PreviewSourceIdentity left, PreviewSourceIdentity right) =>
        left.FileSizeBytes == right.FileSizeBytes && left.LastWriteUtcTicks == right.LastWriteUtcTicks &&
        left.FingerprintVersion == right.FingerprintVersion &&
        string.Equals(left.Fingerprint, right.Fingerprint, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _gate.Dispose();
        _ownedDependency?.Dispose();
    }
}

public sealed class PriorityAsyncGate : IDisposable
{
    private readonly object _sync = new();
    private readonly int _maximum;
    private readonly Queue<Waiter> _visible = new();
    private readonly Queue<Waiter> _normal = new();
    private readonly Queue<Waiter> _background = new();
    private readonly Action? _beforeWaiterAssignment;
    private int _active;
    private bool _disposed;

    public PriorityAsyncGate(int maximum, Action? beforeWaiterAssignment = null)
    {
        if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        _maximum = maximum;
        _beforeWaiterAssignment = beforeWaiterAssignment;
    }

    public async Task<IDisposable> EnterAsync(ThumbnailPriority priority, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Waiter? waiter = null;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active < _maximum) _active++;
            else
            {
                waiter = new(cancellationToken);
                Queue(priority).Enqueue(waiter);
            }
        }
        if (waiter is not null) await waiter.Task.ConfigureAwait(false);
        return new Lease(this);
    }

    private void Release()
    {
        lock (_sync)
        {
            _active--;
            while ((Dequeue(_visible) ?? Dequeue(_normal) ?? Dequeue(_background)) is { } next)
            {
                _beforeWaiterAssignment?.Invoke();
                if (!next.TryAssign()) continue;
                _active++;
                break;
            }
        }
    }

    private Queue<Waiter> Queue(ThumbnailPriority priority) => priority switch
    {
        ThumbnailPriority.Visible => _visible,
        ThumbnailPriority.Background => _background,
        _ => _normal
    };

    private static Waiter? Dequeue(Queue<Waiter> queue)
    {
        while (queue.Count > 0)
        {
            var waiter = queue.Dequeue();
            if (!waiter.IsCanceled) return waiter;
            waiter.Dispose();
        }
        return null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            foreach (var waiter in _visible.Concat(_normal).Concat(_background)) waiter.Cancel();
            _visible.Clear(); _normal.Clear(); _background.Clear();
        }
    }

    private sealed class Lease(PriorityAsyncGate owner) : IDisposable
    {
        private PriorityAsyncGate? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }

    private sealed class Waiter : IDisposable
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;
        public Waiter(CancellationToken cancellationToken) =>
            _registration = cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));
        public Task Task => _completion.Task;
        public bool IsCanceled => _completion.Task.IsCanceled;
        public bool TryAssign()
        {
            var assigned = _completion.TrySetResult();
            _registration.Dispose();
            return assigned;
        }
        public void Cancel() { _completion.TrySetCanceled(); _registration.Dispose(); }
        public void Dispose() => _registration.Dispose();
    }
}

public interface IThumbnailColorSource
{
    Task<ThumbnailColorRender> ResolveAsync(Guid assetId, CancellationToken token);
    Task<string> IdentityAsync(Guid assetId, CancellationToken token);
}

public static class ThumbnailPosterPolicy
{
    public static TimeSpan Position(double? duration) => duration is null or <= 0
        ? TimeSpan.FromSeconds(1)
        : TimeSpan.FromSeconds(duration < 1 ? duration.Value / 2 : Math.Clamp(duration.Value * 0.1, 1, 30));
}
