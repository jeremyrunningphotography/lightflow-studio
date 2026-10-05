using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace LightflowStudio
{

// Used only by explicit automated startup/test tooling. Ordinary application launches do not call it.
internal static class ValidationPresentation
{
    internal static string CurrentDesktopName { get { return ObjectName(GetThreadDesktop(GetCurrentThreadId())); } }

    internal static bool IsNoninteractive
    {
        get
        {
            var name = CurrentDesktopName;
            if (!name.StartsWith("LightflowValidation-", StringComparison.Ordinal)) return false;
            var input = OpenInputDesktop(0, false, 1);
            if (input == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { return name != ObjectName(input); } finally { CloseDesktop(input); }
        }
    }

    internal static void RequireNoninteractive()
    {
        if (!IsNoninteractive) throw new InvalidOperationException(
            "Automated WPF validation requires a private desktop. Run scripts/Test-Noninteractive.ps1 " +
            "(use -Filter for focused tests) or scripts/Build-Release.ps1 for packaged validation.");
    }

    internal static void ValidateStartup(string[] arguments, bool isolatedDataRoot)
    {
        if (Array.IndexOf(arguments, "--startup-smoke-test") < 0 &&
            Array.IndexOf(arguments, "--jobs-workspace-smoke-test") < 0) return;
        RequireNoninteractive();
        if (!isolatedDataRoot)
            throw new InvalidOperationException("Automated startup smoke requires an isolated --data-root.");
    }

    private static string ObjectName(IntPtr handle)
    {
        var name = new StringBuilder(256);
        uint needed;
        if (!GetUserObjectInformation(handle, 2, name, name.Capacity * 2, out needed))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return name.ToString();
    }

    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder info, int length, out uint needed);
}
}
