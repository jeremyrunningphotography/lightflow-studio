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
 public override void OnFrameworkInitializationCompleted(){base.OnFrameworkInitializationCompleted();if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime l){var w=new CompletionWindow();l.MainWindow=w;w.Opened+=async(_,_)=>{try{await w.Run();}catch(Exception e){Program.Exit=1;Program.Log("failure",new{error=e.ToString()});}finally{await w.DisposeGraphics();w.Close();Program.Log("teardown",new{surfaces=Native.x3_live_surfaces(),producers=Native.x3_live_producers()});l.Shutdown(Program.Exit);}};}}
}
// Shared layout consumes a composition surface; only SurfaceAdapter knows IOSurface/Metal.
class SurfaceControl:Control {
 protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer()=>new PlayerPeer(this);
 class PlayerPeer: Avalonia.Automation.Peers.ControlAutomationPeer {public PlayerPeer(Control c):base(c){} protected override Avalonia.Automation.Peers.AutomationControlType GetAutomationControlTypeCore()=>Avalonia.Automation.Peers.AutomationControlType.Pane;}

 public override void Render(DrawingContext context){context.DrawRectangle(Brushes.Transparent,null,new Rect(Bounds.Size));}
 public CompositionDrawingSurface? Surface;public CompositionSurfaceVisual? Child;
 public void Attach(CompositionDrawingSurface s){Surface=s;var v=ElementComposition.GetElementVisual(this)!;Child=v.Compositor.CreateSurfaceVisual();Child.Surface=s;Child.Size=new Vector2((float)Bounds.Width,(float)Bounds.Height);ElementComposition.SetElementChildVisual(this,Child);}
 protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs c){base.OnPropertyChanged(c);if(c.Property==BoundsProperty&&Child!=null)Child.Size=new Vector2((float)Bounds.Width,(float)Bounds.Height);}
 public void Detach(){ElementComposition.SetElementChildVisual(this,null);Child=null;}
}
record FrameOffer(string AssetId,string SessionId,string StreamId,long StartPts,int Generation,ulong Serial,long Pts,int TbNum,int TbDen,long DecodeIdentity,IntPtr Surface,int SurfaceGeneration,int Width,int Height,string Format,string Origin,string Color,int ClockwiseQuarters,string CameraHash,string CreativeHash,bool Compare,IntPtr Ready,ulong ReadyValue,IntPtr Released,ulong ReleasedValue,ulong Device,int Slot,int ColorRevision=0,int HostGeneration=1);
class SurfaceAdapter {
 public IntPtr Producer;public int Width,Height,SurfaceGeneration;
 public ICompositionImportedGpuImage[] Images=new ICompositionImportedGpuImage[3];
 public ICompositionImportedGpuSemaphore[] Ready=new ICompositionImportedGpuSemaphore[3],Released=new ICompositionImportedGpuSemaphore[3];
 public ulong[] Values=new ulong[3]; public int Next;
 public async Task Init(ICompositionGpuInterop gpu,int w,int h,int epoch){Width=w;Height=h;SurfaceGeneration=epoch;Producer=Native.x3_create(w,h);if(Producer==IntPtr.Zero)throw new Exception("producer failed");var dev=Native.x3_device(Producer);var luid=gpu.DeviceLuid==null?"null":Convert.ToHexString(gpu.DeviceLuid);Program.Log("interop",new{w,h,epoch,device=dev,luid,imageTypes=gpu.SupportedImageHandleTypes,eventTypes=gpu.SupportedSemaphoreTypes,sync=gpu.GetSynchronizationCapabilities("IOSurfaceRef").ToString(),arm=RuntimeInformation.ProcessArchitecture.ToString(),avalonia=typeof(Application).Assembly.GetName().Version?.ToString()});
 if(luid!=Convert.ToHexString(BitConverter.GetBytes(dev).Reverse().ToArray()))throw new Exception("Metal device mismatch");
 for(int i=0;i<3;i++){Images[i]=gpu.ImportImage(new PlatformHandle(Native.x3_surface(Producer,i),"IOSurfaceRef"),new PlatformGraphicsExternalImageProperties{Width=w,Height=h,TopLeftOrigin=true,Format=PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm,MemorySize=Native.x3_stride(Producer,i)*(ulong)h});Ready[i]=gpu.ImportSemaphore(new PlatformHandle(Native.x3_event(Producer,i,0),"MetalSharedEvent"));Released[i]=gpu.ImportSemaphore(new PlatformHandle(Native.x3_event(Producer,i,1),"MetalSharedEvent"));await Task.WhenAll(Images[i].ImportCompleted,Ready[i].ImportCompleted,Released[i].ImportCompleted);Program.Log("slot",new{i,id=Native.x3_id(Producer,i),pointer=Native.x3_surface(Producer,i).ToString("X"),stride=Native.x3_stride(Producer,i),textureSameSurface=Native.x3_texture_identity(Producer,i)==1});}}
 public FrameOffer Offer(ulong serial,int generation,int delay=0,int? forcedSlot=null){int i=-1;var value=serial;for(int n=0;n<(forcedSlot.HasValue?1:3);n++){int candidate=forcedSlot??Next++%3;if(Native.x3_acquire(Producer,candidate,value)==1){i=candidate;break;}}if(i<0)throw new Exception("all slots leased: explicit backpressure");Values[i]=value;if(Native.x3_produce(Producer,i,value,(uint)serial,delay)!=1)throw new Exception("produce rejected");return new("synthetic-asset","x3-session","synthetic-stream",1000,generation,serial,1000+(long)serial*512,1,15360,(long)serial,Native.x3_surface(Producer,i),SurfaceGeneration,Width,Height,"BGRA8","top-left","SDR sRGB code values; opaque",0,"none","none",false,Native.x3_event(Producer,i,0),value,Native.x3_event(Producer,i,1),value,Native.x3_device(Producer),i);}
 public async Task Release(FrameOffer f){var watch=Stopwatch.StartNew();while(Native.x3_value(Producer,f.Slot,1)<f.ReleasedValue){if(watch.ElapsedMilliseconds>5000)throw new TimeoutException("release timeline");await Task.Delay(1);}if(Native.x3_release(Producer,f.Slot)!=1)throw new Exception("release failed");}
 public async Task Dispose(){for(int i=0;i<3;i++){if(Images[i]!=null)await Images[i].DisposeAsync();if(Ready[i]!=null)await Ready[i].DisposeAsync();if(Released[i]!=null)await Released[i].DisposeAsync();}if(Producer!=IntPtr.Zero){Native.x3_destroy(Producer);Producer=IntPtr.Zero;}}
}
