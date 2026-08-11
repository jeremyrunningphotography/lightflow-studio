using System.IO;

namespace LightflowStudio;

internal interface ILightflowStorageLocations
{
    string ApplicationDataDirectory { get; }
    string SettingsPath { get; }
    string StatePath { get; }
    string JobHistoryPath { get; }
    string TrimHistoryPath { get; }
    string OutputIdentityDirectory { get; }
    string CatalogDirectory { get; }
    string CatalogDatabasePath { get; }
    string CatalogBackupsDirectory { get; }
    string PreviewsDirectory { get; }
    string TemporaryDirectory { get; }
    string LogsDirectory { get; }
    string ActivityLogPath { get; }
}

internal sealed record LightflowStorageLocationOptions(
    string? CatalogDirectory = null,
    string? PreviewsDirectory = null);

internal sealed record LightflowStorageLocations : ILightflowStorageLocations
{
    public const string CatalogFileName = "LightflowCatalog.db";
    public static LightflowStorageLocations Current { get; } = CreateDefault();

    public required string ApplicationDataDirectory { get; init; }
    public required string SettingsPath { get; init; }
    public required string StatePath { get; init; }
    public required string JobHistoryPath { get; init; }
    public required string TrimHistoryPath { get; init; }
    public required string OutputIdentityDirectory { get; init; }
    public required string CatalogDirectory { get; init; }
    public required string CatalogDatabasePath { get; init; }
    public required string CatalogBackupsDirectory { get; init; }
    public required string PreviewsDirectory { get; init; }
    public required string TemporaryDirectory { get; init; }
    public required string LogsDirectory { get; init; }
    public required string ActivityLogPath { get; init; }

    public static LightflowStorageLocations CreateDefault(LightflowStorageLocationOptions? options = null) =>
        Create(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), options);

    internal static LightflowStorageLocations Create(string localApplicationData,
        LightflowStorageLocationOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        options ??= new();
        var applicationData = Path.GetFullPath(Path.Combine(localApplicationData,
            "Jeremy Running Photography", "Lightflow Studio"));
        var catalogDirectory = ResolveOverride(options.CatalogDirectory,
            Path.Combine(applicationData, "Catalog"), nameof(options.CatalogDirectory));
        var previewsDirectory = ResolveOverride(options.PreviewsDirectory,
            Path.Combine(applicationData, "Previews"), nameof(options.PreviewsDirectory));
        var temporaryDirectory = Path.Combine(applicationData, "Temporary");
        ValidateOwnershipBoundaries(catalogDirectory, previewsDirectory, temporaryDirectory);
        // Keep the established log location stable; log relocation is unrelated to Catalog storage.
        var logsDirectory = applicationData;

        return new()
        {
            ApplicationDataDirectory = applicationData,
            SettingsPath = Path.Combine(applicationData, "settings.json"),
            StatePath = Path.Combine(applicationData, "state.json"),
            JobHistoryPath = Path.Combine(applicationData, "job-history.json"),
            TrimHistoryPath = Path.Combine(applicationData, "trim-history.json"),
            OutputIdentityDirectory = Path.Combine(applicationData, "output-identities"),
            CatalogDirectory = catalogDirectory,
            CatalogDatabasePath = Path.Combine(catalogDirectory, CatalogFileName),
            CatalogBackupsDirectory = Path.Combine(catalogDirectory, "Backups"),
            PreviewsDirectory = previewsDirectory,
            TemporaryDirectory = temporaryDirectory,
            LogsDirectory = logsDirectory,
            ActivityLogPath = Path.Combine(logsDirectory, "activity.log")
        };
    }

    internal LightflowStorageLocations WithOverrides(LightflowStorageLocationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var catalogDirectory = ResolveOverride(options.CatalogDirectory, CatalogDirectory,
            nameof(options.CatalogDirectory));
        var previewsDirectory = ResolveOverride(options.PreviewsDirectory, PreviewsDirectory,
            nameof(options.PreviewsDirectory));
        ValidateOwnershipBoundaries(catalogDirectory, previewsDirectory, TemporaryDirectory);
        return this with
        {
            CatalogDirectory = catalogDirectory,
            CatalogDatabasePath = Path.Combine(catalogDirectory, CatalogFileName),
            CatalogBackupsDirectory = Path.Combine(catalogDirectory, "Backups"),
            PreviewsDirectory = previewsDirectory
        };
    }

    private static string ResolveOverride(string? configured, string fallback, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(fallback);
        if (!Path.IsPathFullyQualified(configured))
            throw new ArgumentException("Storage locations must be absolute paths.", parameterName);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured.Trim()));
    }

    private static void ValidateOwnershipBoundaries(string catalog, string previews, string temporary)
    {
        if (PathsOverlap(catalog, previews) || PathsOverlap(catalog, temporary) || PathsOverlap(previews, temporary))
            throw new ArgumentException("Catalog, Previews, and Temporary locations must not overlap.");
    }

    internal static bool PathsOverlap(string left, string right) =>
        IsSameOrAncestor(left, right) || IsSameOrAncestor(right, left);

    private static bool IsSameOrAncestor(string candidate, string path)
    {
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var child = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (string.Equals(parent, child, StringComparison.OrdinalIgnoreCase)) return true;
        var parentWithSeparator = Path.EndsInDirectorySeparator(parent)
            ? parent
            : parent + Path.DirectorySeparatorChar;
        return child.StartsWith(parentWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
