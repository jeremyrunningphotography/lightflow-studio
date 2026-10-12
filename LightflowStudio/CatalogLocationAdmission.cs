using System.IO;
using Lightflow.Application;
using Lightflow.Domain;
namespace LightflowStudio;

/// <summary>Windows composition of the shared admission owner with the existing native binding effects.</summary>
internal sealed class CatalogLocationAdmission : CatalogStorageAdmission
{
    internal CatalogLocationAdmission(LightflowStorageLocations locations, StorageOperation operation,
        IStorageLocationAssessor? assessor = null, string? reservedCatalogDirectory = null)
        : base(locations, operation, new WindowsCatalogAdmissionEffects(locations, assessor, reservedCatalogDirectory)) { }
    internal new LightflowStorageLocations LocationsForUse() => (LightflowStorageLocations)base.LocationsForUse();
}

internal sealed class WindowsCatalogAdmissionEffects : ICatalogAdmissionNativeEffects
{
    private readonly WindowsStorageLocationAssessor? _owned;
    private readonly LightflowStorageLocations _locations;
    public IStorageLocationAssessor Assessor { get; }
    public bool HasNativeBinding => _owned is not null;
    internal WindowsCatalogAdmissionEffects(LightflowStorageLocations locations, IStorageLocationAssessor? assessor, string? reservedCatalogDirectory)
    {
        _locations = locations;
        Assessor = assessor ?? (_owned = new WindowsStorageLocationAssessor(
            new[] { locations.PreviewsDirectory, locations.TemporaryDirectory }.Concat(reservedCatalogDirectory is null ? [] : new[] { reservedCatalogDirectory }), new Dictionary<string, StorageRole>
            { [locations.PreviewsDirectory] = StorageRole.RebuildablePreview, [locations.TemporaryDirectory] = StorageRole.TemporaryProfile }));
    }
    public void GuardAccess(string databasePath) { if (_locations.IsIsolated) ApplicationDataProfile.GuardAccess(databasePath); }
    public ILightflowStorageLocations RouteForUse(ILightflowStorageLocations configured, StorageLocationAssessment assessment)
    {
        if (_owned is null) return configured;
        var identity = assessment.Identity ?? throw new InvalidOperationException("Native location identity is unavailable.");
        return ((LightflowStorageLocations)configured) with
        {
            CatalogDirectory = identity.CanonicalLocation,
            CatalogDatabasePath = Path.Combine(identity.CanonicalLocation, LightflowStorageLocations.CatalogFileName),
            CatalogBackupsDirectory = Path.Combine(identity.CanonicalLocation, "Backups")
        };
    }
    public void GuardMainDatabase(string databasePath) => _owned?.GuardMainDatabase(databasePath);
    public void ReleaseMainDatabaseGuard() => _owned?.ReleaseMainDatabaseGuard();
    public bool SameDatabase(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    public void Dispose() => _owned?.Dispose();
}
