using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LightflowStudio;

internal sealed record DeletePreflight(IReadOnlyList<FileOperationSource> Sources, int UnrecoverableCount)
{
    public bool RequiresPermanentDelete => UnrecoverableCount > 0;

    public static DeletePreflight Inspect(IReadOnlyList<FileOperationSource> sources,
        Func<string, bool> canRecycle)
    {
        // Capture and inspect every target; no operation/Job exists at this point.
        var captured = sources.ToArray();
        var unavailable = 0;
        foreach (var source in captured)
            if (!canRecycle(source.Path)) unavailable++;
        return new(captured, unavailable);
    }
}

internal sealed record DeleteConfirmation(string Title, string Heading, string Description, string Warning,
    string Action, string Cancel);

internal static class BrowserDeleteOperation
{
    public static async Task RunAsync(IReadOnlyList<FileOperationSource> sources, bool permanent,
        Func<string, bool> canRecycle, Func<DeleteConfirmation, bool> confirm,
        Func<FileOperationKind, IReadOnlyList<FileOperationSource>, Task> execute)
    {
        if (sources.Count == 0) return;
        var captured = sources.ToArray();
        var preflight = permanent ? new DeletePreflight(captured, 0)
            : await Task.Run(() => DeletePreflight.Inspect(captured, canRecycle));
        var count = captured.Length;
        var single = count == 1;
        var items = single ? "1 item" : $"{count} items";
        var contents = captured.Any(source => source.IsDirectory) ? " Folders include all their contents." : "";
        DeleteConfirmation confirmation;
        if (preflight.RequiresPermanentDelete)
        {
            permanent = true;
            confirmation = new("Permanent deletion required", single
                    ? "This item can’t be moved to the Recycle Bin"
                    : "These items can’t be moved to the Recycle Bin",
                preflight.UnrecoverableCount == count
                    ? "Windows cannot guarantee that this selection can be moved to the Recycle Bin."
                    : $"{preflight.UnrecoverableCount} of {count} selected items cannot be safely moved to the Recycle Bin.",
                (single
                    ? "Continuing permanently deletes the selected item. It will not be recoverable from the Recycle Bin."
                    : $"Continuing permanently deletes all {count} selected items. None will be recoverable from the Recycle Bin.") + contents,
                "Delete Permanently", "Cancel");
        }
        else
        {
            confirmation = permanent
                ? new("Permanent Delete", single ? "Permanently delete the selected item?" : "Permanently delete selected items?", $"{items} will be permanently removed.{contents}",
                    single ? "It will not go to the Recycle Bin and this cannot be undone." : "They will not go to the Recycle Bin and this cannot be undone.", "Delete permanently", "Cancel")
                : new("Move to Recycle Bin", single ? "Move the selected item to the Recycle Bin?" : "Move selected items to the Recycle Bin?", $"{items} will be recycled.{contents}",
                    single ? "You can normally restore it from the Windows Recycle Bin." : "You can normally restore them from the Windows Recycle Bin.", "Move to Recycle Bin", single ? "Keep item" : "Keep items");
        }
        if (confirm(confirmation))
            await execute(permanent ? FileOperationKind.PermanentDelete : FileOperationKind.Recycle, captured);
    }
}

internal static class WindowsRecycleCapability
{
    public static bool CanRecycle(string path)
    {
        try
        {
            WindowsRecyclePolicy.EnsureRecoverableLocation(path);
            if (!File.Exists(path) && !Directory.Exists(path)) return false;
            var volume = new StringBuilder(1024);
            if (!GetVolumePathName(Path.GetFullPath(path), volume, volume.Capacity)) return false;
            // Query the actual mounted volume, rather than guessing from a drive-letter prefix.
            if (new DriveInfo(volume.ToString()).DriveType is not DriveType.Fixed and not DriveType.Removable) return false;
            var info = new RecycleBinInfo { Size = Marshal.SizeOf<RecycleBinInfo>() };
            if (SHQueryRecycleBin(volume.ToString(), ref info) != 0) return false;
            return WindowsRecycleOperation.Probe(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or COMException or System.ComponentModel.Win32Exception)
        { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RecycleBinInfo { public int Size; public long Bytes; public long Items; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string path, ref RecycleBinInfo info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(string path, StringBuilder volume, int length);
}

// A noninteractive shell planning pass cancels in PreDeleteItem before the first mutation.
// Execution uses the same shell policy and vetoes any permanent-delete proposal.
internal static class WindowsRecycleOperation
{
    private const uint Flags = 0x0040 | 0x00080000 | 0x00100000 | 0x0400 | 0x0004 | 0x0010;
    internal static bool Probe(string path) => RunSta(path, true);
    internal static void Recycle(string path)
    {
        if (!RunSta(path, false)) throw new IOException("Windows could not recycle this item. It was not permanently deleted.");
    }

    private static bool RunSta(string path, bool probe)
    {
        bool result = false;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = Run(path, probe); }
            catch (Exception exception) { error = exception; }
        }) { IsBackground = true, Name = "Lightflow recycling" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error is not null) throw new IOException("Windows recycling could not be prepared.", error);
        return result;
    }

    private static bool Run(string path, bool probe)
    {
        var operation = (IRecycleFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"))!)!;
        IShellRecycleItem? item = null;
        try
        {
            var iid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(Path.GetFullPath(path), IntPtr.Zero, ref iid, out item));
            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(Flags));
            var sink = new RecycleProgressSink(probe);
            Marshal.ThrowExceptionForHR(operation.DeleteItem(item, sink));
            var hr = operation.PerformOperations();
            if (probe) return sink.RecycleProposed;
            Marshal.ThrowExceptionForHR(hr);
            Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out var aborted));
            return !aborted && sink.Completed;
        }
        finally
        {
            if (item is not null) Marshal.ReleaseComObject(item);
            Marshal.ReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid,
        out IShellRecycleItem item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellRecycleItem
    {
        [PreserveSig] int BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr value);
        [PreserveSig] int GetParent(out IShellRecycleItem parent);
        [PreserveSig] int GetDisplayName(uint kind, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellRecycleItem other, uint hint, out int order);
    }

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IRecycleFileOperation
    {
        [PreserveSig] int Advise(IntPtr sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(uint window);
        [PreserveSig] int ApplyPropertiesToItem(IntPtr item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IntPtr item, IntPtr name, IntPtr sink);
        [PreserveSig] int RenameItems(IntPtr items, IntPtr name);
        [PreserveSig] int MoveItem(IntPtr item, IntPtr destination, IntPtr name, IntPtr sink);
        [PreserveSig] int MoveItems(IntPtr items, IntPtr destination);
        [PreserveSig] int CopyItem(IntPtr item, IntPtr destination, IntPtr name, IntPtr sink);
        [PreserveSig] int CopyItems(IntPtr items, IntPtr destination);
        [PreserveSig] int DeleteItem(IShellRecycleItem item, IRecycleProgressSink sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IntPtr destination, uint attributes, IntPtr name, IntPtr template, IntPtr sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}

[ComVisible(true), Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IRecycleProgressSink
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int result);
    [PreserveSig] int PreRenameItem(uint flags, IntPtr item, IntPtr name);
    [PreserveSig] int PostRenameItem(uint flags, IntPtr item, IntPtr name, int result, IntPtr renamed);
    [PreserveSig] int PreMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name);
    [PreserveSig] int PostMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr moved);
    [PreserveSig] int PreCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name);
    [PreserveSig] int PostCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr copied);
    [PreserveSig] int PreDeleteItem(uint flags, IntPtr item);
    [PreserveSig] int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr recycled);
    [PreserveSig] int PreNewItem(uint flags, IntPtr destination, IntPtr name);
    [PreserveSig] int PostNewItem(uint flags, IntPtr destination, IntPtr name, IntPtr template, uint attributes, int result, IntPtr created);
    [PreserveSig] int UpdateProgress(uint total, uint completed);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class RecycleProgressSink(bool probe) : IRecycleProgressSink
{
    public bool RecycleProposed { get; private set; }
    public bool Completed { get; private set; }
    public int PreDeleteItem(uint flags, IntPtr item)
    {
        RecycleProposed = (flags & 0x80) != 0;
        return probe || !RecycleProposed ? unchecked((int)0x80004004) : 0;
    }
    public int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr recycled)
    { Completed = result >= 0 && recycled != IntPtr.Zero; return 0; }
    public int StartOperations() => 0;
    public int FinishOperations(int result) => 0;
    public int PreRenameItem(uint flags, IntPtr item, IntPtr name) => 0;
    public int PostRenameItem(uint flags, IntPtr item, IntPtr name, int result, IntPtr renamed) => 0;
    public int PreMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name) => 0;
    public int PostMoveItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr moved) => 0;
    public int PreCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name) => 0;
    public int PostCopyItem(uint flags, IntPtr item, IntPtr destination, IntPtr name, int result, IntPtr copied) => 0;
    public int PreNewItem(uint flags, IntPtr destination, IntPtr name) => 0;
    public int PostNewItem(uint flags, IntPtr destination, IntPtr name, IntPtr template, uint attributes, int result, IntPtr created) => 0;
    public int UpdateProgress(uint total, uint completed) => 0;
    public int ResetTimer() => 0;
    public int PauseTimer() => 0;
    public int ResumeTimer() => 0;
}
