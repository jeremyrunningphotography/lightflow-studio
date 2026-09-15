using Xunit;

namespace LightflowStudio.Tests;

public class PremiereSendModelTests
{
    [Fact]
    public void GlobalAndPerItemRangeChoicesOnlyProjectSelectedSourceRanges()
    {
        var ranged = Source("one.mov", new MediaRange(TimeSpan.FromTicks(100), TimeSpan.FromTicks(10), TimeSpan.FromTicks(90)));
        var full = Source("two.mov");
        var secondRanged = Source("three.mov", new MediaRange(TimeSpan.FromTicks(100), TimeSpan.FromTicks(20), TimeSpan.FromTicks(80)));
        var model = new PremiereSendModel([ranged, full, secondRanged]);

        Assert.Equal(true, model.GlobalUseRangeState);
        Assert.Collection(model.Items,
            item => { Assert.True(item.HasRange); Assert.True(item.UseRange); Assert.Equal(24, item.RangeSegmentLeft, 8); Assert.Equal(192, item.RangeSegmentWidth, 8); },
            item => { Assert.False(item.HasRange); Assert.False(item.UseRange); },
            item => { Assert.True(item.HasRange); Assert.True(item.UseRange); });
        Assert.Equal(2, model.PlannedSources.Count(source => source.Range is not null));

        model.SetUseRange(0, false);
        Assert.Null(model.GlobalUseRangeState);
        Assert.Null(model.PlannedSources[0].Range);
        Assert.Null(model.PlannedSources[1].Range);
        Assert.NotNull(model.PlannedSources[2].Range);

        model.SetGlobalUseRanges(false);
        Assert.Equal(false, model.GlobalUseRangeState);
        Assert.All(model.PlannedSources, source => Assert.Null(source.Range));
        model.SetUseRange(2, true);
        Assert.Null(model.PlannedSources[2].Range);

        model.SetGlobalUseRanges(true);
        Assert.Equal(true, model.GlobalUseRangeState);
        Assert.NotNull(model.PlannedSources[0].Range);
        Assert.Null(model.PlannedSources[1].Range);
        Assert.NotNull(model.PlannedSources[2].Range);
    }

    private static PremiereSource Source(string fileName, MediaRange? range = null)
    {
        PremiereRangeProjection? projection = null;
        if (range is not null) Assert.True(PremiereRangeProjection.TryCreate(range, out projection));
        return new PremiereSource(Guid.NewGuid(), Path.Combine(Path.GetTempPath(), fileName), "1", "1", projection);
    }
}
