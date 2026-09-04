using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class FileOperationTests
{
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
}
