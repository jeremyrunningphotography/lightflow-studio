using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public class MediaMetadataTests
{
    private const string ProbeJson = """
        {
          "streams": [
            { "codec_type": "video", "codec_name": "hevc", "width": 3840, "height": 2160, "avg_frame_rate": "60000/1001", "start_time": "3.25", "duration": "134.5" },
            { "codec_type": "audio", "codec_name": "aac" }
          ],
          "format": { "start_time": "3.25", "duration": "137.75" }
        }
        """;

    [Fact]
    public void Parser_ReadsVideoAudioAndFormatDetails()
    {
        Assert.True(MediaMetadataParser.TryParse(ProbeJson, 4_294_967_296, out var metadata));
        Assert.Equal(3840, metadata.Width);
        Assert.Equal(2160, metadata.Height);
        Assert.Equal(59.94, metadata.FrameRate, 2);
        Assert.Equal(134.5, metadata.DurationSeconds);
        Assert.Equal("hevc", metadata.VideoCodec);
        Assert.True(metadata.HasAudio);
        Assert.Equal(TimeSpan.FromSeconds(3.25), metadata.StartTimestamp);
    }

    [Fact]
    public void Parser_RejectsInvalidJsonAndMissingVideo()
    {
        Assert.False(MediaMetadataParser.TryParse("not json", 0, out _));
        Assert.False(MediaMetadataParser.TryParse("""{"streams":[{"codec_type":"audio"}]}""", 0, out _));
    }

    [Fact]
    public void Presentation_ProducesCompactFriendlyDetails()
    {
        var metadata = new MediaMetadata(3840, 2160, 59.94, 134.5, 4_294_967_296, "hevc", true);
        Assert.Equal("3840×2160 · 59.94 fps · 2:14 · 4 GB", MediaMetadataPresentation.Details(metadata));
        Assert.Equal("HEVC video · Audio present", MediaMetadataPresentation.Tooltip(metadata));
        var option = Option("clip", metadata);
        Assert.Equal("3840×2160", option.ResolutionText);
        Assert.Equal("59.94 fps", option.FrameRateText);
        Assert.Equal("2:14", option.DurationText);
        Assert.Equal("4 GB", option.SizeText);
    }

    [Theory]
    [InlineData(23.976)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(29.97)]
    [InlineData(30)]
    [InlineData(50)]
    [InlineData(59.94)]
    [InlineData(60)]
    [InlineData(59.94006)]
    public void WarningAnalyzer_AcceptsStandardFrameRates(double frameRate)
    {
        Assert.True(MediaWarningAnalyzer.IsStandardFrameRate(frameRate));
    }

    [Fact]
    public void WarningAnalyzer_FlagsOnlyActionableMediaConditions()
    {
        var differentResolution = Option("different", new MediaMetadata(1920, 1080, 29.97, 60, 100, "h264", true));
        var nonStandard = Option("odd-fps", new MediaMetadata(3840, 2160, 27, 60, 100, "hevc", true));
        var noAudio = Option("silent", new MediaMetadata(3840, 2160, 60, 60, 100, "hevc", false));
        var both = Option("both", new MediaMetadata(3840, 2160, 27, 60, 100, "hevc", false));

        MediaWarningAnalyzer.Apply([differentResolution, nonStandard, noAudio, both]);

        Assert.False(differentResolution.HasWarning);
        Assert.Equal("NON-STANDARD FPS", nonStandard.WarningLabel);
        Assert.Contains("drop or duplicate frames", nonStandard.WarningTooltip);
        Assert.Contains("Exporting", nonStandard.WarningTooltip);
        Assert.DoesNotContain("Encoding", nonStandard.WarningTooltip);
        Assert.Equal("NO AUDIO", noAudio.WarningLabel);
        Assert.Equal("CHECK MEDIA", both.WarningLabel);
        Assert.DoesNotContain("differs", string.Join(" ", new[] { nonStandard.WarningTooltip, noAudio.WarningTooltip, both.WarningTooltip }), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Summary_IncludesSelectedDurationAndSizeAfterAnalysis()
    {
        var first = Option("first", new MediaMetadata(1920, 1080, 30, 60, 1024, "h264", true));
        var second = Option("second", new MediaMetadata(1920, 1080, 30, 90, 1024, "h264", true));
        second.IsSelected = false;
        Assert.Equal("1 of 2 selected · 1 KB · 1:00 total", BatchFileSelection.Summary([first, second]));
    }

    private static BatchFileOption Option(string name, MediaMetadata metadata)
    {
        var option = new BatchFileOption(name, name, metadata.FileSizeBytes);
        option.ApplyMetadata(metadata);
        return option;
    }
}
