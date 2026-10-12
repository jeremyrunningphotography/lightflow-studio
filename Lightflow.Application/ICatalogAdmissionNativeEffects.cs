using Lightflow.Domain;
using LightflowStudio;
namespace Lightflow.Application;

/// <summary>Native binding effects. Shared storage policy alone decides admission.</summary>
public interface ICatalogAdmissionNativeEffects : IDisposable
{
    IStorageLocationAssessor Assessor { get; }
    bool HasNativeBinding { get; }
    void GuardAccess(string databasePath);
    ILightflowStorageLocations RouteForUse(ILightflowStorageLocations configured, StorageLocationAssessment assessment);
    void GuardMainDatabase(string boundDatabasePath);
    void ReleaseMainDatabaseGuard();
    bool SameDatabase(string left, string right);
}
