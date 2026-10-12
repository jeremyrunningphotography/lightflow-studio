using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace LightflowStudio.Tests;

// Read-only Restart Manager census; never requests shutdown or changes sharing policy.
internal static class JournalLockDiagnostics
{
    internal static void Record(ITestOutputHelper output, string stage, string path, string root)
    {
        output.WriteLine("{0:O} pid={1} {2} journal={3}", DateTimeOffset.UtcNow,
            Environment.ProcessId, stage, path.Replace(root, "[fixture]"));
        var result = RmStartSession(out var session, 0, Guid.NewGuid().ToString("N"));
        if (result != 0) { output.WriteLine("RestartManager start error={0}", result); return; }
        try
        {
            result = RmRegisterResources(session, 1, [path], 0, IntPtr.Zero, 0, null);
            if (result != 0) { output.WriteLine("RestartManager register error={0}", result); return; }
            uint needed = 0, count = 0, reasons = 0;
            result = RmGetList(session, out needed, ref count, null, ref reasons);
            if (result == 234)
            {
                var owners = new ProcessInfo[needed]; count = needed;
                result = RmGetList(session, out needed, ref count, owners, ref reasons);
                output.WriteLine("RestartManager census error={0} count={1}", result, count);
                if (result == 0)
                    foreach (var owner in owners.Take((int)count))
                        output.WriteLine("RestartManager holder pid={0} startFileTime={1} app={2}",
                            owner.Process.Pid, ((ulong)owner.Process.StartHigh << 32) | owner.Process.StartLow, owner.Name);
            }
            else output.WriteLine("RestartManager census error={0} count={1}", result, count);
        }
        finally { output.WriteLine("RestartManager session close error={0}", RmEndSession(session)); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct UniqueProcess { public uint Pid, StartLow, StartHigh; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        public UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Service;
        public uint Type, Status, Session;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmStartSession(out uint session, uint flags, string key);
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmRegisterResources(uint session, uint files, string[] names, uint processes, IntPtr apps, uint services, string[]? serviceNames);
    [DllImport("rstrtmgr.dll")] private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] ProcessInfo[]? apps, ref uint reasons);
    [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint session);
}
