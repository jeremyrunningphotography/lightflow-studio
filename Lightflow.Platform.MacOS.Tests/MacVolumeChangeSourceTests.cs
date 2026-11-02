using System.Runtime.InteropServices;
using Lightflow.Platform.MacOS;
using Xunit;
namespace Lightflow.Platform.MacOS.Tests;
public sealed class MacVolumeChangeSourceTests
{
    [DllImport("libLightflowVolumeTests.dylib",EntryPoint="lf_test_post_volume_notification")]
    private static extern void Post(int kind);
    [Fact]
    public async Task NativeDisposalWaitsForAnInFlightCallbackAndPreventsLaterCallbacks()
    {
        if(!OperatingSystem.IsMacOS())return;
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        using var source=new MacVolumeChangeSource();var calls=0;
        source.Changed+=()=>{Interlocked.Increment(ref calls);entered.Set();release.Wait(TimeSpan.FromSeconds(5));};
        var signal=Task.Run(()=>Post(0));Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var disposing=Task.Run(source.Dispose);
        try {await Task.Delay(20);Assert.False(disposing.IsCompleted);}
        finally {release.Set();}
        await Task.WhenAll(signal,disposing).WaitAsync(TimeSpan.FromSeconds(5));
        var stopped=calls;Post(1);Post(2);Assert.Equal(stopped,calls);
    }
    [Fact]
    public void NativeMountUnmountRenameSubscriptionsStopCleanlyAndCanBeRecreated()
    {
        if(!OperatingSystem.IsMacOS())return;
        for(var cycle=0;cycle<5;cycle++)
        {
            var events=0;var source=new MacVolumeChangeSource();source.Changed+=()=>events++;
            for(var kind=0;kind<3;kind++) { var before=events;Post(kind);Assert.True(events>before); }
            source.Dispose();source.Dispose();var stopped=events;
            for(var kind=0;kind<3;kind++)Post(kind);
            Assert.Equal(stopped,events);
        }
    }
}
