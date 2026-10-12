using System.IO;
using Lightflow.Application;
namespace LightflowStudio;

public partial class CatalogRecoveryService
{
    public static IReadOnlyList<CatalogBackup> ListUserBackups(ICatalogRecoveryNativeEffects effects, ILightflowStorageLocations locations, string destination)
    {
        try
        {
            var directory = CatalogBackupDestinationPolicy.Validate(effects, locations, destination);
            if (!Directory.Exists(directory)) return [];
            return effects.EnumerateOwnedFiles(directory, "LightflowCatalog-User-v*.db", SearchOption.TopDirectoryOnly)
                .Select(path =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(path),
                        @"^LightflowCatalog-User-v(\d+)-(\d{8}T\d{6}Z)(?:_\d{8})?\.db$");
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out var schema) ||
                        !DateTimeOffset.TryParseExact(match.Groups[2].Value, "yyyyMMdd'T'HHmmss'Z'",
                            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var created)) return null;
                    return new CatalogBackup(path, schema, created, CatalogBackupKind.UserRequested);
                }).Where(backup => backup is not null).Cast<CatalogBackup>()
                .Concat(ListTransportBackups(effects, locations, directory)).OrderByDescending(backup => backup.CreatedUtc).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return []; } // Unavailable destinations never fall back to a different profile/directory.
    }

    public Task<CatalogBackupResult> CreateUserBackupAsync(string databasePath, string destination,
        Guid expectedCatalogId, int expectedSchema, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default, Guid? diagnosticOperationId = null) =>
        CreateTransportBackupAsync(databasePath, destination, expectedCatalogId, expectedSchema, progress,
            cancellationToken, diagnosticOperationId);
}
