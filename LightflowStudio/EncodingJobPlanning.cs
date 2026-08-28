using System.IO;

namespace LightflowStudio;

internal sealed record EncodingJobOptions(
    string InputFolder,
    string OutputRoot,
    OutputResolution Resolution,
    RecoveryStrategy Recovery,
    EncodingOptions Encoding,
    string? LutPath,
    string FilenameSuffix,
    bool PreserveFolderStructure,
    bool OverwriteExistingFiles,
    bool DetailedOutput,
    bool IncludeSubfolders = false,
    EncodingColorMode ColorMode = EncodingColorMode.OriginalOrManual,
    int ParallelExports = EncodingJobConcurrency.Default,
    ExportMaterializationPolicy? MaterializationPolicy = null,
    NamePartsDefinition? Naming = null);

internal static class EncodingJobConcurrency
{
    public const int Minimum = 1;
    public const int Maximum = 8;
    public const int Default = 2;

    public static int Validate(int value)
    {
        if (value is < Minimum or > Maximum)
            throw new ArgumentOutOfRangeException(nameof(value), value,
                $"Parallel exports must be between {Minimum} and {Maximum}.");
        return value;
    }
}

internal sealed record EncodingSource(
    string Path,
    long FileSizeBytes,
    TimeSpan? SourceDuration,
    MediaRange? MediaRange = null,
    ResolvedMediaRange? ResolvedRange = null,
    long? LastWriteUtcTicks = null,
    bool? HasAudio = null,
    int? CapabilityOrder = null,
    MaterializedColorPipeline? AssignedColor = null,
    SourceMediaTraits? MediaTraits = null,
    MaterializedExportSettings? RestoredExport = null,
    DateTimeOffset? NamingTimestamp = null,
    MaterializedName? RestoredName = null,
    string? NamingOriginalName = null,
    string? NamingIndexNumberBasis = null,
    ExportItemProvenance? ExportProvenance = null);

internal sealed record EncodingItemResult(
    int ExitCode,
    TimeSpan? SourceDuration,
    MediaRange? RequestedRange,
    TimeSpan? EffectiveDuration);

internal sealed record OutputFileSnapshot(bool Exists, long Length)
{
    public static OutputFileSnapshot Read(string path)
    {
        var file = new FileInfo(path);
        return new(file.Exists, file.Exists ? file.Length : 0);
    }
}

internal static class EncodingJobPlanner
{
    public static JobDefinition<EncodingJobOptions> Define(
        EncodingJobOptions options,
        IEnumerable<EncodingSource> sources,
        Guid? jobId = null,
        DateTimeOffset? createdAt = null)
    {
        if (options.Naming is { } naming)
            options = options with { Naming = naming with { Parts = naming.Parts.ToArray() } };
        var orderedSources = sources
            .OrderBy(source => source.CapabilityOrder.HasValue ? 0 : 1)
            .ThenBy(source => source.CapabilityOrder)
            .ThenBy(source => source.CapabilityOrder.HasValue ? null : source.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var items = orderedSources.Select((source, index) => new JobItemDefinition(
                Guid.NewGuid(),
                source.Path,
                source.FileSizeBytes,
                source.ResolvedRange is { } resolved
                    ? new MediaRange(resolved.RequestedRange.SourceDuration, resolved.RequestedRange.In,
                        resolved.ExclusiveOut - resolved.SourceStartTimestamp)
                    : source.MediaRange ?? (source.SourceDuration is { } duration && duration > TimeSpan.Zero
                        ? new MediaRange(duration)
                        : null),
                source.ResolvedRange,
                source.LastWriteUtcTicks,
                source.HasAudio,
                source.AssignedColor,
                ExportSettingsMaterializer.Materialize(options, source),
                source.RestoredName ?? (options.Naming is { } naming
                    ? NamePartsRenderer.Materialize(naming,
                        new(source.NamingOriginalName ?? Path.GetFileNameWithoutExtension(source.Path), index + 1,
                            source.NamingTimestamp,
                            source.NamingIndexNumberBasis ?? Path.GetFileNameWithoutExtension(source.Path)))
                    : null),
                source.ExportProvenance))
            .ToList();
        return new(jobId ?? Guid.NewGuid(), "video.encode", createdAt ?? DateTimeOffset.Now, options, items);
    }

    public static JobPlan<EncodingJobOptions> Plan(
        JobDefinition<EncodingJobOptions> definition,
        Func<string, OutputFileSnapshot>? inspectOutput = null,
        DateTimeOffset? plannedAt = null,
        string? identityCacheDirectory = null,
        IEncodingLutResourceStore? colorResources = null)
    {
        try { EncodingJobConcurrency.Validate(definition.Options.ParallelExports); }
        catch (ArgumentOutOfRangeException exception)
        {
            return new(definition, plannedAt ?? DateTimeOffset.Now, [],
                [new("encoding.parallel-exports", exception.Message, JobIssueSeverity.Error)], JobWorkUnit.Items);
        }
        inspectOutput ??= OutputFileSnapshot.Read;
        var issues = new List<JobIssue>();
        if (definition.Items.Count == 0)
            issues.Add(new("encoding.no-inputs", "Select at least one video file for this batch.", JobIssueSeverity.Error));
        if (definition.Options.ColorMode == EncodingColorMode.OriginalOrManual && !LutPathIsValid(definition.Options.LutPath))
            issues.Add(new("encoding.invalid-lut", "Select a valid .cube LUT or choose No LUT.", JobIssueSeverity.Error));
        if (definition.Options.ColorMode == EncodingColorMode.Assigned && !string.IsNullOrEmpty(definition.Options.LutPath))
            issues.Add(new("encoding.ambiguous-color", "Assigned Color cannot be combined with a manual Export LUT.", JobIssueSeverity.Error));
        if (definition.Options.MaterializationPolicy is not null && !string.IsNullOrEmpty(definition.Options.LutPath))
            issues.Add(new("encoding.ambiguous-color", "Camera/Creative Export policies cannot be combined with a legacy manual Export LUT.", JobIssueSeverity.Error));
        colorResources ??= new EncodingLutResourceStore(EncodingLutResourceStore.DefaultDirectory);

        var outputJobs = definition.Items.Select(item => new
        {
            Item = item,
            Settings = item.MaterializedExport ?? LegacySettings(definition.Options, item),
            Path = CreateOutputPath(definition.Options, item,
                item.MaterializedExport ?? LegacySettings(definition.Options, item))
        }).ToList();

        var collisions = outputJobs
            .GroupBy(item => NormalizePath(item.Path), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourcePaths = definition.Items.Select(item => NormalizePath(item.SourceIdentity))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (collisions.Count > 0)
            issues.Add(new("encoding.output-collision", "Multiple selected files would create the same output filename.", JobIssueSeverity.Error));

        var useDuration = definition.Items.All(item => item.MediaRange?.EffectiveDuration > TimeSpan.Zero);
        var workUnit = useDuration ? JobWorkUnit.MediaDuration : JobWorkUnit.Items;
        var planItems = outputJobs.Select(output =>
        {
            var itemIssues = new List<JobIssue>();
            if (!string.IsNullOrWhiteSpace(output.Item.MaterializedName?.Problem))
                itemIssues.Add(new("naming.unresolved", output.Item.MaterializedName.Problem, JobIssueSeverity.Error));
            if (output.Item.MaterializedName is { Stem: { } stem } && WindowsOutputNameValidator.ValidateStem(stem) is { } nameProblem)
                itemIssues.Add(new("naming.invalid-filename", nameProblem, JobIssueSeverity.Error));
            if (Path.GetFileName(output.Path).Length > 255 || Path.GetFullPath(output.Path).Length > 32767)
                itemIssues.Add(new("naming.path-too-long", $"The planned output path is too long for Windows: {output.Path}", JobIssueSeverity.Error));
            if (!string.IsNullOrWhiteSpace(output.Settings.MaterializationProblem))
                itemIssues.Add(new("encoding.materialization-unsupported", output.Settings.MaterializationProblem, JobIssueSeverity.Error));
            if (output.Item.MediaRange is { } range) itemIssues.AddRange(range.Validate());
            if (output.Item.ResolvedRange is { } resolvedRange) itemIssues.AddRange(resolvedRange.Validate());
            if (sourcePaths.Contains(NormalizePath(output.Path)))
                itemIssues.Add(new("encoding.source-overwrite",
                    $"The planned output path collides with a selected source file: {output.Path}",
                    JobIssueSeverity.Error));
            if (sourcePaths.Contains(NormalizePath(EncodingOutputLifecycle.PartialPathFor(output.Path))))
                itemIssues.Add(new("encoding.partial-source-collision", "The Lightflow partial output path would collide with a selected source file.", JobIssueSeverity.Error));
            if (collisions.Contains(NormalizePath(output.Path)))
                itemIssues.Add(new("encoding.output-collision", $"The planned output collides with another item: {output.Path}", JobIssueSeverity.Error));
            if (output.Settings.Color is { ColorEnabled: true } color)
            {
                foreach (var resource in color.OrderedPipeline)
                {
                    try { colorResources.Resolve(resource); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        itemIssues.Add(new($"encoding.missing-{resource.Stage.ToString().ToLowerInvariant()}-lut",
                            $"{Path.GetFileName(output.Item.SourceIdentity)} — {EncodingLutResourceStore.StageName(resource.Stage)} LUT '{resource.DisplayName}': {exception.Message}",
                            JobIssueSeverity.Error));
                    }
                }
            }

            var snapshot = inspectOutput(output.Path);
            var preserveExisting = ExistingOutputPolicy.ShouldPreserve(
                definition.Options.OverwriteExistingFiles,
                snapshot.Exists,
                snapshot.Length);
            if (preserveExisting)
            {
                _ = EncodingOutputIdentityStore.Matches(output.Path,
                    EncodingOutputIdentity.Create(output.Item, definition.Options), identityCacheDirectory);
                itemIssues.Add(new("encoding.existing-output-differs",
                    "An output file already exists at this location. It will be skipped unless “Overwrite existing files” is enabled.",
                    JobIssueSeverity.Warning));
            }
            var estimate = useDuration
                ? JobWorkEstimate.Determinate(JobWorkUnit.MediaDuration, output.Item.MediaRange!.EffectiveDuration.TotalSeconds)
                : JobWorkEstimate.Determinate(JobWorkUnit.Items, 1);
            return new JobPlanItem(
                output.Item,
                [output.Path],
                preserveExisting ? JobPlanDisposition.Skip : JobPlanDisposition.Process,
                estimate,
                itemIssues);
        }).ToList();

        return new(definition, plannedAt ?? DateTimeOffset.Now, planItems, issues, workUnit);
    }

    private static string CreateOutputPath(EncodingJobOptions options, JobItemDefinition item,
        MaterializedExportSettings settings)
    {
        if (options.Naming is null)
            return EncodingPathPlanner.CreateJob(options.InputFolder, options.OutputRoot, item.SourceIdentity,
                settings.Resolution, settings.Encoding.Container, options.FilenameSuffix,
                options.PreserveFolderStructure).OutputPath;
        var relativeDirectory = options.PreserveFolderStructure
            ? Path.GetDirectoryName(Path.GetRelativePath(options.InputFolder, item.SourceIdentity)) ?? ""
            : "";
        var stem = item.MaterializedName?.Stem ?? $".lightflow-unresolved-name-{item.Id:N}";
        return Path.Combine(options.OutputRoot, relativeDirectory,
            stem + EncodingPathPlanner.ContainerExtension(settings.Encoding.Container));
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool LutPathIsValid(string? path) => string.IsNullOrEmpty(path)
        || (path.EndsWith(".cube", StringComparison.OrdinalIgnoreCase) && File.Exists(path));

    internal static MaterializedExportSettings LegacySettings(EncodingJobOptions options, JobItemDefinition item) =>
        new(EncodingOptions.Normalize(options.Encoding), options.Resolution,
            options.Encoding.AudioMode switch
            {
                AudioEncodingMode.Copy => new(MaterializedAudioMode.SourceCopyPreferred,
                    new(options.Encoding.AudioBitrateKbps, options.Encoding.AudioSampleRate, options.Encoding.AudioChannels)),
                AudioEncodingMode.Aac => new(MaterializedAudioMode.EncodedAac,
                    new(options.Encoding.AudioBitrateKbps, options.Encoding.AudioSampleRate, options.Encoding.AudioChannels)),
                _ => new(MaterializedAudioMode.None)
            }, options.ColorMode == EncodingColorMode.Assigned ? item.AssignedColor : null, null,
            EncodingQualityPolicy.Explicit);
}
