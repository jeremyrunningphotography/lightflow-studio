using System.Globalization;
using System.IO;
using System.Text.Json;

namespace LightflowStudio;

internal sealed record TrimSourceIdentity(string NormalizedPath, long FileSizeBytes, long LastWriteUtcTicks)
{
    public static TrimSourceIdentity? Read(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return null;
            return new(NormalizePath(file.FullName), file.Length, file.LastWriteTimeUtc.Ticks);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public bool Matches(TrimSourceIdentity other) =>
        string.Equals(NormalizedPath, other.NormalizedPath, StringComparison.OrdinalIgnoreCase) &&
        FileSizeBytes == other.FileSizeBytes &&
        LastWriteUtcTicks == other.LastWriteUtcTicks;

    private static string NormalizePath(string path) => Path.GetFullPath(path)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

internal interface ITrimHistoryStore
{
    MediaRange? Restore(string path);
    void Save(string path, MediaRange range);
    void Remove(string path);
}

internal sealed class TrimHistoryStore : ITrimHistoryStore
{
    internal const int SchemaVersion = 1;
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(90);
    private readonly string _path;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _sync = new();

    public TrimHistoryStore(string path, Func<DateTimeOffset>? utcNow = null)
    {
        _path = path;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public static string StorePath => LightflowStorageLocations.Current.TrimHistoryPath;

    public MediaRange? Restore(string path)
    {
        lock (_sync)
        {
            var identity = TrimSourceIdentity.Read(path);
            if (identity is null) return null;
            var now = _utcNow();
            var records = LoadAndClean(now, out var changed);
            var record = records.FirstOrDefault(item => item.Identity.Matches(identity));
            if (record is null)
            {
                if (changed) TrySaveQuietly(records);
                return null;
            }

            var range = record.ToMediaRange();
            if (range is null)
            {
                records.Remove(record);
                TrySaveQuietly(records);
                return null;
            }

            record.LastUsedUtc = now;
            TrySaveQuietly(records);
            return range;
        }
    }

    public void Save(string path, MediaRange range)
    {
        if (range.IsFullSource || range.Validate().Count != 0) throw new ArgumentException("Only a valid active trim can be persisted.", nameof(range));
        lock (_sync)
        {
            var identity = TrimSourceIdentity.Read(path)
                ?? throw new FileNotFoundException("The trim source no longer exists.", path);
            var now = _utcNow();
            var records = LoadAndClean(now, out _);
            records.RemoveAll(item => string.Equals(item.Identity.NormalizedPath, identity.NormalizedPath, StringComparison.OrdinalIgnoreCase));
            records.Add(TrimHistoryRecord.From(identity, range, now));
            SaveRecords(records);
        }
    }

    public void Remove(string path)
    {
        lock (_sync)
        {
            var identity = TrimSourceIdentity.Read(path);
            if (identity is null) return;
            var records = LoadAndClean(_utcNow(), out var changed);
            changed |= records.RemoveAll(item => item.Identity.Matches(identity)) > 0;
            if (changed) SaveRecords(records);
        }
    }

    private List<TrimHistoryRecord> LoadAndClean(DateTimeOffset now, out bool changed)
    {
        var records = Load(out changed);
        changed |= records.RemoveAll(item => now - item.LastUsedUtc > Retention) > 0;
        return records;
    }

    private List<TrimHistoryRecord> Load(out bool changed)
    {
        changed = false;
        if (!File.Exists(_path)) return [];
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var root = document.RootElement;
            if (!root.TryGetProperty("version", out var version) || version.GetInt32() != SchemaVersion ||
                !root.TryGetProperty("records", out var elements) || elements.ValueKind != JsonValueKind.Array) return [];
            var records = new List<TrimHistoryRecord>();
            foreach (var element in elements.EnumerateArray())
            {
                if (TrimHistoryRecord.TryParse(element, out var record)) records.Add(record);
                else changed = true;
            }
            return records;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            changed = true;
            return [];
        }
    }

    private void TrySaveQuietly(IReadOnlyList<TrimHistoryRecord> records)
    {
        try { SaveRecords(records); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private void SaveRecords(IReadOnlyList<TrimHistoryRecord> records)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            var payload = new
            {
                version = SchemaVersion,
                records = records.Select(record => record.ToJson()).ToList()
            };
            File.WriteAllText(temporary, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(_path + ".tmp"); } catch { }
            throw;
        }
    }

    private sealed class TrimHistoryRecord
    {
        public required TrimSourceIdentity Identity { get; init; }
        public required long SourceDurationTicks { get; init; }
        public long? InTicks { get; init; }
        public long? OutTicks { get; init; }
        public required DateTimeOffset LastUsedUtc { get; set; }

        public MediaRange? ToMediaRange()
        {
            try
            {
                var range = new MediaRange(
                    TimeSpan.FromTicks(SourceDurationTicks),
                    InTicks is { } start ? TimeSpan.FromTicks(start) : null,
                    OutTicks is { } end ? TimeSpan.FromTicks(end) : null);
                return !range.IsFullSource && range.Validate().Count == 0 ? range : null;
            }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        public object ToJson() => new
        {
            normalizedPath = Identity.NormalizedPath,
            fileSizeBytes = Identity.FileSizeBytes,
            lastWriteUtcTicks = Identity.LastWriteUtcTicks,
            sourceDurationTicks = SourceDurationTicks,
            inTicks = InTicks,
            outTicks = OutTicks,
            lastUsedUtc = LastUsedUtc.ToString("O", CultureInfo.InvariantCulture)
        };

        public static TrimHistoryRecord From(TrimSourceIdentity identity, MediaRange range, DateTimeOffset now) => new()
        {
            Identity = identity,
            SourceDurationTicks = range.SourceDuration.Ticks,
            InTicks = range.In?.Ticks,
            OutTicks = range.Out?.Ticks,
            LastUsedUtc = now
        };

        public static bool TryParse(JsonElement element, out TrimHistoryRecord record)
        {
            record = null!;
            try
            {
                var path = element.GetProperty("normalizedPath").GetString();
                var lastUsed = DateTimeOffset.Parse(element.GetProperty("lastUsedUtc").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                if (string.IsNullOrWhiteSpace(path)) return false;
                record = new()
                {
                    Identity = new(path, element.GetProperty("fileSizeBytes").GetInt64(), element.GetProperty("lastWriteUtcTicks").GetInt64()),
                    SourceDurationTicks = element.GetProperty("sourceDurationTicks").GetInt64(),
                    InTicks = OptionalInt64(element, "inTicks"),
                    OutTicks = OptionalInt64(element, "outTicks"),
                    LastUsedUtc = lastUsed
                };
                return record.ToMediaRange() is not null;
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static long? OptionalInt64(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetInt64() : null;
    }
}
