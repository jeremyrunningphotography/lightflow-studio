using System.Security.Cryptography;
using System.Text.Json;
using Lightflow.Domain;

namespace Lightflow.Application;

public enum ClosedCatalogTransferMode { MoveAndActivate, ClosedTransport, BackupTransport, LocalCopyback }
public enum ClosedCatalogTransferState
{
    Preflight, Quiescing, SnapshotFinalized, Staging, Verified, Published, Activating,
    ActivatedPendingConfiguration, ConfigurationCommitted, Completed, Cancelled, Failed, RecoveryRequired
}
public enum ClosedCatalogPublicationMode { AtomicDirectory, VerifiedContainer }
public enum ClosedCatalogArtifactRole { CatalogDatabase, ManagedBackup, BackupMetadata, ManagedDependency }

public sealed record ClosedCatalogTransferRequest(Guid OperationId, long Generation,
    ClosedCatalogTransferMode Mode, Guid CatalogId, int SchemaVersion, int ApplicationId);
public sealed record ClosedCatalogArtifact(string RelativeName, ClosedCatalogArtifactRole Role,
    long ByteLength, string Sha256, string ValidationDisposition = "Validated");
/// <summary>References only. This record never declares external content to be transferred payload.</summary>
public sealed record ClosedCatalogExternalDependency(Guid ResourceId, string ContentSha256,
    string OriginalName, string Diagnostic);
public sealed record ClosedCatalogManifest(int FormatVersion, Guid OperationId, long Generation,
    Guid CatalogId, int SchemaVersion, int ApplicationId, DateTimeOffset SnapshotUtc,
    string SnapshotProvenance, ClosedCatalogPublicationMode PublicationMode, int ReadinessVersion,
    IReadOnlyList<ClosedCatalogArtifact> Artifacts, IReadOnlyList<ClosedCatalogExternalDependency> ExternalDependencies);
public sealed record ClosedCatalogReadiness(int Version, Guid OperationId, long Generation,
    Guid CatalogId, string ManifestDigest, int ArtifactCount);
public sealed record ClosedCatalogTransferEvidence(StorageLocationAssessment Source,
    StorageLocationAssessment Destination, string PreviousConfiguration, string IntendedConfiguration,
    ClosedCatalogPublicationMode PublicationMode);
public sealed record ClosedCatalogTransferReceipt(string Evidence);
public sealed record ClosedCatalogTransferResult(ClosedCatalogTransferState State, Guid OperationId,
    string? ManifestDigest = null, string? Diagnostic = null)
{
    public bool Succeeded => State == ClosedCatalogTransferState.Completed;
}
public sealed record ClosedCatalogJournalRecord(int Version, ClosedCatalogTransferRequest Request,
    long Sequence, ClosedCatalogTransferState State, bool IsIntent, string PreviousRecordDigest,
    string? ManifestDigest, string? Receipt, ClosedCatalogTransferEvidence? PreflightEvidence = null);

/// <summary>Profile-owned durability port. Append returns only after flushed record/readback validation.</summary>
public interface IClosedCatalogTransferJournal : IAsyncDisposable
{
    Task AppendAsync(ClosedCatalogJournalRecord record, CancellationToken token);
    ValueTask IAsyncDisposable.DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Operation-scoped adapters to existing owners. Native effects must retain/revalidate object bindings;
/// snapshot and activation are existing lifecycle capabilities, not independent writer services.
/// All tokens/resources must be released by DisposeAsync, including failures and revoked paths.
/// </summary>
public interface IClosedCatalogTransferOperation : IAsyncDisposable
{
    Task<ClosedCatalogTransferEvidence> PreflightAsync(ClosedCatalogTransferRequest request, CancellationToken token);
    Task QuiesceAsync(CancellationToken token);
    Task<ClosedCatalogManifest> FinalizeSnapshotAsync(CancellationToken token);
    Task StageAsync(ClosedCatalogManifest manifest, CancellationToken token);
    Task RevalidateAsync(CancellationToken token);
    Task<IReadOnlyList<string>> ListStagedArtifactsAsync(CancellationToken token);
    Task<Stream> OpenStagedReadAsync(string relativeName, CancellationToken token);
    Task<ClosedCatalogTransferReceipt> PublishAsync(ClosedCatalogManifest manifest,
        ClosedCatalogReadiness readiness, CancellationToken token);
    Task<ClosedCatalogTransferReceipt> ActivateAsync(CancellationToken token);
    Task<ClosedCatalogTransferReceipt> CommitConfigurationAsync(CancellationToken token);
    Task CompleteAsync(CancellationToken token);
    /// <returns>True only when the prior safe owner/configuration is proven restored. Never delete by remembered paths.</returns>
    Task<bool> RollbackAsync(CancellationToken token);
}

public static class ClosedCatalogManifestFormat
{
    public const int Version = 1;
    public const int ReadinessVersion = 1;
    public const int MaximumArtifacts = 100_000;
    public const int MaximumMetadataBytes = 32 * 1024 * 1024;

    public static void Validate(ClosedCatalogManifest manifest, ClosedCatalogTransferRequest? expected = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.FormatVersion != Version || manifest.ReadinessVersion != ReadinessVersion ||
            manifest.OperationId == Guid.Empty || manifest.Generation < 0 || manifest.CatalogId == Guid.Empty ||
            manifest.SchemaVersion < 1 || manifest.ApplicationId == 0 ||
            !Enum.IsDefined(manifest.PublicationMode) || string.IsNullOrWhiteSpace(manifest.SnapshotProvenance) ||
            manifest.Artifacts is null || manifest.Artifacts.Count is < 1 or > MaximumArtifacts ||
            manifest.ExternalDependencies is null || manifest.ExternalDependencies.Count > MaximumArtifacts)
            throw new InvalidDataException("Unsupported or incomplete closed Catalog manifest.");
        if (expected is not null && (manifest.OperationId != expected.OperationId || manifest.Generation != expected.Generation ||
            manifest.CatalogId != expected.CatalogId || manifest.SchemaVersion != expected.SchemaVersion ||
            manifest.ApplicationId != expected.ApplicationId))
            throw new InvalidDataException("Closed Catalog manifest identity does not match the operation.");
        if (manifest.Artifacts.Any(a => a is null) || manifest.ExternalDependencies.Any(d => d is null))
            throw new InvalidDataException("Missing artifact or external dependency declaration.");
        if (manifest.Artifacts.Count(a => a.Role == ClosedCatalogArtifactRole.CatalogDatabase) != 1)
            throw new InvalidDataException("Exactly one finalized Catalog database is required.");
        var database = manifest.Artifacts.Single(a => a.Role == ClosedCatalogArtifactRole.CatalogDatabase);
        if (database.RelativeName != (manifest.PublicationMode == ClosedCatalogPublicationMode.AtomicDirectory ? "LightflowCatalog.db" : "catalog.snapshot") ||
            manifest.Artifacts.Any(a => a.RelativeName is "pending.json" or "manifest.json" or "ready.json"))
            throw new InvalidDataException("Unsupported database name or reserved transport metadata payload.");
        foreach (var artifact in manifest.Artifacts)
            if (!Enum.IsDefined(artifact.Role) || artifact.ByteLength < 0 || !ValidHash(artifact.Sha256) ||
                !PortablePathIdentityValidator.ValidateRelativePath(artifact.RelativeName).IsSafe ||
                string.IsNullOrWhiteSpace(artifact.ValidationDisposition))
                throw new InvalidDataException("Unsafe or unsupported closed Catalog artifact.");
        var names = PortablePathIdentityValidator.ValidateCandidates(manifest.Artifacts.Select(a =>
            new PathIdentityCandidate(manifest.CatalogId, Guid.Empty, a.RelativeName)));
        if (!names.IsSafe) throw new InvalidDataException(names.Diagnostic);
        var paths = manifest.Artifacts.Select(a => a.RelativeName).ToHashSet(StringComparer.Ordinal);
        if (paths.Any(p => p.Split('/').SkipLast(1).Aggregate((Prefix: "", Conflict: false),
            (s, part) => (s.Prefix.Length == 0 ? part : s.Prefix + "/" + part,
                s.Conflict || paths.Contains(s.Prefix.Length == 0 ? part : s.Prefix + "/" + part))).Conflict))
            throw new InvalidDataException("An artifact cannot also be a containing directory.");
        if (manifest.ExternalDependencies.Any(d => d.ResourceId == Guid.Empty || !ValidHash(d.ContentSha256) ||
            string.IsNullOrWhiteSpace(d.Diagnostic)) ||
            manifest.ExternalDependencies.Select(d => d.ResourceId).Distinct().Count() != manifest.ExternalDependencies.Count)
            throw new InvalidDataException("Invalid external dependency report.");
    }

    public static byte[] Serialize(ClosedCatalogManifest manifest)
    {
        Validate(manifest);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest with
        {
            Artifacts = manifest.Artifacts.OrderBy(a => a.RelativeName, StringComparer.Ordinal).ToArray(),
            ExternalDependencies = manifest.ExternalDependencies.OrderBy(d => d.ResourceId).ToArray()
        });
        if (bytes.Length > MaximumMetadataBytes) throw new InvalidDataException("Manifest exceeds its format bound.");
        return bytes;
    }
    public static ClosedCatalogManifest Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumMetadataBytes) throw new InvalidDataException("Manifest exceeds its format bound.");
        var manifest = JsonSerializer.Deserialize<ClosedCatalogManifest>(bytes) ?? throw new InvalidDataException("Missing manifest.");
        Validate(manifest);
        // One canonical encoding also rejects ignored fields, duplicate properties and inconsistent envelopes.
        if (!Serialize(manifest).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Noncanonical or inconsistent manifest.");
        return manifest;
    }
    public static string Digest(ClosedCatalogManifest manifest) => Convert.ToHexString(SHA256.HashData(Serialize(manifest)));
    public static bool ValidHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    public static void ValidateReadiness(ClosedCatalogManifest manifest, ClosedCatalogReadiness readiness)
    {
        if (readiness.Version != ReadinessVersion || readiness.OperationId != manifest.OperationId ||
            readiness.Generation != manifest.Generation || readiness.CatalogId != manifest.CatalogId ||
            readiness.ArtifactCount != manifest.Artifacts.Count || readiness.ManifestDigest != Digest(manifest))
            throw new InvalidDataException("Incomplete or mismatched Catalog readiness receipt.");
    }

    public static async Task VerifyAsync(ClosedCatalogManifest manifest, IClosedCatalogTransferOperation operation,
        CancellationToken token)
    {
        Validate(manifest);
        await operation.RevalidateAsync(token).ConfigureAwait(false);
        var actual = await operation.ListStagedArtifactsAsync(token).ConfigureAwait(false);
        if (!actual.Order(StringComparer.Ordinal).SequenceEqual(manifest.Artifacts.Select(a => a.RelativeName).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Missing or unexpected transport artifacts.");
        foreach (var artifact in manifest.Artifacts)
        {
            token.ThrowIfCancellationRequested();
            await operation.RevalidateAsync(token).ConfigureAwait(false);
            await using var stream = await operation.OpenStagedReadAsync(artifact.RelativeName, token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long length = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                length = checked(length + read);
                if (length > artifact.ByteLength) throw new InvalidDataException("Artifact grew beyond its declared size.");
                hash.AppendData(buffer.AsSpan(0, read));
            }
            if (length != artifact.ByteLength || Convert.ToHexString(hash.GetHashAndReset()) != artifact.Sha256)
                throw new InvalidDataException($"Closed Catalog readback did not verify: {artifact.RelativeName}.");
        }
        await operation.RevalidateAsync(token).ConfigureAwait(false);
    }
}

/// <summary>One neutral state machine. Only verified publication plus lifecycle/config receipts can complete a move.</summary>
public sealed class ClosedCatalogTransferOrchestrator
{
    public async Task<ClosedCatalogTransferResult> ExecuteAsync(ClosedCatalogTransferRequest request,
        IClosedCatalogTransferOperation operation, IClosedCatalogTransferJournal journal,
        IProgress<ClosedCatalogTransferState>? progress = null, CancellationToken token = default)
    {
        if (request.OperationId == Guid.Empty || request.Generation < 0 || !Enum.IsDefined(request.Mode) ||
            request.CatalogId == Guid.Empty || request.SchemaVersion < 1 || request.ApplicationId == 0)
            throw new ArgumentException("A complete closed transfer request is required.", nameof(request));
        long sequence = 0;
        var previousDigest = "";
        string? digest = null;
        ClosedCatalogTransferEvidence? evidence = null;
        var committed = false;
        var phase = ClosedCatalogTransferState.Preflight;
        async Task Record(ClosedCatalogTransferState state, bool intent, string? receipt = null)
        {
            var record = new ClosedCatalogJournalRecord(1, request, sequence++, state, intent, previousDigest,
                digest, receipt, state == ClosedCatalogTransferState.Preflight && !intent ? evidence : null);
            await journal.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
            previousDigest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(record)));
            if (intent) progress?.Report(state);
            if (intent) phase = state;
        }
        async Task Step(ClosedCatalogTransferState state, Func<CancellationToken, Task<string?>> effect, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            await Record(state, true).ConfigureAwait(false);
            var receipt = await effect(cancellation).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(receipt)) throw new InvalidDataException("Transfer effect returned no verified receipt.");
            await Record(state, false, receipt).ConfigureAwait(false);
        }
        await using var journalLifetime = journal.ConfigureAwait(false);
        await using (operation.ConfigureAwait(false))
        {
            try
            {
                await Step(ClosedCatalogTransferState.Preflight, async t =>
                {
                    evidence = await operation.PreflightAsync(request, t).ConfigureAwait(false);
                    foreach (var assessment in new[] { evidence.Source, evidence.Destination })
                    {
                        var result = StorageLocationPolicy.Evaluate(assessment.Request, assessment, DateTimeOffset.UtcNow, t);
                        if (result.Decision is not (StorageLocationDecision.Eligible or StorageLocationDecision.RequiresVerifiedStaging))
                            throw new InvalidDataException(result.Diagnostic);
                        if (assessment.Request.OperationId != request.OperationId || assessment.Request.Generation != request.Generation)
                            throw new InvalidDataException("Storage evidence belongs to another transfer operation.");
                    }
                    if (evidence.Source.Request.Role is not (StorageRole.ActiveCatalog or StorageRole.ClosedCatalogBackupTransfer))
                        throw new InvalidDataException("Media storage is not a closed Catalog source.");
                    if (request.Mode == ClosedCatalogTransferMode.LocalCopyback && evidence.Destination.Request.Role != StorageRole.ActiveCatalog)
                        throw new InvalidDataException("Copyback requires qualified local Catalog storage.");
                    if (request.Mode == ClosedCatalogTransferMode.MoveAndActivate &&
                        (evidence.Destination.Request.Role != StorageRole.ActiveCatalog ||
                         evidence.PublicationMode != ClosedCatalogPublicationMode.AtomicDirectory))
                        throw new InvalidDataException("Active Catalog publication requires a qualified local atomic directory operation.");
                    return "Storage and configuration preflight verified";
                }, token).ConfigureAwait(false);
                await Step(ClosedCatalogTransferState.Quiescing, async t => { await operation.QuiesceAsync(t).ConfigureAwait(false); return "Existing lifecycle boundary established"; }, token).ConfigureAwait(false);
                ClosedCatalogManifest? manifest = null;
                await Step(ClosedCatalogTransferState.SnapshotFinalized, async t =>
                {
                    manifest = await operation.FinalizeSnapshotAsync(t).ConfigureAwait(false);
                    ClosedCatalogManifestFormat.Validate(manifest, request);
                    if (manifest.PublicationMode != evidence!.PublicationMode) throw new InvalidDataException("Publication capability changed.");
                    digest = ClosedCatalogManifestFormat.Digest(manifest);
                    return manifest.SnapshotProvenance;
                }, token).ConfigureAwait(false);
                await Step(ClosedCatalogTransferState.Staging, async t => { await operation.StageAsync(manifest!, t).ConfigureAwait(false); return digest; }, token).ConfigureAwait(false);
                await Step(ClosedCatalogTransferState.Verified, async t => { await ClosedCatalogManifestFormat.VerifyAsync(manifest!, operation, t).ConfigureAwait(false); return digest; }, token).ConfigureAwait(false);
                // Cancellation is observed once before the noncancellable publication/activation/config interval.
                token.ThrowIfCancellationRequested();
                var ready = new ClosedCatalogReadiness(1, request.OperationId, request.Generation, request.CatalogId, digest!, manifest!.Artifacts.Count);
                await Step(ClosedCatalogTransferState.Published, async t => (await operation.PublishAsync(manifest, ready, t).ConfigureAwait(false)).Evidence, CancellationToken.None).ConfigureAwait(false);
                if (request.Mode == ClosedCatalogTransferMode.MoveAndActivate)
                {
                    await Record(ClosedCatalogTransferState.Activating, true).ConfigureAwait(false);
                    var activated = await operation.ActivateAsync(CancellationToken.None).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(activated.Evidence)) throw new InvalidDataException("Existing lifecycle returned no activation receipt.");
                    await Record(ClosedCatalogTransferState.Activating, false, activated.Evidence).ConfigureAwait(false);
                    await Step(ClosedCatalogTransferState.ActivatedPendingConfiguration, _ => Task.FromResult<string?>(activated.Evidence), CancellationToken.None).ConfigureAwait(false);
                    await Step(ClosedCatalogTransferState.ConfigurationCommitted, async t =>
                    {
                        var receipt = await operation.CommitConfigurationAsync(t).ConfigureAwait(false);
                        committed = true;
                        return receipt.Evidence;
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                await Record(ClosedCatalogTransferState.Completed, true).ConfigureAwait(false);
                // Persist completion before releasing authoring. A failure to release remains recovery-required.
                await Record(ClosedCatalogTransferState.Completed, false, digest).ConfigureAwait(false);
                await operation.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
                return new(ClosedCatalogTransferState.Completed, request.OperationId, digest);
            }
            catch (Exception error)
            {
                var failedPhase = phase;
                var restored = false;
                if (!committed)
                    try { restored = await operation.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception rollback) { error = new AggregateException(error, rollback); }
                var state = restored ? (error is OperationCanceledException ? ClosedCatalogTransferState.Cancelled : ClosedCatalogTransferState.Failed)
                    : ClosedCatalogTransferState.RecoveryRequired;
                var recoveryReceipt = $"RecoveryDisposition={(restored ? "PreviousOwnerVerified" : "AuthoringBlockedEvidenceRetained")}; {error.Message}";
                try { await Record(state, true, recoveryReceipt).ConfigureAwait(false); await Record(state, false, recoveryReceipt).ConfigureAwait(false); }
                catch (Exception journalError) { state = ClosedCatalogTransferState.RecoveryRequired; error = new AggregateException(error, journalError); }
                return new(state, request.OperationId, digest, $"During {failedPhase}: {error.Message}");
            }
        }
    }
}
