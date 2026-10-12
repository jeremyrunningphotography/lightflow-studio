using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using Lightflow.Application;
namespace LightflowStudio;

internal sealed class WindowsCatalogEvidenceEffects : ICatalogEvidenceEffects
{
    internal static WindowsCatalogEvidenceEffects Instance { get; } = new();
    public FileStream OpenOwnershipJournal(string path) => WindowsStorageLocationAssessor.OpenOwnershipJournal(path);
    public FileStream OpenProfileSession(string path) => new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough);
    public void RequireBoundOwnershipJournal(FileStream file, string path) => WindowsStorageLocationAssessor.RequireBoundOwnershipJournal(file.SafeFileHandle, path);
    public void RequireSingleRegularFile(FileStream file) => WindowsStorageLocationAssessor.RequireSingleRegularFile(file.SafeFileHandle);
    public void RequireUnlinked(string path) => ClosedCatalogTransferJournal.RequireUnlinked(path);
    public bool SameDatabase(string left, string right) => string.Equals(left, Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public string Fingerprint(FileStream file)
    {
        if (!GetFileInformationByHandle(file.SafeFileHandle, out var identity))
            throw new IOException("Cannot establish database file identity.");
        var header = new byte[100]; file.Position = 0; file.ReadExactly(header);
        return $"{identity.Volume}:{identity.IndexHigh}:{identity.IndexLow}:{identity.WriteHigh}:{identity.WriteLow}:{file.Length}:{Hash(header)}";
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdentity
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileIdentity information);
}
