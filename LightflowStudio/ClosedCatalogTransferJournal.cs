using System.IO;
using System.Buffers.Binary;
using System.Text.Json;
using Lightflow.Application;
using Lightflow.Domain;

namespace LightflowStudio;

/// <summary>Append-only, length/digest framed profile evidence. No recorded path grants native use authority.</summary>
internal sealed class ClosedCatalogTransferJournal : IClosedCatalogTransferJournal
{
    private readonly string _path;
    private readonly FileStream _file;
    private readonly WindowsStorageLocationAssessor.TransferDirectoryLease _directoryLease;
    private readonly WindowsStorageLocationAssessor _profileAssessor;
    private readonly StorageAssessmentRequest _profileRequest;
    private StorageLocationAssessment _profileEvidence;
    private readonly List<ClosedCatalogJournalRecord> _records = [];
    internal static string DirectoryPath(LightflowStorageLocations locations)
    {
        var path = Path.Combine(locations.ApplicationDataDirectory, "Catalog Transfers");
        if (new[] { locations.CatalogDirectory, locations.PreviewsDirectory, locations.TemporaryDirectory }
            .Any(p => LightflowStorageLocations.PathsOverlap(path, p)))
            throw new IOException("The transfer journal must be outside Catalog, Preview and temporary ownership.");
        return path;
    }
    internal ClosedCatalogTransferJournal(LightflowStorageLocations locations, Guid operationId)
    {
        var directory = DirectoryPath(locations);
        RequireUnlinked(directory);
        _path = Path.Combine(directory, $"{operationId:N}.journal");
        _profileAssessor = new([locations.CatalogDirectory, locations.PreviewsDirectory, locations.TemporaryDirectory]);
        _profileRequest = new(operationId, 0, StorageRole.ActiveCatalog, StorageOperation.Open, directory);
        try
        {
            // Reuse qualified-local durable facts for profile evidence, never admit a SQL writer here.
            _profileEvidence = AssessProfile(null);
            Directory.CreateDirectory(directory);
            _profileEvidence = AssessProfile(_profileEvidence);
            _directoryLease = new(directory, false);
            _file = new FileStream(_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            WindowsStorageLocationAssessor.RequireSingleRegularFile(_file.SafeFileHandle);
            _file.Flush(true);
        }
        catch { _file?.Dispose(); _directoryLease?.Dispose(); _profileAssessor.Dispose(); throw; }
    }
    public Task AppendAsync(ClosedCatalogJournalRecord record, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RequireUnlinked(_path);
        _profileEvidence = AssessProfile(_profileEvidence);
        var candidate = _records.Append(record).ToArray();
        ClosedCatalogJournalFormat.Validate(candidate);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
        if (bytes.Length > ClosedCatalogJournalFormat.MaximumRecordBytes) throw new InvalidDataException("Transfer journal record is too large.");
        _directoryLease.Revalidate(Path.GetDirectoryName(_path)!);
        var file = _file;
        {
            file.Seek(0, SeekOrigin.End);
            Span<byte> size = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(size, bytes.Length);
            file.Write(size);
            file.Write(bytes);
            file.Write(Convert.FromHexString(ClosedCatalogJournalFormat.Digest(record)));
            file.Flush(true);
        }
        _file.Position = 0;
        var readback = ReadFrames(_file);
        if (readback.Count != candidate.Length || ClosedCatalogJournalFormat.Digest(readback[^1]) != ClosedCatalogJournalFormat.Digest(record))
            throw new IOException("Transfer journal readback did not verify.");
        _records.Add(record);
        return Task.CompletedTask;
    }
    private StorageLocationAssessment AssessProfile(StorageLocationAssessment? previous)
    {
        var current = _profileAssessor.AssessAsync(_profileRequest).GetAwaiter().GetResult();
        var result = previous is null ? StorageLocationPolicy.Evaluate(_profileRequest, current, DateTimeOffset.UtcNow)
            : StorageLocationPolicy.Revalidate(_profileRequest, previous, current, DateTimeOffset.UtcNow);
        if (result.Decision != StorageLocationDecision.Eligible)
            throw new IOException($"Transfer journal requires qualified local durable profile storage. {result.Diagnostic}");
        return current;
    }
    internal static IReadOnlyList<ClosedCatalogJournalRecord> Read(string path)
    {
        RequireUnlinked(path);
        using var file = WindowsClosedCatalogTransferOperation.OpenClosedRead(path);
        return ReadFrames(file);
    }
    private static IReadOnlyList<ClosedCatalogJournalRecord> ReadFrames(FileStream file)
    {
        if (file.Length > (long)ClosedCatalogJournalFormat.MaximumRecords * (ClosedCatalogJournalFormat.MaximumRecordBytes + 36))
            throw new InvalidDataException("Transfer journal exceeds its format bound.");
        var records = new List<ClosedCatalogJournalRecord>();
        Span<byte> size = stackalloc byte[4];
        Span<byte> hash = stackalloc byte[32];
        while (file.Position < file.Length)
        {
            if (records.Count >= ClosedCatalogJournalFormat.MaximumRecords) throw new InvalidDataException("Too many transfer journal records.");
            file.ReadExactly(size);
            var length = BinaryPrimitives.ReadInt32LittleEndian(size);
            if (length is < 1 or > ClosedCatalogJournalFormat.MaximumRecordBytes) throw new InvalidDataException("Invalid transfer journal framing.");
            var bytes = new byte[length];
            file.ReadExactly(bytes);
            file.ReadExactly(hash);
            var record = JsonSerializer.Deserialize<ClosedCatalogJournalRecord>(bytes) ?? throw new InvalidDataException("Missing transfer journal record.");
            if (!JsonSerializer.SerializeToUtf8Bytes(record).AsSpan().SequenceEqual(bytes) ||
                !Convert.FromHexString(ClosedCatalogJournalFormat.Digest(record)).AsSpan().SequenceEqual(hash))
                throw new InvalidDataException("Transfer journal record is truncated or inconsistent.");
            records.Add(record);
        }
        ClosedCatalogJournalFormat.Validate(records);
        return records;
    }
    internal static string? InspectPending(LightflowStorageLocations locations)
    {
        try
        {
            var directory = DirectoryPath(locations);
            RequireUnlinked(directory);
            if (!Directory.Exists(directory)) return null;
            foreach (var file in Directory.EnumerateFiles(directory, "*.journal"))
            {
                var records = Read(file);
                if (ClosedCatalogJournalFormat.RequiresRecovery(records))
                    return $"An interrupted Catalog transfer requires recovery before this Catalog can be opened. Operation {records[0].Request.OperationId:N}; journal {file}. {ReassessPending(records)} Source and destination evidence are retained. Reconnect the selected storage and review the transfer journal; Lightflow has not selected or created another Catalog.";
            }
            return null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return $"Catalog transfer recovery evidence could not be verified. No Catalog writer will be opened. {e.Message}"; }
    }
    private static string ReassessPending(IReadOnlyList<ClosedCatalogJournalRecord> records)
    {
        var evidence = records.Select(r => r.PreflightEvidence).FirstOrDefault(e => e is not null);
        if (evidence is null) return "Native identities were not durably recorded; recovery authority is unresolved.";
        var observations = new List<string>();
        foreach (var prior in new[] { evidence.Source, evidence.Destination })
        {
            using var assessor = new WindowsStorageLocationAssessor();
            var current = assessor.AssessAsync(prior.Request).GetAwaiter().GetResult();
            var policy = StorageLocationPolicy.Evaluate(prior.Request, current, DateTimeOffset.UtcNow);
            observations.Add($"{prior.Request.Role}: fresh assessment={policy.Decision}, recorded native identity matches={prior.Identity is not null && prior.Identity == current.Identity}");
        }
        return string.Join("; ", observations) + ". Remembered paths do not authorize automatic activation or cleanup.";
    }
    internal static void RequireUnlinked(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Transfer evidence cannot be accessed through a link or junction.");
    }
    public ValueTask DisposeAsync()
    {
        _file.Dispose(); _directoryLease.Dispose(); _profileAssessor.Dispose();
        return ValueTask.CompletedTask;
    }
}
