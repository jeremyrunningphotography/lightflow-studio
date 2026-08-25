using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace LightflowStudio;

internal enum ExportContainerChoice { SameAsSource, Mp4, Mov, Mkv }
internal enum ExportCodecChoice { SameAsSource, H264, Hevc }

internal sealed record ExportLutChoice(string Label, ColorStagePolicyMode Mode,
    ManagedLutResource? Resource = null);

internal sealed record ExportSubmissionItem(
    int Index,
    string SourceFileName,
    string OutputText,
    string OutputAutomationText,
    bool HasRange,
    bool UseRange,
    string RangeText,
    string RangeAutomationName);

internal sealed class ExportDialogModel : INotifyPropertyChanged
{
    private readonly EncodingHandoffResult _handoff;
    private readonly Func<string, OutputFileSnapshot> _inspectOutput;
    private readonly IEncodingLutResourceStore _resourceStore;
    private IReadOnlyList<MediaMetadata?> _metadata;
    private IReadOnlyList<ResolvedMediaRange?> _resolvedRanges;
    private readonly bool[] _useRanges;
    private JobPlan<EncodingJobOptions>? _plan;
    private string _destination;
    private bool _createSubfolder = true;
    private string _subfolderName = "Exports";
    private NamePartSeparator _separator = NamePartSeparator.Hyphen;
    private ExportContainerChoice _container = ExportContainerChoice.SameAsSource;
    private ExportCodecChoice _codec = ExportCodecChoice.SameAsSource;
    private OutputResolution _resolution = OutputResolution.Source;
    private EncodingOptions _encoding;
    private bool _overwrite;
    private bool _advancedExpanded;
    private EncoderCapability? _encoder;
    private ExportLutChoice _camera;
    private ExportLutChoice _creative;

    public ExportDialogModel(EncodingHandoffResult handoff, EncodingOptions defaults,
        IReadOnlyList<ManagedLutResource> cameraLuts, IReadOnlyList<ManagedLutResource> creativeLuts,
        IEncodingLutResourceStore resourceStore, Func<string, OutputFileSnapshot>? inspectOutput = null)
    {
        _handoff = handoff;
        _destination = handoff.InputFolder ?? "";
        _encoding = EncodingOptions.Normalize(defaults) with { AudioMode = AudioEncodingMode.Copy, FrameRate = 0 };
        _metadata = Enumerable.Repeat<MediaMetadata?>(null, handoff.Inputs.Count).ToArray();
        _resolvedRanges = Enumerable.Repeat<ResolvedMediaRange?>(null, handoff.Inputs.Count).ToArray();
        _useRanges = handoff.Inputs.Select(input => input.InitialTrim is { IsFullSource: false }).ToArray();
        _resourceStore = resourceStore;
        _inspectOutput = inspectOutput ?? OutputFileSnapshot.Read;
        CameraChoices = BuildLutChoices(cameraLuts);
        CreativeChoices = BuildLutChoices(creativeLuts);
        _camera = CameraChoices[0];
        _creative = CreativeChoices[0];
        NameParts = new ObservableCollection<NamePart>([new(NamePartKind.OriginalName), new(NamePartKind.Sequence001)]);
        NameParts.CollectionChanged += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<NamePart> NameParts { get; }
    public IReadOnlyList<ExportLutChoice> CameraChoices { get; }
    public IReadOnlyList<ExportLutChoice> CreativeChoices { get; }
    public IReadOnlyList<JobIssue> Issues => _plan is null ? [] : _plan.Issues.Concat(_plan.Items.SelectMany(x => x.Issues)).ToList();
    public IReadOnlyList<JobIssue> Errors => Issues.Where(x => x.Severity == JobIssueSeverity.Error).ToList();
    public IReadOnlyList<JobIssue> Warnings => Issues.Where(x => x.Severity == JobIssueSeverity.Warning).ToList();
    public bool CanExport => _plan?.IsValid == true && _encoder?.IsUsable == true && _metadata.All(x => x is not null);
    public bool IsAnalyzing => _metadata.Any(x => x is null);
    public string EstimateText => "Estimate unavailable";
    public JobPlan<EncodingJobOptions>? CurrentPlan => _plan;
    public IReadOnlyList<EncodingHandoffInput> Inputs => _handoff.Inputs;
    public string Title => $"Export {_handoff.Inputs.Count} {(_handoff.Inputs.Count == 1 ? "video" : "videos")}";
    public string FilesAutomationName => $"Files to Export, {_handoff.Inputs.Count} {(_handoff.Inputs.Count == 1 ? "file" : "files")}";
    public IReadOnlyList<ExportSubmissionItem> SubmissionItems => BuildSubmissionItems();
    public bool? GlobalUseRangeState
    {
        get
        {
            var applicable = _handoff.Inputs.Select((input, index) => (input, index))
                .Where(value => value.input.InitialTrim is { IsFullSource: false })
                .Select(value => _useRanges[value.index]).ToArray();
            if (applicable.Length == 0 || applicable.All(value => value)) return true;
            if (applicable.All(value => !value)) return false;
            return null;
        }
    }
    public string PreviewName => Preview(PreviewExtension());
    public string PreviewPath => string.IsNullOrWhiteSpace(FinalDestination) ? "Choose an output folder" : Path.Combine(FinalDestination, Preview(PreviewExtension()));
    public string PreviewDirectory => Path.GetDirectoryName(PreviewPath) ?? "";
    public string PreviewFileName => Path.GetFileName(PreviewPath);
    public string PreviewStem => Path.GetFileNameWithoutExtension(PreviewName);
    public string RepresentativeExtension => PreviewExtension();
    public bool HasHeterogeneousExtensions => PlannedExtensions().Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any();
    public string ExtensionHelp => HasHeterogeneousExtensions
        ? $"Representative extension {RepresentativeExtension}. Each file uses the extension from its materialized source container."
        : $"Output extension {RepresentativeExtension}, determined by the materialized container.";
    public bool QualityAuthoritative => _encoding.RateControl == RateControlMode.ConstantQuality;
    public bool TargetBitrateAuthoritative => _encoding.RateControl is RateControlMode.VariableBitrate or RateControlMode.ConstantBitrate;
    public bool MaxBitrateAuthoritative => _encoding.RateControl == RateControlMode.VariableBitrate;
    public bool AudioFallbackAuthoritative => _encoding.AudioMode == AudioEncodingMode.Copy;
    public bool AudioDetailsAuthoritative => _encoding.AudioMode != AudioEncodingMode.None;

    public string Destination { get => _destination; set => Set(ref _destination, value); }
    public bool CreateSubfolder { get => _createSubfolder; set => Set(ref _createSubfolder, value); }
    public string SubfolderName { get => _subfolderName; set => Set(ref _subfolderName, value); }
    public NamePartSeparator Separator { get => _separator; set => Set(ref _separator, value); }
    public ExportContainerChoice Container { get => _container; set => Set(ref _container, value); }
    public ExportCodecChoice Codec { get => _codec; set => Set(ref _codec, value); }
    public OutputResolution Resolution { get => _resolution; set => Set(ref _resolution, value); }
    public EncodingOptions Encoding { get => _encoding; set => Set(ref _encoding, EncodingOptions.Normalize(value)); }
    public bool OverwriteExisting { get => _overwrite; set => Set(ref _overwrite, value); }
    public bool AdvancedExpanded { get => _advancedExpanded; set => Set(ref _advancedExpanded, value); }
    public ExportLutChoice Camera { get => _camera; set => Set(ref _camera, value); }
    public ExportLutChoice Creative { get => _creative; set => Set(ref _creative, value); }
    private string FinalDestination => CreateSubfolder && !string.IsNullOrWhiteSpace(SubfolderName)
        ? Path.Combine(Destination, SubfolderName.Trim()) : Destination;

    public void ApplyMetadata(IReadOnlyList<MediaMetadata?> metadata) { _metadata = metadata.ToArray(); Refresh(); }
    public void ApplyResolvedRanges(IReadOnlyList<ResolvedMediaRange?> ranges) { _resolvedRanges = ranges.ToArray(); Refresh(); }
    public void SetUseRange(int index, bool use)
    {
        if (index < 0 || index >= _useRanges.Length || _handoff.Inputs[index].InitialTrim is not { IsFullSource: false }) return;
        if (_useRanges[index] == use) return;
        _useRanges[index] = use;
        Refresh();
    }
    public void SetGlobalUseRanges(bool use) => SetAllRanges(use);
    public void ApplyEncoderCapability(EncoderCapability capability) { _encoder = capability; Refresh(); }
    public void AddPart(NamePartKind kind) => NameParts.Add(new(kind, kind == NamePartKind.CustomText ? "Text" : null));
    public void RemovePart(int index) { if (index >= 0 && index < NameParts.Count) NameParts.RemoveAt(index); }
    public void MovePart(int index, int delta)
    {
        var target = index + delta;
        if (index >= 0 && index < NameParts.Count && target >= 0 && target < NameParts.Count) NameParts.Move(index, target);
    }
    public void UpdateCustomText(int index, string text)
    {
        if (index >= 0 && index < NameParts.Count && NameParts[index].Kind == NamePartKind.CustomText)
        { NameParts[index] = NameParts[index] with { Text = text }; Refresh(); }
    }

    public async Task<JobPlan<EncodingJobOptions>> MaterializeAcceptedPlanAsync(CancellationToken token = default)
    {
        var camera = await SnapshotPolicyAsync(ColorLutStage.Camera, Camera, token).ConfigureAwait(false);
        var creative = await SnapshotPolicyAsync(ColorLutStage.Creative, Creative, token).ConfigureAwait(false);
        return BuildPlan(camera, creative);
    }

    private JobPlan<EncodingJobOptions> BuildPlan(ColorStagePolicy? camera = null, ColorStagePolicy? creative = null)
    {
        var naming = new NamePartsDefinition(NameParts.ToArray(), Separator);
        var options = new EncodingJobOptions(_handoff.InputFolder ?? "", FinalDestination, Resolution,
            RecoveryStrategy.Normal, _encoding with
            {
                Container = Container switch { ExportContainerChoice.Mov => OutputContainer.Mov, ExportContainerChoice.Mkv => OutputContainer.Mkv, _ => OutputContainer.Mp4 },
                Codec = Codec == ExportCodecChoice.Hevc ? VideoCodec.Hevc : VideoCodec.H264
            }, null, "", _handoff.IncludeSubfolders, OverwriteExisting, false, _handoff.IncludeSubfolders,
            EncodingColorMode.Assigned, EncodingJobConcurrency.Default,
            new(Codec == ExportCodecChoice.SameAsSource ? VideoCodecPolicy.SameAsSource : VideoCodecPolicy.Explicit,
                Container == ExportContainerChoice.SameAsSource ? OutputContainerPolicy.SameAsSource : OutputContainerPolicy.Explicit,
                EncodingQualityPolicy.Automatic, camera ?? PreviewPolicy(Camera), creative ?? PreviewPolicy(Creative),
                new(_encoding.AudioBitrateKbps, _encoding.AudioSampleRate, _encoding.AudioChannels)), naming);
        var sources = _handoff.Inputs.Select((input, index) =>
        {
            var metadata = _metadata.ElementAtOrDefault(index);
            var useRange = _useRanges[index] && input.InitialTrim is { IsFullSource: false };
            return new EncodingSource(input.SourcePath, input.FileSizeBytes,
                metadata is null ? input.InitialTrim?.SourceDuration : TimeSpan.FromSeconds(metadata.DurationSeconds),
                useRange ? input.InitialTrim : null, useRange ? _resolvedRanges.ElementAtOrDefault(index) : null, LastWriteUtcTicks: TrimSourceIdentity.Read(input.SourcePath)?.LastWriteUtcTicks,
                HasAudio: metadata?.HasAudio, CapabilityOrder: index, AssignedColor: input.AssignedColor,
                MediaTraits: metadata is null ? null : new(metadata.VideoCodec, metadata.Width, metadata.Height,
                    metadata.FrameRate, metadata.Container, metadata.AudioCodec, metadata.AudioSampleRate,
                    metadata.AudioChannels, metadata.AudioChannelLayout));
        });
        var definition = EncodingJobPlanner.Define(options, sources);
        var plan = EncodingJobPlanner.Plan(definition, _inspectOutput, colorResources: _resourceStore);
        var extra = new List<JobIssue>();
        if (string.IsNullOrWhiteSpace(Destination) || !Path.IsPathFullyQualified(Destination))
            extra.Add(new("export.destination", "Choose a valid absolute output folder.", JobIssueSeverity.Error));
        if (CreateSubfolder)
            try { OutputDestinationPlanner.ResolveSubfolderName(Resolution, SubfolderName); }
            catch (ArgumentException ex) { extra.Add(new("export.subfolder", ex.Message, JobIssueSeverity.Error)); }
        extra.AddRange(EncodingOptionValidator.Validate(options.Encoding).Select(message => new JobIssue("export.options", message, JobIssueSeverity.Error)));
        if (_encoder is not null && !_encoder.IsUsable) extra.Add(new("export.encoder-unavailable",
            "NVIDIA NVENC could not be initialized. Check the hardware acceleration details.", JobIssueSeverity.Error));
        if (!_metadata.Any(value => value is null))
            for (var index = 0; index < _useRanges.Length; index++)
                if (_useRanges[index] && _handoff.Inputs[index].InitialTrim is { IsFullSource: false }
                    && _resolvedRanges.ElementAtOrDefault(index) is null)
                    extra.Add(new("export.range-unresolved",
                        $"The saved In/Out range for '{Path.GetFileName(_handoff.Inputs[index].SourcePath)}' could not be validated. Ignore its In/Out range to export the full video.",
                        JobIssueSeverity.Error));
        return extra.Count == 0 ? plan : plan with { Issues = plan.Issues.Concat(extra).ToList() };
    }

    private void Refresh()
    {
        try { _plan = BuildPlan(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            var options = new EncodingJobOptions(_handoff.InputFolder ?? "", Destination, Resolution,
                RecoveryStrategy.Normal, _encoding, null, "", false, false, false);
            var definition = EncodingJobPlanner.Define(options, []);
            _plan = new(definition, DateTimeOffset.Now, [], [new("export.configuration", ex.Message, JobIssueSeverity.Error)], JobWorkUnit.Items);
        }
        OnChanged(string.Empty);
    }

    private void SetAllRanges(bool use)
    {
        var changed = false;
        for (var index = 0; index < _useRanges.Length; index++)
        {
            if (_handoff.Inputs[index].InitialTrim is not { IsFullSource: false } || _useRanges[index] == use) continue;
            _useRanges[index] = use;
            changed = true;
        }
        if (changed) Refresh();
    }

    private IReadOnlyList<ExportSubmissionItem> BuildSubmissionItems()
    {
        var planned = _plan?.Items.ToDictionary(item => item.Definition.SourceIdentity, StringComparer.OrdinalIgnoreCase);
        return _handoff.Inputs.Select((input, index) =>
        {
            var sourceName = Path.GetFileName(input.SourcePath);
            var hasRange = input.InitialTrim is { IsFullSource: false };
            var useRange = hasRange && _useRanges[index];
            var rangeText = hasRange && input.InitialTrim is { } range
                ? $"Use In/Out   {FormatTime(range.EffectiveIn)} – {FormatTime(range.EffectiveOut)}"
                : "Use In/Out   No In/Out set";
            var outputText = "Output name unresolved";
            if (planned?.GetValueOrDefault(input.SourcePath) is { } item)
                outputText = item.Definition.MaterializedName?.Problem is null && item.OutputPaths.FirstOrDefault() is { } path
                    ? "→ " + Path.GetFileName(path)
                    : "Output name unresolved";
            var rangeAutomationName = hasRange
                ? $"Use In/Out for {sourceName}"
                : $"Use In/Out for {sourceName}, unavailable because no In/Out is defined";
            return new ExportSubmissionItem(index, sourceName, outputText, $"Output for {sourceName}: {outputText.TrimStart('→', ' ')}",
                hasRange, useRange, rangeText, rangeAutomationName);
        }).ToArray();
    }

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss\.f")
        : value.ToString(@"mm\:ss\.f");

    private static IReadOnlyList<ExportLutChoice> BuildLutChoices(IReadOnlyList<ManagedLutResource> resources) =>
        [new("As selected in Lightflow", ColorStagePolicyMode.AsSelectedInLightflow),
         new("No LUT", ColorStagePolicyMode.NoLut),
         .. resources.Select(x => new ExportLutChoice(x.DisplayName, ColorStagePolicyMode.Override, x))];
    private static ColorStagePolicy PreviewPolicy(ExportLutChoice choice) => choice.Mode == ColorStagePolicyMode.Override
        ? new(ColorStagePolicyMode.NoLut) : new(choice.Mode);
    private async Task<ColorStagePolicy> SnapshotPolicyAsync(ColorLutStage stage, ExportLutChoice choice, CancellationToken token) =>
        choice.Mode == ColorStagePolicyMode.Override
            ? new(choice.Mode, await _resourceStore.SnapshotAsync(stage, choice.Resource!, token).ConfigureAwait(false))
            : new(choice.Mode);
    private string Preview(string extension)
    {
        if (_handoff.Inputs.Count == 0) return "Preview unavailable";
        var stem = NamePartsRenderer.Materialize(new(NameParts.ToArray(), Separator),
            new(Path.GetFileNameWithoutExtension(_handoff.Inputs[0].SourcePath), 1));
        return stem.Problem ?? (stem.Stem + extension);
    }
    private string PreviewExtension()
    {
        var planned = PlannedExtensions();
        if (planned.Count > 0) return planned[0];
        var sourceExtension = _handoff.Inputs.Count == 0 ? "" : Path.GetExtension(_handoff.Inputs[0].SourcePath).ToLowerInvariant();
        return Container switch
        { ExportContainerChoice.Mov => ".mov", ExportContainerChoice.Mkv => ".mkv", _ => sourceExtension is ".mov" or ".mkv" ? sourceExtension : ".mp4" };
    }
    private IReadOnlyList<string> PlannedExtensions() => _plan?.Items
        .Select(item => Path.GetExtension(item.OutputPaths.FirstOrDefault() ?? "").ToLowerInvariant())
        .Where(extension => !string.IsNullOrWhiteSpace(extension)).ToArray() ?? [];
    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; Refresh(); OnChanged(name); }
    private void OnChanged([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new(name));
    public event PropertyChangedEventHandler? PropertyChanged;
}
