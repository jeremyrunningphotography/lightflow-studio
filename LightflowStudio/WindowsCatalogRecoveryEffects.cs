using System.IO;
using Lightflow.Application;
using Microsoft.Win32.SafeHandles;
namespace LightflowStudio;
internal sealed class WindowsCatalogRecoveryEffects : WindowsCatalogTransferEffects, ICatalogRecoveryNativeEffects
{
    internal new static WindowsCatalogRecoveryEffects Instance { get; } = new();
    public void GuardAccess(string path) => ApplicationDataProfile.GuardAccess(path);
    public IEnumerable<string> EnumerateOwnedFiles(string directory, string pattern, SearchOption option) => ApplicationDataProfile.EnumerateOwnedFiles(directory, pattern, option);
    public bool SameDatabase(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    public ICatalogRecoveryReadGuard OpenRecoveryReadGuard(string path, bool closed) => new ReadGuard(WindowsStorageLocationAssessor.OpenRecoveryReadGuard(path, closed));
    private sealed class ReadGuard(SafeFileHandle file) : ICatalogRecoveryReadGuard
    {
        public void RequireSingleRegularFile() => WindowsStorageLocationAssessor.RequireSingleRegularFile(file);
        public void Dispose() => file.Dispose();
    }
    public string RecoverySqlitePath(string path)
    {
        var full = Path.GetFullPath(path);
        // The packaged host is not longPathAware. SQLite's Win32 calls otherwise fail
        // at MAX_PATH (including the appended -journal), even when .NET accepts the path.
        // Scope this to recovery connections; keep filesystem/publication paths unchanged.
        if (!OperatingSystem.IsWindows() || full.StartsWith(@"\\?\", StringComparison.Ordinal)) return full;
        return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }

}
