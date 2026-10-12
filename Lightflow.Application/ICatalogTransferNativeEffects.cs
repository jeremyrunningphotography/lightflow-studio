using Lightflow.Domain;
using LightflowStudio;
namespace Lightflow.Application;

public interface ICatalogStorageAssessor : IStorageLocationAssessor, IDisposable { }
public interface ICatalogDirectoryLease : IDisposable
{
    string Identity { get; }
    void Revalidate(string directory);
    void PublishNoReplace(string destination);
}
/// <summary>Native storage capabilities used by the existing closed transfer/journal owners. Unsupported operations must throw.</summary>
public interface ICatalogTransferNativeEffects
{
    ICatalogStorageAssessor CreateAssessor(IReadOnlyList<string> protectedPaths);
    void RequireIsolatedDestination(ILightflowStorageLocations locations, string destination);
    bool PathsOverlap(string left, string right);
    void RequireUnlinked(string path);
    FileStream OpenClosedRead(string path);
    FileStream OpenJournal(string path);
    void RequireSingleRegularFile(FileStream file);
    ICatalogDirectoryLease AcquireDirectoryLease(string directory, bool allowPublication);
    void RequireDirectoryIdentity(string directory, string identity);
    void CreateExclusiveDirectory(string path);
    ulong? AvailableSpace(string directory);
}
