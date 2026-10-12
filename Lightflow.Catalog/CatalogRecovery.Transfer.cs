using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Lightflow.Application;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

public partial class CatalogRecoveryService
{
    // Existing recovery owner finalizes consistency; neutral transfer never opens SQLite.
    public Task<ClosedCatalogManifest> FinalizeTransferSnapshotAsync(ClosedCatalogTransferRequest request,
        string localDirectory, CancellationToken token, ICatalogRelocationTransfer? compatibility = null) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        var sourceIdentity = Inspect(_locations.CatalogDatabasePath, false, token, "Backup source identity", verifyPages: false);
        if (!sourceIdentity.IsValid || sourceIdentity.CatalogId != request.CatalogId || sourceIdentity.SchemaVersion != request.SchemaVersion)
            throw new InvalidDataException("stage=SourceDatabaseReadiness; " + (sourceIdentity.Diagnostic ?? "Source Catalog identity/schema changed."));
        var moving = request.Mode == ClosedCatalogTransferMode.MoveAndActivate;
        var databaseName = moving ? CatalogLayout.CatalogFileName : "catalog.snapshot";
        if (moving) ValidateCatalogUnit(request);
        var snapshot = Path.Combine(localDirectory, databaseName);
        if (compatibility is not null) compatibility.Backup(_locations.CatalogDatabasePath, snapshot);
        else BackupDatabase(_effects, _locations.CatalogDatabasePath, snapshot);
        token.ThrowIfCancellationRequested();
        var inspected = Inspect(snapshot, true, token, "Closed transfer snapshot full check");
        if (!inspected.IsValid || inspected.CatalogId != request.CatalogId || inspected.SchemaVersion != request.SchemaVersion)
            throw new InvalidDataException(inspected.Diagnostic ?? "Finalized snapshot identity/schema changed.");
        if (new[] { "-wal", "-shm", "-journal" }.Any(suffix => File.Exists(snapshot + suffix)))
            throw new InvalidDataException("Snapshot is not a closed standalone SQLite artifact.");
        using (var durable = new FileStream(snapshot, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) durable.Flush(true);
        var artifacts = new List<ClosedCatalogArtifact> { Describe(snapshot, databaseName, ClosedCatalogArtifactRole.CatalogDatabase) };
        if (moving && Directory.Exists(_locations.CatalogBackupsDirectory))
        {
            foreach (var file in _files.EnumerateRegularFiles(_locations.CatalogBackupsDirectory))
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                var disposition = "Supplementary managed backup metadata";
                var relative = "Backups/" + name;
                var target = _files.Child(localDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using (var input = _files.OpenClosedRead(file))
                await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
                { await input.CopyToAsync(output, token).ConfigureAwait(false); output.Flush(true); }
                if (name.EndsWith(".db", StringComparison.Ordinal))
                {
                    var validation = Inspect(target, true, token, "Managed backup transfer evidence");
                    disposition = !validation.IsValid ? "Retained historical evidence: " + validation.Diagnostic
                        : validation.CatalogId != request.CatalogId ? "Retained historical evidence: different Catalog identity"
                        : "Validated historical backup";
                }
                artifacts.Add(Describe(target, relative, name.EndsWith(".db", StringComparison.Ordinal)
                    ? ClosedCatalogArtifactRole.ManagedBackup : ClosedCatalogArtifactRole.BackupMetadata, disposition));
            }
        }
        var dependencies = ReadExternalLutDependencies(_effects, snapshot);
        return new ClosedCatalogManifest(1, request.OperationId, request.Generation, request.CatalogId,
            request.SchemaVersion, request.ApplicationId, _utcNow().ToUniversalTime(), "Existing recovery SQLite online backup; full integrity/identity/schema checked; connections closed",
            moving ? ClosedCatalogPublicationMode.AtomicDirectory : ClosedCatalogPublicationMode.VerifiedContainer,
            1, artifacts, dependencies);
    }, token);

    public static void FinalizeRelocationCompatibility(ICatalogRecoveryNativeEffects effects, string source, string destination) => BackupDatabase(effects, source, destination);

    private void ValidateCatalogUnit(ClosedCatalogTransferRequest request)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(_locations.CatalogDirectory))
        {
            var name = Path.GetFileName(entry);
            _effects.RequireUnlinked(entry);
            if (name == "Backups" && Directory.Exists(entry)) continue;
            if (name == CatalogLayout.CatalogFileName ||
                name is "LightflowCatalog.db-wal" or "LightflowCatalog.db-shm" or "LightflowCatalog.db-journal" or "LightflowCatalog.db.startup-state") continue;
            // Prior transfer metadata is provenance of pre-open bytes, never a fresh active-Catalog hash receipt.
            if (name == "manifest.json")
            {
                var previous = ClosedCatalogManifestFormat.Deserialize(_files.ReadBounded(entry));
                if (previous.CatalogId == request.CatalogId) continue;
            }
            if (name is "pending.json" or "ready.json")
            {
                using var json = JsonDocument.Parse(_files.ReadBounded(entry));
                if (json.RootElement.TryGetProperty("CatalogId", out var id) && id.GetGuid() == request.CatalogId) continue;
            }
            throw new IOException($"Unknown Catalog-owned entry requires review before transfer: {name}. No files were discarded.");
        }
        if (!Directory.Exists(_locations.CatalogBackupsDirectory)) return;
        var known = ListBackups().Select(b => b.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Directory.EnumerateFileSystemEntries(_locations.CatalogBackupsDirectory))
            if (!known.Contains(entry) && !(entry.EndsWith(".metadata.json", StringComparison.Ordinal) && known.Contains(entry[..^14])))
                throw new IOException($"Unknown managed Backups entry requires review: {Path.GetFileName(entry)}.");
    }
    private ClosedCatalogArtifact Describe(string path, string relative, ClosedCatalogArtifactRole role, string disposition = "Validated")
    {
        using var file = _files.OpenClosedRead(path);
        return new(relative, role, file.Length, Convert.ToHexString(SHA256.HashData(file)), disposition);
    }
    public static IReadOnlyList<ClosedCatalogExternalDependency> ReadExternalLutDependencies(ICatalogRecoveryNativeEffects effects, string snapshot)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = effects.RecoverySqlitePath(snapshot), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT LutId,ContentSha256,OriginalFileName FROM LutResources ORDER BY LutId;";
        using var reader = command.ExecuteReader();
        var result = new List<ClosedCatalogExternalDependency>();
        while (reader.Read()) result.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1).ToUpperInvariant(), reader.GetString(2),
            "External LUT reference retained. File bytes are outside Catalog ownership and are not transferred; existing resource resolution determines availability."));
        reader.Close();
        command.CommandText = "SELECT RootId,AssetId,RelativePath FROM MediaAssets ORDER BY RootId,AssetId;";
        using var paths = command.ExecuteReader();
        var candidates = new List<PathIdentityCandidate>();
        while (paths.Read()) candidates.Add(new(Guid.Parse(paths.GetString(0)), Guid.Parse(paths.GetString(1)), paths.GetString(2)));
        var validation = PortablePathIdentityValidator.ValidateCandidates(candidates);
        if (!validation.IsSafe) throw new InvalidDataException(validation.Diagnostic);
        return result;
    }
}
