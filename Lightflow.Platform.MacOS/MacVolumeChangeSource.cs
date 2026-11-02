using System.Runtime.InteropServices;
using Lightflow.Application;

namespace Lightflow.Platform.MacOS;

/// <summary>NSWorkspace mount/unmount/rename notifications; no mounting, scanning or UI control.</summary>
public sealed class MacVolumeChangeSource : IBrowserLocationChangeSource
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Callback();
    private readonly Callback _callback;
    private IntPtr _observer;
    public event Action? Changed;
    public MacVolumeChangeSource()
    {
        if(!OperatingSystem.IsMacOS())throw new PlatformNotSupportedException();
        _callback=()=>
        {
            try { Changed?.Invoke(); }
            catch(Exception error) { Console.Error.WriteLine($"Volume notification handler failed: {error}"); }
        };
        _observer=Start(_callback);
        if(_observer==IntPtr.Zero)throw new IOException("macOS volume notifications are unavailable; use Refresh.");
    }
    public void Dispose()
    {
        var observer=Interlocked.Exchange(ref _observer,IntPtr.Zero);
        if(observer!=IntPtr.Zero)Stop(observer);
        GC.KeepAlive(_callback);
    }
    [DllImport("libLightflowStorage.dylib",EntryPoint="lf_browser_observe_volumes")]
    private static extern IntPtr Start(Callback callback);
    [DllImport("libLightflowStorage.dylib",EntryPoint="lf_browser_stop_observing_volumes")]
    private static extern void Stop(IntPtr observer);
}
