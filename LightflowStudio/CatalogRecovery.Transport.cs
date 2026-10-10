using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Lightflow.Application;

namespace LightflowStudio;

internal sealed partial class SqliteCatalogRecoveryService
{
    internal async Task<CatalogBackupResult> CreateTransportBackupAsync(string databasePath, string destination,
        Guid expectedCatalogId, int expectedSchema, IProgress<string>? progress, CancellationToken token, Guid? diagnosticOperationId)
    {
        try
        {
            var locations = (LightflowStorageLocations)_locations;
            var directory = CatalogBackupDestination.Validate(locations, destination);
            ClosedCatalogTransferJournal.RequireUnlinked(directory);
            Directory.CreateDirectory(directory);
            var request = new ClosedCatalogTransferRequest(diagnosticOperationId ?? Guid.NewGuid(), 0,
                ClosedCatalogTransferMode.BackupTransport, expectedCatalogId, expectedSchema, CatalogDatabaseService.SqliteApplicationId);
            var now = _utcNow().ToUniversalTime();
            var container = Path.Combine(directory, $"LightflowCatalog-User-v{expectedSchema}-{now:yyyyMMddTHHmmssZ}-{request.OperationId:N}.incomplete");
            await using var journal = new ClosedCatalogTransferJournal(locations, request.OperationId);
            var operation = new WindowsClosedCatalogTransferOperation(locations, container, "Source configuration retained", "No active configuration change",
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

    internal static IReadOnlyList<CatalogBackup> ListTransportBackups(LightflowStorageLocations locations, string directory)
    {
        var result = new List<CatalogBackup>();
        if (!Directory.Exists(directory)) return result;
        foreach (var container in Directory.EnumerateDirectories(directory, "LightflowCatalog-User-*.incomplete"))
            try
            {
                var manifest = VerifyTransport(container);
                var database = manifest.Artifacts.Single(a => a.Role == ClosedCatalogArtifactRole.CatalogDatabase);
                result.Add(new(WindowsClosedCatalogTransferOperation.Child(container, database.RelativeName), manifest.SchemaVersion,
                    manifest.SnapshotUtc, CatalogBackupKind.UserRequested));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
            { /* Incomplete or revoked containers never enter completed backup discovery. */ }
        return result;
    }
    internal static ClosedCatalogManifest VerifyTransport(string directory)
    {
        using var binding = new WindowsStorageLocationAssessor();
        var request = new Lightflow.Domain.StorageAssessmentRequest(Guid.NewGuid(), 0,
            Lightflow.Domain.StorageRole.ClosedCatalogBackupTransfer, Lightflow.Domain.StorageOperation.Read, directory);
        var evidence = binding.AssessAsync(request).GetAwaiter().GetResult();
        var admission = StorageLocationPolicy.Evaluate(request, evidence, DateTimeOffset.UtcNow);
        if (admission.Decision != StorageLocationDecision.RequiresVerifiedStaging) throw new IOException(admission.Diagnostic);
        var manifest = ClosedCatalogManifestFormat.Deserialize(WindowsClosedCatalogTransferOperation.ReadBounded(Path.Combine(directory, "manifest.json")));
        var readyBytes = WindowsClosedCatalogTransferOperation.ReadBounded(Path.Combine(directory, "ready.json"));
        var ready = JsonSerializer.Deserialize<ClosedCatalogReadiness>(readyBytes) ?? throw new InvalidDataException("Missing backup readiness.");
        if (!JsonSerializer.SerializeToUtf8Bytes(ready).AsSpan().SequenceEqual(readyBytes)) throw new InvalidDataException("Inconsistent backup readiness.");
        ClosedCatalogManifestFormat.ValidateReadiness(manifest, ready);
        WindowsClosedCatalogTransferOperation.VerifyMetadata(directory, manifest, ready);
        WindowsClosedCatalogTransferOperation.ValidateDirectorySet(directory, manifest);
        var files = WindowsClosedCatalogTransferOperation.EnumerateRegularFiles(directory)
            .Select(p => Path.GetRelativePath(directory, p).Replace('\\', '/')).Where(p => p is not ("pending.json" or "manifest.json" or "ready.json"))
            .Order(StringComparer.Ordinal);
        if (!files.SequenceEqual(manifest.Artifacts.Select(a => a.RelativeName).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Missing or unexpected backup artifacts.");
        foreach (var artifact in manifest.Artifacts)
        {
            using var input = WindowsClosedCatalogTransferOperation.OpenClosedRead(WindowsClosedCatalogTransferOperation.Child(directory, artifact.RelativeName));
            if (input.Length != artifact.ByteLength || Convert.ToHexString(SHA256.HashData(input)) != artifact.Sha256)
                throw new InvalidDataException("Backup payload readback did not verify.");
        }
        var current = binding.AssessAsync(request).GetAwaiter().GetResult();
        if (StorageLocationPolicy.Revalidate(request, evidence, current, DateTimeOffset.UtcNow).Decision != StorageLocationDecision.RequiresVerifiedStaging)
            throw new IOException("Transport native binding changed while validating it.");
        return manifest;
    }

    internal async Task<string> CopyTransportBackLocallyAsync(string databasePath, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(databasePath)!;
        var manifest = VerifyTransport(directory);
        if (WindowsClosedCatalogTransferOperation.Child(directory, manifest.Artifacts.Single(a => a.Role == ClosedCatalogArtifactRole.CatalogDatabase).RelativeName) != databasePath)
            throw new InvalidDataException("Selected transport payload is not its declared Catalog database.");
        var locations = (LightflowStorageLocations)_locations;
        // This is the verified output of this same copyback owner; avoid copying it again during restore inspection.
        if (directory.StartsWith(ClosedCatalogTransferJournal.DirectoryPath(locations) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            directory.EndsWith(".copyback", StringComparison.Ordinal))
        {
            using var localAdmission = new WindowsStorageLocationAssessor();
            var localRequest = new Lightflow.Domain.StorageAssessmentRequest(Guid.NewGuid(), 0, Lightflow.Domain.StorageRole.ActiveCatalog,
                Lightflow.Domain.StorageOperation.Open, directory);
            var localEvidence = await localAdmission.AssessAsync(localRequest, token).ConfigureAwait(false);
            if (StorageLocationPolicy.Evaluate(localRequest, localEvidence, DateTimeOffset.UtcNow, token).Decision != StorageLocationDecision.Eligible)
                throw new IOException("Verified restore input is no longer on qualified local Catalog storage.");
            return databasePath;
        }
        var request = new ClosedCatalogTransferRequest(Guid.NewGuid(), 0, ClosedCatalogTransferMode.LocalCopyback,
            manifest.CatalogId, manifest.SchemaVersion, manifest.ApplicationId);
        var destination = Path.Combine(ClosedCatalogTransferJournal.DirectoryPath(locations), request.OperationId.ToString("N") + ".copyback");
        await using var journal = new ClosedCatalogTransferJournal(locations, request.OperationId);
        var operation = new WindowsClosedCatalogTransferOperation(locations, destination, "Current Catalog/configuration retained", "Verified local restore input only",
            _ => Task.CompletedTask, async (r, local, t) =>
            {
                VerifyTransport(directory);
                foreach (var artifact in manifest.Artifacts)
                {
                    var target = WindowsClosedCatalogTransferOperation.Child(local, artifact.RelativeName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var input = WindowsClosedCatalogTransferOperation.OpenClosedRead(WindowsClosedCatalogTransferOperation.Child(directory, artifact.RelativeName));
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
        return WindowsClosedCatalogTransferOperation.Child(destination, manifest.Artifacts.Single(a => a.Role == ClosedCatalogArtifactRole.CatalogDatabase).RelativeName);
    }
    internal async Task<string> PrepareRecoveryInputAsync(string databasePath, CancellationToken token)
    {
        if (IsTransportPayload(databasePath)) return await CopyTransportBackLocallyAsync(databasePath, token).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        using var binding = new WindowsStorageLocationAssessor();
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
        var locations = (LightflowStorageLocations)_locations;
        var spool = Path.Combine(ClosedCatalogTransferJournal.DirectoryPath(locations), operationId.ToString("N") + ".legacy-copyback");
        WindowsClosedCatalogTransferOperation.CreateExclusiveDirectory(spool);
        using var destinationBinding = new WindowsStorageLocationAssessor();
        var targetRequest = localRequest with { RequestedLocation = spool };
        var targetEvidence = await destinationBinding.AssessAsync(targetRequest, token).ConfigureAwait(false);
        if (StorageLocationPolicy.Evaluate(targetRequest, targetEvidence, DateTimeOffset.UtcNow, token).Decision != StorageLocationDecision.Eligible)
            throw new IOException("Legacy backup copyback requires qualified local Catalog storage.");
        var target = Path.Combine(spool, "legacy.snapshot");
        await using var input = WindowsClosedCatalogTransferOperation.OpenClosedRead(databasePath);
        var length = input.Length;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false));
        input.Position = 0;
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
        { await input.CopyToAsync(output, token).ConfigureAwait(false); output.Flush(true); }
        using (var readback = WindowsClosedCatalogTransferOperation.OpenClosedRead(target))
            if (readback.Length != length || Convert.ToHexString(SHA256.HashData(readback)) != hash)
                throw new InvalidDataException("Legacy backup local copyback did not verify.");
        var current = await binding.AssessAsync(transportRequest, token).ConfigureAwait(false);
        if (StorageLocationPolicy.Revalidate(transportRequest, transport, current, DateTimeOffset.UtcNow, token).Decision != StorageLocationDecision.RequiresVerifiedStaging)
            throw new IOException("Legacy backup source binding changed during copyback.");
        WindowsClosedCatalogTransferOperation.WriteExclusive(Path.Combine(spool, "legacy-receipt.json"),
            JsonSerializer.SerializeToUtf8Bytes(new { OperationId = operationId, ByteLength = length, Sha256 = hash,
                SourceIdentity = transport.Identity, Disposition = "Verified local bytes; existing recovery must validate Catalog identity/schema/integrity" }));
        return target;
    }
    internal static bool IsTransportPayload(string path) => Path.GetFileName(path) == "catalog.snapshot" ||
        File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "pending.json"));
    private sealed class TransferProgress(Action<ClosedCatalogTransferState> report) : IProgress<ClosedCatalogTransferState>
    { public void Report(ClosedCatalogTransferState state) => report(state); }
}
