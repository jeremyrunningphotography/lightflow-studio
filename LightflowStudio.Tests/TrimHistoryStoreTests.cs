using System.Text.Json;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class TrimHistoryStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-trims-").FullName;
    private string StorePath => Path.Combine(_root, "data", "trim-history.json");
    private string SourcePath => Path.Combine(_root, "source.mp4");
    private static MediaRange Range => new(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(70));

    [Fact]
    public void SaveAndReload_RestoresMatchingIdentityAndVersionedDocument()
    {
        File.WriteAllBytes(SourcePath, new byte[64]);
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        new TrimHistoryStore(StorePath, () => now).Save(SourcePath, Range);

        var restored = new TrimHistoryStore(StorePath, () => now.AddDays(1)).Restore(SourcePath);

        Assert.Equal(Range, restored);
        using var document = JsonDocument.Parse(File.ReadAllText(StorePath));
        Assert.Equal(TrimHistoryStore.SchemaVersion, document.RootElement.GetProperty("version").GetInt32());
        Assert.False(File.Exists(StorePath + ".tmp"));
    }

    [Fact]
    public void PathSizeAndLastWriteMismatches_DoNotRestore()
    {
        File.WriteAllBytes(SourcePath, new byte[64]);
        var other = Path.Combine(_root, "other.mp4");
        File.WriteAllBytes(other, new byte[64]);
        var now = DateTimeOffset.UtcNow;
        var store = new TrimHistoryStore(StorePath, () => now);
        store.Save(SourcePath, Range);
        Assert.Null(store.Restore(other));

        File.AppendAllText(SourcePath, "changed");
        Assert.Null(store.Restore(SourcePath));

        File.WriteAllBytes(SourcePath, new byte[64]);
        store.Save(SourcePath, Range);
        File.SetLastWriteTimeUtc(SourcePath, File.GetLastWriteTimeUtc(SourcePath).AddMinutes(1));
        Assert.Null(store.Restore(SourcePath));
    }

    [Fact]
    public void RestoreRefreshesSlidingRetentionAndExpiredRecordsAreCleaned()
    {
        File.WriteAllBytes(SourcePath, new byte[64]);
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var store = new TrimHistoryStore(StorePath, () => now);
        store.Save(SourcePath, Range);
        now = now.AddDays(80);
        Assert.Equal(Range, store.Restore(SourcePath));
        now = now.AddDays(80);
        Assert.Equal(Range, new TrimHistoryStore(StorePath, () => now).Restore(SourcePath));
        now = now.AddDays(91);
        Assert.Null(new TrimHistoryStore(StorePath, () => now).Restore(SourcePath));
        using var document = JsonDocument.Parse(File.ReadAllText(StorePath));
        Assert.Empty(document.RootElement.GetProperty("records").EnumerateArray());
    }

    [Fact]
    public void MissingMalformedAndMalformedIndividualRecords_AreHarmless()
    {
        File.WriteAllBytes(SourcePath, new byte[64]);
        Assert.Null(new TrimHistoryStore(StorePath).Restore(SourcePath));
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, "not json");
        Assert.Null(new TrimHistoryStore(StorePath).Restore(SourcePath));

        var now = DateTimeOffset.UtcNow;
        var store = new TrimHistoryStore(StorePath, () => now);
        store.Save(SourcePath, Range);
        var json = File.ReadAllText(StorePath).Replace("\"records\": [", "\"records\": [\n    { \"malformed\": true },");
        File.WriteAllText(StorePath, json);
        Assert.Equal(Range, new TrimHistoryStore(StorePath, () => now).Restore(SourcePath));
    }

    [Fact]
    public void ApplyingFullSourceRemovesMatchingPersistedTrim()
    {
        File.WriteAllBytes(SourcePath, new byte[64]);
        var store = new TrimHistoryStore(StorePath);
        store.Save(SourcePath, Range);
        store.Remove(SourcePath);
        Assert.Null(store.Restore(SourcePath));
    }

    [Fact]
    public void ExplicitSaveFailureIsObservableWhileInMemoryTrimRemainsApplied()
    {
        File.WriteAllBytes(SourcePath, new byte[64]);
        var blockedDirectory = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(blockedDirectory, "block directory creation");
        var store = new TrimHistoryStore(Path.Combine(blockedDirectory, "trim-history.json"));
        var option = new BatchFileOption(SourcePath, "source.mp4", 64);

        AssertPersistenceFailure(() => TrimStatePersistence.Apply(option, Range, store));

        Assert.Equal(Range, option.TrimRange);
        Assert.True(option.HasActiveTrim);
    }

    [Fact]
    public void ExplicitRemovalFailureIsObservableWhileInMemoryTrimRemainsReset()
    {
        File.WriteAllBytes(SourcePath, new byte[64]);
        var store = new TrimHistoryStore(StorePath);
        store.Save(SourcePath, Range);
        var option = new BatchFileOption(SourcePath, "source.mp4", 64);
        option.ApplyTrim(Range);

        using (File.Open(StorePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            AssertPersistenceFailure(() => TrimStatePersistence.Apply(option, null, store));
        }

        Assert.Null(option.TrimRange);
        Assert.False(option.HasActiveTrim);
        Assert.Equal(Range, new TrimHistoryStore(StorePath).Restore(SourcePath));
    }

    [Fact]
    public void RestoreRemainsQuietWhenSlidingRefreshCannotBeWritten()
    {
        File.WriteAllBytes(SourcePath, new byte[64]);
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var store = new TrimHistoryStore(StorePath, () => now);
        store.Save(SourcePath, Range);
        now = now.AddDays(1);

        using (File.Open(StorePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(Range, store.Restore(SourcePath));
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static void AssertPersistenceFailure(Action action)
    {
        var exception = Record.Exception(action);
        Assert.True(exception is IOException or UnauthorizedAccessException,
            $"Expected an observable persistence failure, received {exception?.GetType().Name ?? "no exception"}.");
    }
}
