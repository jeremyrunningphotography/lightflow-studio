param([Parameter(Mandatory = $true)][int]$ProcessId)

$ErrorActionPreference = 'Stop'
# Process.CloseMainWindow can choose a visible native helper HWND on a noninteractive
# desktop. Send the same WM_CLOSE request to this smoke process's actual WPF main window.
if (-not ('LightflowPackageSmokeWindow' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class LightflowPackageSmokeWindow {
    private delegate bool WindowVisitor(IntPtr window, IntPtr context);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowVisitor callback, IntPtr context);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder title, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public static bool Close(int processId) {
        IntPtr target = IntPtr.Zero;
        EnumWindows((window, context) => {
            uint owner; GetWindowThreadProcessId(window, out owner);
            if (owner != processId) return true;
            var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
            var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
            if (title.ToString() != "Lightflow Studio" || !name.ToString().StartsWith("HwndWrapper[", StringComparison.Ordinal)) return true;
            target = window; return false;
        }, IntPtr.Zero);
        return target != IntPtr.Zero && IsWindowEnabled(target) && PostMessage(target, 0x0010, IntPtr.Zero, IntPtr.Zero);
    }
}
'@
}
if (-not [LightflowPackageSmokeWindow]::Close($ProcessId)) {
    throw "Could not send a close request to the packaged smoke process's Lightflow main window (PID $ProcessId)."
}
