using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Native;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Input.Raw;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;

class Program {
 public static string Root=""; public static string Mode="smoke"; public static int Exit;
 [STAThread] public static int Main(string[] args){
 var pos=Array.IndexOf(args,"--data-root");if(pos<0||pos+1>=args.Length||!Path.IsPathFullyQualified(args[pos+1]))throw new ArgumentException("absolute --data-root required");Root=args[pos+1];Directory.CreateDirectory(Root);
 pos=Array.IndexOf(args,"--mode");if(pos>=0)Mode=args[pos+1];
 AppBuilder.Configure<App>().UsePlatformDetect().With(new AvaloniaNativePlatformOptions{RenderingMode=new[]{AvaloniaNativeRenderingMode.Metal},OverlayPopups=true}).LogToTrace().StartWithClassicDesktopLifetime(args,ShutdownMode.OnExplicitShutdown);return Exit;
 }
 public static void Log(string kind,object value){var s=JsonSerializer.Serialize(new{kind,value});File.AppendAllText(Path.Combine(Root,"results.jsonl"),s+"\n");Console.WriteLine(s);}
}
class App:Application {
 public override void Initialize(){Styles.Add(new FluentTheme());RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;Name="Lightflow X3 IOSurface Proof";}
 public override void OnFrameworkInitializationCompleted(){base.OnFrameworkInitializationCompleted();if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime l){var w=new ProofWindow();l.MainWindow=w;w.Opened+=async(_,_)=>{try{await w.Run();}catch(Exception e){Program.Exit=1;Program.Log("failure",new{error=e.ToString()});}finally{await w.DisposeGraphics();w.Close();Program.Log("teardown",new{surfaces=Native.x3_live_surfaces(),producers=Native.x3_live_producers()});l.Shutdown(Program.Exit);}};}}
}
// Shared layout consumes a composition surface; only SurfaceAdapter knows IOSurface/Metal.
class SurfaceControl:Control {
 public override void Render(DrawingContext context){context.DrawRectangle(Brushes.Transparent,null,new Rect(Bounds.Size));}
 public CompositionDrawingSurface? Surface;public CompositionSurfaceVisual? Child;
 public void Attach(CompositionDrawingSurface s){Surface=s;var v=ElementComposition.GetElementVisual(this)!;Child=v.Compositor.CreateSurfaceVisual();Child.Surface=s;Child.Size=new Vector2((float)Bounds.Width,(float)Bounds.Height);ElementComposition.SetElementChildVisual(this,Child);}
 protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs c){base.OnPropertyChanged(c);if(c.Property==BoundsProperty&&Child!=null)Child.Size=new Vector2((float)Bounds.Width,(float)Bounds.Height);}
 public void Detach(){ElementComposition.SetElementChildVisual(this,null);Child=null;}
}
record FrameOffer(string AssetId,string SessionId,string StreamId,long StartPts,int Generation,ulong Serial,long Pts,int TbNum,int TbDen,long DecodeIdentity,IntPtr Surface,int SurfaceGeneration,int Width,int Height,string Format,string Origin,string Color,int ClockwiseQuarters,string CameraHash,string CreativeHash,bool Compare,IntPtr Ready,ulong ReadyValue,IntPtr Released,ulong ReleasedValue,ulong Device,int Slot);
class SurfaceAdapter {
 public IntPtr Producer;public int Width,Height,SurfaceGeneration;
 public ICompositionImportedGpuImage[] Images=new ICompositionImportedGpuImage[3];
 public ICompositionImportedGpuSemaphore[] Ready=new ICompositionImportedGpuSemaphore[3],Released=new ICompositionImportedGpuSemaphore[3];
 public ulong[] Values=new ulong[3]; public int Next;
 public async Task Init(ICompositionGpuInterop gpu,int w,int h,int epoch){Width=w;Height=h;SurfaceGeneration=epoch;Producer=Native.x3_create(w,h);if(Producer==IntPtr.Zero)throw new Exception("producer failed");var dev=Native.x3_device(Producer);var luid=gpu.DeviceLuid==null?"null":Convert.ToHexString(gpu.DeviceLuid);Program.Log("interop",new{w,h,epoch,device=dev,luid,imageTypes=gpu.SupportedImageHandleTypes,eventTypes=gpu.SupportedSemaphoreTypes,sync=gpu.GetSynchronizationCapabilities("IOSurfaceRef").ToString(),arm=RuntimeInformation.ProcessArchitecture.ToString(),avalonia=typeof(Application).Assembly.GetName().Version?.ToString()});
 if(luid!=Convert.ToHexString(BitConverter.GetBytes(dev).Reverse().ToArray()))throw new Exception("Metal device mismatch");
 for(int i=0;i<3;i++){Images[i]=gpu.ImportImage(new PlatformHandle(Native.x3_surface(Producer,i),"IOSurfaceRef"),new PlatformGraphicsExternalImageProperties{Width=w,Height=h,TopLeftOrigin=true,Format=PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm,MemorySize=Native.x3_stride(Producer,i)*(ulong)h});Ready[i]=gpu.ImportSemaphore(new PlatformHandle(Native.x3_event(Producer,i,0),"MetalSharedEvent"));Released[i]=gpu.ImportSemaphore(new PlatformHandle(Native.x3_event(Producer,i,1),"MetalSharedEvent"));await Task.WhenAll(Images[i].ImportCompleted,Ready[i].ImportCompleted,Released[i].ImportCompleted);Program.Log("slot",new{i,id=Native.x3_id(Producer,i),pointer=Native.x3_surface(Producer,i).ToString("X"),stride=Native.x3_stride(Producer,i),textureSameSurface=Native.x3_texture_identity(Producer,i)==1});}}
 public FrameOffer Offer(ulong serial,int generation,int delay=0,int? forcedSlot=null){int i=forcedSlot??Next++%3;var value=serial;Values[i]=value;if(Native.x3_acquire(Producer,i,value)!=1)throw new Exception("backpressure slot leased");if(Native.x3_produce(Producer,i,value,(uint)serial,delay)!=1)throw new Exception("produce rejected");return new("synthetic-asset","x3-session","synthetic-stream",1000,generation,serial,1000+(long)serial*512,1,15360,(long)serial,Native.x3_surface(Producer,i),SurfaceGeneration,Width,Height,"BGRA8","top-left","SDR sRGB code values; opaque",0,"none","none",false,Native.x3_event(Producer,i,0),value,Native.x3_event(Producer,i,1),value,Native.x3_device(Producer),i);}
 public async Task Release(FrameOffer f){var watch=Stopwatch.StartNew();while(Native.x3_value(Producer,f.Slot,1)<f.ReleasedValue){if(watch.ElapsedMilliseconds>5000)throw new TimeoutException("release timeline");await Task.Delay(1);}if(Native.x3_release(Producer,f.Slot)!=1)throw new Exception("release failed");}
 public async Task Dispose(){for(int i=0;i<3;i++){if(Images[i]!=null)await Images[i].DisposeAsync();if(Ready[i]!=null)await Ready[i].DisposeAsync();if(Released[i]!=null)await Released[i].DisposeAsync();}if(Producer!=IntPtr.Zero){Native.x3_destroy(Producer);Producer=IntPtr.Zero;}}
}
class ProofWindow:Window {
 public readonly SurfaceControl Video=new(){Width=640,Height=360,Focusable=true};
 public readonly Grid Scene=new(){Width=800,Height=500,Background=new SolidColorBrush(Color.Parse("#18202A"))};
 readonly Border ClipHost;readonly Grid Layer; readonly Button Play=new(){Content="▶",Width=64,Height=48,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center,Background=Brushes.Lime};
 readonly TextBlock Status=new(){Text="Frame status",Foreground=Brushes.White,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Top,Margin=new Thickness(12)};
 readonly Button Edge=new(){Content="Review",HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Bottom,Margin=new Thickness(12)};
 readonly Border Interaction=new(){Width=120,Height=24,Background=Brushes.Magenta,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Bottom,Margin=new Thickness(12)};
 readonly TextBox Editor=new(){Text="Local text",Width=250}; readonly StackPanel Body=new();
 public Compositor? Comp; public CompositionDrawingSurface? Drawing;public ICompositionGpuInterop? Gpu;public SurfaceAdapter? Adapter;public int Generation=1;public ulong LastComposed;public ulong NextSerial=100;public int SurfaceEpoch; public int Clicks,VideoClicks,Keys,Moves,Wheels;
 public ProofWindow(){Title="Lightflow — IOSurface presentation proof";Width=1000;Height=700;Background=new SolidColorBrush(Color.Parse("#101318"));
 var layer=Layer=new Grid{Width=640,Height=360};layer.Children.Add(Video);layer.Children.Add(Play);layer.Children.Add(Status);layer.Children.Add(Edge);layer.Children.Add(Interaction);
 ClipHost=new Border{Width=600,Height=320,ClipToBounds=true,CornerRadius=new CornerRadius(24),Child=layer};Scene.Children.Add(ClipHost);Body.Children.Add(new TextBlock{Text="Lightflow | Shared Player layout · native surface adapter",Margin=new Thickness(12)});Body.Children.Add(Scene);Body.Children.Add(Editor);Content=Body;
 Play.Click+=(_,_)=>{Clicks++;Status.Text="Pause action "+Clicks;};Video.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(Video).Properties.IsLeftButtonPressed){VideoClicks++;Video.Focus();e.Handled=true;}};Video.KeyDown+=(_,e)=>{if(e.Key==Key.Space){Keys++;e.Handled=true;}};
 Video.PointerMoved+=(_,_)=>Moves++;Video.PointerWheelChanged+=(_,_)=>Wheels++;
 Video.ContextMenu=new ContextMenu{ItemsSource=new[]{new MenuItem{Header="Review frame"},new MenuItem{Header="Capture source"}}};
 }
 public async Task Init(int w,int h){Comp=ElementComposition.GetElementVisual(Video)!.Compositor;Gpu=await Comp.TryGetCompositionGpuInterop()??throw new Exception("Metal interop unavailable");Drawing=Comp.CreateDrawingSurface();Video.Attach(Drawing);Adapter=new();await Adapter.Init(Gpu,w,h,++SurfaceEpoch);Program.Log("window",new{RenderScaling,ClientSize,handle=TryGetPlatformHandle()?.HandleDescriptor});}
 public async Task<bool> Compose(FrameOffer f){if(f.Generation!=Generation||f.SurfaceGeneration!=Adapter!.SurfaceGeneration){Program.Log("stale-rejected",new{f.Serial,f.Generation,current=Generation});Native.x3_cancel(Adapter!.Producer,f.Slot);return false;}var t=Stopwatch.StartNew();await Drawing!.UpdateWithTimelineSemaphoresAsync(Adapter!.Images[f.Slot],Adapter.Ready[f.Slot],f.ReadyValue,Adapter.Released[f.Slot],f.ReleasedValue);if(f.Generation!=Generation){Program.Log("cancelled-after-update",new{f.Serial});return false;}LastComposed=f.Serial;Program.Log("surface-update",new{f.Serial,f.Generation,f.SurfaceGeneration,pts=f.Pts,tb=new[]{f.TbNum,f.TbDen},surface=Native.x3_id(Adapter.Producer,f.Slot),f.Slot,f.ReadyValue,f.ReleasedValue,readyObserved=Native.x3_value(Adapter.Producer,f.Slot,0),releaseObserved=Native.x3_value(Adapter.Producer,f.Slot,1),updateMs=t.Elapsed.TotalMilliseconds,presented=false});return true;}
 public async Task CaptureSource(string name){using var image=await Comp!.CreateCompositionVisualSnapshot(Video.Child!,RenderScaling);var path=Path.Combine(Program.Root,name+"-source.png");image.Save(path);using var pixels=SkiaSharp.SKBitmap.Decode(path);uint decoded=0;for(int b=0;b<16;b++){int x=(int)((b*32+16)*pixels.Width/(double)Adapter!.Width);int y=(int)(16*pixels.Height/(double)Adapter.Height);if(pixels.GetPixel(x,y).Red>128)decoded|=1u<<b;}var c=pixels.GetPixel(pixels.Width/2,pixels.Height/2);Program.Log("frame-identity",new{name,LastComposed,decoded,match=decoded==(LastComposed&65535),color=new[]{c.Red,c.Green,c.Blue,c.Alpha}});if(decoded!=(LastComposed&65535))throw new Exception("captured frame identity mismatch");}
 public async Task Capture(string name){await Task.Delay(40);using var image=await Comp!.CreateCompositionVisualSnapshot(ElementComposition.GetElementVisual(name=="context-menu"?this:Scene)!,RenderScaling);image.Save(Path.Combine(Program.Root,name+".png"));Program.Log("capture",new{name,LastComposed,Generation,width=image.PixelSize.Width,height=image.PixelSize.Height,scale=RenderScaling,semantics="offscreen composition snapshot; not scanout"});}
 public async Task Run(){await Task.Delay(200);await Init(1920,1080);var f=Adapter!.Offer(1,Generation);await Compose(f);await Adapter.Release(f);await CaptureSource("normal");await Capture("normal");if(Program.Mode=="smoke")return;await Scenarios();}
 public async Task Scenarios(){
 var a=Adapter!;var f=a.Offer(2,Generation,200);var start=Stopwatch.StartNew();await Compose(f);var taskMs=start.Elapsed.TotalMilliseconds;await a.Release(f);Program.Log("delayed-producer",new{taskMs,releaseMs=start.Elapsed.TotalMilliseconds,ready=Native.x3_value(a.Producer,f.Slot,0)});await CaptureSource("delayed");await Capture("delayed");
 var held=a.Offer(3,Generation);await Compose(held);while(Native.x3_value(a.Producer,held.Slot,1)<held.ReleasedValue)await Task.Delay(1);var hash=Native.x3_hash(a.Producer,held.Slot);var blocked=Native.x3_acquire(a.Producer,held.Slot,99)==0;
 for(ulong s=4;s<10;s++){int slot=(held.Slot+1+(int)(s%2))%3;var later=a.Offer(s,Generation,forcedSlot:slot);await Compose(later);await a.Release(later);}
 var stable=hash==Native.x3_hash(a.Producer,held.Slot);await a.Release(held);Program.Log("held-lease",new{blocked,stable,hash=hash.ToString("X"),held.Serial});if(!blocked||!stable)throw new Exception("lease violation");
 var stale=a.Offer(10,Generation);Generation++;var accepted=await Compose(stale);if(accepted)throw new Exception("stale accepted");Program.Log("generation",new{Generation,LastComposed,staleAccepted=accepted});
 // Producer loss is modeled as absence of offers and explicit visible-authority invalidation.
 Program.Log("producer-loss",new{simulated=true,LastComposed,visiblePresented=false});
 await CaptureSource("overlays");await Capture("overlays");Play.IsVisible=false;Status.IsVisible=false;Edge.IsVisible=false;Interaction.IsVisible=false;await Capture("overlays-off");Play.IsVisible=true;Status.IsVisible=true;Edge.IsVisible=true;Interaction.IsVisible=true;
 Video.Opacity=.5;Video.RenderTransform=new RotateTransform(8);await Capture("opacity-transform");Video.Opacity=1;Video.RenderTransform=null;
 for(int i=0;i<6;i++){Width=900+i*30;Height=650+i*10;await Task.Delay(50);Program.Log("resize",new{i,RenderScaling,ClientSize,sourceWidth=a.Width,sourceHeight=a.Height,Generation});}await Capture("resized");
 Video.Detach();await Capture("detached");Video.Attach(Drawing!);await Capture("reattached");
 WindowState=WindowState.FullScreen;await Task.Delay(600);Program.Log("fullscreen",new{WindowState,ClientSize,RenderScaling,native=Marshal.PtrToStringUTF8(Native.x3_windows())});await Capture("fullscreen");WindowState=WindowState.Normal;await Task.Delay(600);Program.Log("fullscreen-exit",new{WindowState,ClientSize,RenderScaling,native=Marshal.PtrToStringUTF8(Native.x3_windows())});
 Editor.Focus();var editorBefore=Editor.IsFocused;var focused=a.Offer(11,Generation);await Compose(focused);await a.Release(focused);Program.Log("focus",new{editorBefore,editorAfter=Editor.IsFocused,videoFocused=Video.IsFocused});
 Play.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Video.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Space});Program.Log("input-api",new{Clicks,Keys,scope="routed events only; no native hit-test proof"});
 await InputCases();await IdentityCases();await Performance(1920,1080,10,false);await Performance(1920,1080,10,true);await Performance(3840,2160,30,false);await Performance(3840,2160,30,true);await CpuFallback();await Reopen();
 }
 public async Task InputCases(){
 using var mouse=(MouseDevice)Activator.CreateInstance(typeof(MouseDevice),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic,null,new object?[]{null},null)!;ulong stamp=100;
 void Raw(RawInputEventArgs e){typeof(TopLevel).GetMethod("HandleInput",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(this,new object[]{e});}
 T Create<T>(object[] args)=>(T)Activator.CreateInstance(typeof(T),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic,null,args,null)!;
 void Pointer(Point point,RawPointerEventType kind,RawInputModifiers mods=RawInputModifiers.None){Raw(Create<RawPointerEventArgs>(new object[]{mouse,stamp+=100,this,kind,point,mods}));}
 void Click(Point point){Pointer(point,RawPointerEventType.Move);Pointer(point,RawPointerEventType.LeftButtonDown,RawInputModifiers.LeftMouseButton);Pointer(point,RawPointerEventType.LeftButtonUp);}
 var overlay=Play.TranslatePoint(new Point(Play.Bounds.Width/2,Play.Bounds.Height/2),this)!.Value;
 var video=Video.TranslatePoint(new Point(100,150),this)!.Value;
 var clipped=Video.TranslatePoint(new Point(2,2),this)!.Value;
 var prior=Clicks;Click(overlay);var overlayWorks=Clicks==prior+1;prior=VideoClicks;Click(video);var videoWorks=VideoClicks==prior+1;
 var clippedHit=this.InputHitTest(clipped);Pointer(video,RawPointerEventType.Move);Raw(Create<RawMouseWheelEventArgs>(new object[]{mouse,stamp+=100,this,video,new Avalonia.Vector(0,1),RawInputModifiers.None}));
 Pointer(video,RawPointerEventType.RightButtonDown,RawInputModifiers.RightMouseButton);Pointer(video,RawPointerEventType.RightButtonUp);await Task.Delay(60);var context=Video.ContextMenu!.IsOpen;await Capture("context-menu");Video.ContextMenu.Close();
 Video.Focus();var keys=Keys;var keyboard=typeof(AvaloniaNativePlatformOptions).Assembly.GetType("Avalonia.Native.AvaloniaNativePlatform")!.GetField("KeyboardDevice",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public)!.GetValue(null)!;
 void SendSpace(){Raw(Create<RawKeyEventArgs>(new object[]{keyboard,stamp+=100,this,RawKeyEventType.KeyDown,Key.Space,RawInputModifiers.None,PhysicalKey.Space," "}));Raw(Create<RawKeyEventArgs>(new object[]{keyboard,stamp+=100,this,RawKeyEventType.KeyUp,Key.Space,RawInputModifiers.None,PhysicalKey.Space," "}));}
 SendSpace();var player=Keys==keys+1;
 Editor.Focus();keys=Keys;SendSpace();Raw(Create<RawTextInputEventArgs>(new object[]{keyboard,stamp+=100,this,"Z"}));var local=Keys==keys&&Editor.Text!.Contains("Z");
 Program.Log("input",new{overlayWorks,videoWorks,clippedHit=clippedHit?.GetType().Name,context,player,local,Clicks,VideoClicks,Keys,Moves,Wheels,scope="pinned private-API reflection: window-bound MouseDevice raw hit-testing; raw key/text through TopLevel input pipeline; no OS key translation"});if(!overlayWorks||!videoWorks||!player||!local||Moves==0||Wheels==0||!context)throw new Exception("input fixture failed");
 }
 public async Task IdentityCases(){
 var a=Adapter!;foreach(var item in new[]{(12UL,"pause",6144L),(13UL,"forward",6656L),(14UL,"reverse",6144L),(15UL,"seek",30720L),(16UL,"color-state",30720L)}){
 if(item.Item2=="seek")Generation++;var f=a.Offer(item.Item1,Generation) with{Pts=item.Item3,Compare=item.Item2=="color-state",CameraHash=item.Item2=="color-state"?"synthetic-state-hash":"none"};await Compose(f);await a.Release(f);await CaptureSource(item.Item2);Program.Log("action",new{action=item.Item2,f.Serial,f.Pts,f.Generation,f.Compare,f.CameraHash});}
 var delayed=a.Offer(17,Generation,200,forcedSlot:1);var oldReady=Native.x3_value(a.Producer,1,0);var releaseBefore=Native.x3_value(a.Producer,1,1);await Compose(delayed);await a.Release(delayed);await CaptureSource("stale-timeline");Program.Log("stale-timeline",new{oldReady,expected=delayed.ReadyValue,releaseBefore,releaseAfter=Native.x3_value(a.Producer,1,1),oldInsufficient=oldReady<delayed.ReadyValue});
 var delayedConsumer=a.Offer(18,Generation);await Task.Delay(150);var before=Native.x3_value(a.Producer,delayedConsumer.Slot,1);var blocked=Native.x3_acquire(a.Producer,delayedConsumer.Slot,999)==0;await Compose(delayedConsumer);await a.Release(delayedConsumer);Program.Log("delayed-consumer",new{before,expected=delayedConsumer.ReleasedValue,blocked});
 var reused=a.Offer(19,Generation,forcedSlot:delayedConsumer.Slot);while(Native.x3_value(a.Producer,reused.Slot,0)<reused.ReadyValue)await Task.Delay(1);await CaptureSource("snapshot-after-source-reuse");await Compose(reused);await a.Release(reused);await CaptureSource("reused-source-new-frame");
 }
 public async Task Performance(int width,int height,int seconds,bool overlays){Scene.Width=width/2;Scene.Height=height/2;Layer.Width=Video.Width=width/2;Layer.Height=Video.Height=height/2;ClipHost.Width=width/2-40;ClipHost.Height=height/2-40;await Task.Delay(60);await DisposeGraphics();Generation++;await Init(width,height);Play.IsVisible=Status.IsVisible=Edge.IsVisible=Interaction.IsVisible=overlays;var a=Adapter!;var process=Process.GetCurrentProcess();var cpu=process.TotalProcessorTime;var allocated=GC.GetTotalAllocatedBytes();var wall=Stopwatch.StartNew();int n=0,late=0;var durations=new List<double>();var samples=new List<object>();while(wall.Elapsed.TotalSeconds<seconds){var target=n/30.0;var wait=target-wall.Elapsed.TotalSeconds;if(wait>0)await Task.Delay(TimeSpan.FromSeconds(wait));var tick=Stopwatch.StartNew();if(overlays)Status.Text="Review "+n;var f=a.Offer(NextSerial++,Generation);await Compose(f);await a.Release(f);durations.Add(tick.Elapsed.TotalMilliseconds);n++;if(wall.Elapsed.TotalSeconds-n/30.0>1/30.0)late++;if(n%30==0){process.Refresh();samples.Add(new{n,rss=process.WorkingSet64,allocated=GC.GetTotalAllocatedBytes()-allocated,gpuProducerMs=Native.x3_gpu_ms(a.Producer),surfaces=Native.x3_live_surfaces()});}}process.Refresh();Program.Log("performance",new{width,height,seconds,overlays,n,updateReleaseFps=n/wall.Elapsed.TotalSeconds,late,elapsed=wall.Elapsed.TotalSeconds,cpuPercent=(process.TotalProcessorTime-cpu).TotalSeconds/wall.Elapsed.TotalSeconds*100,allocated=GC.GetTotalAllocatedBytes()-allocated,rss=process.WorkingSet64,p50=durations.Order().ElementAt(durations.Count/2),p95=durations.Order().ElementAt((int)(durations.Count*.95)),samples,targetCadence=30,compositionPixels=new[]{(int)(Scene.Width*RenderScaling),(int)(Scene.Height*RenderScaling)},presentedFps=(double?)null});await Capture($"performance-{width}-{overlays}");}
 public async Task CpuFallback(){
 var a=Adapter!;Video.Detach();var fallback=new Image{Width=640,Height=360,IsHitTestVisible=false};Scene.Children.Add(fallback);var timings=new List<double>();var ptr=Marshal.AllocHGlobal(a.Width*a.Height*4);try{for(int i=0;i<30;i++){
 var f=a.Offer(NextSerial++,Generation);while(Native.x3_value(a.Producer,f.Slot,0)<f.ReadyValue)await Task.Delay(1);var t=Stopwatch.StartNew();Native.x3_read(a.Producer,f.Slot,ptr);using var bitmap=new Avalonia.Media.Imaging.Bitmap(PixelFormat.Bgra8888,AlphaFormat.Opaque,ptr,new PixelSize(a.Width,a.Height),new Avalonia.Vector(96,96),a.Width*4);fallback.Source=bitmap;using var captured=await Comp!.CreateCompositionVisualSnapshot(ElementComposition.GetElementVisual(Scene)!,RenderScaling);timings.Add(t.Elapsed.TotalMilliseconds);if(i==29)captured.Save(Path.Combine(Program.Root,"cpu-fallback.png"));Native.x3_cancel(a.Producer,f.Slot);fallback.Source=null;}
 }finally{Marshal.FreeHGlobal(ptr);Scene.Children.Remove(fallback);Video.Attach(Drawing!);}Program.Log("cpu-fallback",new{width=a.Width,height=a.Height,n=timings.Count,p50=timings.Order().ElementAt(15),p95=timings.Order().ElementAt(28),fpsUpperBound=1000/timings.Average(),path="CPU readback + Bitmap allocation/copy + full composition snapshot/readback; includes audit capture cost, not presented fps"});
 }
 public async Task Reopen(){await DisposeGraphics();var next=new ProofWindow{Generation=Generation+1,SurfaceEpoch=SurfaceEpoch};next.Show();await Task.Delay(150);try{await next.Init(1920,1080);var f=next.Adapter!.Offer(NextSerial++,next.Generation,200);var pending=next.Compose(f);next.Close();await pending;await next.Adapter.Release(f);Program.Log("close-pending",new{f.Serial,updateCompleted=pending.IsCompleted,visiblePresented=false,native=Marshal.PtrToStringUTF8(Native.x3_windows())});}finally{await next.DisposeGraphics();}
 var last=new ProofWindow{Generation=Generation+2,SurfaceEpoch=SurfaceEpoch+1};last.Show();await Task.Delay(150);try{await last.Init(1920,1080);var f=last.Adapter!.Offer(NextSerial++,last.Generation);await last.Compose(f);await last.Adapter.Release(f);await last.CaptureSource("reopen");await last.Capture("reopen");Program.Log("reopen",new{f.Serial,last.Generation,visiblePresented=false,native=Marshal.PtrToStringUTF8(Native.x3_windows())});}finally{await last.DisposeGraphics();last.Close();}}
 public async Task DisposeGraphics(){Video.Detach();Drawing?.Dispose();Drawing=null;if(Adapter!=null){await Adapter.Dispose();Adapter=null;}}
}
