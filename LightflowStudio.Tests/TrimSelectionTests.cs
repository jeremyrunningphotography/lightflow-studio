using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class TrimSelectionTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(100);

    [Fact]
    public void NewSelection_DefaultsToFullSourceAndApplyReturnsNoTrim()
    {
        var selection = new TrimSelection(Duration);
        Assert.Equal(TimeSpan.Zero, selection.In);
        Assert.Equal(Duration, selection.Out);
        Assert.Null(selection.Apply());
    }

    [Fact]
    public void ValidInAndOut_ApplyAsTimestampBackedMediaRange()
    {
        var selection = new TrimSelection(Duration);
        Assert.True(selection.SetIn(TimeSpan.FromSeconds(20)));
        Assert.True(selection.SetOut(TimeSpan.FromSeconds(70)));
        var range = selection.Apply();
        Assert.Equal(TimeSpan.FromSeconds(20), range!.EffectiveIn);
        Assert.Equal(TimeSpan.FromSeconds(70), range.EffectiveOut);
    }

    [Fact]
    public void InOnlyTrim_LeavesOutAsFullSourceBoundary()
    {
        var selection = new TrimSelection(Duration);
        selection.SetIn(TimeSpan.FromSeconds(20));
        var range = selection.Apply();
        Assert.Equal(TimeSpan.FromSeconds(20), range!.In);
        Assert.Null(range.Out);
    }

    [Fact]
    public void OutOnlyTrim_LeavesInAsFullSourceBoundary()
    {
        var selection = new TrimSelection(Duration);
        selection.SetOut(TimeSpan.FromSeconds(70));
        var range = selection.Apply();
        Assert.Null(range!.In);
        Assert.Equal(TimeSpan.FromSeconds(70), range.Out);
    }

    [Fact]
    public void InvalidAndZeroLengthBoundaries_AreRejectedWithoutChangingDraft()
    {
        var selection = new TrimSelection(Duration);
        Assert.True(selection.SetIn(TimeSpan.FromSeconds(20)));
        Assert.False(selection.SetOut(TimeSpan.FromSeconds(20)));
        Assert.False(selection.SetIn(Duration));
        Assert.Equal(TimeSpan.FromSeconds(20), selection.In);
        Assert.Equal(Duration, selection.Out);
    }

    [Fact]
    public void ResetIsDraftOnlyAndApplyingItRemovesTrim()
    {
        var applied = new MediaRange(Duration, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(80));
        var selection = new TrimSelection(Duration, applied);
        selection.Reset();
        Assert.Null(selection.Apply());
        Assert.Same(applied, selection.Cancel());
    }

    [Fact]
    public void CancelReturnsOriginalAppliedRangeAfterDraftEdits()
    {
        var applied = new MediaRange(Duration, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(80));
        var selection = new TrimSelection(Duration, applied);
        selection.SetIn(TimeSpan.FromSeconds(30));
        selection.SetOut(TimeSpan.FromSeconds(60));
        Assert.Same(applied, selection.Cancel());
    }
}
