using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class JobsCleanupTests
{
    [Theory]
    [InlineData((int)FileOperationState.Completed)]
    [InlineData((int)FileOperationState.CompletedWithFailures)]
    [InlineData((int)FileOperationState.Failed)]
    [InlineData((int)FileOperationState.Cancelled)]
    [InlineData((int)FileOperationState.Interrupted)]
    public void FilesystemRemovalSurvivesReloadAndPreservesMediaAndActiveRecovery(int terminal)
    {
        var root = Path.Combine(Path.GetTempPath(), "jobs-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.mov");
            var output = Path.Combine(root, "output.mov");
            File.WriteAllText(source, "original source");
            File.WriteAllText(output, "successful output");
            var path = Path.Combine(root, "history.json");
            var store = new FileOperationHistoryStore(path);
            var done = new FileOperationIntent(Guid.NewGuid(), FileOperationKind.Move,
                [new(null, source)], root, DateTimeOffset.UtcNow, null, false, FileOperationExecution.Job);
            var active = done with { OperationId = Guid.NewGuid() };
            var result = new FileOperationResult(done.OperationId, (FileOperationState)terminal, 1, 10, [], DateTimeOffset.UtcNow);
            store.Begin(done);
            store.Complete(done, result);
            store.Begin(active);
            var checkpoint = File.ReadAllBytes(path + ".active");
            var row = Assert.Single(JobsWorkspacePresentation.ProjectFileOperations([], store.Load()));
            Assert.True(row.CanRemove);
            Assert.False(row.CanRetry);
            Assert.Equal(JobRemovalKind.FileOperationHistory, row.RemovalKind);
            Assert.Equal(1, store.Remove(new HashSet<Guid> { done.OperationId, active.OperationId }));
            Assert.Empty(new FileOperationHistoryStore(path).Load());
            Assert.Equal(checkpoint, File.ReadAllBytes(path + ".active"));
            Assert.Equal("original source", File.ReadAllText(source));
            Assert.Equal("successful output", File.ReadAllText(output));
            store.RecoverInterrupted();
            Assert.Equal(active.OperationId, Assert.Single(store.Load()).Intent.OperationId);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData((int)JobState.Completed)]
    [InlineData((int)JobState.CompletedWithWarnings)]
    [InlineData((int)JobState.Failed)]
    [InlineData((int)JobState.Cancelled)]
    [InlineData((int)JobState.Skipped)]
    public void MixedTerminalSelectionsRequireLifecycleEligibilityNotSharedPayload(int terminal)
    {
        var items = new[] { "Export", "File operation", "Visual Index", "Premiere handoff" }.Select(capability =>
            new JobsWorkspaceItem(Guid.NewGuid(), null, null, true, false, capability, capability,
                (JobState)terminal, 100, "", "", "", "", "", DateTimeOffset.UtcNow, 0,
                SupportsQueueControls: capability == "Export", SupportsRetry: capability == "Visual Index")).ToArray();
        Assert.True(JobsSelectionEligibility.For(items).CanClearHistory);
        Assert.False(JobsSelectionEligibility.For(items).CanCancel);
        Assert.True(items[2].CanRetry);
        Assert.False(items[1].CanRetry);
        foreach (var state in new[] { JobState.Planned, JobState.Queued, JobState.Running, JobState.Paused,
                     JobState.Pausing, JobState.Cancelling, JobState.NeedsAttention })
            Assert.False(JobsSelectionEligibility.For(items.Append(items[0] with { State = state })).CanClearHistory);
    }

    [Fact]
    public void RetryAndReviewAreIndependentTypedContractsAndActiveWorkCannotRetry()
    {
        Assert.False(JobActionState.For(JobState.Failed).CanRetry);
        Assert.True(JobActionState.For(JobState.Failed, retry: true).CanRetry);
        Assert.False(JobActionState.For(JobState.Running, retry: true).CanRetry);
        Assert.True(JobActionState.For(JobState.NeedsAttention, queueControls: true).CanRetry);
        Assert.False(JobActionState.For(JobState.Failed, queueControls: true).CanRetry);
        Assert.True(JobActionState.For(JobState.Failed, current: false, reviewAndRerun: true).CanReviewAndRerun);
        Assert.False(JobActionState.For(JobState.Running, reviewAndRerun: true).CanReviewAndRerun);
    }
}
