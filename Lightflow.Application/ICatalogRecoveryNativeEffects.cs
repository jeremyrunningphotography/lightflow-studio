using LightflowStudio;
namespace Lightflow.Application;

public interface ICatalogRecoveryReadGuard : IDisposable { void RequireSingleRegularFile(); }
public interface ICatalogRecoveryNativeEffects : ICatalogTransferNativeEffects
{
    void GuardAccess(string path);
    IEnumerable<string> EnumerateOwnedFiles(string directory, string pattern, SearchOption option);
    string RecoverySqlitePath(string path);
    bool SameDatabase(string left, string right);
    ICatalogRecoveryReadGuard OpenRecoveryReadGuard(string path, bool closed);
}
