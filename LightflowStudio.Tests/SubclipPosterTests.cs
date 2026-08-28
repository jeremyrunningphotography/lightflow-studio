namespace LightflowStudio.Tests;

using Xunit;

public sealed class SubclipPosterTests
{
    [Fact]
    public void CacheIdentity_TracksSourceAndAuthoritativeInButNotRenameOrderOrRevision()
    {
        var now = DateTimeOffset.UtcNow;
        var asset = new MediaAsset(Guid.NewGuid(), Guid.NewGuid(), "clip.mp4", "CLIP.MP4", "video", 1234, 5678,
            new(1, "fingerprint-a"), MediaAssetSourceStatus.Available, now, now, now);
        var subclip = new Subclip(Guid.NewGuid(), asset.AssetId, "First", 0, TimeSpan.FromTicks(123456789),
            TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60), 1, now, now);
        var identity = SubclipPosterService.CacheIdentity(asset, subclip);

        Assert.Equal(identity, SubclipPosterService.CacheIdentity(asset,
            subclip with { Name = "Renamed", Ordinal = 4, Revision = 9 }));
        Assert.NotEqual(identity, SubclipPosterService.CacheIdentity(asset,
            subclip with { In = subclip.In + TimeSpan.FromTicks(1) }));
        Assert.NotEqual(identity, SubclipPosterService.CacheIdentity(
            asset with { Fingerprint = new(1, "fingerprint-b") }, subclip));
    }

    [Fact]
    public void PanelPresentation_UsesExactStoredTimestampsAndStableIdentity()
    {
        var now = DateTimeOffset.UtcNow;
        var subclip = new Subclip(Guid.NewGuid(), Guid.NewGuid(), "Take", 2,
            TimeSpan.FromMilliseconds(1234), TimeSpan.FromMilliseconds(3456), TimeSpan.FromMinutes(1), 7, now, now);
        var item = new SubclipPanelItem(subclip);

        Assert.Equal(subclip.SubclipId, item.SubclipId);
        Assert.Equal("00:00:01.234 – 00:00:03.456", item.RangeSummary);
        Assert.Equal("00:00:02.222 duration", item.DurationSummary);
    }
}
