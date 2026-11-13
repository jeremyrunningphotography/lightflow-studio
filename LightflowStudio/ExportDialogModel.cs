using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace LightflowStudio;

internal enum ExportContainerChoice { SameAsSource, Mp4, Mov, Mkv }
internal enum ExportCodecChoice { SameAsSource, H264, Hevc }
internal enum ExportNamingDefault { Ordinary, Subclip }
internal enum ExportFilePreflightState { Pending, Checking, Ready, Warning, Error }

internal sealed record ExportLutChoice(string Label, ColorStagePolicyMode Mode,
    ManagedLutResource? Resource = null);

internal sealed record ExportSubmissionItem(
    int Index,
    Guid PlannedItemId,
    string SourceFileName,
    string OutputText,
    string OutputAutomationText,
    bool HasRange,
    bool UseRange,
    bool RangeControlEnabled,
    string RangeAutomationName,
    double ExportSegmentLeft,
    double ExportSegmentWidth,
    string RangeToolTip,
    string TimelineAutomationName,
    IReadOnlyList<JobIssue> Issues,
    ExportFilePreflightState State = ExportFilePreflightState.Ready,
    bool WillSkip = false,
    bool WillOverwrite = false)
{
    public bool IsChecking => State is ExportFilePreflightState.Pending or ExportFilePreflightState.Checking;
    public string IndicatorState => IsChecking ? "Generating" : "Completed";
    public string StatusAutomationName => $"{SourceFileName}: {(State == ExportFilePreflightState.Pending ? "Preparing video" : IsChecking ? "Checking video" : HasError ? "Needs attention" : WillSkip ? "Will be skipped" : WillOverwrite ? "Existing file will be replaced" : HasIssues ? "Ready with warning" : "Ready")}";
    private IEnumerable<JobIssue> OrderedIssues => Issues
        .OrderByDescending(issue => issue.Severity)
        .ThenBy(issue => issue.Code is "encoding.source-overwrite" or "encoding.partial-source-collision" ? 0 : issue.Code == "encoding.output-collision" ? 1 : 2)
        .ThenBy(issue => issue.Code, StringComparer.Ordinal);
    public bool HasIssues => !IsChecking && Issues.Count > 0;
    public bool HasError => !IsChecking && Issues.Any(issue => issue.Severity == JobIssueSeverity.Error);
    public string IssueSeverityText => HasError ? "Error" : "Warning";
    public string IssueToolTip
    {
        get
        {
            if (!HasIssues) return "";
            var issue = OrderedIssues.First();
            return issue.Code switch
            {
                "encoding.source-overwrite" or "encoding.partial-source-collision" =>
                    "This export would replace one of your selected videos.\nChoose a different destination or filename. Overwrite cannot replace a selected video.",
                "encoding.output-collision" =>
                    "Two exports would create the same file.\nChoose different filenames or destinations.",
                "encoding.existing-output-differs" =>
                    "A file with this name already exists here. This export will be skipped.",
                "encoding.existing-output-replaced" =>
                    "A file with this name already exists here. It will be replaced.",
                "naming.path-too-long" =>
                    "Output path is too long\nChoose a shorter destination or filename.",
                _ => issue.Message
            };
        }
    }
    public string IssueAutomationName => HasIssues
        ? $"{IssueSeverityText}: {IssueToolTip}"
        : "";
}

internal sealed class ExportDialogModel : INotifyPropertyChanged
{
    private readonly EncodingHandoffResult _handoff;
    private readonly Func<string, OutputFileSnapshot> _inspectOutput;
    private readonly IEncodingLutResourceStore _resourceStore;
    private readonly Func<bool, CancellationToken, Task<EncodingHandoffResult>>? _revalidate;
    private IReadOnlyList<MediaMetadata?> _metadata;
    private IReadOnlyList<string?> _metadataDiagnostics = [];
    private IReadOnlyList<ResolvedMediaRange?> _resolvedRanges;
    private readonly bool[] _metadataRead;
    private readonly bool[] _rangeRead;
    private readonly bool[] _checking;
    private readonly string?[] _rangeDiagnostics;
    private readonly TrimSourceIdentity?[] _sourceIdentities;
    private long _preflightGeneration;
    private long _revision;
    private readonly bool[] _useRanges;
    private bool _rangesGloballyEnabled = true;
    private bool _includeNoSubclipSources = true;
    private JobPlan<EncodingJobOptions>? _plan;
    private string _destination;
    private ExportDestinationMode _destinationMode = ExportDestinationMode.SpecificFolder;
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
    private string? _preflightError;
    private ExportLutChoice _camera;
    private ExportLutChoice _creative;

    public ExportDialogModel(EncodingHandoffResult handoff, EncodingOptions defaults,
        IReadOnlyList<ManagedLutResource> cameraLuts, IReadOnlyList<ManagedLutResource> creativeLuts,
        IEncodingLutResourceStore resourceStore, Func<string, OutputFileSnapshot>? inspectOutput = null,
        Func<bool, CancellationToken, Task<EncodingHandoffResult>>? revalidate = null,
        ExportNamingDefault namingDefault = ExportNamingDefault.Ordinary,
        NamePartsDefinition? restoredNaming = null)
    {
        _handoff = handoff;
        _destination = handoff.InputFolder ?? "";
        _encoding = EncodingOptions.Normalize(defaults) with { AudioMode = AudioEncodingMode.Copy, FrameRate = 0 };
        _metadata = Enumerable.Repeat<MediaMetadata?>(null, handoff.Inputs.Count).ToArray();
        _resolvedRanges = Enumerable.Repeat<ResolvedMediaRange?>(null, handoff.Inputs.Count).ToArray();
        _metadataRead = new bool[handoff.Inputs.Count];
        _rangeRead = new bool[handoff.Inputs.Count];
        _checking = new bool[handoff.Inputs.Count];
        _rangeDiagnostics = new string?[handoff.Inputs.Count];
        _sourceIdentities = handoff.Inputs.Select(input => input.SourceIdentity ?? TrimSourceIdentity.Read(input.SourcePath)).ToArray();
        _useRanges = handoff.Inputs.Select(input => input.InitialTrim is { IsFullSource: false }).ToArray();
        _resourceStore = resourceStore;
        _revalidate = revalidate;
        _inspectOutput = inspectOutput ?? OutputFileSnapshot.Read;
        CameraChoices = BuildLutChoices(cameraLuts);
        CreativeChoices = BuildLutChoices(creativeLuts);
        _camera = CameraChoices[0];
        _creative = CreativeChoices[0];
        var naming = restoredNaming ?? (namingDefault == ExportNamingDefault.Subclip
            ? new NamePartsDefinition([new(NamePartKind.OriginalName)], NamePartSeparator.Hyphen)
            : new NamePartsDefinition([new(NamePartKind.OriginalName), new(NamePartKind.Sequence001)], NamePartSeparator.Hyphen));
        _separator = naming.Separator;
        NameParts = new ObservableCollection<NamePart>(naming.Parts);
        NameParts.CollectionChanged += (_, _) => { _revision++; Refresh(); };
        Refresh();
    }

    public ObservableCollection<NamePart> NameParts { get; }
    public IReadOnlyList<ExportLutChoice> CameraChoices { get; }
    public IReadOnlyList<ExportLutChoice> CreativeChoices { get; }
    public IReadOnlyList<JobIssue> Issues => _plan is null ? [] : _plan.Issues.Concat(_plan.Items.SelectMany(x => x.Issues)).ToList();
    public IReadOnlyList<JobIssue> Errors => Issues.Where(x => x.Severity == JobIssueSeverity.Error).ToList();
    public IReadOnlyList<JobIssue> Warnings => Issues.Where(x => x.Severity == JobIssueSeverity.Warning).ToList();
    public IReadOnlyList<JobIssue> GlobalErrors => _plan?.Issues.Where(x => x.Severity == JobIssueSeverity.Error).ToList() ?? [];
    public IReadOnlyList<JobIssue> GlobalWarnings => _plan?.Issues.Where(x => x.Severity == JobIssueSeverity.Warning).ToList() ?? [];
    public EncoderCapability? EncoderCapability => _encoder;
    public bool CanExport => _plan?.IsValid == true && _encoder?.IsUsable == true
        && _plan.ExecutableItems.Count > 0
        && SubmissionItems.All(item => item.State is ExportFilePreflightState.Ready or ExportFilePreflightState.Warning);
    public bool IsAnalyzing => ActiveInputs().Any(value => SourcePending(value.index)) || _encoder is null;
    public string ReadySummary
    {
        get
        {
            var items = SubmissionItems;
            var count = items.Count;
            var noun = count == 1 ? "video" : "videos";
            var validated = items.Where(item => item.State is ExportFilePreflightState.Ready or ExportFilePreflightState.Warning).ToArray();
            var skipped = validated.Count(item => item.WillSkip);
            var overwritten = validated.Count(item => item.WillOverwrite);
            var ready = validated.Length - skipped - overwritten;
            var outputCounts = new List<string>();
            if (ready > 0) outputCounts.Add($"{ready} ready");
            if (overwritten > 0) outputCounts.Add($"{overwritten} will be overwritten");
            if (skipped > 0) outputCounts.Add($"{skipped} will be skipped");
            var errors = items.Count(item => item.State == ExportFilePreflightState.Error);
            if (items.Any(item => item.IsChecking))
                return items.All(item => item.State == ExportFilePreflightState.Pending)
                    ? $"Preparing {count} {noun}…"
                    : $"Analyzing {noun}… {ready} of {count} ready" + (overwritten > 0 ? $" · {overwritten} will be overwritten" : "")
                        + (skipped > 0 ? $" · {skipped} will be skipped" : "")
                        + (errors > 0 ? $" · {errors} {(errors == 1 ? "needs" : "need")} attention" : "");
            if (errors > 0) return $"{errors} of {count} {noun} {(errors == 1 ? "needs" : "need")} attention";
            if (GlobalErrors.Any(issue => issue.Code != "export.metadata-pending")) return "Review export settings";
            if (skipped == count && count > 0) return $"{count} will be skipped · Nothing to export";
            if (skipped > 0 || overwritten > 0) return string.Join(" · ", outputCounts);
            var text = $"{count} {noun} ready to export";
            var rotated = ActiveInputs().Count(value => value.input.Rotation.Degrees != 0);
            if (rotated > 0) text += $" · Lightflow rotation baked into {rotated} {(rotated == 1 ? "video" : "videos")}";
            return EstimateOutputBytes() is { } bytes ? $"{text} · Est. {FormatBytes(bytes)}" : text;
        }
    }
    public JobPlan<EncodingJobOptions>? CurrentPlan => _plan;
    public IReadOnlyList<EncodingHandoffInput> Inputs => ActiveInputs().Select(value => value.input).ToArray();
    public IReadOnlyList<EncodingHandoffInput> PreflightInputs => _handoff.Inputs;
    public bool IsSubclipExport => _handoff.Inputs.Any(input => input.ExportProvenance?.Kind is ExportItemKind.Subclip or ExportItemKind.NoSubclipFullSourceFallback);
    public bool HasNoSubclipFallbackCandidates => _handoff.Inputs.Any(input => input.ExportProvenance?.Kind == ExportItemKind.NoSubclipFullSourceFallback);
    public bool IncludeNoSubclipSources { get => _includeNoSubclipSources; set => Set(ref _includeNoSubclipSources, value); }
    public bool ShowGlobalRangeControl => !IsSubclipExport;
    public string Title => IsSubclipExport ? "Export Subclips" : $"Export {_handoff.Inputs.Count} {(_handoff.Inputs.Count == 1 ? "video" : "videos")}";
    public string FilesAutomationName => $"Files to Export, {Inputs.Count} {(Inputs.Count == 1 ? "file" : "files")}";
    public IReadOnlyList<ExportSubmissionItem> SubmissionItems => BuildSubmissionItems();
    public bool? GlobalUseRangeState
    {
        get
        {
            if (IsSubclipExport) return _includeNoSubclipSources;
            var applicable = ActiveInputs()
                .Where(value => !value.input.RangeIsFixed && value.input.InitialTrim is { IsFullSource: false })
                .Select(value => _useRanges[value.index]).ToArray();
            if (!_rangesGloballyEnabled) return false;
            if (applicable.Length == 0 || applicable.All(value => value)) return true;
            if (applicable.All(value => !value)) return false;
            return null;
        }
    }
    public string PreviewName => Preview(PreviewExtension());
    public string PreviewPath
    {
        get
        {
            try
            {
                var source = ActiveInputs().FirstOrDefault().input?.SourcePath;
                if (source is null) return "Choose an output folder";
                return Path.Combine(CurrentDestination().ResolveDirectory(source), Preview(PreviewExtension()));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
            {
                return "Choose an output folder";
            }
        }
    }
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
    public ExportDestinationMode DestinationMode { get => _destinationMode; set => Set(ref _destinationMode, value); }
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
    private ExportDestination CurrentDestination() => new(DestinationMode, Destination,
        CreateSubfolder ? SubfolderName : null);

    public void ApplyMetadata(IReadOnlyList<MediaMetadata?> metadata, IReadOnlyList<string?>? diagnostics = null)
    {
        _metadata = metadata.ToArray();
        _metadataDiagnostics = diagnostics?.ToArray() ?? [];
        Array.Fill(_metadataRead, true);
        Refresh();
    }
    public void ApplyResolvedRanges(IReadOnlyList<ResolvedMediaRange?> ranges)
    { _resolvedRanges = ranges.ToArray(); Array.Fill(_rangeRead, true); Refresh(); }

    public long BeginPreflight()
    {
        _preflightGeneration++;
        Array.Clear(_metadataRead); Array.Clear(_rangeRead); Array.Clear(_checking); Array.Clear(_rangeDiagnostics);
        _metadata = new MediaMetadata?[_handoff.Inputs.Count];
        _resolvedRanges = new ResolvedMediaRange?[_handoff.Inputs.Count];
        _metadataDiagnostics = new string?[_handoff.Inputs.Count];
        _encoder = null; _preflightError = null;
        Refresh();
        return _preflightGeneration;
    }

    public void CancelPreflight(long generation)
    { if (generation == _preflightGeneration) _preflightGeneration++; }

    public bool SetChecking(long generation, int index)
    {
        if (generation != _preflightGeneration) return false;
        _checking[index] = true; Refresh(); return true;
    }

    public bool ApplySourceProbe(long generation, int index, ExportSourceProbe probe)
    {
        if (generation != _preflightGeneration) return false;
        var metadata = _metadata.ToArray(); metadata[index] = probe.Metadata; _metadata = metadata;
        var diagnostics = _metadataDiagnostics.ToArray(); diagnostics[index] = probe.Diagnostic; _metadataDiagnostics = diagnostics;
        _metadataRead[index] = true; _sourceIdentities[index] = probe.Identity;
        Refresh(); return true;
    }

    public bool ApplySourceRange(long generation, int index, ResolvedMediaRange? range, string? diagnostic = null)
    {
        if (generation != _preflightGeneration) return false;
        var ranges = _resolvedRanges.ToArray(); ranges[index] = range; _resolvedRanges = ranges;
        _rangeRead[index] = true; _rangeDiagnostics[index] = diagnostic; _checking[index] = false;
        Refresh(); return true;
    }

    public bool ApplyEncoderCapability(long generation, EncoderCapability capability)
    {
        if (generation != _preflightGeneration) return false;
        ApplyEncoderCapability(capability); return true;
    }

    private bool UsesRange(int index) => _handoff.Inputs[index].ExportProvenance?.Kind == ExportItemKind.Subclip
        || !_handoff.Inputs[index].RangeIsFixed && _useRanges[index] && _handoff.Inputs[index].InitialTrim is { IsFullSource: false };
    private bool SourcePending(int index) => !_metadataRead[index]
        || _metadata.ElementAtOrDefault(index) is not null && UsesRange(index) && !_rangeRead[index];
    public void SetUseRange(int index, bool use)
    {
        if (!_rangesGloballyEnabled || index < 0 || index >= _useRanges.Length
            || _handoff.Inputs[index].RangeIsFixed
            || _handoff.Inputs[index].InitialTrim is not { IsFullSource: false }) return;
        if (_useRanges[index] == use) return;
        _useRanges[index] = use;
        _revision++; Refresh();
    }
    public void SetGlobalUseRanges(bool use)
    {
        _rangesGloballyEnabled = use;
        _revision++;
        SetAllRanges(use, refreshWhenUnchanged: true);
    }
    public void ApplyEncoderCapability(EncoderCapability capability) { _encoder = capability; Refresh(); }
    public void ApplyPreflightFailure(string diagnostic)
    {
        _preflightError = diagnostic;
        var diagnostics = new string?[_handoff.Inputs.Count];
        for (var index = 0; index < _handoff.Inputs.Count; index++)
        {
            diagnostics[index] = _metadataDiagnostics.ElementAtOrDefault(index);
            if (!_metadataRead[index]) { _metadataRead[index] = true; diagnostics[index] = diagnostic; }
            if (!_rangeRead[index]) { _rangeRead[index] = true; _rangeDiagnostics[index] = diagnostic; }
        }
        _metadataDiagnostics = diagnostics;
        Refresh();
    }
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
        var revision = _revision;
        var generation = _preflightGeneration;
        var expected = Inputs;
        if (_revalidate is not null)
        {
            var current = await _revalidate(_includeNoSubclipSources, token);
            if (!current.Succeeded)
                throw new InvalidOperationException(string.Join(Environment.NewLine, current.Errors));
            var actual = current.Inputs.Where(input =>
                input.ExportProvenance?.Kind != ExportItemKind.NoSubclipFullSourceFallback ||
                _includeNoSubclipSources).ToArray();
            if (!ProspectiveStateMatches(expected, actual))
                throw new InvalidOperationException("The source or Subclip state changed while Export was open. Review the current Subclips and try again.");
        }
        var camera = await SnapshotPolicyAsync(ColorLutStage.Camera, Camera, token);
        var creative = await SnapshotPolicyAsync(ColorLutStage.Creative, Creative, token);
        if (revision != _revision || generation != _preflightGeneration)
            throw new InvalidOperationException("Export settings or selection changed during final validation. Review the current settings and try again.");
        await Task.Run(() =>
        {
            foreach (var (_, index) in ActiveInputs())
                if (_sourceIdentities[index] is { } identity)
                {
                    var current = TrimSourceIdentity.Read(_handoff.Inputs[index].SourcePath);
                    if (current is null || !identity.Matches(current))
                        throw new InvalidOperationException("The source changed after preflight. Refresh the source and reopen Export.");
                }
        }, token);
        if (revision != _revision || generation != _preflightGeneration)
            throw new InvalidOperationException("Export settings or selection changed during final validation. Review the current settings and try again.");
        var plan = BuildPlan(camera, creative);
        if (plan.IsValid && plan.ExecutableItems.Count == 0)
            throw new InvalidOperationException("All existing outputs will be skipped. Enable Overwrite existing files or choose another destination or filename.");
        return plan;
    }

    private JobPlan<EncodingJobOptions> BuildPlan(ColorStagePolicy? camera = null, ColorStagePolicy? creative = null)
    {
        var naming = new NamePartsDefinition(NameParts.ToArray(), Separator);
        var destination = CurrentDestination();
        var outputRoot = DestinationMode == ExportDestinationMode.SpecificFolder
            ? Destination : _handoff.InputFolder ?? "";
        var options = new EncodingJobOptions(_handoff.InputFolder ?? "", outputRoot, Resolution,
            RecoveryStrategy.Normal, _encoding with
            {
                Container = Container switch { ExportContainerChoice.Mov => OutputContainer.Mov, ExportContainerChoice.Mkv => OutputContainer.Mkv, _ => OutputContainer.Mp4 },
                Codec = Codec == ExportCodecChoice.Hevc ? VideoCodec.Hevc : VideoCodec.H264
            }, null, "", PreserveFolderStructure: false, OverwriteExistingFiles: OverwriteExisting,
            DetailedOutput: false,
            IncludeSubfolders: _handoff.IncludeSubfolders,
            ColorMode: EncodingColorMode.Assigned, ParallelExports: EncodingJobConcurrency.Default,
            MaterializationPolicy: new(Codec == ExportCodecChoice.SameAsSource ? VideoCodecPolicy.SameAsSource : VideoCodecPolicy.Explicit,
                Container == ExportContainerChoice.SameAsSource ? OutputContainerPolicy.SameAsSource : OutputContainerPolicy.Explicit,
                EncodingQualityPolicy.Automatic, camera ?? PreviewPolicy(Camera), creative ?? PreviewPolicy(Creative),
                new(_encoding.AudioBitrateKbps, _encoding.AudioSampleRate, _encoding.AudioChannels)), Naming: naming,
            Destination: destination);
        var sources = ActiveInputs().Select(value =>
        {
            var (input, index) = value;
            var metadata = _metadata.ElementAtOrDefault(index);
            var useRange = input.ExportProvenance?.Kind == ExportItemKind.Subclip ||
                !input.RangeIsFixed && _useRanges[index] && input.InitialTrim is { IsFullSource: false };
            return new EncodingSource(input.SourcePath, input.FileSizeBytes,
                metadata is null ? input.InitialTrim?.SourceDuration : TimeSpan.FromSeconds(metadata.DurationSeconds),
                useRange ? input.InitialTrim : null, useRange ? _resolvedRanges.ElementAtOrDefault(index) : null, LastWriteUtcTicks: _sourceIdentities[index]?.LastWriteUtcTicks,
                HasAudio: metadata?.HasAudio, CapabilityOrder: index, AssignedColor: input.AssignedColor,
                MediaTraits: metadata is null ? null : new(metadata.VideoCodec, metadata.Width, metadata.Height,
                    metadata.FrameRate, metadata.Container, metadata.AudioCodec, metadata.AudioSampleRate,
                    metadata.AudioChannels, metadata.AudioChannelLayout),
                NamingOriginalName: input.NamingOriginalName,
                NamingIndexNumberBasis: input.NamingIndexNumberBasis,
                ExportProvenance: input.ExportProvenance, Rotation: input.Rotation);
        });
        var definition = EncodingJobPlanner.Define(options, sources);
        var plan = EncodingJobPlanner.Plan(definition, _inspectOutput, colorResources: _resourceStore);
        var active = ActiveInputs();
        plan = plan with { Items = plan.Items.Select((item, planIndex) =>
        {
            var (_, index) = active[planIndex];
            if (_metadata.ElementAtOrDefault(index) is not null) return item;
            var issue = !_metadataRead[index]
                ? new JobIssue("export.metadata-pending", "Reading authoritative source metadata…", JobIssueSeverity.Warning)
                : new JobIssue("export.metadata-failed", _metadataDiagnostics.ElementAtOrDefault(index)
                    ?? "Source metadata could not be read. Check source availability and reopen Export.", JobIssueSeverity.Error);
            return item with { Issues = item.Issues.Where(value => value.Code != "encoding.materialization-unsupported").Append(issue).ToList() };
        }).ToList() };
        var extra = new List<JobIssue>();
        if (ActiveInputs().Any(value => SourcePending(value.index)) || _encoder is null)
            extra.Add(new("export.metadata-pending", "Source metadata is still loading. Export will be available after preflight completes.", JobIssueSeverity.Error));
        if (_preflightError is not null)
            extra.Add(new("export.preflight", _preflightError, JobIssueSeverity.Error));
        foreach (var (input, index) in ActiveInputs())
            if (UsesRange(index) && _resolvedRanges.ElementAtOrDefault(index) is { RetainsSourceEof: false, BoundaryDiagnostic: { } diagnostic })
                extra.Add(new("export.out-corrected", $"{Path.GetFileName(input.SourcePath)}: {diagnostic}", JobIssueSeverity.Warning));
        var itemIssues = new Dictionary<int, List<JobIssue>>();
        if (DestinationMode == ExportDestinationMode.SpecificFolder &&
            (string.IsNullOrWhiteSpace(Destination) || !Path.IsPathFullyQualified(Destination)))
            extra.Add(new("export.destination", "Choose a valid absolute output folder.", JobIssueSeverity.Error));
        if (CreateSubfolder)
            try { OutputDestinationPlanner.ResolveSubfolderName(Resolution, SubfolderName); }
            catch (ArgumentException ex) { extra.Add(new("export.subfolder", ex.Message, JobIssueSeverity.Error)); }
        // Same as Source resolves codec per source. Validate that actual codec, not
        // the H.264 placeholder (which incorrectly rejects supported HEVC P010).
        extra.AddRange(plan.Items.Where((item, index) => Codec != ExportCodecChoice.SameAsSource
                || _metadata.ElementAtOrDefault(active[index].index) is not null).SelectMany(item => EncodingOptionValidator.Validate(
            item.Definition.MaterializedExport?.Encoding ?? options.Encoding)).Distinct()
            .Select(message => new JobIssue("export.options", message, JobIssueSeverity.Error)));
        if (_encoder is not null && !_encoder.IsUsable) extra.Add(new("export.encoder-unavailable",
            "NVIDIA NVENC could not be initialized. Check the hardware acceleration details.", JobIssueSeverity.Error));
        if (_metadataRead.Any(value => value))
            foreach (var (value, planIndex) in ActiveInputs().Select((value, planIndex) => (value, planIndex)))
            {
                var (input, index) = value;
                if ((input.ExportProvenance?.Kind == ExportItemKind.Subclip || _useRanges[index])
                    && input.InitialTrim is { IsFullSource: false }
                    && _rangeRead[index] && _metadata.ElementAtOrDefault(index) is not null
                    && _resolvedRanges.ElementAtOrDefault(index) is null)
                    itemIssues[planIndex] = [new("export.range-unresolved",
                        _rangeDiagnostics[index] ?? (input.RangeIsFixed
                            ? $"The fixed Subclip range for '{input.ExportProvenance?.SubclipName}' could not be validated."
                            : $"The saved In/Out range for '{Path.GetFileName(input.SourcePath)}' could not be validated. Ignore its In/Out range to export the full video."),
                        JobIssueSeverity.Error)];
            }
        if (itemIssues.Count > 0)
            plan = plan with
            {
                Items = plan.Items.Select((item, index) => itemIssues.TryGetValue(index, out var additions)
                    ? item with { Issues = item.Issues.Concat(additions).ToList() }
                    : item).ToList()
            };
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

    private void SetAllRanges(bool use, bool refreshWhenUnchanged = false)
    {
        var changed = false;
        for (var index = 0; index < _useRanges.Length; index++)
        {
            if (_handoff.Inputs[index].RangeIsFixed ||
                _handoff.Inputs[index].InitialTrim is not { IsFullSource: false } || _useRanges[index] == use) continue;
            _useRanges[index] = use;
            changed = true;
        }
        if (changed || refreshWhenUnchanged) Refresh();
    }

    private IReadOnlyList<ExportSubmissionItem> BuildSubmissionItems()
    {
        var planned = _plan?.Items;
        return ActiveInputs().Select((value, planIndex) =>
        {
            var (input, index) = value;
            var sourceName = Path.GetFileName(input.SourcePath);
            var hasRange = input.InitialTrim is { IsFullSource: false };
            var useRange = input.ExportProvenance?.Kind == ExportItemKind.Subclip ||
                hasRange && !input.RangeIsFixed && _useRanges[index];
            var outputText = "Output name unresolved";
            var item = planned?.ElementAtOrDefault(planIndex);
            if (item is not null)
                outputText = item.Definition.MaterializedName?.Problem is null && item.OutputPaths.FirstOrDefault() is { } path
                    ? "→ " + Path.GetFileName(path)
                    : "Output name unresolved";
            var proposedRange = useRange
                ? item?.Definition.ResolvedRange?.RequestedRange ?? item?.Definition.MediaRange ?? input.InitialTrim
                : item?.Definition.MediaRange;
            var sourceDuration = proposedRange?.SourceDuration ?? input.InitialTrim?.SourceDuration
                ?? (_metadata.ElementAtOrDefault(index) is { } metadata ? TimeSpan.FromSeconds(metadata.DurationSeconds) : TimeSpan.Zero);
            var timeline = MediaRangeTimelinePresentation.For(sourceName, sourceDuration, proposedRange, useRange, "Export");
            var rangeAutomationName = hasRange
                ? $"Use In/Out for {sourceName}"
                : $"Use In/Out for {sourceName}, unavailable because no In/Out is defined";
            var displayName = input.ExportProvenance?.SubclipName ?? sourceName;
            var issues = (item?.Issues ?? []).Where(issue => issue.Code != "export.metadata-pending")
                .Concat(GlobalErrors.Where(issue => issue.Code is "export.encoder-unavailable" or "export.preflight")).ToArray();
            if (useRange && _resolvedRanges.ElementAtOrDefault(index) is { RetainsSourceEof: false, BoundaryDiagnostic: { } correction })
                issues = issues.Append(new JobIssue("export.out-corrected", correction, JobIssueSeverity.Warning)).ToArray();
            var sourceError = _metadataRead[index] && _metadata.ElementAtOrDefault(index) is null
                || UsesRange(index) && _rangeRead[index] && _resolvedRanges.ElementAtOrDefault(index) is null;
            if (sourceError && issues.Length == 0)
                issues = [new("export.preflight", "The source range could not be verified. Reopen Export and review the saved Subclip.", JobIssueSeverity.Error)];
            var state = sourceError ? ExportFilePreflightState.Error
                : SourcePending(index) || _encoder is null
                    ? _checking[index] || _metadataRead[index] ? ExportFilePreflightState.Checking : ExportFilePreflightState.Pending
                : issues.Any(issue => issue.Severity == JobIssueSeverity.Error) ? ExportFilePreflightState.Error
                : issues.Length > 0 ? ExportFilePreflightState.Warning : ExportFilePreflightState.Ready;
            return new ExportSubmissionItem(index, item?.Definition.Id ?? Guid.Empty, displayName, outputText, $"Output for {displayName}: {outputText.TrimStart('→', ' ')}",
                !input.RangeIsFixed && hasRange, useRange,
                !input.RangeIsFixed && hasRange && _rangesGloballyEnabled, rangeAutomationName, timeline.SegmentLeft, timeline.SegmentWidth,
                timeline.ToolTip, timeline.AutomationName, issues, state,
                item?.Disposition == JobPlanDisposition.Skip,
                item?.Issues.Any(issue => issue.Code == "encoding.existing-output-replaced") == true);
        }).ToArray();
    }

    private static bool ProspectiveStateMatches(IReadOnlyList<EncodingHandoffInput> expected,
        IReadOnlyList<EncodingHandoffInput> actual) => expected.Count == actual.Count &&
        expected.Zip(actual).All(pair =>
            pair.First.AssetId == pair.Second.AssetId &&
            pair.First.SourcePath == pair.Second.SourcePath &&
            pair.First.FileSizeBytes == pair.Second.FileSizeBytes &&
            pair.First.SourceIdentity == pair.Second.SourceIdentity &&
            pair.First.InitialTrim == pair.Second.InitialTrim &&
            pair.First.AssignedColor == pair.Second.AssignedColor &&
            pair.First.Rotation == pair.Second.Rotation &&
            pair.First.ExportProvenance == pair.Second.ExportProvenance);

    private IReadOnlyList<(EncodingHandoffInput input, int index)> ActiveInputs() => _handoff.Inputs
        .Select((input, index) => (input, index))
        .Where(value => value.input.ExportProvenance?.Kind != ExportItemKind.NoSubclipFullSourceFallback ||
            _includeNoSubclipSources)
        .ToArray();

    private double? EstimateOutputBytes()
    {
        if (_plan is null || _plan.Items.Count == 0 || _metadata.Any(value => value is null)) return null;
        double totalBits = 0;
        foreach (var item in _plan.Items)
        {
            var settings = item.Definition.MaterializedExport;
            var duration = item.Definition.MediaRange?.EffectiveDuration;
            if (settings is null || settings.Encoding.RateControl != RateControlMode.ConstantBitrate
                || duration is null or { Ticks: <= 0 }) return null;
            var bitsPerSecond = settings.Encoding.TargetBitrateMbps * 1_000_000d;
            if (item.Definition.SourceHasAudio != false)
            {
                if (settings.Audio.Mode == MaterializedAudioMode.SourceCopyPreferred) return null;
                if (settings.Audio.Mode == MaterializedAudioMode.EncodedAac)
                    bitsPerSecond += (settings.Audio.Fallback?.BitrateKbps ?? 0) * 1_000d;
            }
            totalBits += bitsPerSecond * duration.Value.TotalSeconds;
        }
        return totalBits / 8d * 1.02d;
    }

    private static string FormatBytes(double bytes) => bytes >= 1_000_000_000
        ? $"{bytes / 1_000_000_000:0.00} GB"
        : bytes >= 1_000_000 ? $"{bytes / 1_000_000:0} MB" : $"{bytes / 1_000:0} KB";

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
        if (ActiveInputs().FirstOrDefault().input is not { } input) return "Preview unavailable";
        var stem = NamePartsRenderer.Materialize(new(NameParts.ToArray(), Separator),
            new(input.NamingOriginalName ?? Path.GetFileNameWithoutExtension(input.SourcePath), 1,
                IndexNumberBasis: input.NamingIndexNumberBasis ?? Path.GetFileNameWithoutExtension(input.SourcePath)));
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
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; _revision++; Refresh(); OnChanged(name); }
    private void OnChanged([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new(name));
    public event PropertyChangedEventHandler? PropertyChanged;
}
