using Xunit;

namespace LightflowStudio.Tests;

public sealed class JobsAdmissionTests
{
    [Fact]
    public async Task SharedLimitIsFifoAndLiveDecreaseDrainsWithoutInterruptingActiveWork()
    {
        var admission = new JobsAdmission(2);
        using var first = await admission.AcquireAsync(Guid.NewGuid(), default);
        var second = await admission.AcquireAsync(Guid.NewGuid(), default);
        var third = admission.AcquireAsync(Guid.NewGuid(), default);
        var fourth = admission.AcquireAsync(Guid.NewGuid(), default);
        Assert.False(third.IsCompleted);
        admission.Maximum = 1;
        second.Dispose();
        Assert.False(third.IsCompleted);
        first.Dispose();
        using var thirdSlot = await third.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(fourth.IsCompleted);
        admission.Maximum = 2;
        using var fourthSlot = await fourth.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task QueuePauseCancellationAndExclusiveLaneDoNotConsumeWaitingCapacity()
    {
        var admission = new JobsAdmission(2, paused: true);
        using var cancellation = new CancellationTokenSource();
        var cancelled = admission.AcquireAsync(Guid.NewGuid(), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var first = admission.AcquireAsync(Guid.NewGuid(), default, "premiere");
        var second = admission.AcquireAsync(Guid.NewGuid(), default, "premiere");
        var other = admission.AcquireAsync(Guid.NewGuid(), default);
        Assert.False(first.IsCompleted);
        admission.IsPaused = false;
        using var firstSlot = await first.WaitAsync(TimeSpan.FromSeconds(5));
        using var otherSlot = await other.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(second.IsCompleted);
        admission.IsPaused = true;
        firstSlot.Dispose();
        Assert.False(second.IsCompleted);
        admission.IsPaused = false;
        using var secondSlot = await second.WaitAsync(TimeSpan.FromSeconds(5));
        otherSlot.Dispose();
        secondSlot.Dispose();
        Assert.False(admission.HasWork);
    }
}
