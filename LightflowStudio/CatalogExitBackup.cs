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
            return ApplicationDataProfile.EnumerateOwnedFiles(directory, "LightflowCatalog-User-v*.db", SearchOption.TopDirectoryOnly)
                .Select(path =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(path),
                        @"^LightflowCatalog-User-v(\d+)-(\d{8}T\d{6}Z)(?:_\d{8})?\.db$");
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out var schema) ||
                        !DateTimeOffset.TryParseExact(match.Groups[2].Value, "yyyyMMdd'T'HHmmss'Z'",
                            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var created)) return null;
                    return new CatalogBackup(path, schema, created, CatalogBackupKind.UserRequested);
                }).Where(backup => backup is not null).Cast<CatalogBackup>()
                .Concat(ListTransportBackups(locations, directory)).OrderByDescending(backup => backup.CreatedUtc).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return []; } // Unavailable destinations never fall back to a different profile/directory.
    }

    internal Task<CatalogBackupResult> CreateUserBackupAsync(string databasePath, string destination,
        Guid expectedCatalogId, int expectedSchema, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default, Guid? diagnosticOperationId = null) =>
        CreateTransportBackupAsync(databasePath, destination, expectedCatalogId, expectedSchema, progress,
            cancellationToken, diagnosticOperationId);
}
