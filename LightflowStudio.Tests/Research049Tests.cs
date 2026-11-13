using System.Globalization;
using Xunit;
namespace LightflowStudio.Tests;
public sealed partial class FlyleafPlaybackIntegrationTests
{
 [Fact]
 public async Task Research049_Matrix()
 {
  var deps = PlaybackDependencyLocator.FindSharedLibraries()!;
  var output = new List<string>{"fixture,speed,playing,direction,before_ms,after_ms,delta_ms,state,error"};
  foreach(var vfr in new[]{false,true})
  {
   var fixture=Path.Combine(_root,vfr?"049-vfr.mkv":"049-cfr.mkv");
   if(vfr) GenerateVfrFixture(Path.Combine(deps,"ffmpeg.exe"),fixture); else GenerateCfrFixture(Path.Combine(deps,"ffmpeg.exe"),fixture);
   await StaDispatcher.RunAsync(async()=>{
    TestWpfApplication.EnsureLoaded();
    await using var backend=new FlyleafPlaybackBackend(deps);
    await using var service=new MediaPlaybackService(backend);
    await service.OpenAsync(fixture);    using var presentation=service.CreatePresentation();
    var window=new System.Windows.Window{Content=presentation.Surface,Width=320,Height=200,Left=-32000,ShowActivated=false,ShowInTaskbar=false};
    window.Show();
    foreach(var speed in PlaybackReviewOptions.Speeds)
    foreach(var playing in new[]{false,true})
    foreach(var forward in new[]{true,false})
    {
     using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
     await service.SetReviewOptionsAsync(new(speed));
     await service.SeekAsync(TimeSpan.FromMilliseconds(400),timeout.Token);
     if(playing){await service.PlayAsync(timeout.Token); await Task.Delay(80);}
     var before=service.Snapshot.DisplayedTimestamp!.Position.TotalMilliseconds;
     string error="";
     try {if(forward)await service.StepForwardAsync(timeout.Token);else await service.StepBackwardAsync(timeout.Token);}
     catch(Exception e){error=e.GetType().Name+":"+e.Message.Replace(',',';');}
     var after=service.Snapshot.DisplayedTimestamp!.Position.TotalMilliseconds;
     output.Add(FormattableString.Invariant($"{(vfr?"VFR":"CFR")},{speed},{playing},{(forward?"forward":"backward")},{before},{after},{after-before},{service.Snapshot.State},{error}"));
    }
     window.Content=null; window.Close();
   });
  }
  var dest=Path.Combine(AppContext.BaseDirectory,"research049-surface-matrix.csv");
  File.WriteAllLines(dest,output);
  Console.WriteLine("RESEARCH049="+dest);
 }
}

