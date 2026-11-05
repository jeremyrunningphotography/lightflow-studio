using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LightflowStudio;

public enum PreviewComponentState { Missing, Current, Stale, Failed }
public enum PreviewSourceAvailability { Unknown, Available, Missing, Unavailable }
public enum PreviewArtifactKind { Thumbnail, StandardPreview }

public static class PreviewVisualIdentity
{
    public const string Original = "lightflow-preview-original-v1";
}

public sealed record PreviewSourceIdentity
{
    public PreviewSourceIdentity(long fileSizeBytes, long lastWriteUtcTicks, int fingerprintVersion, string fingerprint)
    {
        if (fileSizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(fileSizeBytes));
        if (lastWriteUtcTicks < 0) throw new ArgumentOutOfRangeException(nameof(lastWriteUtcTicks));
        if (fingerprintVersion <= 0) throw new ArgumentOutOfRangeException(nameof(fingerprintVersion));
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        if (fingerprint.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Preview source fingerprints must be hexadecimal.", nameof(fingerprint));
        FileSizeBytes = fileSizeBytes;
        LastWriteUtcTicks = lastWriteUtcTicks;
        FingerprintVersion = fingerprintVersion;
        Fingerprint = fingerprint;
    }

    public long FileSizeBytes { get; }
    public long LastWriteUtcTicks { get; }
    public int FingerprintVersion { get; }
    public string Fingerprint { get; }
}

public sealed record PreviewRecord(
    Guid AssetId,
    PreviewSourceIdentity Source,
    PreviewSourceAvailability SourceAvailability,
    int? MetadataProbeVersion,
    PreviewComponentState MetadataState,
    string? MetadataJson,
    string? RawMetadataJson,
    int? ThumbnailGeneratorVersion,
    PreviewComponentState ThumbnailState,
    string? ThumbnailRelativePath,
    int? StandardPreviewGeneratorVersion,
    PreviewComponentState StandardPreviewState,
    string? StandardPreviewRelativePath,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string? ThumbnailVisualIdentity = null,
    string? StandardPreviewVisualIdentity = null,
    PreviewFailureReason ThumbnailFailureReason = PreviewFailureReason.Unknown,
    DateTimeOffset? MetadataRetryAfterUtc = null, DateTimeOffset? ThumbnailRetryAfterUtc = null);

public sealed record PreviewComponentUpdate(
    int GeneratorVersion,
    PreviewComponentState State,
    string? RelativePath = null,
    string? PayloadJson = null,
    string? RawPayloadJson = null,
    string? VisualIdentity = null,
    PreviewFailureReason FailureReason = PreviewFailureReason.Unknown, PreviewSourceIdentity? ExpectedSource = null);

public interface IPreviewStoreService : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<PreviewRecord?> GetAsync(Guid assetId, CancellationToken cancellationToken = default);
    async Task<IReadOnlyDictionary<Guid, PreviewRecord>> GetManyAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default)
    {
        var records = new Dictionary<Guid, PreviewRecord>();
        foreach (var assetId in assetIds.Distinct())
            if (await GetAsync(assetId, cancellationToken).ConfigureAwait(false) is { } record)
                records[assetId] = record;
        return records;
    }
    Task<long> CountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PreviewRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task<PreviewRecord> ObserveSourceAsync(Guid assetId, PreviewSourceIdentity source,
        CancellationToken cancellationToken = default);
    Task<PreviewRecord?> SetSourceAvailabilityAsync(Guid assetId, PreviewSourceAvailability availability,
        CancellationToken cancellationToken = default);
    Task<PreviewRecord?> SetMetadataAsync(Guid assetId, PreviewComponentUpdate update,
        CancellationToken cancellationToken = default);
    Task<PreviewRecord?> ClearMetadataAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<PreviewRecord?> SetArtifactAsync(Guid assetId, PreviewArtifactKind kind, PreviewComponentUpdate update,
        CancellationToken cancellationToken = default);
    Task<PreviewRecord?> ClearArtifactAsync(Guid assetId, PreviewArtifactKind kind,
        CancellationToken cancellationToken = default);
    Task ClearAllAsync(CancellationToken cancellationToken = default);
    string GetArtifactPath(Guid assetId, PreviewArtifactKind kind, int generatorVersion,
        PreviewSourceIdentity source, string extension);
    string GetArtifactPath(Guid assetId, PreviewArtifactKind kind, int generatorVersion,
        PreviewSourceIdentity source, string extension, string visualIdentity) =>
        GetArtifactPath(assetId, kind, generatorVersion, source, extension);
}


public static class PreviewCachePaths
{
    public static string ResolveContained(string root, string relative)
    {
        var normalized = CatalogRelativePaths.NormalizeRelativePath(relative);
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(parent, normalized.Replace('/',Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(parent + Path.DirectorySeparatorChar,comparison))
            throw new ArgumentException("Preview path leaves its storage root.",nameof(relative));
        return path;
    }
}
