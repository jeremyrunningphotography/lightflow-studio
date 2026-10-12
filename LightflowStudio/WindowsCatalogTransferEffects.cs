using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Lightflow.Application;
using Lightflow.Domain;
namespace LightflowStudio;

internal class WindowsCatalogTransferEffects : ICatalogTransferNativeEffects
{
    internal static WindowsCatalogTransferEffects Instance { get; } = new();
    public ICatalogStorageAssessor CreateAssessor(IReadOnlyList<string> protectedPaths) => new Assessor(protectedPaths);
    public bool PathsOverlap(string left, string right) => LightflowStorageLocations.PathsOverlap(left, right);
    public void RequireIsolatedDestination(ILightflowStorageLocations locations, string destination)
    {
        if (locations is LightflowStorageLocations { IsIsolated: true }) ApplicationDataProfile.RequireContained(locations.ApplicationDataDirectory, destination);
    }
    public FileStream OpenJournal(string path) => new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
    public void RequireSingleRegularFile(FileStream file) => WindowsStorageLocationAssessor.RequireSingleRegularFile(file.SafeFileHandle);
    public ICatalogDirectoryLease AcquireDirectoryLease(string directory, bool allowPublication) => new DirectoryLease(directory, allowPublication);
    public void RequireDirectoryIdentity(string directory, string identity) => WindowsStorageLocationAssessor.RequireDirectoryIdentity(directory, identity);
    public ulong? AvailableSpace(string directory) => GetDiskFreeSpaceExW(WindowsStorageLocationAssessor.NativePath(directory), out var available, out _, out _) ? available : null;
    private sealed class Assessor(IReadOnlyList<string> protectedPaths) : ICatalogStorageAssessor
    {
        private readonly WindowsStorageLocationAssessor _native = new(protectedPaths);
        public Task<StorageLocationAssessment> AssessAsync(StorageAssessmentRequest request, CancellationToken token = default) => _native.AssessAsync(request, token);
        public void Dispose() => _native.Dispose();
    }
    private sealed class DirectoryLease(string directory, bool allowPublication) : ICatalogDirectoryLease
    {
        private readonly WindowsStorageLocationAssessor.TransferDirectoryLease _native = new(directory, allowPublication);
        public string Identity => _native.Identity;
        public void Revalidate(string path) => _native.Revalidate(path);
        public void PublishNoReplace(string destination) => _native.PublishNoReplace(destination);
        public void Dispose() => _native.Dispose();
    }
    public FileStream OpenClosedRead(string path)
    {
        RequireUnlinked(path);
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try { WindowsStorageLocationAssessor.RequireSingleRegularFile(file.SafeFileHandle); return file; }
        catch { file.Dispose(); throw; }
    }
    public void RequireUnlinked(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Transfer evidence cannot be accessed through a link or junction.");
    }
    public void CreateExclusiveDirectory(string path)
    {
        RequireUnlinked(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!CreateDirectoryW(WindowsStorageLocationAssessor.NativePath(path), IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Operation-owned staging directory could not be exclusively created.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateDirectoryW(string path, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceExW(string directory, out ulong available, out ulong total, out ulong free);
}
