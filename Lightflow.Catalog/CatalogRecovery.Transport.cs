using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Lightflow.Application;

namespace LightflowStudio;

public partial class CatalogRecoveryService
{
    public async Task<CatalogBackupResult> CreateTransportBackupAsync(string databasePath, string destination,
        Guid expectedCatalogId, int expectedSchema, IProgress<string>? progress, CancellationToken token, Guid? diagnosticOperationId)
    {
        try
        {
            var locations = _locations;
            var directory = CatalogBackupDestinationPolicy.Validate(_effects, locations, destination);
            _effects.RequireUnlinked(directory);
            Directory.CreateDirectory(directory);
            var request = new ClosedCatalogTransferRequest(diagnosticOperationId ?? Guid.NewGuid(), 0,
                ClosedCatalogTransferMode.BackupTransport, expectedCatalogId, expectedSchema, CatalogDatabaseService.SqliteApplicationId);
            var now = _utcNow().ToUniversalTime();
            var container = Path.Combine(directory, $"LightflowCatalog-User-v{expectedSchema}-{now:yyyyMMddTHHmmssZ}-{request.OperationId:N}.incomplete");
            await using var journal = new CatalogTransferJournal(_effects, locations, request.OperationId);
            var operation = new ClosedCatalogTransferOperation(_effects, locations, container, "Source configuration retained", "No active configuration change",
                _ => Task.CompletedTask, (r, local, t) => FinalizeTransferSnapshotAsync(r, local, t));
            var stage = "DestinationAssessment";
            var stateProgress = new TransferProgress(state =>
            {
                if (state is ClosedCatalogTransferState.Failed or ClosedCatalogTransferState.Cancelled or ClosedCatalogTransferState.RecoveryRequired) return;
                stage = state switch { ClosedCatalogTransferState.SnapshotFinalized => "SQLiteSnapshot",
                    ClosedCatalogTransferState.Verified => "Verification", _ => state.ToString() };
                progress?.Report(state switch
                {
                    ClosedCatalogTransferState.SnapshotFinalized => "Creating a local Catalog snapshot…",
                    ClosedCatalogTransferState.Staging => "Copying the closed Catalog backup…",
                    ClosedCatalogTransferState.Verified => "Validating the backup by reading it back…",
                    _ => "Finishing the Catalog backup safely…"
                });
            });
            var result = await new ClosedCatalogTransferOrchestrator().ExecuteAsync(request, operation,
                journal, stateProgress, token).ConfigureAwait(false);
            if (result.State == ClosedCatalogTransferState.Cancelled) throw new OperationCanceledException(token);
            if (!result.Succeeded) return new(false, Diagnostic: $"Catalog backup: stage={stage}; did not complete. {result.Diagnostic} operationId={request.OperationId:N}; {result.State}.");
            return new(true, new(Path.Combine(container, "catalog.snapshot"), expectedSchema, operation.SnapshotUtc, CatalogBackupKind.UserRequested));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        { return new(false, Diagnostic: $"Catalog backup: stage=DestinationAssessment; failed: {e.Message}"); }
    }

    public static IReadOnlyList<CatalogBackup> ListTransportBackups(ICatalogRecoveryNativeEffects effects, ILightflowStorageLocations locations, string directory)
    {
        var result = new List<CatalogBackup>();
        if (!Directory.Exists(directory)) return result;
        foreach (var container in Directory.EnumerateDirectories(directory, "LightflowCatalog-User-*.incomplete"))
            try
            {
                var manifest = VerifyTransport(effects, container);
                var database = manifest.Artifacts.Single(a => a.Role == ClosedCatalogArtifactRole.CatalogDatabase);
                result.Add(new(new CatalogTransferFiles(effects).Child(container, database.RelativeName), manifest.SchemaVersion,
                    manifest.SnapshotUtc, CatalogBackupKind.UserRequested));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            { /* Incomplete or revoked containers never enter completed backup discovery. */ }
        return result;
    }
    public static ClosedCatalogManifest VerifyTransport(ICatalogRecoveryNativeEffects effects, string directory, bool allowCatalogOwnershipEvidence = false)
    {
        using var binding = effects.CreateAssessor([]);
        var request = new Lightflow.Domain.StorageAssessmentRequest(Guid.NewGuid(), 0,
            Lightflow.Domain.StorageRole.ClosedCatalogBackupTransfer, Lightflow.Domain.StorageOperation.Read, directory);
        var evidence = binding.AssessAsync(request).GetAwaiter().GetResult();
        var admission = StorageLocationPolicy.Evaluate(request, evidence, DateTimeOffset.UtcNow);
        if (admission.Decision != StorageLocationDecision.RequiresVerifiedStaging) throw new IOException(admission.Diagnostic);
        var manifest = ClosedCatalogManifestFormat.Deserialize(new CatalogTransferFiles(effects).ReadBounded(Path.Combine(directory, "manifest.json")));
        var readyBytes = new CatalogTransferFiles(effects).ReadBounded(Path.Combine(directory, "ready.json"));
        var ready = JsonSerializer.Deserialize<ClosedCatalogReadiness>(readyBytes) ?? throw new InvalidDataException("Missing backup readiness.");
        if (!JsonSerializer.SerializeToUtf8Bytes(ready).AsSpan().SequenceEqual(readyBytes)) throw new InvalidDataException("Inconsistent backup readiness.");
        ClosedCatalogManifestFormat.ValidateReadiness(manifest, ready);
        new CatalogTransferFiles(effects).VerifyMetadata(directory, manifest, ready);
        new CatalogTransferFiles(effects).ValidateDirectorySet(directory, manifest);
        // The caller already holds and validates this exclusive native ownership handle.
        // Do not reopen its journal as payload; all manifest artifacts still use closed reads.
        var ownedJournal = allowCatalogOwnershipEvidence && manifest.PublicationMode == ClosedCatalogPublicationMode.AtomicDirectory
            ? StartupStoreEvidence.JournalPath(Path.Combine(directory, CatalogLayout.CatalogFileName)) : null;
        var files = new CatalogTransferFiles(effects).EnumerateRegularFiles(directory, ownedJournal)
            .Select(p => Path.GetRelativePath(directory, p).Replace('\\', '/')).Where(p => p is not ("pending.json" or "manifest.json" or "ready.json"))
            .Where(p => !(allowCatalogOwnershipEvidence && manifest.PublicationMode == ClosedCatalogPublicationMode.AtomicDirectory && p == "LightflowCatalog.db.startup-state"))
            .Order(StringComparer.Ordinal);
        if (!files.SequenceEqual(manifest.Artifacts.Select(a => a.RelativeName).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Missing or unexpected backup artifacts.");
        foreach (var artifact in manifest.Artifacts)
        {
            using var input = new CatalogTransferFiles(effects).OpenClosedRead(new CatalogTransferFiles(effects).Child(directory, artifact.RelativeName));
            if (input.Length != artifact.ByteLength || Convert.ToHexString(SHA256.HashData(input)) != artifact.Sha256)
                throw new InvalidDataException("Backup payload readback did not verify.");
        }
        var current = binding.AssessAsync(request).GetAwaiter().GetResult();
        if (StorageLocationPolicy.Revalidate(request, evidence, current, DateTimeOffset.UtcNow).Decision != StorageLocationDecision.RequiresVerifiedStaging)
            throw new IOException("Transport native binding changed while validating it.");
        return manifest;
    }

    public async Task<string> CopyTransportBackLocallyAsync(string databasePath, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(databasePath)!;
        var manifest = VerifyTransport(_effects, directory);
        if (_files.Child(directory, manifest.Artifacts.Single(a => a.Role == ClosedCatalogArtifactRole.CatalogDatabase).RelativeName) != databasePath)
            throw new InvalidDataException("Selected transport payload is not its declared Catalog database.");
        var locations = _locations;
        // This is the verified output of this same copyback owner; avoid copying it again during restore inspection.
        if (directory.StartsWith(CatalogTransferJournal.DirectoryPath(_effects, locations) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            directory.EndsWith(".copyback", StringComparison.Ordinal))
        {
            using var localAdmission = _effects.CreateAssessor([]);
            var localRequest = new Lightflow.Domain.StorageAssessmentRequest(Guid.NewGuid(), 0, Lightflow.Domain.StorageRole.ActiveCatalog,
                Lightflow.Domain.StorageOperation.Open, directory);
            var localEvidence = await localAdmission.AssessAsync(localRequest, token).ConfigureAwait(false);
            if (StorageLocationPolicy.Evaluate(localRequest, localEvidence, DateTimeOffset.UtcNow, token).Decision != StorageLocationDecision.Eligible)
                throw new IOException("Verified restore input is no longer on qualified local Catalog storage.");
            return databasePath;
        }
        var request = new ClosedCatalogTransferRequest(Guid.NewGuid(), 0, ClosedCatalogTransferMode.LocalCopyback,
            manifest.CatalogId, manifest.SchemaVersion, manifest.ApplicationId);
        var destination = Path.Combine(CatalogTransferJournal.DirectoryPath(_effects, locations), request.OperationId.ToString("N") + ".copyback");
        await using var journal = new CatalogTransferJournal(_effects, locations, request.OperationId);
        var operation = new ClosedCatalogTransferOperation(_effects, locations, destination, "Current Catalog/configuration retained", "Verified local restore input only",
            _ => Task.CompletedTask, async (r, local, t) =>
            {
                VerifyTransport(_effects, directory);
                foreach (var artifact in manifest.Artifacts)
                {
                    var target = _files.Child(local, artifact.RelativeName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var input = _files.OpenClosedRead(_files.Child(directory, artifact.RelativeName));
                    await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
                    await input.CopyToAsync(output, t).ConfigureAwait(false); output.Flush(true);
                }
                return manifest with { OperationId = r.OperationId, Generation = r.Generation,
                    PublicationMode = ClosedCatalogPublicationMode.VerifiedContainer,
                    SnapshotProvenance = "Verified closed transport copied back locally; existing recovery validates SQLite before restore" };
            }, sourceDirectory: directory);
        var result = await new ClosedCatalogTransferOrchestrator().ExecuteAsync(request, operation,
            journal, token: token).ConfigureAwait(false);
        if (result.State == ClosedCatalogTransferState.Cancelled) throw new OperationCanceledException(token);
        if (!result.Succeeded) throw new IOException($"Backup local copyback failed: {result.Diagnostic}; operation {request.OperationId:N}.");
        return _files.Child(destination, manifest.Artifacts.Single(a => a.Role == ClosedCatalogArtifactRole.CatalogDatabase).RelativeName);
    }
    public async Task<string> PrepareRecoveryInputAsync(string databasePath, CancellationToken token)
    {
        if (IsTransportPayload(databasePath)) return await CopyTransportBackLocallyAsync(databasePath, token).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        using var binding = _effects.CreateAssessor([]);
        var operationId = Guid.NewGuid();
        var localRequest = new Lightflow.Domain.StorageAssessmentRequest(operationId, 0, Lightflow.Domain.StorageRole.ActiveCatalog,
            Lightflow.Domain.StorageOperation.Open, directory);
        var local = await binding.AssessAsync(localRequest, token).ConfigureAwait(false);
        if (StorageLocationPolicy.Evaluate(localRequest, local, DateTimeOffset.UtcNow, token).Decision == StorageLocationDecision.Eligible)
            return databasePath;
        var transportRequest = localRequest with { Role = Lightflow.Domain.StorageRole.ClosedCatalogBackupTransfer, Operation = Lightflow.Domain.StorageOperation.Read };
        var transport = await binding.AssessAsync(transportRequest, token).ConfigureAwait(false);
        if (StorageLocationPolicy.Evaluate(transportRequest, transport, DateTimeOffset.UtcNow, token).Decision != StorageLocationDecision.RequiresVerifiedStaging)
            throw new IOException("The legacy backup source cannot be safely read for local recovery.");
        if (new[] { "-wal", "-shm", "-journal" }.Any(suffix => File.Exists(databasePath + suffix)))
            throw new IOException("A legacy backup with live SQLite companions is not a closed transport input.");
        var locations = _locations;
        var spool = Path.Combine(CatalogTransferJournal.DirectoryPath(_effects, locations), operationId.ToString("N") + ".legacy-copyback");
        _effects.CreateExclusiveDirectory(spool);
        using var destinationBinding = _effects.CreateAssessor([]);
        var targetRequest = localRequest with { RequestedLocation = spool };
        var targetEvidence = await destinationBinding.AssessAsync(targetRequest, token).ConfigureAwait(false);
        if (StorageLocationPolicy.Evaluate(targetRequest, targetEvidence, DateTimeOffset.UtcNow, token).Decision != StorageLocationDecision.Eligible)
            throw new IOException("Legacy backup copyback requires qualified local Catalog storage.");
        var target = Path.Combine(spool, "legacy.snapshot");
        await using var input = _files.OpenClosedRead(databasePath);
        var length = input.Length;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false));
        input.Position = 0;
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
        { await input.CopyToAsync(output, token).ConfigureAwait(false); output.Flush(true); }
        using (var readback = _files.OpenClosedRead(target))
            if (readback.Length != length || Convert.ToHexString(SHA256.HashData(readback)) != hash)
                throw new InvalidDataException("Legacy backup local copyback did not verify.");
        var current = await binding.AssessAsync(transportRequest, token).ConfigureAwait(false);
        if (StorageLocationPolicy.Revalidate(transportRequest, transport, current, DateTimeOffset.UtcNow, token).Decision != StorageLocationDecision.RequiresVerifiedStaging)
            throw new IOException("Legacy backup source binding changed during copyback.");
        _files.WriteExclusive(Path.Combine(spool, "legacy-receipt.json"),
            JsonSerializer.SerializeToUtf8Bytes(new { OperationId = operationId, ByteLength = length, Sha256 = hash,
                SourceIdentity = transport.Identity, Disposition = "Verified local bytes; existing recovery must validate Catalog identity/schema/integrity" }));
        return target;
    }
    public static bool IsTransportPayload(string path) => Path.GetFileName(path) == "catalog.snapshot" ||
        File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "pending.json"));
    private async Task<RecoveryReadLease> AcquireRecoveryReadLeaseAsync(string path, bool closed, CancellationToken token)
    {
        path = await PrepareRecoveryInputAsync(path, token).ConfigureAwait(false);
        var assessor = _effects.CreateAssessor([]);
        try
        {
            var request = new Lightflow.Domain.StorageAssessmentRequest(Guid.NewGuid(), 0, Lightflow.Domain.StorageRole.ActiveCatalog,
                Lightflow.Domain.StorageOperation.Open, Path.GetDirectoryName(Path.GetFullPath(path))!);
            var evidence = await assessor.AssessAsync(request, token).ConfigureAwait(false);
            if (StorageLocationPolicy.Evaluate(request, evidence, DateTimeOffset.UtcNow, token).Decision != StorageLocationDecision.Eligible)
                throw new IOException("SQLite recovery input requires a fresh qualified local binding.");
            var resolvedPath = Path.Combine(evidence.Identity!.CanonicalLocation, Path.GetFileName(path));
            var file = _effects.OpenRecoveryReadGuard(resolvedPath, closed);
            return new(assessor, request, evidence, file, resolvedPath);
        }
        catch { assessor.Dispose(); throw; }
    }
    private sealed class RecoveryReadLease(ICatalogStorageAssessor assessor, Lightflow.Domain.StorageAssessmentRequest request,
        Lightflow.Domain.StorageLocationAssessment evidence, ICatalogRecoveryReadGuard file, string path) : IDisposable
    {
        internal string Path => path;
        internal void Revalidate(CancellationToken token)
        {
            var current = assessor.AssessAsync(request, token).GetAwaiter().GetResult();
            if (StorageLocationPolicy.Revalidate(request, evidence, current, DateTimeOffset.UtcNow, token).Decision != StorageLocationDecision.Eligible)
                throw new IOException("SQLite recovery input native binding changed.");
            file.RequireSingleRegularFile();
        }
        public void Dispose() { file.Dispose(); assessor.Dispose(); }
    }
    private sealed class TransferProgress(Action<ClosedCatalogTransferState> report) : IProgress<ClosedCatalogTransferState>
    { public void Report(ClosedCatalogTransferState state) => report(state); }
}
