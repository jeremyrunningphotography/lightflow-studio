using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class JobModelTests
{
    [Fact]
    public void Definitions_PreserveStableIdentityTypedOptionsAndItems()
    {
        var jobId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var created = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var item = new JobItemDefinition(itemId, "source.mov", 42);

        var definition = new JobDefinition<string>(jobId, "test.capability", created, "typed options", [item]);

        Assert.Equal(jobId, definition.Id);
        Assert.Equal("test.capability", definition.Capability);
        Assert.Equal("typed options", definition.Options);
        Assert.Equal(itemId, Assert.Single(definition.Items).Id);
    }

    [Fact]
    public void MediaRange_RepresentsEffectiveDurationIndependentlyFromSourceDuration()
    {
        var range = new MediaRange(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(45));

        Assert.Equal(TimeSpan.FromMinutes(30), range.SourceDuration);
        Assert.Equal(TimeSpan.FromSeconds(45), range.EffectiveDuration);
        Assert.False(range.IsFullSource);
        Assert.Empty(range.Validate());
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, 10)]
    [InlineData(20, 10)]
    public void MediaRange_RejectsInvalidOrEmptyBoundaries(double inSeconds, double outSeconds)
    {
        var range = new MediaRange(
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(inSeconds),
            TimeSpan.FromSeconds(outSeconds));

        Assert.Contains(range.Validate(), issue => issue.Severity == JobIssueSeverity.Error);
    }

    [Fact]
    public void WorkEstimate_CanBeDeterminateOrIndeterminateWithoutChangingUnits()
    {
        var determinate = JobWorkEstimate.Determinate(JobWorkUnit.Bytes, 1024);
        var indeterminate = JobWorkEstimate.Indeterminate(JobWorkUnit.Bytes);

        Assert.True(determinate.IsDeterminate);
        Assert.Equal(1024, determinate.Value);
        Assert.False(indeterminate.IsDeterminate);
        Assert.Equal(JobWorkUnit.Bytes, indeterminate.Unit);
    }

    [Fact]
    public void Export_item_provenance_snapshots_subclip_identity_without_changing_source_identity()
    {
        var assetId = Guid.NewGuid();
        var subclipId = Guid.NewGuid();
        var provenance = new ExportItemProvenance(ExportItemKind.Subclip, assetId, subclipId, "Interview answer", 9);
        var item = new JobItemDefinition(Guid.NewGuid(), "C:\\media\\CAM123.mov", ExportProvenance: provenance,
            MediaRange: new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(7)));

        Assert.Equal("C:\\media\\CAM123.mov", item.SourceIdentity);
        Assert.Equal((assetId, subclipId, "Interview answer", 9L),
            (item.ExportProvenance!.AssetId, item.ExportProvenance.SubclipId,
                item.ExportProvenance.SubclipName, item.ExportProvenance.SubclipRevision));
    }
}
