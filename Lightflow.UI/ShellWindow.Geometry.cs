using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Lightflow.UI;

public sealed partial class ShellWindow
{
    private readonly DispatcherTimer _geometrySave = new() { Interval=TimeSpan.FromMilliseconds(300) };
    private bool _restoringWindow, _windowOpened, _pendingSize, _pendingPosition;
    private long _geometryGeneration;
    private ShellWorkArea[] _knownWorkAreas=[];
    public event Action<string>? WindowGeometryDiagnostic;
    private void TraceWindow(string reason) => WindowGeometryDiagnostic?.Invoke(
        $"{reason} state={WindowState} client={ClientSize} frame={FrameSize} renderScale={RenderScaling} size={Width}x{Height} position={Position} min={MinWidth}x{MinHeight} placement={_geometryGeneration} restoring={_restoringWindow}");

    private ShellWorkArea[] WorkAreas()
    {
        var decoration=FrameSize is { } frame && ClientSize.Height>0 ? Math.Max(0,frame.Height-ClientSize.Height) : 0;
        return Screens.All.OrderByDescending(s=>s.IsPrimary).Select(s=>new ShellWorkArea(
            new PixelRect(s.WorkingArea.X,s.WorkingArea.Y,s.WorkingArea.Width,
                Math.Max(1,s.WorkingArea.Height-(int)Math.Ceiling(decoration*s.Scaling))),s.Scaling)).ToArray();
    }
    private void InitializeWindowGeometry()
    {
        ApplyWindowPlacement();
        _geometrySave.Tick+=(_,_)=> { _geometrySave.Stop(); CaptureWindowPreference(); };
        Resized+=(_,e)=>
        {
            TraceWindow($"resize:{e.Reason}");
            if (!_windowOpened || _restoringWindow || WindowState!=WindowState.Normal ||
                e.Reason is WindowResizeReason.Application or WindowResizeReason.Layout or WindowResizeReason.DpiChange) return;
            _pendingSize=true;_geometrySave.Stop();_geometrySave.Start();
        };
        PositionChanged+=(_,_)=>
        {
            TraceWindow("position");
            if(!_windowOpened || _restoringWindow || WindowState!=WindowState.Normal)return;
            _pendingPosition=true;_geometrySave.Stop();_geometrySave.Start();
        };
        void ScreensChanged(object? sender,EventArgs e)
        {
            // A notification alone is not a display change. Never feed ordinary resize/state
            // notifications back into saved placement. Real topology/work-area changes are distinct.
            var areas=WorkAreas();
            if(areas.SequenceEqual(_knownWorkAreas))return;
            TraceWindow("display-change");
            if(!_restoringWindow && WindowState==WindowState.Normal)
            {
                if(_pendingPosition) CaptureWindowPreference();
                ApplyWindowPlacement();
            }
        }
        Screens.Changed+=ScreensChanged;
        ScalingChanged+=ScreensChanged;
        PropertyChanged+=(_,e)=>
        {
            if(e.Property!=WindowStateProperty || !_windowOpened || _restoringWindow)return;
            // Avalonia.Native maps Maximized to NSWindow zoom, and FullScreen to its own state.
            // Drop queued resize notifications from the transition before they can replace normal bounds.
            _geometrySave.Stop();_pendingSize=_pendingPosition=false;
            _preferences=_preferences with { Zoomed=ShellWindowState.RememberZoom(WindowState,_preferences.Zoomed) };
            LayoutChanged?.Invoke(_preferences);
            // Observation only. AppKit owns unzoom and live resize. Writing Width/Height here
            // fights native windowDidResize -> isZoomed -> Normal notifications during a drag.
            TraceWindow("state-observed");
        };
        Opened+=(_,_)=> { _windowOpened=true;ApplyWindowPlacement(restoreZoom:true); };
        Closed+=(_,_)=>
        {
            _geometrySave.Stop();CaptureWindowPreference();
            Screens.Changed-=ScreensChanged;ScalingChanged-=ScreensChanged;
        };
    }
    private void ApplyWindowPlacement(bool restoreZoom = false)
    {
        var areas=WorkAreas();
        _knownWorkAreas=areas;
        var area=ShellGeometry.Select(_preferences.Window,areas);
        var preferred=_preferences.Window ?? ShellGeometry.FirstRun(area);
        _preferences=_preferences with { Window=preferred };
        var effective=ShellGeometry.Clamp(preferred,area);
        _restoringWindow=true;_pendingSize=_pendingPosition=false;
        _geometrySave.Stop();var generation=++_geometryGeneration;
        MinWidth=Math.Min(ShellGeometry.MinimumWidth,area.Width);MinHeight=Math.Min(ShellGeometry.MinimumHeight,area.Height);
        WindowStartupLocation=WindowStartupLocation.Manual;
        Width=effective.Width;Height=effective.Height;Position=new PixelPoint(effective.X,effective.Y);
        TraceWindow(restoreZoom ? "startup-placement" : "safe-placement");
        Dispatcher.UIThread.Post(()=>
        {
            if(generation!=_geometryGeneration)return;
            _restoringWindow=false;
            if(_windowOpened)
            {
                LayoutChanged?.Invoke(_preferences);
                // Establish safe restored bounds first so native unzoom has the correct destination.
                if(restoreZoom && _preferences.Zoomed)
                {
                    TraceWindow("startup-native-zoom-request");
                    WindowState=WindowState.Maximized;
                }
            }
        },DispatcherPriority.Loaded);
    }
    private void CaptureWindowPreference()
    {
        if(!_windowOpened || _restoringWindow || WindowState!=WindowState.Normal)return;
        var observed=new ShellWindowGeometry(ClientSize.Width,ClientSize.Height,Position.X,Position.Y,RenderScaling);
        var area=ShellGeometry.Select(observed,WorkAreas());
        var preferred=ShellGeometry.Remember(_preferences.Window!,observed,area,_pendingSize,_pendingPosition);
        _pendingSize=_pendingPosition=false;
        _preferences=_preferences with { Window=preferred };
        TraceWindow("preference-observed");
        LayoutChanged?.Invoke(_preferences);
    }
}
