using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal static class CatalogBackupDestination
{
    public static string Default(LightflowStorageLocations locations) =>
        Path.Combine(locations.ApplicationDataDirectory, "Catalog Backups");

    public static string Validate(LightflowStorageLocations locations, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new ArgumentException("Choose an absolute backup folder path.");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory.Trim()));
        if (locations.IsIsolated) ApplicationDataProfile.RequireContained(locations.ApplicationDataDirectory, path);
        if (LightflowStorageLocations.PathsOverlap(path, locations.CatalogDirectory) ||
            LightflowStorageLocations.PathsOverlap(path, locations.PreviewsDirectory) ||
            LightflowStorageLocations.PathsOverlap(path, locations.TemporaryDirectory))
            throw new ArgumentException("Choose a backup folder separate from the live Catalog, Previews, and temporary storage.");
        // Reject aliases into managed storage, including non-isolated profiles.
        foreach (var storagePath in new[] { path, locations.CatalogDirectory, locations.PreviewsDirectory, locations.TemporaryDirectory })
        for (var ancestor = storagePath; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            if ((Directory.Exists(ancestor) || File.Exists(ancestor)) &&
                (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Catalog and backup storage must not use links or junctions; their separation could not be verified.");
        if (File.Exists(path)) throw new IOException("The backup location is a file, not a folder.");
        return path;
    }
}

internal sealed partial class SqliteCatalogRecoveryService
{
    internal static IReadOnlyList<CatalogBackup> ListUserBackups(LightflowStorageLocations locations, string destination)
    {
        try
        {
            var directory = CatalogBackupDestination.Validate(locations, destination);
            if (!Directory.Exists(directory)) return [];
            return Directory.EnumerateFiles(directory, "LightflowCatalog-User-v*.db")
                .Select(path =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(path),
                        @"^LightflowCatalog-User-v(\d+)-(\d{8}T\d{6}Z)(?:_\d{8})?\.db$");
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out var schema) ||
                        !DateTimeOffset.TryParseExact(match.Groups[2].Value, "yyyyMMdd'T'HHmmss'Z'",
                            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var created)) return null;
                    return new CatalogBackup(path, schema, created, CatalogBackupKind.UserRequested);
                }).Where(backup => backup is not null).Cast<CatalogBackup>().OrderByDescending(backup => backup.CreatedUtc).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return []; } // Unavailable destinations never fall back to a different profile/directory.
    }

    // A separate filename family prevents old automatic retention (even in older app versions)
    // from deleting user-requested copies. The file itself is an ordinary recoverable Catalog.
    internal Task<CatalogBackupResult> CreateUserBackupAsync(string databasePath, string destination,
        Guid expectedCatalogId, int expectedSchema, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) => Task.Run<CatalogBackupResult>(() =>
    {
        string? staging = null;
        string? metadataStaging = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = CatalogBackupDestination.Validate((LightflowStorageLocations)_locations, destination);
            progress?.Report("Checking the backup destination…");
            Directory.CreateDirectory(directory);
            // Creation/write failures are real errors; never switch to another destination.
            using (var probe = new FileStream(Path.Combine(directory, $".lightflow-probe-{Guid.NewGuid():N}"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            { probe.WriteByte(1); probe.Flush(true); }
            var identity = Inspect(databasePath, false, cancellationToken, "Backup source identity", verifyPages: false);
            if (!identity.IsValid || identity.CatalogId != expectedCatalogId || identity.SchemaVersion != expectedSchema)
                return new(false, Diagnostic: identity.Diagnostic ?? "The active Catalog identity or schema changed.");
            var now = _utcNow().ToUniversalTime();
            var basename = $"LightflowCatalog-User-v{expectedSchema}-{now:yyyyMMddTHHmmssZ}";
            var final = Path.Combine(directory, basename + ".db");
            for (var sequence = 1; File.Exists(final) || File.Exists(final + ".metadata.json"); sequence++)
                final = Path.Combine(directory, $"{basename}_{sequence:D8}.db");
            staging = final + $".{Guid.NewGuid():N}.incomplete";
            metadataStaging = staging + ".metadata.json";
            progress?.Report("Copying the Catalog…");
            BackupDatabase(databasePath, staging);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Validating the backup…");
            var validation = Inspect(staging, true, cancellationToken, "User backup full check");
            if (!validation.IsValid || validation.CatalogId != expectedCatalogId || validation.SchemaVersion != expectedSchema)
                return new(false, Diagnostic: validation.Diagnostic ?? "The backup identity or schema did not match the Catalog.");
            File.WriteAllText(metadataStaging, JsonSerializer.Serialize(new
            {
                Kind = CatalogBackupKind.UserRequested, CatalogId = expectedCatalogId,
                SchemaVersion = expectedSchema, CreatedUtc = now
            }));
            using (var durable = new FileStream(staging, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) durable.Flush(true);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staging, final); // Same-directory atomic publication, only after successful validation.
            // Metadata is supplementary: database identity/schema are self-contained.
            try { File.Move(metadataStaging, final + ".metadata.json"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return new(true, new(final, expectedSchema, now, CatalogBackupKind.UserRequested), $"Backup validated; supplementary metadata could not be saved: {ex.Message}"); }
            return new(true, new(final, expectedSchema, now, CatalogBackupKind.UserRequested));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or ArgumentException or NotSupportedException)
        { return new(false, Diagnostic: $"Catalog backup failed: {ex.Message}"); }
        finally
        {
            if (staging is not null) DeleteCatalogFiles(staging);
            if (metadataStaging is not null) try { File.Delete(metadataStaging); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }, cancellationToken);
}
