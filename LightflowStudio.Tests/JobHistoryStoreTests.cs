using System.Text.Json;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class JobHistoryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LightflowHistoryTests", Guid.NewGuid().ToString("N"));
    private string StorePath => Path.Combine(_root, "job-history.json");

    public JobHistoryStoreTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void MissingStore_IsAnEmptyHistory() => Assert.Empty(new JobHistoryStore(StorePath).Load());

    [Fact]
    public void CompletedEncodingJob_RoundTripsTypedOptionsResultsAndRanges()
    {
        var record = Record(JobState.CompletedWithWarnings, DateTimeOffset.Parse("2026-01-02T03:04:05Z"));
        new JobHistoryStore(StorePath).Add(record);

        var actual = Assert.Single(new JobHistoryStore(StorePath).Load());
        Assert.Equal(record.JobId, actual.JobId);
        Assert.Equal(OutputResolution.Qhd1440, actual.Definition.Options.Resolution);
        Assert.Equal(VideoCodec.Hevc, actual.Definition.Options.Encoding.Codec);
        Assert.Equal(JobState.CompletedWithWarnings, actual.Result.State);
        Assert.Equal("warning", Assert.Single(actual.Result.Items).Warnings.Single());
        Assert.Equal("error detail", actual.Result.Items.Single().Errors.Single());
        Assert.Equal(TimeSpan.FromSeconds(10), actual.Plan.Items.Single().Definition.MediaRange!.EffectiveIn);
        Assert.Equal(TimeSpan.FromSeconds(20), actual.Result.Items.Single().Data!.EffectiveDuration);
        Assert.False(File.Exists(StorePath + ".tmp"));
    }

    [Fact]
    public void CompletedEncodingJob_RoundTripsMaterializedColorAndRerunRetainsIt()
    {
        var hash = new string('a', 64);
        var color = new MaterializedColorPipeline(false,
            new MaterializedLutResource(Guid.NewGuid(), ColorLutStage.Camera, "Technical", hash,
                $"aa/{hash}.cube"),
            new MaterializedLutResource(Guid.NewGuid(), ColorLutStage.Creative, "Look", hash,
                $"aa/{hash}.cube"));
        var source = Path.Combine(_root, "color-source.mp4");
        File.WriteAllText(source, "source");
        var item = Item(source) with { AssignedColor = color };
        var record = Record(JobState.Completed, DateTimeOffset.UtcNow, [item]);
        var options = record.Definition.Options with { ColorMode = EncodingColorMode.Assigned };
        record = record with
        {
            Definition = record.Definition with { Options = options },
            Plan = record.Plan with { Definition = record.Definition with { Options = options } }
        };
        new JobHistoryStore(StorePath).Add(record);

        var loaded = Assert.Single(new JobHistoryStore(StorePath).Load());
        Assert.Equal(color, loaded.Definition.Items.Single().AssignedColor);
        var restored = Assert.Single(EncodingHistoryRerun.Materialize(
            EncodingHistoryRerun.Prepare(loaded)).Restored);
        Assert.Equal(color, restored.AssignedColor);
        Assert.True(restored.AssignedColor!.ColorEnabled);
    }

    [Fact]
    public void CompletedEncodingJob_RoundTripsNamingAndRerunRestoresDefinitionAndMaterializedName()
    {
        var source = Path.Combine(_root, "DJI_0042.MP4");
        File.WriteAllText(source, "source");
        var name = new MaterializedName("DJI_0042-0042", 1, "0042", null);
        var definition = new NamePartsDefinition([
            new(NamePartKind.OriginalName), new(NamePartKind.IndexNumber)
        ], NamePartSeparator.Hyphen);
        var record = Record(JobState.Completed, DateTimeOffset.UtcNow, [Item(source) with { MaterializedName = name }]);
        var options = record.Definition.Options with { Naming = definition };
        var jobDefinition = record.Definition with { Options = options };
        record = record with
        {
            Definition = jobDefinition,
            Plan = record.Plan with { Definition = jobDefinition }
        };
        new JobHistoryStore(StorePath).Add(record);

        var loaded = Assert.Single(new JobHistoryStore(StorePath).Load());
        Assert.Equal(NamePartSeparator.Hyphen, loaded.Definition.Options.Naming!.Separator);
        Assert.Equal(name, loaded.Definition.Items.Single().MaterializedName);
        var rerun = EncodingHistoryRerun.Prepare(loaded);
        Assert.Equal(NamePartKind.IndexNumber, rerun.Options.Naming!.Parts[1].Kind);
        Assert.Equal(name, EncodingHistoryRerun.Materialize(rerun).Restored.Single().RestoredName);
    }

    [Fact]
    public void MultipleRecords_LoadNewestFirst()
    {
        var store = new JobHistoryStore(StorePath);
        store.Add(Record(JobState.Completed, DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        store.Add(Record(JobState.Failed, DateTimeOffset.Parse("2026-01-03T00:00:00Z")));
        store.Add(Record(JobState.Cancelled, DateTimeOffset.Parse("2026-01-02T00:00:00Z")));
        Assert.Equal([JobState.Failed, JobState.Cancelled, JobState.Completed], store.Load().Select(record => record.State));
    }

    [Fact]
    public void Remove_AtomicallyDeletesOnlyExplicitBackingRecords()
    {
        var store = new JobHistoryStore(StorePath);
        var keep = Record(JobState.Completed, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var remove = Record(JobState.Failed, DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            [Item("legacy-a.mp4"), Item("legacy-b.mp4")]);
        store.Add(keep);
        store.Add(remove);

        Assert.Equal(1, store.Remove(new HashSet<Guid> { remove.JobId }));
        Assert.Equal(keep.JobId, Assert.Single(store.Load()).JobId);
        Assert.False(File.Exists(StorePath + ".tmp"));
    }

    [Fact]
    public void Remove_UnknownOrEmptyScopeDoesNotRewriteHistory()
    {
        var store = new JobHistoryStore(StorePath);
        var record = Record(JobState.Completed, DateTimeOffset.UtcNow);
        store.Add(record);
        var before = File.GetLastWriteTimeUtc(StorePath);

        Assert.Equal(0, store.Remove(new HashSet<Guid>()));
        Assert.Equal(0, store.Remove(new HashSet<Guid> { Guid.NewGuid() }));
        Assert.Equal(before, File.GetLastWriteTimeUtc(StorePath));
        Assert.Equal(record.JobId, Assert.Single(store.Load()).JobId);
    }

    [Theory]
    [InlineData((int)JobState.Completed)]
    [InlineData((int)JobState.CompletedWithWarnings)]
    [InlineData((int)JobState.Skipped)]
    [InlineData((int)JobState.Cancelled)]
    [InlineData((int)JobState.Failed)]
    public void TerminalOutcomes_RemainDistinct(int stateValue)
    {
        var state = (JobState)stateValue;
        var store = new JobHistoryStore(StorePath);
        store.Add(Record(state, DateTimeOffset.UtcNow));
        Assert.Equal(state, Assert.Single(store.Load()).State);
    }

    [Fact]
    public void MalformedDocumentAndUnsupportedVersion_AreIgnored()
    {
        File.WriteAllText(StorePath, "not json");
        Assert.Empty(new JobHistoryStore(StorePath).Load());
        File.WriteAllText(StorePath, "{\"version\":99,\"records\":[]}");
        Assert.Empty(new JobHistoryStore(StorePath).Load());
    }

    [Fact]
    public void MalformedIndividualRecord_DoesNotPoisonValidRecord()
    {
        var validPath = Path.Combine(_root, "valid.json");
        new JobHistoryStore(validPath).Add(Record(JobState.Completed, DateTimeOffset.UtcNow));
        using var document = JsonDocument.Parse(File.ReadAllText(validPath));
        var valid = document.RootElement.GetProperty("records")[0].GetRawText();
        File.WriteAllText(StorePath, $"{{\"version\":1,\"records\":[{{\"jobId\":7}},{valid}]}}");
        Assert.Single(new JobHistoryStore(StorePath).Load());
    }

    [Fact]
    public void Retention_KeepsNewestOneHundredRecords()
    {
        var store = new JobHistoryStore(StorePath);
        for (var index = 0; index < 105; index++) store.Add(Record(JobState.Completed, DateTimeOffset.UnixEpoch.AddMinutes(index)));
        var records = store.Load();
        Assert.Equal(JobHistoryStore.MaximumRecords, records.Count);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(104), records[0].CompletedAt);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(5), records[^1].CompletedAt);
    }

    [Fact]
    public void FailedTemporaryWrite_DoesNotReplaceExistingHistory()
    {
        var store = new JobHistoryStore(StorePath);
        var original = Record(JobState.Completed, DateTimeOffset.UnixEpoch);
        store.Add(original);
        Directory.CreateDirectory(StorePath + ".tmp");

        var failure = Xunit.Record.Exception(() => store.Add(Record(JobState.Failed, DateTimeOffset.UtcNow)));
        Assert.True(failure is IOException or UnauthorizedAccessException);

        Assert.Equal(original.JobId, Assert.Single(store.Load()).JobId);
    }

    [Fact]
    public void RerunPreparation_RejectsMissingAndChangedSourcesAndKeepsMatchingRange()
    {
        var matching = Path.Combine(_root, "matching.mp4");
        var changed = Path.Combine(_root, "changed.mp4");
        File.WriteAllText(matching, "source");
        File.WriteAllText(changed, "changed");
        var good = Item(matching);
        var stale = Item(changed) with { SourceSizeBytes = 1 };
        var missing = Item(Path.Combine(_root, "missing.mp4"));
        var record = Record(JobState.Completed, DateTimeOffset.UtcNow, [good, stale, missing]);

        var prepared = EncodingHistoryRerun.Prepare(record);
        var available = Assert.Single(prepared.Available);
        Assert.Equal(matching, available.Item.SourceIdentity);
        Assert.Equal(TimeSpan.FromSeconds(10), available.Item.MediaRange!.EffectiveIn);
        Assert.Equal(2, prepared.Sources.Count(source => !source.IsAvailable));
    }

    [Fact]
    public void RerunMaterialization_RestoresValidatedSourceEvenWhenFolderDiscoveryDoesNotReturnIt()
    {
        var inputFolder = Path.Combine(_root, "historic-input");
        var sourceFolder = Path.Combine(_root, "still-available-elsewhere");
        Directory.CreateDirectory(inputFolder);
        Directory.CreateDirectory(sourceFolder);
        var sourcePath = Path.Combine(sourceFolder, "source.mp4");
        File.WriteAllText(sourcePath, "source");
        var historic = Item(sourcePath);
        var original = Record(JobState.Completed, DateTimeOffset.UtcNow, [historic]);
        var options = original.Definition.Options with { InputFolder = inputFolder };
        var definition = original.Definition with { Options = options };
        var record = original with { Definition = definition, Plan = original.Plan with { Definition = definition } };

        Assert.Empty(BatchFileSelection.Discover(inputFolder, recursive: true));
        var restoration = EncodingHistoryRerun.Materialize(EncodingHistoryRerun.Prepare(record));

        var restored = Assert.Single(restoration.Restored);
        Assert.True(restored.IsSelected);
        Assert.Equal(sourcePath, restored.FilePath);
        Assert.Equal(historic.MediaRange, restored.TrimRange);
        Assert.Empty(restoration.Unavailable);
        Assert.Equal("Restored 1 file from History — review before export", EncodingHistoryRerun.RestorationMessage(restoration));
    }

    [Fact]
    public void RerunMaterialization_ReportsValidatedSourceThatDisappearsBeforeRowCreationAsUnavailable()
    {
        var sourcePath = Path.Combine(_root, "disappearing.mp4");
        File.WriteAllText(sourcePath, "source");
        var preparation = EncodingHistoryRerun.Prepare(Record(JobState.Completed, DateTimeOffset.UtcNow, [Item(sourcePath)]));
        Assert.Single(preparation.Available);
        File.Delete(sourcePath);

        var restoration = EncodingHistoryRerun.Materialize(preparation);

        Assert.Empty(restoration.Restored);
        Assert.Single(restoration.Unavailable);
        Assert.Equal("Restored 0 unchanged files; 1 unavailable", EncodingHistoryRerun.RestorationMessage(restoration));
    }

    private static JobItemDefinition Item(string path)
    {
        var info = new FileInfo(path);
        return new(Guid.NewGuid(), path, info.Exists ? info.Length : null,
            new MediaRange(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)),
            null, info.Exists ? info.LastWriteTimeUtc.Ticks : null, true);
    }

    private static EncodingJobHistoryRecord Record(JobState state, DateTimeOffset completedAt, IReadOnlyList<JobItemDefinition>? definitions = null)
    {
        definitions ??= [Item("source.mp4")];
        var options = new EncodingJobOptions("input", "output", OutputResolution.Qhd1440, RecoveryStrategy.Normal,
            EncodingPresetCatalog.Recommended with { Codec = VideoCodec.Hevc }, null, "_test", false, false, true);
        var id = Guid.NewGuid();
        var definition = new JobDefinition<EncodingJobOptions>(id, "video.encode", completedAt.AddMinutes(-2), options, definitions);
        var planItems = definitions.Select(item => new JobPlanItem(item, [$"{item.SourceIdentity}.out.mp4"],
            state == JobState.Skipped ? JobPlanDisposition.Skip : JobPlanDisposition.Process,
            JobWorkEstimate.Determinate(JobWorkUnit.MediaDuration, 20), [])).ToList();
        var plan = new JobPlan<EncodingJobOptions>(definition, completedAt.AddMinutes(-1), planItems, [], JobWorkUnit.MediaDuration);
        var results = definitions.Select(item => new JobItemResult<EncodingItemResult>(item.Id, state,
            [$"{item.SourceIdentity}.out.mp4"], state == JobState.CompletedWithWarnings ? ["warning"] : [],
            ["error detail"], new EncodingItemResult(0, TimeSpan.FromSeconds(60), item.MediaRange, TimeSpan.FromSeconds(20)))).ToList();
        var summary = new JobResultSummary(results.Count,
            state == JobState.Completed ? results.Count : 0,
            state == JobState.CompletedWithWarnings ? results.Count : 0,
            state == JobState.Skipped ? results.Count : 0,
            state == JobState.Cancelled ? results.Count : 0,
            state == JobState.Failed ? results.Count : 0);
        var result = new JobResult<EncodingItemResult>(id, state, completedAt.AddMinutes(-1), completedAt, results, summary,
            state == JobState.CompletedWithWarnings ? ["warning"] : [], state == JobState.Failed ? ["error detail"] : []);
        return new(id, "video.encode", definition.CreatedAt, result.StartedAt, completedAt, state, definition, plan, result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
