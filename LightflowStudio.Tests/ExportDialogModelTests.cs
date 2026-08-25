using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExportDialogModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-export-modal-").FullName;

    [Fact]
    public void Defaults_AreModernIndependentAndBounded()
    {
        var model = CreateModel("clip.mp4");
        Assert.Equal([NamePartKind.OriginalName, NamePartKind.Sequence001], model.NameParts.Select(x => x.Kind));
        Assert.Equal(ExportContainerChoice.SameAsSource, model.Container);
        Assert.Equal(ExportCodecChoice.SameAsSource, model.Codec);
        Assert.Equal(ColorStagePolicyMode.AsSelectedInLightflow, model.Camera.Mode);
        Assert.Equal(ColorStagePolicyMode.AsSelectedInLightflow, model.Creative.Mode);
        Assert.Equal(AudioEncodingMode.Copy, model.Encoding.AudioMode);
        Assert.True(model.CreateSubfolder);
        Assert.Equal("Estimate unavailable", model.EstimateText);
    }

    [Fact]
    public void NamingPreview_ReorderSeparatorCustomAndFailuresUseSharedRenderer()
    {
        var model = CreateModel("clip42.mp4");
        Ready(model, Metadata("h264", "mp4"));
        model.AddPart(NamePartKind.CustomText);
        model.UpdateCustomText(2, "web");
        model.MovePart(2, -1);
        model.MovePart(1, -1);
        model.RemovePart(2);
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
        Assert.Equal(["a-001.mp4", "b-002.mov"], plan.Items.Select(x => Path.GetFileName(x.OutputPaths.Single())));
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
    public void ModernRecipeDoesNotAuthorSchedulerConcurrency()
    {
        var model = CreateModel("clip.mp4");
        Ready(model, Metadata("h264", "mp4"));
        Assert.Equal(EncodingJobConcurrency.Default, model.CurrentPlan!.Definition.Options.ParallelExports);
    }

    [Fact]
    public void NewExportDefaultsToHyphenWithoutChangingExplicitStoredDefinitions()
    {
        var model = CreateModel("clip.mp4");
        Assert.Equal(NamePartSeparator.Hyphen, model.Separator);
        var restored = new NamePartsDefinition([new(NamePartKind.OriginalName), new(NamePartKind.Sequence01)], NamePartSeparator.Underscore);
        Assert.Equal(NamePartSeparator.Underscore, restored.Separator);
    }

    [Fact]
    public void SubmissionReviewUsesDeterministicOrderTitlesAndMaterializedNames()
    {
        var model = CreateModel("zeta.mp4", "alpha.mov");
        Ready(model, Metadata("h264", "mp4"), Metadata("h264", "mov"));

        Assert.Equal("Export 2 videos", model.Title);
        Assert.Equal("Files to Export, 2 files", model.FilesAutomationName);
        Assert.Equal(["zeta.mp4", "alpha.mov"], model.SubmissionItems.Select(item => item.SourceFileName));
        Assert.Equal(["→ zeta-001.mp4", "→ alpha-002.mov"], model.SubmissionItems.Select(item => item.OutputText));
        Assert.Equal(model.CurrentPlan!.Items.Select(item => Path.GetFileName(item.Definition.SourceIdentity)),
            model.SubmissionItems.Select(item => item.SourceFileName));
        Assert.Equal("Export 1 video", CreateModel("solo.mp4").Title);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    public void SubmissionReviewPreservesEveryItemForBoundedListSizes(int count)
    {
        var model = CreateModelWithRanges(Enumerable.Range(1, count)
            .Select(index => ($"clip-{index:D2}.mp4", (MediaRange?)null)).ToArray());
        model.ApplyMetadata(Enumerable.Range(1, count).Select(_ => Metadata("h264", "mp4")).ToArray());

        Assert.Equal(count, model.SubmissionItems.Count);
        Assert.Equal($"clip-{count:D2}.mp4", model.SubmissionItems[^1].SourceFileName);
        Assert.Equal($"→ clip-{count:D2}-{count:D3}.mp4", model.SubmissionItems[^1].OutputText);
    }

    [Fact]
    public void SubmissionReviewShowsUnresolvedNameInsteadOfFabricatingOutput()
    {
        var model = CreateModel("clip.mp4");
        Ready(model, Metadata("h264", "mp4"));
        model.NameParts.Clear();
        model.AddPart(NamePartKind.Date);

        Assert.Equal("Output name unresolved", model.SubmissionItems.Single().OutputText);
    }

    [Fact]
    public void RangeChoicesDefaultOnAndRematerializeWithoutMutatingSavedRange()
    {
        var saved = new MediaRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        var model = CreateModelWithRanges(("trimmed.mp4", saved), ("full.mp4", null));
        Ready(model, Metadata("h264", "mp4"), Metadata("h264", "mp4"));
        var resolved = new ResolvedMediaRange(saved, TimeSpan.Zero, TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3.04), TimeSpan.FromSeconds(2.04));
        model.ApplyResolvedRanges([resolved, null]);

        Assert.True(model.SubmissionItems[0].HasRange);
        Assert.True(model.SubmissionItems[0].UseRange);
        Assert.Contains("00:01.0 – 00:03.0", model.SubmissionItems[0].RangeText);
        Assert.False(model.SubmissionItems[1].HasRange);
        Assert.Equal("", model.SubmissionItems[1].RangeText);
        Assert.False(model.SubmissionItems[1].RangeControlEnabled);
        Assert.True(model.GlobalUseRangeState);
        var ranged = model.CurrentPlan!.Items[0];
        var rangedIdentity = EncodingOutputIdentity.Create(ranged.Definition, model.CurrentPlan.Definition.Options);
        Assert.NotNull(ranged.Definition.ResolvedRange);
        Assert.Equal(2.04, ranged.WorkEstimate.Value!.Value, 2);

        model.SetUseRange(0, false);
        var full = model.CurrentPlan!.Items[0];
        Assert.Null(full.Definition.ResolvedRange);
        Assert.Null(full.Definition.MediaRange!.In);
        Assert.Equal(5, full.WorkEstimate.Value);
        Assert.NotEqual(rangedIdentity, EncodingOutputIdentity.Create(full.Definition, model.CurrentPlan.Definition.Options));
        Assert.Equal(saved, model.Inputs[0].InitialTrim);

        model.SetGlobalUseRanges(true);
        Assert.NotNull(model.CurrentPlan!.Items[0].Definition.ResolvedRange);
        model.SetGlobalUseRanges(false);
        Assert.All(model.SubmissionItems, item => Assert.False(item.UseRange));
        Assert.False(model.SubmissionItems[0].RangeControlEnabled);
        model.SetUseRange(0, true);
        Assert.False(model.SubmissionItems[0].UseRange);
        model.SetGlobalUseRanges(true);
        Assert.True(model.SubmissionItems[0].UseRange);
        Assert.True(model.SubmissionItems[0].RangeControlEnabled);
        model.SetUseRange(0, true);
        Assert.True(model.SubmissionItems[0].UseRange);
    }

    [Fact]
    public void UnvalidatedSavedRangeBlocksOnlyRangeExportAndCanBeIgnored()
    {
        var saved = new MediaRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        var model = CreateModelWithRanges(("trimmed.mp4", saved));
        Ready(model, Metadata("h264", "mp4"));

        Assert.Contains(model.Errors, issue => issue.Code == "export.range-unresolved");
        model.SetGlobalUseRanges(false);
        Assert.DoesNotContain(model.Errors, issue => issue.Code == "export.range-unresolved");
        Assert.True(model.CanExport);
    }

    [Fact]
    public void GlobalRangeStateIsCheckedUncheckedOrMixedFromApplicableFiles()
    {
        var first = new MediaRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        var second = new MediaRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
        var model = CreateModelWithRanges(("first.mp4", first), ("second.mp4", second), ("full.mp4", null));

        Assert.True(model.GlobalUseRangeState);
        model.SetUseRange(0, false);
        Assert.Null(model.GlobalUseRangeState);
        model.SetGlobalUseRanges(false);
        Assert.False(model.GlobalUseRangeState);
        Assert.All(model.SubmissionItems.Where(item => item.HasRange), item => Assert.False(item.RangeControlEnabled));
        Assert.False(model.SubmissionItems[2].UseRange);
        Assert.False(model.SubmissionItems[2].HasRange);
        model.SetGlobalUseRanges(true);
        Assert.True(model.GlobalUseRangeState);
        Assert.All(model.SubmissionItems.Where(item => item.HasRange), item => Assert.True(item.UseRange));
    }

    [Fact]
    public void PresentationMappingsUseFriendlyProductLabels()
    {
        Assert.Equal("Original name", ExportPresentation.NamePartLabel(NamePartKind.OriginalName));
        Assert.Equal("Custom text", ExportPresentation.NamePartLabel(NamePartKind.CustomText));
        Assert.Equal("Sequence 0001", ExportPresentation.NamePartLabel(NamePartKind.Sequence0001));
        Assert.Equal("Index Number", ExportPresentation.NamePartLabel(NamePartKind.IndexNumber));
        Assert.Equal(["Same as Source", "4K UHD (3840 × 2160 canvas)", "1440p (1440 px high)",
            "1080p (1080 px high)", "720p (720 px high)", "480p (480 px high)"],
            ExportPresentation.Resolutions.Select(x => x.Label));
        Assert.Equal(["Constant Quality", "Variable Bitrate", "Constant Bitrate"],
            ExportPresentation.RateControls.Select(x => x.Label));
        Assert.Equal("NVIDIA NVENC", Assert.Single(ExportPresentation.Encoders).Label);
        Assert.Equal("High Quality", ExportPresentation.Tunes[0].Label);
        Assert.Contains(ExportPresentation.MultipassModes, x => x.Label == "Full Resolution");
        Assert.Contains(ExportPresentation.PixelFormats, x => x.Label == "YUV 4:2:0 (8-bit)");
        Assert.Equal(1, ExportPresentation.EncoderPresets.First().Value);
        Assert.Equal("P1 — Fastest", ExportPresentation.EncoderPresets.First().Label);
        Assert.Equal(7, ExportPresentation.EncoderPresets.Last().Value);
        Assert.Equal("P7 — Highest Quality", ExportPresentation.EncoderPresets.Last().Label);
        Assert.DoesNotContain(ExportPresentation.Containers, x => x.Label == x.Value.ToString());
        Assert.DoesNotContain(ExportPresentation.Codecs, x => x.Label == x.Value.ToString());
        Assert.DoesNotContain(ExportPresentation.RateControls, x => x.Label == x.Value.ToString());
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(15, 15)]
    [InlineData(40, 15)]
    public void AqStrengthPresentationClampsToBackendRange(int value, int expected) =>
        Assert.Equal(expected, ExportPresentation.AqStrength(value));

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(18, 18)]
    [InlineData(51, 51)]
    [InlineData(70, 51)]
    public void ConstantQualityPresentationClampsToBackendRange(int value, int expected) =>
        Assert.Equal(expected, ExportPresentation.ConstantQuality(value));

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void AqStrengthAuthorityFollowsEitherAdaptiveQuantizationMode(bool spatial, bool temporal, bool expected) =>
        Assert.Equal(expected, ExportPresentation.IsAqStrengthEnabled(spatial, temporal));

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

    [Fact]
    public void ExtensionPreviewIsOutsideNamePartsAndTracksRepresentativeAndHeterogeneousContainers()
    {
        var model = CreateModel("a.mp4", "b.mov");
        Ready(model, Metadata("h264", "mp4"), Metadata("h264", "mov"));
        Assert.Equal(".mp4", model.RepresentativeExtension);
        Assert.True(model.HasHeterogeneousExtensions);
        Assert.Contains("Each file", model.ExtensionHelp);
        Assert.Equal(2, model.NameParts.Count);

        model.Container = ExportContainerChoice.Mkv;
        Assert.Equal(".mkv", model.RepresentativeExtension);
        Assert.EndsWith(".mkv", model.PreviewName);
        Assert.False(model.HasHeterogeneousExtensions);
        Assert.Equal(2, model.NameParts.Count);
    }

    [Theory]
    [InlineData((int)RateControlMode.ConstantQuality, 17, 40, 80)]
    [InlineData((int)RateControlMode.VariableBitrate, 18, 32, 64)]
    [InlineData((int)RateControlMode.ConstantBitrate, 18, 25, 80)]
    public void PrimaryQualityValuesMaterializeToTypedBackend(int modeValue, int quality, int target, int maximum)
    {
        var model = CreateModel("clip.mp4"); Ready(model, Metadata("h264", "mp4"));
        model.Encoding = model.Encoding with { RateControl = (RateControlMode)modeValue, Quality = quality,
            TargetBitrateMbps = target, MaxBitrateMbps = maximum };
        var encoding = model.CurrentPlan!.Definition.Items.Single().MaterializedExport!.Encoding;
        Assert.Equal(((RateControlMode)modeValue, quality, target, maximum),
            (encoding.RateControl, encoding.Quality, encoding.TargetBitrateMbps, encoding.MaxBitrateMbps));
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

    private ExportDialogModel CreateModelWithRanges(params (string Name, MediaRange? Range)[] definitions)
    {
        var inputs = definitions.Select(definition =>
        {
            var path = Path.Combine(_root, definition.Name); File.WriteAllText(path, "source");
            return new EncodingHandoffInput(Guid.NewGuid(), Guid.NewGuid(), path, definition.Name, 6, definition.Range);
        }).ToArray();
        return new(new(inputs, [], _root), new EncodingOptions(), [], [], new FakeResources(), _ => new(false, 0));
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
