using Lightflow.Application;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal interface ISourceFingerprintService
{
    Task<SourceFingerprint> CreateAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Version 1 hashes bounded samples, not the complete source. It detects many ordinary changes cheaply but is not
/// cryptographic proof that two media files are byte-for-byte identical.
/// </summary>
internal sealed class SampledSourceFingerprintService : ISourceFingerprintService
{
    public const int CurrentVersion = 1;
    internal const int SampleBytes = 64 * 1024;
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes("Lightflow.SourceFingerprint.v1\0");

    public Task<SourceFingerprint> CreateAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Create(path, cancellationToken), cancellationToken);

    private static SourceFingerprint Create(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, SampleBytes, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Domain);
        Span<byte> length = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(length, stream.Length);
        hash.AppendData(length);

        var buffer = new byte[SampleBytes];
        var headLength = ReadAtMost(stream, buffer, Math.Min(SampleBytes, stream.Length), cancellationToken);
        hash.AppendData(buffer, 0, headLength);
        if (stream.Length > SampleBytes)
        {
            stream.Position = Math.Max(SampleBytes, stream.Length - SampleBytes);
            var tailLength = ReadAtMost(stream, buffer, Math.Min(SampleBytes, stream.Length - stream.Position), cancellationToken);
            hash.AppendData(buffer, 0, tailLength);
        }

        return new(CurrentVersion, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static int ReadAtMost(Stream stream, byte[] buffer, long count, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, total, (int)Math.Min(buffer.Length - total, count - total));
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}

internal interface IMediaAssetService
{
    async Task<IReadOnlyList<MediaAsset>> ListScopeAsync(Guid rootId, string folder, bool recursive,
        CancellationToken cancellationToken = default) =>
        (await ListAsync(cancellationToken).ConfigureAwait(false)).Where(asset =>
            MediaAssetScope.Contains(asset, rootId, folder, recursive)).ToArray();
    Task<MediaAssetOperationResult> CreateAsync(Guid rootId, string relativePath, string mediaType = "unknown",
        CancellationToken cancellationToken = default);
    Task<MediaAssetResolution?> GetAsync(Guid assetId, CancellationToken cancellationToken = default);
    // Raw Catalog projection: omit absent IDs, retain missing sources, and perform no filesystem resolution.
    Task<IReadOnlyDictionary<Guid, MediaAsset>> GetManyAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaAssetResolution?> FindAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default);
    Task<MediaAssetOperationResult> ObserveAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default);
    Task<MediaAssetOperationResult> RelocateAsync(Guid assetId, Guid rootId, string relativePath,
        CancellationToken cancellationToken = default) => Task.FromResult(new MediaAssetOperationResult(
            MediaAssetOperationStatus.Failed, Diagnostic: "Catalog relocation is not supported by this asset provider."));
}

internal sealed class MediaAssetService(IMediaAssetRepository repository, IMediaRootService roots,
    ISourceFingerprintService fingerprints) : IMediaAssetService, ICatalogMutationParticipant
{
    public CatalogMutationLifecycle Mutations { get; } = CatalogMutationLifecycle.From(repository);
    public Task<IReadOnlyList<MediaAsset>> ListScopeAsync(Guid rootId, string folder, bool recursive,
        CancellationToken cancellationToken = default) => repository.ListScopeAsync(rootId, folder, recursive, cancellationToken);
    public async Task<MediaAssetOperationResult> CreateAsync(Guid rootId, string relativePath, string mediaType = "unknown",
        CancellationToken cancellationToken = default)
    {
        return await Mutations.RunAsync<MediaAssetOperationResult>(async () => {
        var normalized = MediaPathSemantics.NormalizeRelativePath(relativePath);
        var key = MediaPathSemantics.RelativePathKey(normalized);
        if (await repository.FindAsync(rootId, key, cancellationToken).ConfigureAwait(false) is not null)
            return new(MediaAssetOperationStatus.AlreadyExists, Diagnostic: "An asset already exists at that logical location.");
        var portableCheck = PortablePathIdentityValidator.ValidateRelativePath(relativePath.Replace('\\', '/'), cancellationToken);
        if (!portableCheck.IsSafe) return new(MediaAssetOperationStatus.Failed, Diagnostic: portableCheck.Diagnostic);
        MediaPathResolution resolved;
        try { resolved = await roots.ResolveAsync(rootId, normalized, cancellationToken).ConfigureAwait(false); }
        catch (KeyNotFoundException)
        {
            return new(MediaAssetOperationStatus.RootNotFound, Diagnostic: "The Media Root does not exist.");
        }
        if (resolved.IdentityStatus != PathIdentityStatus.Safe)
            return new(MediaAssetOperationStatus.Failed, Diagnostic: resolved.Diagnostic);
        if (resolved.RootAvailability != MediaRootAvailability.Online)
            return new(MediaAssetOperationStatus.RootUnavailable, Diagnostic: resolved.Diagnostic);
        if (!resolved.Exists || resolved.PhysicalPath is null)
            return new(MediaAssetOperationStatus.SourceMissing, Diagnostic: resolved.Diagnostic);

        try
        {
            var observation = await ObserveFileAsync(resolved.PhysicalPath, cancellationToken).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var asset = new MediaAsset(Guid.NewGuid(), rootId, normalized, key, NormalizeMediaType(mediaType),
                observation.Size, observation.LastWriteUtcTicks, observation.Fingerprint, MediaAssetSourceStatus.Available,
                now, now, now);
            var created = await repository.CreateAsync(asset, cancellationToken).ConfigureAwait(false);
            if (created != MediaAssetOperationStatus.Succeeded)
                return new(created, Diagnostic: "An asset already exists at that logical location.");
            return new(created, new(asset, resolved.RootAvailability, resolved.PhysicalPath, true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(MediaAssetOperationStatus.Failed, Diagnostic: $"The source could not be observed: {exception.Message}");
        }
        catch (SqliteException exception)
        {
            return new(MediaAssetOperationStatus.Failed, Diagnostic: $"The asset could not be saved: {exception.Message}");
        }
    }, cancellationToken);
    }

    public async Task<MediaAssetResolution?> GetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await repository.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        return asset is null ? null : await ResolveAsync(asset, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyDictionary<Guid, MediaAsset>> GetManyAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default) => repository.GetManyAsync(assetIds, cancellationToken);

    public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken = default) =>
        repository.ListAsync(cancellationToken);

    public async Task<MediaAssetResolution?> FindAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default)
    {
        var key = MediaPathSemantics.RelativePathKey(relativePath);
        var asset = await repository.FindAsync(rootId, key, cancellationToken).ConfigureAwait(false);
        return asset is null ? null : await ResolveAsync(asset, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MediaAssetOperationResult> ObserveAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        return await Mutations.RunAsync<MediaAssetOperationResult>(async () => {
        var asset = await repository.GetAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (asset is null) return new(MediaAssetOperationStatus.NotFound, Diagnostic: "The asset no longer exists.");
        var resolved = await roots.ResolveAsync(asset.RootId, asset.RelativePath, cancellationToken).ConfigureAwait(false);
        if (resolved.IdentityStatus != PathIdentityStatus.Safe)
            return new(MediaAssetOperationStatus.Failed, Diagnostic: resolved.Diagnostic);
        if (resolved.RootAvailability != MediaRootAvailability.Online)
            return new(MediaAssetOperationStatus.RootUnavailable,
                new(asset, resolved.RootAvailability, null, false, resolved.Diagnostic), resolved.Diagnostic);
        if (!resolved.Exists || resolved.PhysicalPath is null)
        {
            if (!await repository.MarkMissingAsync(asset.AssetId, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false))
                return new(MediaAssetOperationStatus.NotFound, Diagnostic: "The asset no longer exists.");
            var missing = (await repository.GetAsync(asset.AssetId, cancellationToken).ConfigureAwait(false))!;
            return new(MediaAssetOperationStatus.SourceMissing,
                new(missing, MediaRootAvailability.Online, resolved.PhysicalPath, false, resolved.Diagnostic), resolved.Diagnostic);
        }

        try
        {
            var observation = await ObserveFileAsync(resolved.PhysicalPath, cancellationToken).ConfigureAwait(false);
            if (!await repository.UpdateObservationAsync(asset.AssetId, observation.Size, observation.LastWriteUtcTicks,
                observation.Fingerprint, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false))
                return new(MediaAssetOperationStatus.NotFound, Diagnostic: "The asset no longer exists.");
            var updated = (await repository.GetAsync(asset.AssetId, cancellationToken).ConfigureAwait(false))!;
            return new(MediaAssetOperationStatus.Succeeded,
                new(updated, MediaRootAvailability.Online, resolved.PhysicalPath, true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(MediaAssetOperationStatus.Failed,
                new(asset, MediaRootAvailability.Online, resolved.PhysicalPath, false), exception.Message);
        }
    }, cancellationToken);
    }

    public Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default) {
        return Mutations.RunAsync<int>(() => { return repository.MarkMissingAsync(assetIds, DateTimeOffset.UtcNow, cancellationToken); }, cancellationToken);
    }

    public async Task<MediaAssetOperationResult> RelocateAsync(Guid assetId, Guid rootId, string relativePath,
        CancellationToken cancellationToken = default)
    {
        return await Mutations.RunAsync<MediaAssetOperationResult>(async () => {
        var portableCheck = PortablePathIdentityValidator.ValidateRelativePath(relativePath.Replace('\\', '/'), cancellationToken);
        if (!portableCheck.IsSafe) return new(MediaAssetOperationStatus.Failed, Diagnostic: portableCheck.Diagnostic);
        var normalized = MediaPathSemantics.NormalizeRelativePath(relativePath);
        var existing = await repository.FindAsync(rootId, MediaPathSemantics.RelativePathKey(normalized), cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null && existing.AssetId != assetId)
            return new(MediaAssetOperationStatus.AlreadyExists, Diagnostic: "Another asset already owns the destination location.");
        var resolution = await roots.ResolveAsync(rootId, normalized, cancellationToken).ConfigureAwait(false);
        if (resolution.IdentityStatus != PathIdentityStatus.Safe)
            return new(MediaAssetOperationStatus.Failed, Diagnostic: resolution.Diagnostic);
        if (resolution.RootAvailability != MediaRootAvailability.Online)
            return new(MediaAssetOperationStatus.RootUnavailable, Diagnostic: resolution.Diagnostic);
        var status = await repository.RelocateAsync(assetId, rootId, normalized, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        if (status != MediaAssetOperationStatus.Succeeded) return new(status, Diagnostic: "The Catalog location could not be updated.");
        return await ObserveAsync(assetId, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);
    }

    private async Task<MediaAssetResolution> ResolveAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        var resolved = await roots.ResolveAsync(asset.RootId, asset.RelativePath, cancellationToken).ConfigureAwait(false);
        return new(asset, resolved.RootAvailability, resolved.PhysicalPath, resolved.Exists, resolved.Diagnostic);
    }

    private async Task<(long Size, long LastWriteUtcTicks, SourceFingerprint Fingerprint)> ObserveFileAsync(
        string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var before = ReadFacts(path);
            var fingerprint = await fingerprints.CreateAsync(path, cancellationToken).ConfigureAwait(false);
            var after = ReadFacts(path);
            if (before == after) return (after.Size, after.LastWriteUtcTicks, fingerprint);
        }
        throw new IOException("The source changed while Lightflow was observing it. Try again after writes finish.");
    }

    private static (long Size, long LastWriteUtcTicks) ReadFacts(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists) throw new FileNotFoundException("The source is no longer available.", path);
        return (info.Length, info.LastWriteTimeUtc.Ticks);
    }

    private static string NormalizeMediaType(string mediaType) =>
        string.IsNullOrWhiteSpace(mediaType) ? "unknown" : mediaType.Trim().ToLowerInvariant();
}
