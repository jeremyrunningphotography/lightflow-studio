using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LightflowStudio;

// An invocation snapshot, never a second Browser selection. Reject changed mappings,
// removed nodes and replaced directories rather than retargeting an old menu.
internal sealed class BrowserFolderContextTarget
{
    internal BrowserTreeNode Node { get; }
    internal string Path { get; }
    private readonly Guid? rootId;
    private readonly string? relative;
    private readonly string identity;

    private BrowserFolderContextTarget(BrowserTreeNode node, string path, string fingerprint)
    { Node = node; Path = path; rootId = node.RootId; relative = node.RelativeFolder; identity = fingerprint; }

    internal static BrowserFolderContextTarget? Capture(BrowserTreeNode? node)
    {
        if (node is null || node.IsPlaceholder || node.Availability != MediaRootAvailability.Online || node.AbsolutePath is not { } path)
            return null;
        try { return new(node, path, Fingerprint(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    internal bool IsCurrent(IEnumerable<BrowserTreeNode> nodes)
    {
        if (!nodes.Contains(Node) || Node.AbsolutePath != Path || Node.RootId != rootId || Node.RelativeFolder != relative ||
            Node.Availability != MediaRootAvailability.Online) return false;
        try { return identity == Fingerprint(Path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    internal bool CanChangeEntry => System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(Path)) is not null;

    private static string Fingerprint(string path)
    {
        using var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x10) == 0)
            throw new IOException("The context folder is unavailable.");
        return $"{info.Volume}:{info.IndexHigh}:{info.IndexLow}:{info.CreationHigh}:{info.CreationLow}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Information
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Information information);
}
