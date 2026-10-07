using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

class CompletionWindow:Window {
 public readonly ContentControl Body=new();
 public readonly SurfaceControl Video=new(){Width=640,Height=360,Focusable=true};
 public readonly Button Play=new(){Content="Play / Pause"};
 public readonly TextBox Editor=new(){Text="Local caption",Width=220};
 public readonly TextBlock Status=new(){Text="G3 completion proof"};
 public Compositor Comp=null!; public SurfaceAdapter Adapter=null!; ICompositionGpuInterop Gpu=null!;
 CompositionDrawingSurface? drawing; FrameOffer? accepted;
 int generation=1,color=1,host=1,epoch=1;ulong latest;bool attached=true;
 readonly SemaphoreSlim gate=new(1); long transaction;
 public CompletionWindow(){Title="Lightflow — G3 IOSurface completion";Width=1200;Height=800;Background=Brush.Parse("#101318");
 var shell=new DockPanel();var top=new StackPanel{Orientation=Orientation.Horizontal,Spacing=16,Margin=new Thickness(12),Children={new TextBlock{Text="Lightflow | Browser",FontSize=20},Play,Editor,Status}};DockPanel.SetDock(top,Dock.Top);shell.Children.Add(top);shell.Children.Add(Body);Content=shell;
 AutomationProperties.SetName(Video,"Player presentation, accepted source frame");AutomationProperties.SetName(Play,"Play or pause current media");AutomationProperties.SetName(Editor,"Caption editor");
 Activated+=(_,_)=>Program.Log("activation",new{active=true});Deactivated+=(_,_)=>Program.Log("activation",new{active=false});
 }
 public static void Check(bool ok,string message){Program.Log("assert",new{message,ok});if(!ok)throw new Exception(message);}
 async Task WaitFence(FrameOffer f){var t=Stopwatch.StartNew();while(Native.x3_value(Adapter.Producer,f.Slot,1)<f.ReleasedValue){if(t.ElapsedMilliseconds>5000)throw new TimeoutException();await Task.Delay(1);}}
 bool Current(FrameOffer f)=>attached&&f.Generation==generation&&f.ColorRevision==color&&f.HostGeneration==host&&f.SurfaceGeneration==epoch&&f.Serial==latest;
 public async Task<bool> Offer(FrameOffer f,string scenario){latest=Math.Max(latest,f.Serial);await gate.WaitAsync();CompositionDrawingSurface? next=null;
 try{if(!Current(f)){Native.x3_cancel(Adapter.Producer,f.Slot);Program.Log("rejected",new{scenario,f.Serial,f.Generation,f.ColorRevision,stage="before-update"});return false;}
 next=Comp.CreateDrawingSurface();var t=Stopwatch.StartNew();await next.UpdateWithTimelineSemaphoresAsync(Adapter.Images[f.Slot],Adapter.Ready[f.Slot],f.ReadyValue,Adapter.Released[f.Slot],f.ReleasedValue);await WaitFence(f);
 Check(Native.x3_value(Adapter.Producer,f.Slot,0)>=f.ReadyValue,"ready before candidate installation");
 if(!Current(f)){await Adapter.Release(f);Program.Log("rejected",new{scenario,f.Serial,stage="after-update"});return false;}
 var previous=drawing;drawing=next;next=null;Video.Attach(drawing);
 // Public boundary: changes applied on compositor render thread; never claims scanout.
 await Comp.RequestCommitAsync();
 if(!Current(f)){Video.Detach();await Comp.RequestCommitAsync();await Adapter.Release(f);Program.Log("rejected",new{scenario,f.Serial,stage="after-commit"});previous?.Dispose();return false;}
 var old=accepted;accepted=f;transaction++;AutomationProperties.SetName(Video,$"Player presentation, accepted frame {f.Serial}, source PTS {f.Pts}, Color revision {f.ColorRevision}");
 Program.Log("UIAccepted",new{scenario,f.Serial,f.Generation,f.ColorRevision,f.SurfaceGeneration,f.HostGeneration,surface=Native.x3_id(Adapter.Producer,f.Slot),transaction,ms=t.Elapsed.TotalMilliseconds,callbackThread=Environment.CurrentManagedThreadId,dispatcher=Dispatcher.UIThread.CheckAccess(),boundary="new isolated snapshot installed, RequestCommitAsync processed, current token rechecked",physicalScanout=false});
 if(old!=null)await Adapter.Release(old);previous?.Dispose();await CaptureToken(scenario);return true;
 }finally{next?.Dispose();gate.Release();}}
 async Task CaptureToken(string scenario){var f=accepted!;var hash=Native.x3_hash(Adapter.Producer,f.Slot);using var image=await Comp.CreateCompositionVisualSnapshot(Video.Child!,RenderScaling);var path=Path.Combine(Program.Root,scenario+"-source.png");image.Save(path);using var b=SkiaSharp.SKBitmap.Decode(path);uint serial=0;for(int i=0;i<16;i++)if(b.GetPixel((int)((i*32+16)*b.Width/(double)Adapter.Width),(int)(16*b.Height/(double)Adapter.Height)).Red>128)serial|=1u<<i;
 var blocked=Native.x3_acquire(Adapter.Producer,f.Slot,f.Serial+10000)==0;
 Check(serial==(f.Serial&65535)&&blocked,"accepted capture identity and retained lease");Program.Log("capture-token",new{scenario,f.Serial,decoded=serial,f.ColorRevision,f.Generation,hash=hash.ToString("X"),blocked,releaseTimeline=Native.x3_value(Adapter.Producer,f.Slot,1)});}
 FrameOffer New(ulong serial,int delay=0)=>Adapter.Offer(serial,generation,delay) with{ColorRevision=color,HostGeneration=host};
 async Task Invalidate(){attached=false;host++;accepted=null;Video.Detach();await Comp.RequestCommitAsync();}
 async Task Interop(){Body.Content=Video;await Task.Delay(150);Comp=ElementComposition.GetElementVisual(Video)!.Compositor;Gpu=await Comp.TryGetCompositionGpuInterop()??throw new Exception("no GPU interop");Adapter=new();await Adapter.Init(Gpu,1024,576,epoch);
 await Offer(New(1),"pause");await Offer(New(2),"step");generation++;await Offer(New(3),"seek");color++;await Offer(New(4),"color");color++;await Offer(New(5) with{Compare=true},"compare");
 var stale=New(6) with{Generation=generation-1};Check(!await Offer(stale,"stale-generation"),"stale generation rejected");var staleColor=New(7) with{ColorRevision=color-1};Check(!await Offer(staleColor,"stale-color"),"stale Color rejected");
 var delayed=New(8,100);var pending=Offer(delayed,"inflight-stale-color");await Task.Delay(10);color++;Check(!await pending,"inflight old Color rejected");await Offer(New(9),"new-color");
 var first=New(10,60);var p=Offer(first,"rapid-obsolete");await Task.Delay(1);var second=New(11);var p2=Offer(second,"rapid-latest");await Task.WhenAll(p,p2);Check(!p.Result&&p2.Result&&accepted!.Serial==11,"latest rapid offer accepted exclusively");
 var retained=accepted!;await Invalidate();await Adapter.Release(retained);var detached=New(12);Check(!await Offer(detached,"detached"),"detached rejected");attached=true;await Offer(New(13),"reattached");
 retained=accepted!;await Invalidate();await Adapter.Release(retained);drawing?.Dispose();drawing=null;await Adapter.Dispose();epoch++;Adapter=new();await Adapter.Init(Gpu,1280,720,epoch);attached=true;
 var obsolete=New(14) with{SurfaceGeneration=epoch-1};Check(!await Offer(obsolete,"old-surface"),"obsolete surface generation rejected");await Offer(New(15),"resized-surface");
 InputProof.DumpNative("Player-accepted");DetailsProof.DumpPeers(Video,"Player-accepted");
 Program.Log("interop-complete",new{RenderScaling,epoch,host,transaction,versions=new{avalonia=typeof(Application).Assembly.GetName().Version,skia=typeof(SkiaSharp.SKBitmap).Assembly.GetName().Version}});
 }
 public async Task Run(){await Task.Delay(250);if(Program.Mode is "all" or "interop" or "smoke")await Interop();if(Program.Mode is "all" or "details")await DetailsProof.Run(this);if(Program.Mode is "all" or "input")await InputProof.Run(this);if(Program.Mode is "all" or "images")ImageProof.Run();if(Program.Mode is "all" or "lifecycle")await Lifecycle();}
 public async Task Snapshot(string name){await Task.Delay(80);var comp=ElementComposition.GetElementVisual(this)!.Compositor;using var b=await comp.CreateCompositionVisualSnapshot(ElementComposition.GetElementVisual(this)!,RenderScaling);b.Save(Path.Combine(Program.Root,name+".png"));}
 async Task Lifecycle(){var original=Body.Content;Body.Content=new StackPanel{Children={new TextBlock{Text="Lifecycle / focus proof"},new Button{Content="Return to Browser"}}};Editor.Focus();await Task.Delay(50);var before=Editor.IsFocused;WindowState=WindowState.FullScreen;await Task.Delay(600);Program.Log("fullscreen",new{WindowState,RenderScaling,native=Marshal.PtrToStringUTF8(Native.x3_windows())});WindowState=WindowState.Normal;await Task.Delay(600);Editor.Focus();Check(before&&Editor.IsFocused,"focus restored after fullscreen");
 var child=new Window{Title="Lightflow G3 lifecycle child",Width=500,Height=300,Content=new TextBox{Text="Reopen probe"}};int activates=0,deactivates=0;Activated+=(_,_)=>activates++;Deactivated+=(_,_)=>deactivates++;child.Show();child.Activate();await Task.Delay(120);Activate();await Task.Delay(120);child.Close();var reopened=new Window{Title="Lightflow G3 lifecycle reopened",Width=500,Height=300,Content=new Button{Content="Reopened"}};reopened.Show();await Task.Delay(100);Check(reopened.IsVisible,"close/new-window reopen");reopened.Close();Activate();Editor.Focus();Program.Log("lifecycle",new{activates,deactivates,focus=Editor.IsFocused,scale=RenderScaling,closeReopen=true});Body.Content=original;}
 public async Task DisposeGraphics(){if(Adapter!=null&&Adapter.Producer!=IntPtr.Zero){if(accepted!=null){await Adapter.Release(accepted);accepted=null;}Video.Detach();if(Comp!=null)await Comp.RequestCommitAsync();drawing?.Dispose();await Adapter.Dispose();}}
}
