using Lightflow.Application;
using Xunit;
namespace Lightflow.Application.Tests;
public sealed class BrowserLocationMonitorTests
{
    private sealed class Source : IBrowserLocationChangeSource
    {
        public event Action? Changed;
        public int Disposals;
        public void Fire()=>Changed?.Invoke();
        public void Dispose()=>Disposals++;
    }
    [Fact]
    public async Task RepeatedCatalogReadinessIsNotAVolumePollButNewEpochAndNativeEventsReconcile()
    {
        var source=new Source();var reads=0;
        await using var monitor=new BrowserLocationMonitor(source,_=>{reads++;return Task.CompletedTask;},TimeSpan.Zero);
        monitor.RequestAfterOpen(1);await monitor.WhenIdleAsync();Assert.Equal(1,reads);
        for(var i=0;i<20;i++)monitor.RequestAfterOpen(1);
        await monitor.WhenIdleAsync();Assert.Equal(1,reads);
        monitor.RequestAfterOpen(2);await monitor.WhenIdleAsync();Assert.Equal(2,reads);
        source.Fire();await monitor.WhenIdleAsync();Assert.Equal(3,reads);
    }
    [Fact]
    public async Task DuplicateBurstCoalescesAndDisposalStopsQueuedWork()
    {
        var source=new Source();var reads=0;
        var monitor=new BrowserLocationMonitor(source,_=>{Interlocked.Increment(ref reads);return Task.CompletedTask;},TimeSpan.FromMilliseconds(30));
        for(var i=0;i<100;i++)source.Fire();
        await monitor.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(1,reads);
        source.Fire();await monitor.DisposeAsync();source.Fire();await monitor.DisposeAsync();
        Assert.Equal(1,reads);Assert.Equal(1,source.Disposals);
    }
    [Fact]
    public async Task NewHintCancelsInFlightWorkAndOnlyLatestObservationPublishes()
    {
        var source=new Source();var began=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads=0;var publications=0;CancellationToken first=default;
        await using var monitor=new BrowserLocationMonitor(source,async token=>
        {
            var read=Interlocked.Increment(ref reads);
            if(read==1){first=token;began.SetResult();await Task.Delay(Timeout.Infinite,token);}
            token.ThrowIfCancellationRequested();Interlocked.Increment(ref publications);
        },TimeSpan.Zero);
        source.Fire();await began.Task.WaitAsync(TimeSpan.FromSeconds(5));source.Fire();
        await monitor.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(first.IsCancellationRequested);Assert.Equal(2,reads);Assert.Equal(1,publications);
    }
    [Fact]
    public async Task DisposalCancelsActiveReadWithoutWaitingForUncooperativeNativeWork()
    {
        var source=new Source();var began=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var native=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);CancellationToken observed=default;
        var monitor=new BrowserLocationMonitor(source,token=>{observed=token;began.SetResult();return native.Task;},TimeSpan.Zero);
        source.Fire();await began.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(observed.IsCancellationRequested);native.SetResult();Assert.Equal(1,source.Disposals);
    }
}
