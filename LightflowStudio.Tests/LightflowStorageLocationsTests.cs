using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class LightflowStorageLocationsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lightflow-storage-{Guid.NewGuid():N}");

    [Fact]
    public void Create_ProvidesStableSeparatedDefaultsWithoutTouchingDisk()
    {
        var locations = LightflowStorageLocations.Create(_root);
        var applicationData = Path.Combine(_root, "Jeremy Running Photography", "Lightflow Studio");

        Assert.Equal(Path.GetFullPath(applicationData), locations.ApplicationDataDirectory);
        Assert.Equal(Path.Combine(applicationData, "Catalog"), locations.CatalogDirectory);
        Assert.Equal(Path.Combine(applicationData, "Catalog", LightflowStorageLocations.CatalogFileName), locations.CatalogDatabasePath);
        Assert.Equal(Path.Combine(applicationData, "Catalog", "Backups"), locations.CatalogBackupsDirectory);
        Assert.Equal(Path.Combine(applicationData, "Previews"), locations.PreviewsDirectory);
        Assert.Equal(Path.Combine(applicationData, "Temporary"), locations.TemporaryDirectory);
        Assert.Equal(Path.Combine(applicationData, "settings.json"), locations.SettingsPath);
        Assert.Equal(Path.Combine(applicationData, "state.json"), locations.StatePath);
        Assert.Equal(Path.Combine(applicationData, "trim-history.json"), locations.TrimHistoryPath);
        Assert.Equal(Path.Combine(applicationData, "job-history.json"), locations.JobHistoryPath);
        Assert.Equal(Path.Combine(applicationData, "output-identities"), locations.OutputIdentityDirectory);
        Assert.Equal(applicationData, locations.LogsDirectory);
        Assert.Equal(Path.Combine(applicationData, "activity.log"), locations.ActivityLogPath);
        Assert.False(Directory.Exists(applicationData));
    }

    [Fact]
    public void Create_ResolvesCatalogAndPreviewsOverridesIndependently()
    {
        var catalog = Path.Combine(_root, "precious-catalog");
        var previews = Path.Combine(_root, "rebuildable-previews");

        var locations = LightflowStorageLocations.Create(_root,
            new LightflowStorageLocationOptions(catalog, previews));

        Assert.Equal(Path.GetFullPath(catalog), locations.CatalogDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(catalog), LightflowStorageLocations.CatalogFileName), locations.CatalogDatabasePath);
        Assert.Equal(Path.Combine(Path.GetFullPath(catalog), "Backups"), locations.CatalogBackupsDirectory);
        Assert.Equal(Path.GetFullPath(previews), locations.PreviewsDirectory);
        Assert.StartsWith(locations.ApplicationDataDirectory, locations.TemporaryDirectory);
    }

    [Theory]
    [InlineData("relative/catalog", null)]
    [InlineData(null, "relative/previews")]
    internal void Create_RejectsRelativeConfiguredLocations(string? catalog, string? previews)
    {
        Assert.Throws<ArgumentException>(() => LightflowStorageLocations.Create(_root,
            new LightflowStorageLocationOptions(catalog, previews)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_RejectsOverlappingPreciousAndRebuildableLocations(bool previewsInsideCatalog)
    {
        var catalog = Path.Combine(_root, "storage", previewsInsideCatalog ? "Catalog" : "Catalog", previewsInsideCatalog ? "" : "Nested");
        var previews = previewsInsideCatalog
            ? Path.Combine(catalog, "Previews")
            : Path.Combine(_root, "storage");

        Assert.Throws<ArgumentException>(() => LightflowStorageLocations.Create(_root,
            new LightflowStorageLocationOptions(catalog, previews)));
    }

    [Theory]
    [InlineData(@"C:\Lightflow", @"c:\LIGHTFLOW\Previews\", true)]
    [InlineData(@"C:\Lightflow\Catalog\..\Catalog", @"C:/Lightflow/Catalog/Backups", true)]
    [InlineData(@"C:\", @"C:\Lightflow", true)]
    [InlineData(@"C:\Lightflow", @"C:\Lightflow2", false)]
    [InlineData(@"C:\Lightflow\Catalog", @"D:\Lightflow\Catalog", false)]
    internal void PathsOverlap_UsesNormalizedWindowsPathSegments(string left, string right, bool expected)
    {
        Assert.Equal(expected, LightflowStorageLocations.PathsOverlap(left, right));
        Assert.Equal(expected, LightflowStorageLocations.PathsOverlap(right, left));
    }

    [Fact]
    public void Create_RejectsDriveRootCatalogBecauseItContainsOtherStorageClasses()
    {
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(_root))!;

        Assert.Throws<ArgumentException>(() => LightflowStorageLocations.Create(_root,
            new LightflowStorageLocationOptions(driveRoot, Path.Combine(_root, "previews"))));
    }

    [Fact]
    public void ExistingPersistenceDefaultsConsumeCentralLocations()
    {
        var current = LightflowStorageLocations.Current;

        Assert.Equal(current.SettingsPath, AppSettingsStore.SettingsPath);
        Assert.Equal(current.StatePath, AppStateStore.StatePath);
        Assert.Equal(current.JobHistoryPath, JobHistoryStore.StorePath);
        Assert.Equal(current.TrimHistoryPath, TrimHistoryStore.StorePath);
        Assert.Equal(current.OutputIdentityDirectory, EncodingOutputIdentityStore.CacheDirectory);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(current.SettingsPath)!, "activity.log"), current.ActivityLogPath);
    }

    [Fact]
    public void CatalogBackupsStayWithCatalogWhileRebuildableStoresRemainOutsideIt()
    {
        var locations = LightflowStorageLocations.Create(_root);

        Assert.StartsWith(locations.CatalogDirectory, locations.CatalogBackupsDirectory);
        Assert.DoesNotContain(locations.CatalogDirectory, locations.PreviewsDirectory);
        Assert.DoesNotContain(locations.CatalogDirectory, locations.TemporaryDirectory);
    }

    [Fact]
    public void WithOverrides_ChangesOnlyCatalogAndPreviewOwnershipPaths()
    {
        var original = LightflowStorageLocations.Create(_root);
        var changed = original.WithOverrides(new(
            Path.Combine(_root, "custom-catalog"), Path.Combine(_root, "custom-previews")));

        Assert.Equal(original.ApplicationDataDirectory, changed.ApplicationDataDirectory);
        Assert.Equal(original.SettingsPath, changed.SettingsPath);
        Assert.Equal(original.StatePath, changed.StatePath);
        Assert.Equal(original.JobHistoryPath, changed.JobHistoryPath);
        Assert.Equal(original.TrimHistoryPath, changed.TrimHistoryPath);
        Assert.Equal(original.OutputIdentityDirectory, changed.OutputIdentityDirectory);
        Assert.Equal(original.ActivityLogPath, changed.ActivityLogPath);
        Assert.Equal(original.TemporaryDirectory, changed.TemporaryDirectory);
        Assert.Equal(original.MachineIdentityPath, changed.MachineIdentityPath);
        Assert.Equal(Path.Combine(_root, "custom-catalog"), changed.CatalogDirectory);
        Assert.Equal(Path.Combine(_root, "custom-previews"), changed.PreviewsDirectory);
    }

    [Fact]
    public void WithOverrides_AllowsNetworkPreviewsWhileCatalogPolicyRejectsNetworkElsewhere()
    {
        var original = LightflowStorageLocations.Create(_root);
        var changed = original.WithOverrides(new(original.CatalogDirectory, @"\\server\share\LightflowPreviews"));

        Assert.Equal(@"\\server\share\LightflowPreviews", changed.PreviewsDirectory);
        Assert.Equal(original.CatalogDirectory, changed.CatalogDirectory);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
