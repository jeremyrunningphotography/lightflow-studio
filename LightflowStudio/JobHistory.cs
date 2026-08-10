using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightflowStudio;

internal sealed record EncodingJobHistoryRecord(
    Guid JobId, string Capability, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt,
    DateTimeOffset CompletedAt, JobState State, JobDefinition<EncodingJobOptions> Definition,
    JobPlan<EncodingJobOptions> Plan, JobResult<EncodingItemResult> Result)
{
    public string CompletedDisplay => CompletedAt.ToLocalTime().ToString("g");
    public string CapabilityDisplay => "Batch Encoding";
    public string StateDisplay => State == JobState.CompletedWithWarnings ? "Completed with warnings" : State.ToString();
    public string SummaryDisplay => $"{Result.Summary.Completed + Result.Summary.CompletedWithWarnings} completed · {Result.Summary.Skipped} skipped · {Result.Summary.Failed} failed · {Result.Summary.Cancelled} cancelled";
    public string DetailDisplay => JobHistoryPresentation.Describe(this);
}

internal interface IJobHistoryStore
{
    IReadOnlyList<EncodingJobHistoryRecord> Load();
    void Add(EncodingJobHistoryRecord record);
}

internal sealed class JobHistoryStore : IJobHistoryStore
{
    public const int SchemaVersion = 1;
    public const int MaximumRecords = 100;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    private readonly string _path;

    public JobHistoryStore(string path) => _path = path;
    public static string StorePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Jeremy Running Photography", "Lightflow Studio", "job-history.json");

    public IReadOnlyList<EncodingJobHistoryRecord> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var root = document.RootElement;
            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var schemaVersion) || schemaVersion != SchemaVersion
                || !root.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array) return [];
            return records.EnumerateArray().Select(TryRead).Where(record => record is not null)
                .Cast<EncodingJobHistoryRecord>().OrderByDescending(record => record.CompletedAt).Take(MaximumRecords).ToList();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException) { return []; }
    }

    public void Add(EncodingJobHistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var records = Load().Where(existing => existing.JobId != record.JobId).Append(record)
            .OrderByDescending(existing => existing.CompletedAt).Take(MaximumRecords).ToList();
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new HistoryDocument(SchemaVersion, records), JsonOptions));
            File.Move(temporary, _path, true);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }

    private static EncodingJobHistoryRecord? TryRead(JsonElement element)
    {
        try
        {
            var record = element.Deserialize<EncodingJobHistoryRecord>(JsonOptions);
            return record is not null && record.Definition is not null && record.Definition.Options is not null
                && record.Definition.Items is not null && record.Plan is not null && record.Plan.Items is not null
                && record.Result is not null && record.Result.Items is not null && record.Result.Summary is not null
                && record.JobId != Guid.Empty && record.Capability == "video.encode"
                && record.Definition.Id == record.JobId && record.Result.JobId == record.JobId ? record : null;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException) { return null; }
    }

    private sealed record HistoryDocument(int Version, IReadOnlyList<EncodingJobHistoryRecord> Records);
}

internal static class JobHistoryPresentation
{
    public static string Describe(EncodingJobHistoryRecord record)
    {
        var lines = new List<string> { $"Job: {record.JobId}", $"Created: {record.CreatedAt.ToLocalTime():g}",
            $"Started: {(record.StartedAt is { } started ? started.ToLocalTime().ToString("g") : "Not recorded")}",
            $"Finished: {record.CompletedAt.ToLocalTime():g}", $"State: {record.StateDisplay}", $"Summary: {record.SummaryDisplay}" };
        if (record.Result.Warnings.Count > 0)
        {
            lines.Add("");
            lines.Add("Job warnings:");
            lines.AddRange(record.Result.Warnings.Select(warning => $"• {warning}"));
        }
        if (record.Result.Errors.Count > 0)
        {
            lines.Add("");
            lines.Add("Job errors:");
            lines.AddRange(record.Result.Errors.Select(error => $"• {error}"));
        }
        lines.Add("");
        lines.Add("Inputs and results:");
        foreach (var item in record.Plan.Items)
        {
            var result = record.Result.Items.FirstOrDefault(value => value.ItemId == item.Definition.Id);
            lines.Add($"• {item.Definition.SourceIdentity}");
            lines.Add($"  State: {result?.State.ToString() ?? "Unknown"}");
            if (item.Definition.MediaRange is { } range && !range.IsFullSource)
                lines.Add($"  Range: {range.EffectiveIn:c} – {range.EffectiveOut:c} ({range.EffectiveDuration:c})");
            foreach (var output in result?.OutputPaths ?? item.OutputPaths) lines.Add($"  Output: {output}");
            foreach (var warning in result?.Warnings ?? []) lines.Add($"  Warning: {warning}");
            foreach (var error in result?.Errors ?? []) lines.Add($"  Error: {error}");
        }
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record EncodingRerunSource(JobItemDefinition Item, bool IsAvailable, string? Problem);
internal sealed record EncodingRerunPreparation(EncodingJobOptions Options, IReadOnlyList<EncodingRerunSource> Sources)
{
    public IReadOnlyList<EncodingRerunSource> Available => Sources.Where(source => source.IsAvailable).ToList();
}
internal sealed record EncodingRerunRestoration(
    IReadOnlyList<BatchFileOption> Restored,
    IReadOnlyList<EncodingRerunSource> Unavailable);

internal static class EncodingHistoryRerun
{
    public static EncodingRerunPreparation Prepare(EncodingJobHistoryRecord record)
    {
        var sources = record.Definition.Items.Select(item =>
        {
            try
            {
                var info = new FileInfo(item.SourceIdentity);
                if (!info.Exists) return new EncodingRerunSource(item, false, "Source file is missing.");
                if (item.SourceSizeBytes is { } size && info.Length != size) return new EncodingRerunSource(item, false, "Source file size has changed.");
                if (item.SourceLastWriteUtcTicks is { } ticks && info.LastWriteTimeUtc.Ticks != ticks) return new EncodingRerunSource(item, false, "Source file has changed.");
                return new EncodingRerunSource(item, true, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            { return new EncodingRerunSource(item, false, "Source file could not be inspected."); }
        }).ToList();
        return new(record.Definition.Options, sources);
    }

    public static EncodingRerunRestoration Materialize(EncodingRerunPreparation preparation)
    {
        var restored = new List<BatchFileOption>();
        var unavailable = preparation.Sources.Where(source => !source.IsAvailable).ToList();
        foreach (var source in preparation.Available)
        {
            try
            {
                var info = new FileInfo(source.Item.SourceIdentity);
                var currentIdentity = TrimSourceIdentity.Read(source.Item.SourceIdentity);
                if (!info.Exists || currentIdentity is null
                    || source.Item.SourceSizeBytes is { } size && info.Length != size
                    || source.Item.SourceLastWriteUtcTicks is { } ticks && currentIdentity.LastWriteUtcTicks != ticks)
                {
                    unavailable.Add(source with { IsAvailable = false, Problem = "Source file changed before it could be restored." });
                    continue;
                }

                var option = new BatchFileOption(source.Item.SourceIdentity,
                    Path.GetRelativePath(preparation.Options.InputFolder, source.Item.SourceIdentity), info.Length);
                if (source.Item.MediaRange is { } range && !range.IsFullSource) option.ApplyTrim(range);
                option.IsSelected = true;
                restored.Add(option);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                unavailable.Add(source with { IsAvailable = false, Problem = "Source file could not be restored." });
            }
        }
        return new(restored, unavailable);
    }

    public static string RestorationMessage(EncodingRerunRestoration restoration)
    {
        var restored = restoration.Restored.Count;
        var unavailable = restoration.Unavailable.Count;
        return unavailable == 0
            ? $"Restored {restored} file{(restored == 1 ? "" : "s")} from History — review before encoding"
            : $"Restored {restored} unchanged file{(restored == 1 ? "" : "s")}; {unavailable} unavailable";
    }
}
