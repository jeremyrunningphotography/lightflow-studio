using System.IO;

namespace LightflowStudio;

internal static class EncodingJobRecovery
{
    public static IReadOnlyList<JobIssue> Revalidate(
        JobPlanItem item,
        EncodingJobOptions options,
        string? identityCacheDirectory = null,
        IEncodingLutResourceStore? colorResources = null)
    {
        var issues = new List<JobIssue>();
        try
        {
            var source = new FileInfo(item.Definition.SourceIdentity);
            if (!source.Exists)
                issues.Add(Error("jobs.source-missing", "The materialized source is missing."));
            else
            {
                if (item.Definition.SourceSizeBytes is { } size && source.Length != size)
                    issues.Add(Error("jobs.source-size-changed", "The materialized source size changed."));
                if (item.Definition.SourceLastWriteUtcTicks is { } ticks && source.LastWriteTimeUtc.Ticks != ticks)
                    issues.Add(Error("jobs.source-modified", "The materialized source was modified."));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { issues.Add(Error("jobs.source-unreadable", $"The materialized source cannot be inspected: {exception.Message}")); }

        var settings = item.Definition.MaterializedExport ?? EncodingJobPlanner.LegacySettings(options, item.Definition);
        if (settings.Color is { ColorEnabled: true } color)
        {
            colorResources ??= new EncodingLutResourceStore(EncodingLutResourceStore.DefaultDirectory);
            foreach (var resource in color.OrderedPipeline)
            {
                try { colorResources.Resolve(resource); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                { issues.Add(Error("jobs.lut-unavailable", $"Materialized LUT '{resource.DisplayName}' is unavailable: {exception.Message}")); }
            }
        }

        foreach (var output in item.OutputPaths)
        {
            if (!File.Exists(output)) continue;
            if (!EncodingOutputIdentityStore.Matches(output,
                    EncodingOutputIdentity.Create(item.Definition, options), identityCacheDirectory))
                issues.Add(Error("jobs.output-identity-changed", "An output exists but does not match the materialized output identity."));
        }
        return issues;
    }

    private static JobIssue Error(string code, string message) => new(code, message, JobIssueSeverity.Error);
}
