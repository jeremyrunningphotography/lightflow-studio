using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class FileOperationTests
{
    [Fact]
    public async Task Executor_ReturnsSuccessfulMutationsAsOneCompletionBatch()
    {
        var platform = new FakePlatform();
        var executor = new FileOperationExecutor(platform, null!, null!);
        var source = new FileOperationSource(null, @"C:\media\clip.mov", 10);
        var intent = new FileOperationIntent(Guid.NewGuid(), FileOperationKind.Recycle, [source], null,
            DateTimeOffset.UtcNow, 10, false, FileOperationExecution.Direct);

        var result = await executor.ExecuteAsync(intent);

        var mutation = Assert.Single(result.CompletedMutations);
        Assert.Equal(FileOperationKind.Recycle, mutation.Kind);
        Assert.Equal(source.Path, mutation.SourcePath);
    }

    [Fact]
    public async Task PromotedJob_DoesNotBecomeTerminalUntilPresentationSynchronizationCompletes()
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"lightflow-file-job-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var executor = new FileOperationExecutor(new FakePlatform(), null!, null!);
            var jobs = new FileOperationJobs(executor, new FileOperationHistoryStore(Path.Combine(temporary, "history.json")),
                async _ => { entered.TrySetResult(); await release.Task; });
            var source = new FileOperationSource(null, @"C:\media\clip.mov", 10);
            var intent = new FileOperationIntent(Guid.NewGuid(), FileOperationKind.Recycle, [source], null,
                DateTimeOffset.UtcNow, 10, false, FileOperationExecution.Job);

            jobs.Enqueue(intent);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(FileOperationState.Running, Assert.Single(jobs.Jobs).State);
            release.TrySetResult();
            await WaitUntilAsync(() => Assert.Single(jobs.Jobs).State == FileOperationState.Completed);
        }
        finally { Directory.Delete(temporary, true); }
    }

    [Fact]
    public void PromotionPolicy_KeepsSmallKnownLocalWorkDirect()
    {
        Assert.Equal(FileOperationExecution.Direct,
            FileOperationPromotionPolicy.Decide(FileOperationKind.Copy, 2, 1024, false, false));
        Assert.Equal(FileOperationExecution.Direct,
            FileOperationPromotionPolicy.Decide(FileOperationKind.Move, 8, FileOperationPromotionPolicy.MaximumDirectBytes, false, false));
    }

    [Theory]
    [InlineData(9, 1024, false, false)]
    [InlineData(1, 268435457, false, false)]
    [InlineData(1, 1024, true, false)]
    [InlineData(1, 1024, false, true)]
    public void PromotionPolicy_PromotesMeaningfulWork(int items, long bytes, bool crossVolume, bool directory) =>
        Assert.Equal(FileOperationExecution.Job,
            FileOperationPromotionPolicy.Decide(FileOperationKind.Copy, items, bytes, crossVolume, directory));

    [Fact]
    public void DragKind_FollowsExplorerModifiers()
    {
        Assert.Equal(FileOperationKind.Move, FileOperationPathSemantics.DragKind(@"C:\a.mov", @"C:\target", false, false));
        Assert.Equal(FileOperationKind.Copy, FileOperationPathSemantics.DragKind(@"C:\a.mov", @"D:\target", false, false));
        Assert.Equal(FileOperationKind.Copy, FileOperationPathSemantics.DragKind(@"C:\a.mov", @"C:\target", true, false));
        Assert.Equal(FileOperationKind.Move, FileOperationPathSemantics.DragKind(@"C:\a.mov", @"D:\target", false, true));
    }

    [Fact]
    public void Planner_RejectsDuplicateSourcesBeforeMutation()
    {
        var source = new FileOperationSource(Guid.NewGuid(), @"C:\media\clip.mov", 10);
        Assert.Throws<ArgumentException>(() => FileOperationPlanner.Plan(FileOperationKind.PermanentDelete, [source, source], null));
    }

    [Fact]
    public void DescendantCheck_IsSegmentAware()
    {
        Assert.True(FileOperationPathSemantics.IsSameOrDescendant(@"C:\media\day1\selects", @"C:\media\day1"));
        Assert.False(FileOperationPathSemantics.IsSameOrDescendant(@"C:\media\day10", @"C:\media\day1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("CON")]
    [InlineData("con.mov")]
    [InlineData("bad:name")]
    [InlineData("trailing.")]
    [InlineData("two\\parts")]
    public void WindowsNamePolicy_RejectsUnsafeNames(string name) =>
        Assert.Throws<ArgumentException>(() => WindowsFileNamePolicy.Validate(name));

    [Theory]
    [InlineData("New Folder")]
    [InlineData("renamed clip.mov")]
    [InlineData("COM10")]
    public void WindowsNamePolicy_AcceptsOrdinaryNames(string name) =>
        Assert.Equal(name, WindowsFileNamePolicy.Validate(name));

    [Fact]
    public void Planner_CopyInPlaceChoosesFirstDeterministicAvailableSibling()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"lightflow-copy-plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var source = Path.Combine(folder, "Clip.mp4"); File.WriteAllText(source, "x");
            File.WriteAllText(Path.Combine(folder, "Clip (1).mp4"), "x");
            var intent = FileOperationPlanner.Plan(FileOperationKind.Copy, [new(null, source, 1)], folder);
            Assert.Equal(Path.Combine(folder, "Clip (2).mp4"), Assert.Single(intent.PlannedDestinations!));
        }
        finally { Directory.Delete(folder, true); }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The file-operation Job did not reach its terminal state.");
            await Task.Delay(10);
        }
    }

    private sealed class FakePlatform : IFileOperationPlatform
    {
        public Task CopyFileAsync(string source, string destination, IProgress<long>? progress,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public void Move(string source, string destination) { }
        public void Recycle(string path) { }
        public void PermanentlyDelete(string path) { }
    }
}
