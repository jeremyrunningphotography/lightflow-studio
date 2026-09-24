using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserFilterAuthoringTests
{
    [Fact]
    public void DurationUpperLimitAndUnsetLabelRoundTripAsSemanticPredicates()
    {
        var maximum = BrowserStructuredFilterInput.Duration.Parse(["00:30"]) with { Comparison = BrowserNumberComparison.LessThanOrEqual };
        var unset = BrowserFilterPredicate.ForUnsetColorLabel();
        var red = BrowserFilterPredicate.ForText(BrowserFilterField.ColorLabel, "Red");
        var intent = BrowserQueryIntent.Deserialize(new BrowserQueryIntent { Filters = [maximum, unset, red] }.Serialize());
        var tile = Tile(); tile.ApplyMetadata(null, 30); tile.SetAssetState(BrowserAssetState.None);
        Assert.Single(BrowserQueryEngine.Filter([tile], intent.ToQuery()));
        tile.ApplyMetadata(null, 30.001); Assert.Empty(BrowserQueryEngine.Filter([tile], intent.ToQuery()));
        tile.ApplyMetadata(null, 29.999); Assert.Single(BrowserQueryEngine.Filter([tile], intent.ToQuery()));
        Assert.True(unset.MatchUnset); Assert.Null(unset.TextValue);
        Assert.Equal("Not set", BrowserFilterDescriptors.ValueLabel(unset));
        Assert.Equal(3, intent.Version);
        Assert.Throws<ArgumentException>(() => new BrowserQueryIntent { Filters = [maximum with { Comparison = BrowserNumberComparison.Equal }] }.Serialize());
        Assert.Throws<ArgumentException>(() => new BrowserQueryIntent { Filters = [unset with { Field = BrowserFilterField.Camera }] }.Serialize());
        Assert.All(BrowserFilterDescriptors.All.Where(d => d.Editor == BrowserFilterEditorKind.State), d => Assert.Equal(2, d.StateOperators.Length));
    }

    [Fact]
    public void ResolutionPresetsAreIndependentOfCurrentSourceAndRemainNumeric()
    {
        var descriptor = BrowserFilterDescriptors.Get(BrowserFilterField.Resolution);
        var presets = descriptor.Suggestions!([]).ToArray();
        Assert.Contains(BrowserFilterPredicate.ForResolution(1280, 720), presets);
        Assert.Contains(BrowserFilterPredicate.ForResolution(1920, 1080), presets);
        Assert.Contains(BrowserFilterPredicate.ForResolution(2560, 1440), presets);
        Assert.Contains(BrowserFilterPredicate.ForResolution(3840, 2160), presets);
        Assert.Contains(BrowserFilterPredicate.ForResolution(4096, 2160), presets);
        Assert.All(presets, p => Assert.Null(p.TextValue));
    }

    [Fact]
    public void EmptySourceStillSupportsStructuredAuthoringButNotEmptyObservedFacets()
    {
        var excluded = BrowserFilterDescriptors.All.Where(d => !d.CanAuthor([])).Select(d => d.Field).ToArray();
        Assert.Equal([BrowserFilterField.Camera, BrowserFilterField.Lens, BrowserFilterField.Keyword], excluded);
        Assert.Equal(17, BrowserFilterDescriptors.All.Count);
        Assert.Empty(BrowserFilterDescriptors.Values(BrowserFilterField.FrameRate, [])); // ordinary Browser remains contextual
        var rates = BrowserFilterDescriptors.Get(BrowserFilterField.FrameRate).Suggestions!([]).ToArray();
        Assert.Equal(MediaFrameRate.Canonical.Select(r => r.DisplayValue), rates.Select(p => p.NumberValue!.Value));
    }

    [Fact]
    public void ResolutionDefinitionCanPrecedeItsFirstMatchingAsset()
    {
        var filter = BrowserStructuredFilterInput.Resolution.Parse(["3840", "2160"]);
        var intent = BrowserQueryIntent.Deserialize(new BrowserQueryIntent { Filters = [filter] }.Serialize());
        Assert.Empty(BrowserQueryEngine.Filter([], intent.ToQuery()));
        var tile = Tile(); tile.ApplyMetadata(new(null, null, null, null, null, 3840, 2160, null));
        Assert.Single(BrowserQueryEngine.Filter([tile], intent.ToQuery()));
        tile.ApplyMetadata(new(null, null, null, null, null, 2160, 3840, null));
        Assert.Empty(BrowserQueryEngine.Filter([tile], intent.ToQuery()));
        Assert.Throws<FormatException>(() => BrowserStructuredFilterInput.Resolution.Parse(["3840.5", "2160"]));
        Assert.Throws<FormatException>(() => BrowserStructuredFilterInput.Resolution.Parse(["0", "2160"]));
    }

    [Theory]
    [InlineData("30", 30)]
    [InlineData("00:30", 30)]
    [InlineData("1:02:03", 3723)]
    [InlineData("00:00", 0)]
    public void DurationUsesInclusiveMinimumSeconds(string text, double seconds)
    {
        var input = BrowserStructuredFilterInput.Duration;
        var filter = input.Parse([text]);
        Assert.Equal(seconds, filter.NumberValue);
        Assert.Equal("is at least", input.Operator);
        var tile = Tile(); tile.ApplyMetadata(null, seconds);
        Assert.True(filter.Matches(tile));
        tile.ApplyMetadata(null, seconds - 0.001); Assert.False(filter.Matches(tile));
        Assert.Equal(filter, input.Parse(input.Format(filter)));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1:60")]
    [InlineData("1:2:3:4")]
    [InlineData("NaN")]
    public void DurationRejectsInvalidTime(string text) => Assert.Throws<FormatException>(() => BrowserStructuredFilterInput.Duration.Parse([text]));

    [Fact]
    public void FrameRateUsesSharedNormalizationForCanonicalAndCustomValues()
    {
        var input = BrowserStructuredFilterInput.FrameRate;
        var canonical = input.Parse(["29.97002997"]);
        Assert.Equal(29.97, canonical.NumberValue);
        var custom = input.Parse(["120"]);
        var tile = Tile(); tile.ApplyMetadata(new(null, null, null, null, null, null, null, 120));
        Assert.True(custom.Matches(tile)); Assert.False(canonical.Matches(tile));
        Assert.Throws<FormatException>(() => input.Parse(["0"]));
        Assert.Throws<FormatException>(() => input.Parse(["0.0001"]));
    }

    [Fact]
    public void InOutStateUsesSavedRangePresenceAndKeepsPendingStateUnknown()
    {
        var descriptor = BrowserFilterDescriptors.Get(BrowserFilterField.ReviewRangeState);
        Assert.Equal("In/Out Range", descriptor.Name); Assert.Equal("is set", descriptor.StateOperators[0]);
        var set = BrowserFilterPredicate.ForState(descriptor.Field, true);
        var unset = BrowserFilterPredicate.ForState(descriptor.Field, false);
        var tile = Tile(); Assert.False(set.Matches(tile)); Assert.False(unset.Matches(tile));
        tile.SetAssetState(BrowserAssetState.ReviewRange); Assert.True(set.Matches(tile)); Assert.False(unset.Matches(tile));
        tile.SetAssetState(BrowserAssetState.None); Assert.False(set.Matches(tile)); Assert.True(unset.Matches(tile));
    }
    private static BrowserGridTile Tile() => new(new(Guid.NewGuid(), "test.mp4", "TEST.MP4", "test.mp4", false,
        new(MediaTypeCategory.Video), 10, DateTimeOffset.UtcNow), 0);
}
