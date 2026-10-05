using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

// Shared by PowerShell validation tooling and the test assembly. Never switches the input desktop.
public sealed class ValidationDesktop : IDisposable
{
    private IntPtr desktop, job, process;
    public string Name { get; private set; }
    public uint ProcessId { get; private set; }
    public ValidationDesktop()
    {
        Name = "LightflowValidation-" + Guid.NewGuid().ToString("N");
        try
        {
            // No DESKTOP_SWITCHDESKTOP access; no cross-account hooks.
            desktop = CreateDesktop(Name, IntPtr.Zero, IntPtr.Zero, 0, 0x00ff, IntPtr.Zero);
            Check(desktop != IntPtr.Zero);
            job = CreateJobObject(IntPtr.Zero, ""); Check(job != IntPtr.Zero);
            var limits = new ExtendedLimits(); limits.basic.limitFlags = 0x2000; // KILL_ON_JOB_CLOSE
            Check(SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf(limits)));
        }
        catch { Dispose(); throw; }
    }
    public void Start(string executable, string commandLine, string directory)
    {
        if (process != IntPtr.Zero) throw new InvalidOperationException("Validation already started.");
        var startup = new StartupInfo(); startup.cb = Marshal.SizeOf(startup); startup.desktop = Name;
        ProcessInformation created;
        // Suspended until job membership is established: every descendant belongs to this run.
        Check(CreateProcess(executable, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
            0x08000004, IntPtr.Zero, directory, ref startup, out created));
        process = created.process; ProcessId = created.processId;
        try
        {
            Check(AssignProcessToJobObject(job, process));
            if (ResumeThread(created.thread) == 0xffffffff) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch { TerminateProcess(process, 1); throw; }
        finally { CloseHandle(created.thread); }
    }
    public bool HasExited { get { return process != IntPtr.Zero && WaitForSingleObject(process, 0) == 0; } }
    public int ExitCode
    {
        get
        {
            if (!HasExited) throw new InvalidOperationException("Validation is still running.");
            uint result; Check(GetExitCodeProcess(process, out result)); return unchecked((int)result);
        }
    }
    public uint ActiveProcesses
    {
        get { Accounting info; Check(QueryInformationJobObject(job, 1, out info, Marshal.SizeOf(typeof(Accounting)), IntPtr.Zero)); return info.activeProcesses; }
    }
    public static string CurrentDesktopName { get { return LightflowStudio.ValidationPresentation.CurrentDesktopName; } }
    public static bool IsNoninteractive { get { return LightflowStudio.ValidationPresentation.IsNoninteractive; } }
    public static void RequireNoninteractive() { LightflowStudio.ValidationPresentation.RequireNoninteractive(); }
    private static void Check(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    public void Dispose()
    {
        // Wait for owned HWNDs/threads to die before releasing the desktop. Kernel job
        // kill-on-close remains the fallback if the launcher itself is terminated.
        try
        {
            if (job != IntPtr.Zero && ActiveProcesses != 0)
            {
                Check(TerminateJobObject(job, 1));
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (ActiveProcesses != 0 && timer.ElapsedMilliseconds < 5000) System.Threading.Thread.Sleep(10);
                if (ActiveProcesses != 0) throw new InvalidOperationException("Validation process-tree cleanup did not complete.");
            }
        }
        finally
        {
            if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; }
            if (process != IntPtr.Zero) { CloseHandle(process); process = IntPtr.Zero; }
            if (desktop != IntPtr.Zero) { Check(CloseDesktop(desktop)); desktop = IntPtr.Zero; }
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct StartupInfo
    {
        public int cb; public string reserved, desktop, title;
        public int x,y,xSize,ySize,xCountChars,yCountChars,fillAttribute,flags;
        public short showWindow, reservedSize; public IntPtr reservedPointer, input, output, error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr process, thread; public uint processId, threadId; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    {
        public long processTime, jobTime; public uint limitFlags; public UIntPtr minimumWorkingSet, maximumWorkingSet;
        public uint activeProcessLimit; public UIntPtr affinity; public uint priority, scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong readOps, writeOps, otherOps, readBytes, writeBytes, otherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    {
        public BasicLimits basic; public IoCounters io; public UIntPtr processMemory, jobMemory, peakProcessMemory, peakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Accounting
    {
        public long userTime, kernelTime, periodUserTime, periodKernelTime; public uint faults, totalProcesses, activeProcesses, terminatedProcesses;
    }
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr CreateJobObject(IntPtr security, string name);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref ExtendedLimits limits, int size);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool QueryInformationJobObject(IntPtr job, int kind, out Accounting info, int size, IntPtr returned);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool TerminateJobObject(IntPtr job, uint code);
}


