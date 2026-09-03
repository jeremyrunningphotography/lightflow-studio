using System.Runtime.InteropServices;
using WpfPoint = System.Windows.Point;

namespace LightflowStudio;

internal sealed class LowLevelMouseWheelHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseWheel = 0x020A;
    private readonly Func<WpfPoint, int, bool> _routeWheel;
    private readonly HookProcedure _procedure;
    private nint _handle;

    private LowLevelMouseWheelHook(Func<WpfPoint, int, bool> routeWheel)
    {
        _routeWheel = routeWheel;
        _procedure = HookCallback;
    }

    public static LowLevelMouseWheelHook? TryInstall(Func<WpfPoint, int, bool> routeWheel)
    {
        var hook = new LowLevelMouseWheelHook(routeWheel);
        hook._handle = SetWindowsHookEx(WhMouseLl, hook._procedure, GetModuleHandle(null), 0);
        return hook._handle == 0 ? null : hook;
    }

    public void Dispose()
    {
        if (_handle == 0) return;
        UnhookWindowsHookEx(_handle);
        _handle = 0;
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && unchecked((int)(long)wParam) == WmMouseWheel)
        {
            var input = Marshal.PtrToStructure<LowLevelMouseInput>(lParam);
            var delta = unchecked((short)(input.MouseData >> 16));
            try
            {
                if (_routeWheel(new WpfPoint(input.Point.X, input.Point.Y), delta)) return 1;
            }
            catch
            {
                // Never let an input-hook callback destabilize the active OLE drag loop.
            }
        }
        return CallNextHookEx(_handle, code, wParam, lParam);
    }

    private delegate nint HookProcedure(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseInput
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hook, HookProcedure callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
