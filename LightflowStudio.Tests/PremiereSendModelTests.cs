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
            item =>
            {
                Assert.False(item.HasRange); Assert.False(item.UseRange);
                Assert.Equal(0, item.RangeSegmentLeft); Assert.Equal(MediaRangeTimelinePresentation.Width, item.RangeSegmentWidth);
                Assert.Equal("Full source", item.RangeToolTip);
                Assert.Equal("Full Premiere source handoff for two.mov", item.TimelineAutomationName);
            },
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

    [Fact]
    public void MixedSourcesKeepEveryTimelineWhileOnlySavedRangesOfferInOutControls()
    {
        var model = new PremiereSendModel([
            Source("ranged.mov", new MediaRange(TimeSpan.FromTicks(100), TimeSpan.FromTicks(10), TimeSpan.FromTicks(90))),
            Source("full-one.mov"),
            Source("full-two.mov"),
            Source("second-ranged.mov", new MediaRange(TimeSpan.FromTicks(100), TimeSpan.FromTicks(20), TimeSpan.FromTicks(80)))
        ]);

        Assert.Collection(model.Items,
            item => Assert.True(item.HasRange),
            item => AssertFullSource(item),
            item => AssertFullSource(item),
            item => Assert.True(item.HasRange));
    }

    [Fact]
    public void SubclipModeProjectsEverySavedRangeAndUsesReviewOrFullSourceFallback()
    {
        var source = Source("one.mov", new MediaRange(TimeSpan.FromTicks(100), TimeSpan.FromTicks(10), TimeSpan.FromTicks(90)));
        var saved = new Subclip(Guid.NewGuid(), source.AssetId, "Close up", 0, TimeSpan.FromTicks(20),
            TimeSpan.FromTicks(60), TimeSpan.FromTicks(100), 4, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var savedSecond = saved with { SubclipId = Guid.NewGuid(), Name = "Reaction", Ordinal = 1,
            In = TimeSpan.FromTicks(70), Out = TimeSpan.FromTicks(90) };
        var planned = PremiereSendPlanning.Subclips([source], new Dictionary<Guid, IReadOnlyList<Subclip>>
        { [source.AssetId] = [savedSecond, saved] });
        var model = new PremiereSendModel([source], planned);

        Assert.Equal(PremiereSendMode.Subclips, model.Mode);
        var item = model.Items[0];
        Assert.Equal("Close up", item.SourceFileName);
        Assert.Equal("one.mov", item.DetailText);
        Assert.False(item.ShowRangeControl);
        Assert.Equal<Guid?>([saved.SubclipId, savedSecond.SubclipId],
            model.PlannedSubclips.Select(value => value.Projection.SubclipId).ToArray());

        var fallback = Assert.Single(PremiereSendPlanning.Subclips([source],
            new Dictionary<Guid, IReadOnlyList<Subclip>>()));
        Assert.True(fallback.Projection.IsSourceFallback);
        Assert.Equal(source.Range, fallback.Projection.Range);
        var full = Source("full.mov");
        Assert.Null(Assert.Single(PremiereSendPlanning.Subclips([full],
            new Dictionary<Guid, IReadOnlyList<Subclip>>())).Projection.Range);
    }

    private static void AssertFullSource(PremiereSendItem item)
    {
        Assert.False(item.HasRange);
        Assert.False(item.RangeControlEnabled);
        Assert.Equal(0, item.RangeSegmentLeft);
        Assert.Equal(MediaRangeTimelinePresentation.Width, item.RangeSegmentWidth);
    }

    private static PremiereSource Source(string fileName, MediaRange? range = null)
    {
        PremiereRangeProjection? projection = null;
        if (range is not null) Assert.True(PremiereRangeProjection.TryCreate(range, out projection));
        return new PremiereSource(Guid.NewGuid(), Path.Combine(Path.GetTempPath(), fileName), "1", "1", projection);
    }
}
