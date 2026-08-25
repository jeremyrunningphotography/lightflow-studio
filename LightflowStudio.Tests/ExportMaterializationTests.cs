using System.Text.Json;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExportMaterializationTests
{
    [Fact]
    public void SameAsSource_MaterializesHeterogeneousCodecContainerAndAudioPerItem()
    {
        var options = Options(new(VideoCodecPolicy.SameAsSource, OutputContainerPolicy.SameAsSource,
            SourceAudioFallback: new(224)));
        var sources = new[]
        {
            Source("a.mp4", new("h264", 1920, 1080, 24, "mp4", "aac", 44100, 2, "stereo")),
            Source("b.mov", new("hevc", 3840, 2160, 60, "mov", "pcm_s24le", 48000, 6, "5.1"))
        };

        var definition = EncodingJobPlanner.Define(options, sources);
        var first = definition.Items[0].MaterializedExport!;
        var second = definition.Items[1].MaterializedExport!;

        Assert.Equal((VideoCodec.H264, OutputContainer.Mp4, 24d, 44100, 2),
            (first.Encoding.Codec, first.Encoding.Container, first.SourceTraits!.FrameRate,
                first.Audio.Fallback!.SampleRate, first.Audio.Fallback.Channels));
        Assert.Equal((VideoCodec.Hevc, OutputContainer.Mov, 60d, 48000, 6),
            (second.Encoding.Codec, second.Encoding.Container, second.SourceTraits!.FrameRate,
                second.Audio.Fallback!.SampleRate, second.Audio.Fallback.Channels));
        Assert.Equal(0, first.Encoding.FrameRate);
        var plan = EncodingJobPlanner.Plan(definition, _ => new(false, 0));
        Assert.EndsWith(".mp4", plan.Items[0].OutputPaths.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".mov", plan.Items[1].OutputPaths.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EncodingQualityPolicy.Automatic, first.QualityPolicy);
        Assert.Equal(RateControlMode.ConstantQuality, first.Encoding.RateControl);
    }

    [Theory]
    [InlineData("vp9", "mp4")]
    [InlineData("h264", "avi")]
    public void UnsupportedSameAsSource_IsAnExplicitPreflightError(string codec, string container)
    {
        var definition = EncodingJobPlanner.Define(
            Options(new(VideoCodecPolicy.SameAsSource, OutputContainerPolicy.SameAsSource)),
            [Source("clip.any", new(codec, 1280, 720, 30, container))]);
        var plan = EncodingJobPlanner.Plan(definition, _ => new(false, 0));
        Assert.False(plan.IsValid);
        Assert.Contains(plan.Items.Single().Issues, issue => issue.Code == "encoding.materialization-unsupported");
    }

    [Fact]
    public void SourceResolutionAndCadence_AddNoConversionFilters()
    {
        var args = FfmpegCommandBuilder.Encode("in", "out", null, RecoveryStrategy.Normal,
            OutputResolution.Source, encoding: new EncodingOptions { FrameRate = 0 });
        Assert.DoesNotContain("scale", string.Join(' ', args));
        Assert.DoesNotContain("fps=", string.Join(' ', args));
    }

    [Fact]
    public void SourceAudio_UsesCopyUntrimmedAndMaterializedFallbackWhenTrimmed()
    {
        var encoding = new EncodingOptions { AudioMode = AudioEncodingMode.Copy, AudioBitrateKbps = 224, AudioSampleRate = 48000, AudioChannels = 6 };
        var copy = FfmpegCommandBuilder.Encode("in", "out", null, RecoveryStrategy.Normal, OutputResolution.Source, encoding: encoding);
        Assert.Equal("copy", ValueAfter(copy, "-c:a"));
        var range = new MediaRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9));
        var resolved = new ResolvedMediaRange(range, TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(8));
        var fallback = FfmpegCommandBuilder.Encode("in", "out", null, RecoveryStrategy.Normal, OutputResolution.Source, encoding: encoding, trim: resolved);
        Assert.Equal("aac", ValueAfter(fallback, "-c:a"));
        Assert.Equal("48000", ValueAfter(fallback, "-ar"));
        Assert.Equal("6", ValueAfter(fallback, "-ac"));
    }

    [Fact]
    public void ColorPolicies_AreIndependentAndDoNotMutateSelectedPipeline()
    {
        var camera = Lut(ColorLutStage.Camera, "camera");
        var creative = Lut(ColorLutStage.Creative, "creative");
        var overrideCreative = Lut(ColorLutStage.Creative, "override");
        var selected = new MaterializedColorPipeline(true, camera, creative);
        var policy = new ExportMaterializationPolicy(Camera: new(ColorStagePolicyMode.NoLut),
            Creative: new(ColorStagePolicyMode.Override, overrideCreative));
        var materialized = ExportSettingsMaterializer.Materialize(Options(policy), Source("a.mov",
            new("h264", 1, 1, 24, "mov"), selected));
        Assert.Null(materialized.Color!.Camera);
        Assert.Equal(overrideCreative, materialized.Color.Creative);
        Assert.Equal(camera, selected.Camera);
        Assert.Equal(creative, selected.Creative);
    }

    [Fact]
    public void ColorPolicies_BothNoLut_MaterializeOriginalFromActiveSource()
    {
        var selected = new MaterializedColorPipeline(true,
            Lut(ColorLutStage.Camera, "camera"), Lut(ColorLutStage.Creative, "creative"));
        var policy = new ExportMaterializationPolicy(
            Camera: new(ColorStagePolicyMode.NoLut), Creative: new(ColorStagePolicyMode.NoLut));

        var color = MaterializeColor(policy, selected);

        Assert.Null(color.Camera);
        Assert.Null(color.Creative);
        Assert.False(color.ColorEnabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ColorPolicies_OneSelectedStageRemaining_IsActive(bool retainCamera)
    {
        var retainedStage = retainCamera ? ColorLutStage.Camera : ColorLutStage.Creative;
        var camera = Lut(ColorLutStage.Camera, "camera");
        var creative = Lut(ColorLutStage.Creative, "creative");
        var selected = new MaterializedColorPipeline(true, camera, creative);
        var policy = retainedStage == ColorLutStage.Camera
            ? new ExportMaterializationPolicy(Camera: new(), Creative: new(ColorStagePolicyMode.NoLut))
            : new ExportMaterializationPolicy(Camera: new(ColorStagePolicyMode.NoLut), Creative: new());

        var color = MaterializeColor(policy, selected);

        Assert.Equal(retainedStage == ColorLutStage.Camera ? camera : null, color.Camera);
        Assert.Equal(retainedStage == ColorLutStage.Creative ? creative : null, color.Creative);
        Assert.True(color.ColorEnabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ColorPolicies_OneOverrideOnUnassignedSource_IsActive(bool overrideCamera)
    {
        var overrideStage = overrideCamera ? ColorLutStage.Camera : ColorLutStage.Creative;
        var resource = Lut(overrideStage, "override");
        var policy = overrideStage == ColorLutStage.Camera
            ? new ExportMaterializationPolicy(Camera: new(ColorStagePolicyMode.Override, resource), Creative: new(ColorStagePolicyMode.NoLut))
            : new ExportMaterializationPolicy(Camera: new(ColorStagePolicyMode.NoLut), Creative: new(ColorStagePolicyMode.Override, resource));

        var color = MaterializeColor(policy, null);

        Assert.Equal(overrideStage == ColorLutStage.Camera ? resource : null, color.Camera);
        Assert.Equal(overrideStage == ColorLutStage.Creative ? resource : null, color.Creative);
        Assert.True(color.ColorEnabled);
    }

    [Fact]
    public void CorrectedOriginalColor_RoundTripsThroughHistoryIntentAndOutputIdentity()
    {
        var options = Options(new(Camera: new(ColorStagePolicyMode.NoLut), Creative: new(ColorStagePolicyMode.NoLut)));
        var selected = new MaterializedColorPipeline(true,
            Lut(ColorLutStage.Camera, "camera"), Lut(ColorLutStage.Creative, "creative"));
        var item = EncodingJobPlanner.Define(options,
            [Source("color.mov", new("h264", 1, 1, 24, "mov"), selected)]).Items.Single();
        var identity = EncodingOutputIdentity.Create(item, options);

        var restored = JsonSerializer.Deserialize<JobItemDefinition>(JsonSerializer.Serialize(item))!;

        Assert.False(restored.MaterializedExport!.Color!.ColorEnabled);
        Assert.Empty(restored.MaterializedExport.Color.OrderedPipeline);
        Assert.Equal(identity, EncodingOutputIdentity.Create(restored, options));
    }

    [Fact]
    public void MaterializedIntent_RoundTripsAndChangesOutputIdentity()
    {
        var options = Options(new(VideoCodecPolicy.SameAsSource, OutputContainerPolicy.SameAsSource));
        var h264 = EncodingJobPlanner.Define(options, [Source("a.mp4", new("h264", 1, 1, 24, "mp4"))]).Items.Single();
        var hevc = EncodingJobPlanner.Define(options, [Source("a.mp4", new("hevc", 1, 1, 24, "mp4"))]).Items.Single();
        var json = JsonSerializer.Serialize(h264);
        Assert.Equal(h264.MaterializedExport, JsonSerializer.Deserialize<JobItemDefinition>(json)!.MaterializedExport);
        Assert.NotEqual(EncodingOutputIdentity.Create(h264, options).OptionsHash,
            EncodingOutputIdentity.Create(hevc, options).OptionsHash);
    }

    [Fact]
    public async Task EncoderCapabilities_DistinguishAndCacheAvailability()
    {
        var probe = new FakeProbe();
        var service = new EncoderCapabilityService("ffmpeg", probe);
        var first = await service.GetAsync();
        var second = await service.GetAsync();
        Assert.Same(first, second);
        Assert.Equal(2, probe.Calls);
        Assert.Equal(EncoderCapabilityState.ImplementedAndAvailable,
            first.Single(value => value.Backend == EncoderBackend.NvidiaNvenc).State);
        Assert.All(first.Where(value => value.Backend != EncoderBackend.NvidiaNvenc),
            value => Assert.Equal(EncoderCapabilityState.NotImplemented, value.State));
    }

    private static EncodingJobOptions Options(ExportMaterializationPolicy policy) => new(
        ".", ".", OutputResolution.Source, RecoveryStrategy.Normal,
        new EncodingOptions { AudioMode = AudioEncodingMode.Copy }, null, "_Source", true, false, false,
        MaterializationPolicy: policy);

    private static EncodingSource Source(string path, SourceMediaTraits traits, MaterializedColorPipeline? color = null) =>
        new(path, 1, TimeSpan.FromSeconds(1), AssignedColor: color, MediaTraits: traits);

    private static MaterializedColorPipeline MaterializeColor(ExportMaterializationPolicy policy,
        MaterializedColorPipeline? selected) => ExportSettingsMaterializer.Materialize(Options(policy),
        Source("color.mov", new("h264", 1, 1, 24, "mov"), selected)).Color!;

    private static MaterializedLutResource Lut(ColorLutStage stage, string name) =>
        new(Guid.NewGuid(), stage, name, new string('a', 64), $"aa/{name}.cube");

    private static string ValueAfter(IReadOnlyList<string> args, string option) => args[args.IndexOf(option) + 1];

    private sealed class FakeProbe : IEncoderCapabilityProbe
    {
        public int Calls { get; private set; }
        public Task<(bool Available, string Diagnostic)> ProbeAsync(string ffmpeg, string encoder, CancellationToken token)
        { Calls++; return Task.FromResult((true, "ok")); }
    }
}

file static class ListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++) if (values[index] == value) return index;
        return -1;
    }
}
