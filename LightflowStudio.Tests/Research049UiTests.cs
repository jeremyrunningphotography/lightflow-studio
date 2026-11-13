using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Xunit;
namespace LightflowStudio.Tests;
public sealed partial class FlyleafPlaybackIntegrationTests
{
 [Fact]
 public async Task Research049_PlayerControls()
 {
 var deps=PlaybackDependencyLocator.FindSharedLibraries()!;
 var fixture=Path.Combine(_root,"049-ui.mkv"); GenerateCfrFixture(Path.Combine(deps,"ffmpeg.exe"),fixture,6);
 var rows=new List<string>{"speed,input,owner,handled,before_ms,after_ms,slider_ms"};
 await StaDispatcher.RunAsync(async()=>{
 TestWpfApplication.EnsureLoaded();
 await using var backend=new FlyleafPlaybackBackend(deps);
 MediaPlaybackService? service=null;
 await using var coordinator=new MediaPlaybackCoordinator(()=>service=new MediaPlaybackService(backend));
 var host=new PlayerViewerHost(coordinator);
 var window=new Window{Content=host,Width=1100,Height=800,Left=-32000,ShowActivated=false,ShowInTaskbar=false}; window.Show();
 try{
 var asset=new PlayerViewerAsset(Guid.NewGuid(),"clip.mkv","clip.mkv","clip.mkv",MediaPresentationKind.Video);
 await host.OpenAsync(asset,new(asset.RootId,asset.RelativePath,asset.Key,fixture,MediaRootAvailability.Online,true));
 foreach(var speed in PlaybackReviewOptions.Speeds)
 foreach(var forward in new[]{true,false})
 foreach(var input in new[]{"button","key-host","key-speed"}){
 host.SpeedChoice.SelectedIndex=Array.IndexOf(PlaybackReviewOptions.Speeds,speed);
 await service!.SetReviewOptionsAsync(new(speed));
 await service.SeekAsync(TimeSpan.FromSeconds(2)); await Task.Delay(100);
 var before=service.Snapshot.DisplayedTimestamp!.Position.TotalMilliseconds;
 bool handled=true;
 if(input=="button") (forward?host.NextFrameButton:host.PreviousFrameButton).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
 else handled=host.TryHandleShortcut(forward?Key.Right:Key.Left,input=="key-host"?host:host.SpeedChoice,ModifierKeys.None);
 await Task.Delay(250);
 var after=service.Snapshot.DisplayedTimestamp!.Position.TotalMilliseconds;
 rows.Add(FormattableString.Invariant($"{speed},{input},{(forward?"forward":"backward")},{handled},{before},{after},{host.PositionSlider.Value}"));
 }
 }finally{await host.CloseAsync();window.Content=null;window.Close();}
 });
 File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"research049-ui.csv"),rows);
 }
}
