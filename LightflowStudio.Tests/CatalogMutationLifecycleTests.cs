using Xunit;

namespace LightflowStudio.Tests;

public sealed class CatalogMutationLifecycleTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task DrainCoversWholeLogicalOperationAndRejectsNewWorkUntilReopened()
    {
        using var lifecycle = new CatalogMutationLifecycle();
        var firstTransaction = Signal();
        var continueOperation = Signal();
        var writes = 0;
        var operation = lifecycle.RunAsync(async () =>
        {
            await lifecycle.RunAsync(() => { writes++; return Task.CompletedTask; });
            firstTransaction.SetResult();
            await continueOperation.Task;
            await lifecycle.RunAsync(() => { writes++; return Task.CompletedTask; });
        });
        await firstTransaction.Task;
        var drain = lifecycle.QuiesceAsync();
        Assert.False(drain.IsCompleted);
        var newWork = lifecycle.RunAsync(() => { writes++; return Task.CompletedTask; });
        Assert.False(newWork.IsCompleted);
        continueOperation.SetResult();
        await operation;
        using (await drain.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.Equal(2, writes);
            Assert.False(newWork.IsCompleted);
        }
        await newWork.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, writes);
    }

    [Fact]
    public async Task MultipleMutationsMustAllFinish()
    {
        using var lifecycle = new CatalogMutationLifecycle();
        var first = Signal();
        var second = Signal();
        var a = lifecycle.RunAsync(() => first.Task);
        var b = lifecycle.RunAsync(() => second.Task);
        var drain = lifecycle.QuiesceAsync();
        first.SetResult();
        await a;
        Assert.False(drain.IsCompleted);
        second.SetResult();
        await b;
        using var quiescence = await drain.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FailureAndCancellationReleaseAdmission()
    {
        using var lifecycle = new CatalogMutationLifecycle();
        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.RunAsync(
            () => Task.FromException(new InvalidOperationException())));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lifecycle.RunAsync(
            () => Task.FromCanceled(new CancellationToken(true))));
        using var quiescence = await lifecycle.QuiesceAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancelledDrainReopensWithoutCancellingAcceptedOperation()
    {
        using var lifecycle = new CatalogMutationLifecycle();
        var finish = Signal();
        var work = lifecycle.RunAsync(() => finish.Task);
        using var cancel = new CancellationTokenSource();
        var drain = lifecycle.QuiesceAsync(cancel.Token);
        var queued = lifecycle.RunAsync(() => Task.CompletedTask);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain);
        await queued.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(work.IsCompleted);
        finish.SetResult();
        await work;
        using var retry = await lifecycle.QuiesceAsync();
    }

    [Fact]
    public async Task RepeatedCyclesAndShutdownWakeWaiters()
    {
        using var lifecycle = new CatalogMutationLifecycle();
        for (var i = 0; i < 5; i++)
        {
            using (await lifecycle.QuiesceAsync()) { }
            await lifecycle.RunAsync(() => Task.CompletedTask);
        }
        using var final = await lifecycle.QuiesceAsync();
        var queued = lifecycle.RunAsync(() => Task.CompletedTask);
        final.CompleteShutdown();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => lifecycle.RunAsync(() => Task.CompletedTask));
    }

    [Fact]
    public async Task AdmissionAndQuiesceRaceNeverAllowsSnapshotDuringAdmittedWork()
    {
        for (var i = 0; i < 100; i++)
        {
            using var lifecycle = new CatalogMutationLifecycle();
            var barrier = Signal();
            var active = 0;
            var writer = Task.Run(async () =>
            {
                await barrier.Task;
                await lifecycle.RunAsync(async () =>
                {
                    Interlocked.Increment(ref active);
                    await Task.Yield();
                    Interlocked.Decrement(ref active);
                });
            });
            var snapshot = Task.Run(async () =>
            {
                await barrier.Task;
                using var quiet = await lifecycle.QuiesceAsync();
                Assert.Equal(0, Volatile.Read(ref active));
                await Task.Yield();
                Assert.Equal(0, Volatile.Read(ref active));
            });
            barrier.SetResult();
            await Task.WhenAll(writer, snapshot).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ExternalContinuationCanFinishItsOwnerDuringDrainButCannotOutliveIt()
    {
        using var lifecycle = new CatalogMutationLifecycle();
        Func<Func<Task>, Task>? callback = null;
        var end = Signal();
        var operation = lifecycle.RunAsync(async () =>
        {
            callback = lifecycle.CaptureContinuation();
            await end.Task;
        });
        var quiet = lifecycle.QuiesceAsync();
        var committed = false;
        await Task.Run(() => callback!(() => lifecycle.RunAsync(() =>
        { committed = true; return Task.CompletedTask; })));
        Assert.True(committed);
        Assert.False(quiet.IsCompleted);
        end.SetResult();
        await operation;
        using var lease = await quiet;
        await Assert.ThrowsAsync<InvalidOperationException>(() => callback!(() => Task.CompletedTask));
    }

    [Fact]
    public async Task DetachedExecutionContextCannotReuseReleasedParentAdmission()
    {
        using var lifecycle = new CatalogMutationLifecycle();
        var go = Signal();
        Task? detached = null;
        var wrote = false;
        await lifecycle.RunAsync(() =>
        {
            detached = Task.Run(async () =>
            {
                await go.Task;
                await lifecycle.RunAsync(() => { wrote = true; return Task.CompletedTask; });
            });
            return Task.CompletedTask;
        });
        using (await lifecycle.QuiesceAsync())
        {
            go.SetResult();
            Assert.False(wrote);
        }
        await detached!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(wrote);
    }
}
