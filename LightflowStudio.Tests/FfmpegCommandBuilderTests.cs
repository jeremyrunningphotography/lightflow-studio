using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class FfmpegCommandBuilderTests
{
    [Fact]
    public void Encode_NormalModeCopiesOptionalAudioAndEscapesLutPath()
    {
        var args = FfmpegCommandBuilder.Encode("input.mov", "output.mp4", @"C:\LUT's\Film.cube", RecoveryStrategy.Normal, OutputResolution.FullHd);

        AssertContainsSequence(args, "-map", "0:a?");
        AssertContainsSequence(args, "-c:a", "copy");
        AssertContainsSequence(args, "-vf", "lut3d=file='C\\:/LUT\\'s/Film.cube',scale=-2:1080");
        Assert.Equal("output.mp4", args[^1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Encode_OmitsLutFilterWhenNoLutIsSelected(string? lut)
    {
        var args = FfmpegCommandBuilder.Encode("input.mov", "output.mp4", lut, RecoveryStrategy.Normal, OutputResolution.FullHd);

        AssertContainsSequence(args, "-vf", "scale=-2:1080");
        Assert.DoesNotContain(args, arg => arg.Contains("lut3d"));
    }

    [Fact]
    public void Encode_OmitsVfEntirelyWhenNoLutAndNoOtherFiltersApply()
    {
        var args = FfmpegCommandBuilder.Encode("input.mov", "output.mp4", null, RecoveryStrategy.Normal, OutputResolution.Source);

        Assert.DoesNotContain("-vf", args);
    }

    [Fact]
    public void Encode_SalvageModeUsesRecoveryFlagsAndAacResampling()
    {
        var args = FfmpegCommandBuilder.Encode("in", "out", "lut", RecoveryStrategy.Salvage, OutputResolution.Source);

        AssertContainsSequence(args, "-fflags", "+discardcorrupt+genpts", "-err_detect", "ignore_err");
        AssertContainsSequence(args, "-map", "0:a:0?");
        AssertContainsSequence(args, "-c:a", "aac", "-b:a", "192k", "-af", "aresample=async=1:first_pts=0");
        AssertContainsSequence(args, "-vf", "lut3d=file='lut'");
    }

    [Fact]
    public void Encode_VideoOnlyModeOmitsAudioMappingAndDisablesAudio()
    {
        var args = FfmpegCommandBuilder.Encode("in", "out", "lut", RecoveryStrategy.VideoOnly, OutputResolution.UltraHd);

        Assert.DoesNotContain("0:a?", args);
        Assert.DoesNotContain("0:a:0?", args);
        Assert.Contains("-an", args);
        Assert.Contains("lut3d=file='lut',scale=3840:2160:force_original_aspect_ratio=decrease,pad=3840:2160:(ow-iw)/2:(oh-ih)/2", args);
    }

    [Theory]
    [InlineData(OutputResolution.Sd480, "scale=-2:480")]
    [InlineData(OutputResolution.Hd720, "scale=-2:720")]
    [InlineData(OutputResolution.Qhd1440, "scale=-2:1440")]
    internal void Encode_AppliesScaleFilterForEveryAspectPreservingResolution(OutputResolution resolution, string expectedScale)
    {
        var args = FfmpegCommandBuilder.Encode("in", "out", "lut", RecoveryStrategy.Normal, resolution);

        AssertContainsSequence(args, "-vf", $"lut3d=file='lut',{expectedScale}");
    }

    [Fact]
    public void Encode_RejectsUnknownResolution()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegCommandBuilder.Encode("in", "out", "lut", RecoveryStrategy.Normal, (OutputResolution)99));
    }

    [Fact]
    public void Encode_DetailedOutputRequestsVerboseFfmpegMessages()
    {
        var args = FfmpegCommandBuilder.Encode("in", "out", "lut", RecoveryStrategy.Normal, OutputResolution.Source, detailedOutput: true);

        AssertContainsSequence(args, "-loglevel", "verbose");
    }

    [Fact]
    public void Encode_NormalOutputUsesStandardFfmpegMessages()
    {
        var args = FfmpegCommandBuilder.Encode("in", "out", "lut", RecoveryStrategy.Normal, OutputResolution.Source);

        AssertContainsSequence(args, "-loglevel", "info");
    }

    [Theory]
    [InlineData(OutputContainer.Mp4, "mp4")]
    [InlineData(OutputContainer.Mov, "mov")]
    [InlineData(OutputContainer.Mkv, "matroska")]
    internal void Encode_ExplicitlySelectsMuxerForLightflowPartialPath(OutputContainer container, string muxer)
    {
        var options = EncodingPresetCatalog.Recommended with { Container = container };
        var output = $"output{EncodingPathPlanner.ContainerExtension(container)}.lightflow";

        var args = FfmpegCommandBuilder.Encode("input.mov", output, null, RecoveryStrategy.Normal,
            OutputResolution.Source, encoding: options);

        AssertContainsSequence(args, "-f", muxer, output);
        Assert.Equal(output, args[^1]);
    }

    [Fact]
    public void Encode_HevcTenBitUsesSelectedPresetTuneAndMultipass()
    {
        var options = EncodingPresetCatalog.Get(EncodingPreset.MaximumQuality) with { Tune = EncoderTune.LowLatency, Container = OutputContainer.Mkv };

        var args = FfmpegCommandBuilder.Encode("in", "out.mkv", "lut", RecoveryStrategy.Normal,
            OutputResolution.Source, encoding: options);

        AssertContainsSequence(args, "-c:v", "hevc_nvenc");
        AssertContainsSequence(args, "-preset", "p7", "-tune", "ll");
        AssertContainsSequence(args, "-multipass", "fullres");
        AssertContainsSequence(args, "-pix_fmt", "p010le");
        Assert.DoesNotContain("+faststart", args);
    }

    [Fact]
    public void Encode_HevcMp4UsesAppleCompatibleSampleEntryWithoutTimecodeTrack()
    {
        var options = EncodingPresetCatalog.Get(EncodingPreset.EfficientHevc) with
        {
            Container = OutputContainer.Mp4
        };

        var args = FfmpegCommandBuilder.Encode("in.mov", "out.mp4.lightflow", null,
            RecoveryStrategy.Normal, OutputResolution.Source, encoding: options);

        AssertContainsSequence(args, "-c:v", "hevc_nvenc");
        AssertContainsSequence(args, "-tag:v", "hvc1");
        AssertContainsSequence(args, "-write_tmcd", "0");
        AssertContainsSequence(args, "-f", "mp4", "out.mp4.lightflow");
    }

    [Theory]
    [InlineData(VideoCodec.H264, OutputContainer.Mp4)]
    [InlineData(VideoCodec.Hevc, OutputContainer.Mov)]
    [InlineData(VideoCodec.Hevc, OutputContainer.Mkv)]
    internal void Encode_NonHevcMp4OutputsDoNotReceiveAppleMp4CompatibilityArguments(
        VideoCodec codec, OutputContainer container)
    {
        var options = EncodingPresetCatalog.Recommended with { Codec = codec, Container = container };

        var args = FfmpegCommandBuilder.Encode("in.mov", "out.lightflow", null,
            RecoveryStrategy.Normal, OutputResolution.Source, encoding: options);

        Assert.DoesNotContain("-tag:v", args);
        Assert.DoesNotContain("hvc1", args);
        Assert.DoesNotContain("-write_tmcd", args);
    }

    [Fact]
    public void Encode_VariableBitrateAddsTargetMaximumAndBuffer()
    {
        var options = EncodingPresetCatalog.Recommended with
        {
            RateControl = RateControlMode.VariableBitrate,
            TargetBitrateMbps = 30,
            MaxBitrateMbps = 60
        };

        var args = FfmpegCommandBuilder.Encode("in", "out.mp4", "lut", RecoveryStrategy.Normal,
            OutputResolution.Source, encoding: options);

        AssertContainsSequence(args, "-rc", "vbr", "-b:v", "30M", "-maxrate", "60M", "-bufsize", "120M");
        Assert.DoesNotContain("-cq", args);
    }

    [Fact]
    public void Encode_AdvancedFiltersAndAacOptionsAreApplied()
    {
        var options = EncodingPresetCatalog.Recommended with
        {
            Deinterlace = true,
            FrameRate = 29.97,
            AudioMode = AudioEncodingMode.Aac,
            AudioBitrateKbps = 256,
            AudioSampleRate = 48000,
            AudioChannels = 2
        };

        var args = FfmpegCommandBuilder.Encode("in", "out.mov", "lut", RecoveryStrategy.Normal,
            OutputResolution.FullHd, encoding: options);

        AssertContainsSequence(args, "-vf", "lut3d=file='lut',bwdif,fps=29.97,scale=-2:1080");
        AssertContainsSequence(args, "-c:a", "aac", "-b:a", "256k", "-ar", "48000", "-ac", "2");
        AssertContainsSequence(args, "-movflags", "+faststart");
    }

    [Fact]
    public void Encode_RejectsInvalidOptionCombination()
    {
        var invalid = EncodingPresetCatalog.Recommended with { PixelFormat = VideoPixelFormat.P010 };

        Assert.Throws<ArgumentException>(() => FfmpegCommandBuilder.Encode("in", "out", "lut",
            RecoveryStrategy.Normal, OutputResolution.Source, encoding: invalid));
    }
    [Fact]
    public void ProbeAndInspectArgumentsTargetProvidedFile()
    {
        Assert.Equal(["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", "clip.mov"], FfmpegCommandBuilder.ProbeDuration("clip.mov"));
        Assert.Equal(["-v", "error", "-show_entries", "format=format_name,duration,start_time:stream=codec_type,codec_name,width,height,avg_frame_rate,start_time,duration,sample_rate,channels,channel_layout", "-of", "json", "clip.mov"], FfmpegCommandBuilder.ProbeMetadata("clip.mov"));
        var derived = FfmpegCommandBuilder.ProbeDerivedMetadata("clip.mov");
        Assert.Contains("format=format_name,format_long_name,duration,start_time,size,bit_rate,tags:stream=index,codec_type,codec_name,codec_long_name,profile,width,height,pix_fmt,bits_per_raw_sample,color_space,color_transfer,color_primaries,avg_frame_rate,r_frame_rate,duration,start_time,sample_rate,channels,channel_layout,bit_rate,tags", derived);
        Assert.Equal("clip.mov", derived[^1]);
        var thumbnail = FfmpegCommandBuilder.ExtractThumbnail("clip.mov", TimeSpan.FromSeconds(12.5), 512, "thumb.tmp");
        AssertContainsSequence(thumbnail, "-ss", "12.5", "-i", "clip.mov");
        AssertContainsSequence(thumbnail, "-vf", "scale=512:512:force_original_aspect_ratio=decrease");
        AssertContainsSequence(thumbnail, "-c:v", "mjpeg", "-q:v", "3", "-f", "image2", "thumb.tmp");
        Assert.Equal(["-hide_banner", "-show_format", "-show_streams", "clip.mov"], FfmpegCommandBuilder.Inspect("clip.mov"));
        Assert.Equal(["-v", "error", "-select_streams", "v:0", "-read_intervals", "7%10", "-show_entries", "frame=best_effort_timestamp_time", "-of", "json", "clip.mov"],
            FfmpegCommandBuilder.ProbeVideoFrames("clip.mov", new MediaRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(9))));
        Assert.Equal(["-v", "error", "-select_streams", "v:0", "-read_intervals", "7%10", "-show_packets", "-show_entries", "packet=pts_time", "-of", "json", "clip.mov"],
            FfmpegCommandBuilder.ProbeVideoPackets("clip.mov", new MediaRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(9))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Trim_UsesAccurateAbsoluteSeekExclusiveDurationAndAlignedAudio(int recoveryValue)
    {
        var recovery = (RecoveryStrategy)recoveryValue;
        var requested = new MediaRange(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
        var trim = new ResolvedMediaRange(requested, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(9.04), TimeSpan.FromSeconds(2.04));
        var args = FfmpegCommandBuilder.Encode("in", "out", null, recovery, OutputResolution.Source,
            encoding: EncodingPresetCatalog.Recommended, trim: trim);

        AssertContainsSequence(args, "-ss", "2", "-copyts", "-i", "in");
        Assert.Contains("-copyts", args);
        var videoFilter = args[args.IndexOf("-vf") + 1];
        Assert.Contains("trim=start=7:end=", videoFilter);
        Assert.Contains("setpts=PTS-STARTPTS", videoFilter);
        if (recovery == RecoveryStrategy.VideoOnly) Assert.Contains("-an", args);
        else
        {
            AssertContainsSequence(args, "-c:a", "aac");
            Assert.Contains(args, argument => argument.Contains("atrim=start=7:end=") && argument.Contains("asetpts=PTS-STARTPTS"));
        }
    }

    [Fact]
    public void VerifyAndRewrapArgumentsPreserveExpectedOperations()
    {
        Assert.Equal(["-v", "warning", "-i", "in", "-map", "0:v:0", "-f", "null", "NUL"], FfmpegCommandBuilder.Verify("in"));
        Assert.Equal(["-hide_banner", "-y", "-i", "in", "-map", "0", "-c", "copy", "-movflags", "+faststart", "out"], FfmpegCommandBuilder.Rewrap("in", "out"));
    }

    [Fact]
    public void ProxyAndContactSheetArgumentsPreserveOutputsAndFilters()
    {
        var proxy = FfmpegCommandBuilder.Proxy("in", "proxy.mp4");
        AssertContainsSequence(proxy, "-vf", "scale=-2:1080");
        Assert.Equal("proxy.mp4", proxy[^1]);
        var sheet = FfmpegCommandBuilder.ContactSheet("in", "sheet.jpg");
        AssertContainsSequence(sheet, "-vf", "fps=1/10,scale=480:-1,tile=4x4:padding=8:margin=8");
        Assert.Equal("sheet.jpg", sheet[^1]);
    }

    private static void AssertContainsSequence(IReadOnlyList<string> actual, params string[] expected)
    {
        for (var index = 0; index <= actual.Count - expected.Length; index++)
            if (actual.Skip(index).Take(expected.Length).SequenceEqual(expected)) return;
        Assert.Fail($"Expected sequence was not found: {string.Join(" ", expected)}");
    }
}
