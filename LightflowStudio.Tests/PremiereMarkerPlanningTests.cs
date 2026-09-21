using System.Text.Json;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class PremiereMarkerPlanningTests
{
    private static TimelineMarker Marker(Guid asset, long ticks, string name = "") =>
        new(Guid.NewGuid(), asset, TimeSpan.FromTicks(ticks), name, 4, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    [Fact]
    public void SourceIncludesEveryMarkerWithExactIdentityRevisionOptionalNameAndTiming()
    {
        var asset = Guid.NewGuid();
        var markers = new[] { Marker(asset, 0), Marker(asset, 10000001, "awkward"), Marker(asset, long.MaxValue) };
        var planned = PremiereMarkerPlanning.Plan(markers.Append(Marker(Guid.NewGuid(), 10)), asset, "target");
        Assert.Equal(3, planned.Count);
        for (var i = 0; i < markers.Length; i++)
        {
            Assert.Equal(markers[i].MarkerId, planned[i].MarkerId);
            Assert.Equal(asset, planned[i].AssetId);
            Assert.Equal(4, planned[i].Revision);
            Assert.Equal(markers[i].Name, planned[i].Name);
            Assert.Equal(markers[i].Position.Ticks.ToString(), planned[i].SourcePositionTicks);
            Assert.Equal(planned[i].SourcePositionTicks, planned[i].PositionTicks);
            Assert.Equal(planned[i], JsonSerializer.Deserialize<PremiereMarkerProjection>(JsonSerializer.Serialize(planned[i], PremiereProtocol.Json), PremiereProtocol.Json));
        }
    }
    [Fact]
    public void HalfOpenSubclipsIncludeInExcludeOutAndKeepIndependentOverlappingTargets()
    {
        var asset = Guid.NewGuid();
        var markers = new[] { Marker(asset, 99), Marker(asset, 100), Marker(asset, 123), Marker(asset, 200) };
        var subclip = new PremiereSubclipProjection(Guid.NewGuid(), "First", 1, new("100", "200", "300"), "source");
        var first = PremiereMarkerPlanning.Plan(markers, asset, "first", subclip);
        Assert.Equal(new[] { markers[1].MarkerId, markers[2].MarkerId }, first.Select(m => m.MarkerId));
        Assert.Equal(new[] { "0", "23" }, first.Select(m => m.PositionTicks));
        var second = PremiereMarkerPlanning.Plan(markers, asset, "second", subclip with { SubclipId = Guid.NewGuid(), Range = new("110", "250", "300") });
        var shared = second.Single(m => m.MarkerId == markers[2].MarkerId);
        Assert.Equal("13", shared.PositionTicks);
        Assert.NotEqual(first[1].TargetKey, shared.TargetKey);
        var source = PremiereMarkerPlanning.Plan(markers, asset, "source");
        Assert.NotEqual(source[2].TargetKey, shared.TargetKey);
    }
    [Fact]
    public void CompleteVideoFallbackUsesAllSourceMarkers()
    {
        var asset = Guid.NewGuid();
        var marker = Marker(asset, 120000001);
        var result = Assert.Single(PremiereMarkerPlanning.Plan([marker], asset, "source",
            new(null, "Whole source", 1, null, "source", IsSourceFallback: true)));
        Assert.Null(result.SubclipId); Assert.Equal(result.SourcePositionTicks, result.PositionTicks);
    }
}
