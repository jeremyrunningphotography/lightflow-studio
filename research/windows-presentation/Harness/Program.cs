using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Rendering.Composition;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace WindowsPresentation;

record Frame(long SourceGeneration, long SourcePts, int TimebaseNumerator, int TimebaseDenominator,
    long Token, long ColorRevision, long SurfaceEpoch, long HostGeneration)
{
    // Unique diagnostic 24-bit code for every complete identity in this bounded run.
    public byte R => (byte)(40+Token%160);
    public byte G => (byte)(50+(Token/160)%160);
    public byte B => (byte)(60+(Token/25600)%160);
}

static class Program
{
    public static string Root = "";
    public static bool Interactive;
    [STAThread] public static int Main(string[] args)
    {
        int index=Array.IndexOf(args,"--data-root");
        if(index<0 || index+1>=args.Length || !Path.IsPathFullyQualified(args[index+1])) throw new ArgumentException("Absolute --data-root required");
        Root=Path.GetFullPath(args[index+1]); Directory.CreateDirectory(Root);
        Interactive=args.Contains("--interactive");
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
        return App.ExitCode;
    }
}

sealed class SurfaceControl : Control
{
    public override void Render(DrawingContext context) => context.FillRectangle(Brushes.Transparent,new Rect(Bounds.Size));
    public CompositionSurfaceVisual? Visual;
    public void Install(Compositor compositor, CompositionDrawingSurface surface)
    {
        Visual ??= compositor.CreateSurfaceVisual();
        Visual.Size=new(Bounds.Width,Bounds.Height); Visual.Surface=surface;
        ElementComposition.SetElementChildVisual(this,Visual);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property==BoundsProperty && Visual!=null) Visual.Size=new(Bounds.Width,Bounds.Height);
    }
    public void Clear() { ElementComposition.SetElementChildVisual(this,null); Visual=null; }
}

sealed class App : Application
{
    public static int ExitCode=1;
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window=new Window { Title="LF-WIN-RES-002 — GPU presentation proof", Width=860,Height=560 };
            var video=new SurfaceControl { Width=640,Height=360,ClipToBounds=true };
            var overlay=new Button { Content="Shared overlay",Background=Brushes.Magenta,Foreground=Brushes.Black,Width=160,Height=48,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Top,Margin=new Thickness(30) };
            var scene=new Grid { Width=640,Height=360,ClipToBounds=true,Background=Brushes.Black };
            scene.Children.Add(video); scene.Children.Add(overlay);
            overlay.ZIndex=1;
            window.Content=new StackPanel { Spacing=12, Margin=new Thickness(24), Children={new TextBlock {Text="Synthetic D3D11 frames • public Avalonia GPU import"},scene} };
            desktop.MainWindow=window;
            window.Opened+=async (_,_) => {
                try { await new Proof(window,scene,video,overlay).Run(); ExitCode=0; }
                catch(Exception error) { File.WriteAllText(Path.Combine(Program.Root,"failure.txt"),error.ToString()); Console.Error.WriteLine(error); }
                finally { if(!Program.Interactive) desktop.Shutdown(ExitCode); }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}

sealed class Proof(Window window, Grid scene, SurfaceControl video, Button overlay)
{
    readonly List<object> records=[];
    readonly List<Slot> slots=[];
    readonly Dictionary<Slot,CompositionDrawingSurface> surfaces=[];
    bool measuring;
    Compositor compositor=null!;
    ICompositionGpuInterop interop=null!;
    Gpu gpu=null!;
    Slot? retained;
    long source=1,color=1,epoch=1,host=1,latest,sequence;
    long transaction;
    int assertions,allocated,destroyed;
    Frame New(long? generation=null,long? revision=null,long? hostId=null,long? surfaceId=null,long? pts=null) =>
        new(generation??source,pts??sequence*1001,1,30000,++sequence,revision??color,surfaceId??epoch,hostId??host);
    void Log(string kind,object data) { records.Add(new {kind,data,utc=DateTimeOffset.UtcNow}); if(!measuring) Save(); }
    void Save() => File.WriteAllText(Path.Combine(Program.Root,"records.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions {WriteIndented=true}));
    void Check(bool ok,string name) { Log("assertion",new {name,pass=ok}); if(!ok) throw new Exception(name); assertions++; }
    bool Current(Frame id) => id.SourceGeneration==source && id.ColorRevision==color && id.SurfaceEpoch==epoch && id.HostGeneration==host && id.Token==latest;
    async Task<bool> Offer(Frame id,int width=320,int height=180,int readyDelay=0,bool deferRelease=false,Action? duringUpdate=null)
    {
        if(!Current(id)) { Log("rejected",id); return false; }
        var slot=new Slot(gpu,id,width,height); slots.Add(slot); allocated++;
        Log("decoded",id);
        var surface=compositor.CreateDrawingSurface(); surfaces.Add(slot,surface);
        slot.Submit(interop,surface);
        var timer=Stopwatch.StartNew();
        if(readyDelay>0) {
            await Task.Delay(readyDelay);
            Check(!slot.Update!.IsCompleted,"ready delay blocks import update");
        }
        slot.SignalReady(); duringUpdate?.Invoke();
        await slot.Update!.WaitAsync(TimeSpan.FromSeconds(10));
        Log("renderReady",new {id,updateMs=timer.Elapsed.TotalMilliseconds});
        if(!Current(id) || interop.IsLost || slot.Imported!.IsLost) {
            Check(slot.TryRelease(),"rejected update releases independently"); Log("rejectedAfterUpdate",id); return false;
        }
        // Await resumes on UI dispatcher; this transaction installs this exact updated snapshot.
        Dispatcher.UIThread.VerifyAccess();
        video.Install(compositor,surface);
        await compositor.RequestCommitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        if(!Current(id)) { Check(slot.TryRelease(),"invalidated commit releases independently"); video.Clear(); Log("rejectedAfterCommit",id); return false; }
        if(retained!=null) retained.Leases--;
        retained=slot; slot.Leases++;
        Log("UIAccepted",new {id,transaction=++transaction});
        if(deferRelease) {
            Check(!slot.Released,"UIAccepted does not grant producer reuse");
            await Task.Delay(80);
            Check(!CanReuse(slot),"release observation delay prevents reuse");
        }
        Check(slot.TryRelease(),"consumer mutex release observed");
        Log("release",new {id,elapsedMs=timer.Elapsed.TotalMilliseconds});
        Check(!CanReuse(slot),"retained capture lease prevents reuse after GPU release");
        return true;
    }
    static bool CanReuse(Slot slot) => slot.Released && slot.Leases==0 && slot.Update?.IsCompleted==true;
    async Task Retire()
    {
        foreach(var slot in slots.Where(s=>!s.Destroyed && CanReuse(s))) { surfaces[slot].Dispose(); surfaces.Remove(slot); await slot.DisposeAsync(); destroyed++; }
    }
    void Capture(string name)
    {
        var slot=retained??throw new Exception("No accepted token");
        slot.Leases++;
        try {
            var bytes=slot.Capture(); var id=slot.Identity;
            Log("captureAttempt",new {name,id,firstPixel=bytes.Take(4).ToArray(),expected=new byte[]{id.B,id.G,id.R,255}});
            File.WriteAllBytes(Path.Combine(Program.Root,name+".bgra"),bytes);
            for(int i=0;i<bytes.Length;i+=4) {
                bool marker=(i/4)%slot.Width<slot.Width/4 && (i/4)/slot.Width<slot.Height/4;
                if(bytes[i]!=(marker?255:id.B) || bytes[i+1]!=(marker?255:id.G) || bytes[i+2]!=(marker?255:id.R) || bytes[i+3]!=255) throw new Exception("Capture identity/pixels differ at "+i);
            }
            File.WriteAllBytes(Path.Combine(Program.Root,name+".bgra"),bytes);
            Log("capture",new {name,id,slot.Width,slot.Height,sha256=Convert.ToHexString(SHA256.HashData(bytes)),allPixelsMatch=true});
            Check(true,name+" same-token full-pixel capture");
        } finally {slot.Leases--;}
    }
    async Task Accept(Frame id,int width=320,int height=180,int delay=0,bool deferRelease=false)
    { latest=id.Token; Check(await Offer(id,width,height,delay,deferRelease),"current full identity accepted"); await Retire(); }
    public async Task Run()
    {
        await Task.Delay(150);
        compositor=ElementComposition.GetElementVisual(video)!.Compositor;
        interop=await compositor.TryGetCompositionGpuInterop()??throw new Exception("GPU import unavailable");
        Log("runtime",new {agent="LF-WIN-RES-002",framework=typeof(Application).Assembly.FullName,runtime=Environment.Version.ToString(),os=Environment.OSVersion.ToString(),interop.SupportedImageHandleTypes,interop.SupportedSemaphoreTypes,luid=interop.DeviceLuid==null?null:Convert.ToHexString(interop.DeviceLuid)});
        Check(interop.SupportedImageHandleTypes.Contains(Avalonia.Platform.KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle),"public D3D11 handle supported");
        Check(interop.GetSynchronizationCapabilities(Avalonia.Platform.KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle).HasFlag(CompositionGpuImportedImageSynchronizationCapabilities.KeyedMutex),"keyed mutex supported");
        gpu=new Gpu(interop.DeviceLuid); Log("adapter",gpu.Adapter);
        try {
            await Accept(New()); Capture("current");
            await VisualChecks("initial");
            var first=retained!.Identity;
            var stale=New(generation:0); latest=stale.Token; Check(!await Offer(stale),"stale source rejected");
            stale=New(revision:0); latest=stale.Token; Check(!await Offer(stale),"stale Color rejected");
            stale=New(); latest=stale.Token+1; Check(!await Offer(stale),"superseded offer rejected");
            stale=New(hostId:0); latest=stale.Token; Check(!await Offer(stale),"stale host rejected");
            stale=New(surfaceId:0); latest=stale.Token; Check(!await Offer(stale),"stale surface epoch rejected");
            await Task.Delay(100); Check(retained.Identity==first,"paused frame retained"); Capture("paused");
            color++; await Accept(New(pts:first.SourcePts),deferRelease:true); Capture("color-revision");
            Check(retained.Identity.SourcePts==first.SourcePts,"Color revision keeps source PTS");
            await Accept(New(),delay:100); Capture("ready-delay");
            var changedColor=New(); latest=changedColor.Token;
            Check(!await Offer(changedColor,duringUpdate:()=>color++),"in-flight stale Color callback rejected");
            var changedSource=New(); latest=changedSource.Token;
            Check(!await Offer(changedSource,duringUpdate:()=>source++),"in-flight stale source callback rejected");
            var invalidated=New(); latest=invalidated.Token;
            Check(!await Offer(invalidated,duringUpdate:()=>host++),"no callback publishes after host invalidation");
            retained.Leases--; retained=null; video.Clear(); await Retire();
            await Accept(New()); Capture("rehost");
            var resizeTimer=Stopwatch.StartNew();
            window.Width=1000; scene.Width=720; video.Width=720;
            await compositor.RequestCommitAsync();
            Log("resizeCost",new {commitMs=resizeTimer.Elapsed.TotalMilliseconds,includesPhysicalScanout=false});
            await Task.Delay(100); Check(video.Visual!.Size.X==video.Bounds.Width,"resize matches composition bounds");
            var rehostTimer=Stopwatch.StartNew();
            scene.Children.Remove(video); host++; video.Clear(); retained!.Leases--; retained=null;
            scene.Children.Insert(0,video); await compositor.RequestCommitAsync(); await Accept(New());
            Log("rehostCost",new {freshAcceptanceMs=rehostTimer.Elapsed.TotalMilliseconds}); Capture("reattach");
            epoch++; await Accept(New()); Capture("replacement");
            // Deliberate producer-device replacement; not physical TDR/compositor loss.
            video.Clear(); retained!.Leases--; retained=null; await Retire();
            var deviceTimer=Stopwatch.StartNew();
            gpu.Dispose(); gpu=new Gpu(interop.DeviceLuid); epoch++; host++;
            await Accept(New()); Log("producerRecreationCost",new {freshAcceptanceMs=deviceTimer.Elapsed.TotalMilliseconds}); Capture("producer-device-recreated");
            await VisualChecks("recreated");
            for(int cycle=0;cycle<8;cycle++) { epoch++; await Accept(New()); }
            await Cadence(1920,1080); await Cadence(3840,2160);
            Log("limitations",new {hardwareDeviceRemoval="not injected; producer recreation is tested separately",physicalScanout="not measured",gpuCopies="external snapshot implementation must be inspected; no zero-copy claim",input="logical hit testing; external physical pointer not synthesized"});
        } finally {
            video.Clear(); if(retained!=null) {retained.Leases--;retained=null;}
            await Retire(); foreach(var surface in surfaces.Values) surface.Dispose(); gpu.Dispose();
            Log("teardown",new {allocated,destroyed,live=slots.Count(s=>!s.Destroyed),assertions});
        }
        Check(allocated==destroyed,"balanced texture/import teardown");
        Console.WriteLine($"PASS executed assertions: {assertions}; evidence: {Program.Root}");
    }
    async Task Cadence(int width,int height)
    {
        using var process=Process.GetCurrentProcess(); var cpu=process.TotalProcessorTime; var timer=Stopwatch.StartNew();
        measuring=true;
        var costs=new List<double>(); long maxWorking=0,maxPrivate=0;
        for(int i=0;i<30;i++) { var tick=Stopwatch.StartNew(); await Accept(New(),width,height); costs.Add(tick.Elapsed.TotalMilliseconds); process.Refresh(); maxWorking=Math.Max(maxWorking,process.WorkingSet64);maxPrivate=Math.Max(maxPrivate,process.PrivateMemorySize64); }
        double seconds=timer.Elapsed.TotalSeconds; measuring=false;
        Log("cadence",new {width,height,frames=30,seconds,fps=30/seconds,cpuCorePercent=(process.TotalProcessorTime-cpu).TotalSeconds/seconds*100,maxWorking,maxPrivate,frameMs=costs,allocationPolicy="one immutable texture per offer; no optimized pool",presentationCpuReadbacks=0});
        Capture($"{width}x{height}");
    }
    async Task VisualChecks(string name)
    {
        await compositor.RequestCommitAsync(); await Task.Delay(100);
        var image=NativeCapture.Capture(window); image.Save(Path.Combine(Program.Root,name+"-window.bgra"));
        var origin=scene.PointToScreen(new Point(0,0)); var client=window.PointToScreen(new Point(0,0));
        int x=origin.X-client.X,y=origin.Y-client.Y;
        var id=retained!.Identity;
        Log("windowCapture",new {name,image.Width,image.Height,x,y,id});
        Check(image.ColorNear(x+300,y+200,id.R,id.G,id.B),"native HWND capture contains accepted GPU pixels");
        Check(image.ColorNear(x+10,y+10,255,255,255),"GPU marker orientation top-left");
        Log("overlayDiagnostic",new {bounds=overlay.Bounds.ToString(),videoBounds=video.Bounds.ToString(),zIndex=overlay.ZIndex,hit=scene.InputHitTest(new Point(60,50))?.GetType().FullName});
        Check(image.ColorNear(x+40,y+40,255,0,255),"shared overlay renders above GPU marker");
        var hit=scene.InputHitTest(new Point(60,50));
        Check(hit is Visual v && (v==overlay || overlay.IsVisualAncestorOf(v)),"overlay visible pixels match input target");
        video.RenderTransform=new TranslateTransform(100,0);
        await compositor.RequestCommitAsync(); await Task.Delay(100);
        image=NativeCapture.Capture(window); image.Save(Path.Combine(Program.Root,name+"-translated.bgra"));
        Check(image.ColorNear(x+20,y+200,0,0,0),"shared transform moves GPU content");
        Check(image.ColorNear(x+200,y+200,id.R,id.G,id.B),"translated GPU pixels align");
        Check(!image.ColorNear(x+(int)scene.Bounds.Width+10,y+200,id.R,id.G,id.B),"parent clipping prevents GPU punch-through");
        Check(scene.InputHitTest(new Point(200,200))==video,"translated video pixels match input target");
        Check(scene.InputHitTest(new Point(20,200))!=video,"vacated transformed area does not target video");
        video.RenderTransform=null; await compositor.RequestCommitAsync();
    }
}
