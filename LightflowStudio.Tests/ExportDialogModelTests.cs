using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExportDialogModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-export-modal-").FullName;

    [Fact]
    public void Defaults_AreModernIndependentAndBounded()
    {
        var model = CreateModel("clip.mp4");
        Assert.Equal([NamePartKind.OriginalName], model.NameParts.Select(x => x.Kind));
        Assert.Equal(ExportContainerChoice.SameAsSource, model.Container);
        Assert.Equal(ExportCodecChoice.SameAsSource, model.Codec);
        Assert.Equal(ColorStagePolicyMode.AsSelectedInLightflow, model.Camera.Mode);
        Assert.Equal(ColorStagePolicyMode.AsSelectedInLightflow, model.Creative.Mode);
        Assert.Equal(AudioEncodingMode.Copy, model.Encoding.AudioMode);
        Assert.Equal(EncodingJobConcurrency.Default, model.ParallelExports);
        Assert.True(model.CreateSubfolder);
        Assert.Equal("Estimate unavailable", model.EstimateText);
    }

    [Fact]
    public void NamingPreview_ReorderSeparatorCustomAndFailuresUseSharedRenderer()
    {
        var model = CreateModel("clip42.mp4");
        Ready(model, Metadata("h264", "mp4"));
        model.AddPart(NamePartKind.CustomText);
        model.UpdateCustomText(1, "web");
        model.MovePart(1, -1);
        model.Separator = NamePartSeparator.Hyphen;
        Assert.Equal("web-clip42.mp4", model.PreviewName);

        model.NameParts.Clear(); model.AddPart(NamePartKind.Date);
        Assert.Contains("no explicit naming timestamp", model.PreviewName);
        Assert.False(model.CurrentPlan!.IsValid);
        model.NameParts.Clear(); model.AddPart(NamePartKind.IndexNumber);
        Assert.True(model.CurrentPlan!.IsValid);
    }

    [Fact]
    public void DestinationPolicyAndSameSourceMaterializeCompleteMixedBatch()
    {
        var model = CreateModel("a.mp4", "b.mov");
        model.SubfolderName = "Delivery";
        Ready(model, Metadata("h264", "mp4"), Metadata("hevc", "mov"));
        var plan = model.CurrentPlan!;
        Assert.True(plan.IsValid);
        Assert.Equal(["a.mp4", "b.mov"], plan.Items.Select(x => Path.GetFileName(x.OutputPaths.Single())));
        Assert.All(plan.Items, x => Assert.Contains(Path.Combine("Delivery", ""), x.OutputPaths.Single()));
        Assert.Equal([VideoCodec.H264, VideoCodec.Hevc], plan.Items.Select(x => x.Definition.MaterializedExport!.Encoding.Codec));
    }

    [Fact]
    public void ConditionalAuthorityAndPreflightGateFollowTypedOptions()
    {
        var model = CreateModel("clip.mp4");
        Ready(model, Metadata("h264", "mp4"));
        Assert.True(model.CanExport);
        model.Encoding = model.Encoding with { RateControl = RateControlMode.VariableBitrate, TargetBitrateMbps = 20, MaxBitrateMbps = 10 };
        Assert.False(model.QualityAuthoritative);
        Assert.True(model.TargetBitrateAuthoritative);
        Assert.True(model.MaxBitrateAuthoritative);
        Assert.False(model.CanExport);
        model.Encoding = model.Encoding with { RateControl = RateControlMode.ConstantBitrate, TargetBitrateMbps = 20, MaxBitrateMbps = 20, AudioMode = AudioEncodingMode.None };
        Assert.False(model.MaxBitrateAuthoritative);
        Assert.False(model.AudioDetailsAuthoritative);
    }

    [Fact]
    public void EncoderUnavailableDisablesExportAndWarningsDoNot()
    {
        var model = CreateModel("clip.mp4", inspect: _ => new(true, 10));
        model.OverwriteExisting = false;
        model.ApplyMetadata([Metadata("h264", "mp4")]);
        model.ApplyEncoderCapability(new(EncoderBackend.NvidiaNvenc, EncoderCapabilityState.ImplementedButUnavailable, "No NVIDIA encoder was found."));
        Assert.False(model.CanExport);
        Assert.Contains(model.Errors, x => x.Code == "export.encoder-unavailable");
        model.ApplyEncoderCapability(new(EncoderBackend.NvidiaNvenc, EncoderCapabilityState.ImplementedAndAvailable, "ok"));
        Assert.True(model.CanExport);
    }

    [Fact]
    public void ParallelExportBoundsAreClamped()
    {
        var model = CreateModel("clip.mp4");
        model.ParallelExports = 99; Assert.Equal(EncodingJobConcurrency.Maximum, model.ParallelExports);
        model.ParallelExports = -1; Assert.Equal(EncodingJobConcurrency.Minimum, model.ParallelExports);
    }

    [Fact]
    public void PresentationMappingsUseFriendlyProductLabels()
    {
        Assert.Equal("Original name", ExportPresentation.NamePartLabel(NamePartKind.OriginalName));
        Assert.Equal("Custom text", ExportPresentation.NamePartLabel(NamePartKind.CustomText));
        Assert.Equal("Sequence 0001", ExportPresentation.NamePartLabel(NamePartKind.Sequence0001));
        Assert.Equal("Index Number", ExportPresentation.NamePartLabel(NamePartKind.IndexNumber));
        Assert.Equal(["Same as Source", "3840 × 2160 (4K UHD)", "2560 × 1440 (1440p)",
            "1920 × 1080 (1080p)", "1280 × 720 (720p)", "854 × 480 (480p)"],
            ExportPresentation.Resolutions.Select(x => x.Label));
        Assert.Equal(["Constant Quality", "Variable Bitrate", "Constant Bitrate"],
            ExportPresentation.RateControls.Select(x => x.Label));
        Assert.Equal("NVIDIA NVENC", Assert.Single(ExportPresentation.Encoders).Label);
        Assert.Equal("High Quality", ExportPresentation.Tunes[0].Label);
        Assert.Contains(ExportPresentation.MultipassModes, x => x.Label == "Full Resolution");
        Assert.Contains(ExportPresentation.PixelFormats, x => x.Label == "YUV 4:2:0 (8-bit)");
        Assert.DoesNotContain(ExportPresentation.Containers, x => x.Label == x.Value.ToString());
        Assert.DoesNotContain(ExportPresentation.Codecs, x => x.Label == x.Value.ToString());
        Assert.DoesNotContain(ExportPresentation.RateControls, x => x.Label == x.Value.ToString());
    }

    [Fact]
    public void ComposerPreservesTypedOrderAndProvidesAccessibleActions()
    {
        var chips = ExportPresentation.Composer([
            new(NamePartKind.OriginalName), new(NamePartKind.CustomText, "web"), new(NamePartKind.Sequence0001)
        ]);
        Assert.Equal(["Original name", "Custom text", "Sequence 0001"], chips.Select(x => x.Label));
        Assert.Equal([0, 1, 2], chips.Select(x => x.Index));
        Assert.True(chips[1].IsCustomText);
        Assert.Equal("Remove Custom text name part", chips[1].RemoveAutomationName);
        Assert.Equal("Move Sequence 0001 name part earlier", chips[2].MoveEarlierAutomationName);
    }

    [Fact]
    public void HardwareCapabilityPresentationIsConciseAndKeepsDiagnosticSeparate()
    {
        var available = ExportPresentation.Hardware(new(EncoderBackend.NvidiaNvenc,
            EncoderCapabilityState.ImplementedAndAvailable, "H.264 and HEVC probes succeeded."));
        Assert.Equal("✓ Hardware acceleration available", available.Heading);
        Assert.Equal("NVIDIA NVENC", available.Detail);
        Assert.DoesNotContain("probe", available.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("probes", available.Diagnostic);

        var unavailable = ExportPresentation.Hardware(new(EncoderBackend.NvidiaNvenc,
            EncoderCapabilityState.ImplementedButUnavailable, "driver detail"));
        Assert.Equal("Hardware acceleration unavailable", unavailable.Heading);
        Assert.Equal("NVIDIA NVENC could not be initialized.", unavailable.Detail);
        Assert.False(unavailable.Available);
    }

    private ExportDialogModel CreateModel(string first, string? second = null,
        Func<string, OutputFileSnapshot>? inspect = null)
    {
        var names = new[] { first, second }.Where(x => x is not null).Cast<string>().ToArray();
        var inputs = names.Select((name, index) =>
        {
            var path = Path.Combine(_root, name); File.WriteAllText(path, "source");
            return new EncodingHandoffInput(Guid.NewGuid(), Guid.NewGuid(), path, name, 6, null);
        }).ToArray();
        return new(new(inputs, [], _root), new EncodingOptions(), [], [], new FakeResources(), inspect ?? (_ => new(false, 0)));
    }

    private static void Ready(ExportDialogModel model, params MediaMetadata[] metadata)
    {
        model.ApplyMetadata(metadata);
        model.ApplyEncoderCapability(new(EncoderBackend.NvidiaNvenc, EncoderCapabilityState.ImplementedAndAvailable, "ok"));
    }
    private static MediaMetadata Metadata(string codec, string container) => new(1920, 1080, 24, 5, 6, codec, true, Container: container, AudioCodec: "aac", AudioSampleRate: 48000, AudioChannels: 2);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
    private sealed class FakeResources : IEncodingLutResourceStore
    {
        public Task<MaterializedLutResource> SnapshotAsync(ColorLutStage stage, ManagedLutResource resource, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string Resolve(MaterializedLutResource resource) => "lut.cube";
    }
}
