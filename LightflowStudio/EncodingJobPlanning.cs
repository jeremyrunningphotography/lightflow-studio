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
    EncodingColorMode ColorMode = EncodingColorMode.OriginalOrManual);

internal sealed record EncodingSource(
    string Path,
    long FileSizeBytes,
    TimeSpan? SourceDuration,
    MediaRange? MediaRange = null,
    ResolvedMediaRange? ResolvedRange = null,
    long? LastWriteUtcTicks = null,
    bool? HasAudio = null,
    int? CapabilityOrder = null,
    MaterializedColorPipeline? AssignedColor = null);

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
        var items = sources
            .OrderBy(source => source.CapabilityOrder.HasValue ? 0 : 1)
            .ThenBy(source => source.CapabilityOrder)
            .ThenBy(source => source.CapabilityOrder.HasValue ? null : source.Path, StringComparer.OrdinalIgnoreCase)
            .Select(source => new JobItemDefinition(
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
                source.AssignedColor))
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
        inspectOutput ??= OutputFileSnapshot.Read;
        var issues = new List<JobIssue>();
        if (definition.Items.Count == 0)
            issues.Add(new("encoding.no-inputs", "Select at least one video file for this batch.", JobIssueSeverity.Error));
        if (definition.Options.ColorMode == EncodingColorMode.OriginalOrManual && !LutPathIsValid(definition.Options.LutPath))
            issues.Add(new("encoding.invalid-lut", "Select a valid .cube LUT or choose No LUT.", JobIssueSeverity.Error));
        if (definition.Options.ColorMode == EncodingColorMode.Assigned && !string.IsNullOrEmpty(definition.Options.LutPath))
            issues.Add(new("encoding.ambiguous-color", "Assigned Color cannot be combined with a manual Encoding LUT.", JobIssueSeverity.Error));
        colorResources ??= new EncodingLutResourceStore(EncodingLutResourceStore.DefaultDirectory);

        var outputJobs = definition.Items.Select(item => new
        {
            Item = item,
            Path = EncodingPathPlanner.CreateJob(
                definition.Options.InputFolder,
                definition.Options.OutputRoot,
                item.SourceIdentity,
                definition.Options.Resolution,
                definition.Options.Encoding.Container,
                definition.Options.FilenameSuffix,
                definition.Options.PreserveFolderStructure).OutputPath
        }).ToList();

        var collisions = outputJobs
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourcePaths = definition.Items.Select(item => Path.GetFullPath(item.SourceIdentity))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (collisions.Count > 0)
            issues.Add(new("encoding.output-collision", "Multiple selected files would create the same output filename.", JobIssueSeverity.Error));

        var useDuration = definition.Items.All(item => item.MediaRange?.EffectiveDuration > TimeSpan.Zero);
        var workUnit = useDuration ? JobWorkUnit.MediaDuration : JobWorkUnit.Items;
        var planItems = outputJobs.Select(output =>
        {
            var itemIssues = new List<JobIssue>();
            if (output.Item.MediaRange is { } range) itemIssues.AddRange(range.Validate());
            if (output.Item.ResolvedRange is { } resolvedRange) itemIssues.AddRange(resolvedRange.Validate());
            if (string.Equals(Path.GetFullPath(output.Item.SourceIdentity), Path.GetFullPath(output.Path), StringComparison.OrdinalIgnoreCase))
                itemIssues.Add(new("encoding.source-overwrite", "The output path cannot be the same as the source path.", JobIssueSeverity.Error));
            if (sourcePaths.Contains(EncodingOutputLifecycle.PartialPathFor(output.Path)))
                itemIssues.Add(new("encoding.partial-source-collision", "The Lightflow partial output path would collide with a selected source file.", JobIssueSeverity.Error));
            if (collisions.Contains(output.Path))
                itemIssues.Add(new("encoding.output-collision", $"The planned output collides with another item: {output.Path}", JobIssueSeverity.Error));
            if (definition.Options.ColorMode == EncodingColorMode.Assigned && output.Item.AssignedColor is { ColorEnabled: true } color)
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
            if (preserveExisting && output.Item.ResolvedRange is not null
                && !EncodingOutputIdentityStore.Matches(output.Path, EncodingOutputIdentity.Create(output.Item, definition.Options), identityCacheDirectory))
                itemIssues.Add(new("encoding.existing-output-differs",
                    "The existing output was preserved, but it was created with a different source, trim, or encoding configuration.",
                    JobIssueSeverity.Warning));
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

    private static bool LutPathIsValid(string? path) => string.IsNullOrEmpty(path)
        || (path.EndsWith(".cube", StringComparison.OrdinalIgnoreCase) && File.Exists(path));
}
