using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;

namespace LightflowStudio;

internal enum StartupValidationReason { CleanShutdown, UncertainState, UnexpectedShutdown, Migration, Restore, DatabaseAnomaly, ExplicitValidation }

// One exclusive lease beside each database. A certificate is consumed and durably overwritten
// BEFORE any SQLite open. Clean publication is allowed only after the owning lifecycle drains.
// A torn/missing record cannot fall back to an older certificate. This attests lifecycle completion,
// not immunity to latent hardware corruption. No scan, database schema or media enumeration here.
internal sealed class StartupStoreEvidence : IDisposable
{
    private const int Format = 1;
    private readonly FileStream _journal;
    private readonly string _database;
    private readonly string _configuredDatabase;
    private readonly string _store;
    private bool _completed;
    internal bool Completed => _completed;
    private readonly StartupSessionCompletion? _session;
    internal bool KnownClean { get; private set; }
    internal StartupValidationReason Reason { get; private set; }
    internal void RequireCompletePeer(bool complete)
    {
        if (KnownClean && !complete) { KnownClean = false; Reason = StartupValidationReason.DatabaseAnomaly; }
    }
    internal static string JournalPath(string database) => database + ".startup-state";

    private StartupStoreEvidence(string database, string store, FileStream journal, StartupSessionCompletion? session, string? resolvedDatabase)
    {
        _configuredDatabase = Path.GetFullPath(database);
        _database = Path.GetFullPath(resolvedDatabase ?? database); _store = store; _journal = journal; _session = session;
        Reason = session?.PriorReason == StartupValidationReason.UnexpectedShutdown
            ? StartupValidationReason.UnexpectedShutdown : StartupValidationReason.UncertainState;
        try
        {
            if (journal.Length is > 0 and < 32768)
            {
                var bytes = new byte[(int)journal.Length]; journal.ReadExactly(bytes);
                var envelope = JsonSerializer.Deserialize<Envelope>(bytes);
                if (envelope is not null && envelope.Checksum == Hash(Encoding.UTF8.GetBytes(envelope.Payload)))
                {
                    var certificate = JsonSerializer.Deserialize<Certificate>(envelope.Payload);
                    if (certificate is { Version: Format, State: "Clean" } && certificate.Store == store &&
                        certificate.Database == _configuredDatabase && session?.Previous is not null && certificate.Session == session.Previous)
                    {
                        Reason = StartupValidationReason.DatabaseAnomaly;
                        KnownClean = certificate.Fingerprint == Fingerprint(_database) && NoRecoveryFiles(_database);
                        Reason = KnownClean ? StartupValidationReason.CleanShutdown : StartupValidationReason.DatabaseAnomaly;
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        { /* Unreadable/untrusted evidence never grants the fast path. */ }
        Write(new(Format, "Dirty", _store, _configuredDatabase, null, session?.Current)); // Flush must succeed before caller can open SQLite.
    }

    internal static StartupStoreEvidence? Begin(string database, string store, bool createDirectory, StartupSessionCompletion? session = null,
        string? resolvedDatabase = null)
    {
        var accessDatabase = resolvedDatabase ?? database;
        var directory = Path.GetDirectoryName(Path.GetFullPath(accessDatabase))!;
        if (createDirectory) Directory.CreateDirectory(directory);
        if (!Directory.Exists(directory)) return null;
        var journal = new FileStream(JournalPath(accessDatabase), FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.Delete, 4096, FileOptions.WriteThrough);
        try { return new(database, store, journal, session, resolvedDatabase); }
        catch { journal.Dispose(); throw; }
    }

    // All providers/connections must already be closed and admission must remain closed.
    // A busy checkpoint, flush failure or changed location leaves Dirty, never a false Clean.
    internal void Complete(string currentDatabase)
    {
        if (_completed || !string.Equals(_database, Path.GetFullPath(currentDatabase), StringComparison.OrdinalIgnoreCase)) return;
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = _database, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.GetInt32(0) != 0) throw new IOException("Storage checkpoint is still busy.");
        }
        if (!NoRecoveryFiles(_database)) throw new IOException("Storage recovery files remain after close.");
        // Also excludes another writer for the entire certificate publication window.
        using (var flush = new FileStream(_database, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            flush.Flush(flushToDisk: true);
        // Windows finalizes last-write metadata when the last write handle closes. Capture only
        // afterward, under a read-only handle that excludes new writers until publication ends.
        using var database = new FileStream(_database, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fingerprint = Fingerprint(database);
        Write(new(Format, "Clean", _store, _configuredDatabase, fingerprint, _session?.Current));
        _completed = true;
    }

    private void Write(Certificate value)
    {
        var payload = JsonSerializer.Serialize(value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(payload, Hash(Encoding.UTF8.GetBytes(payload))));
        _journal.Position = 0;
        _journal.Write(bytes);
        _journal.SetLength(bytes.Length);
        _journal.Flush(flushToDisk: true);
    }
    private static bool NoRecoveryFiles(string path) =>
        new[] { path + "-wal", path + "-journal" }.All(p => !File.Exists(p) || new FileInfo(p).Length == 0);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Fingerprint(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Fingerprint(file);
    }
    private static string Fingerprint(FileStream file)
    {
        if (!GetFileInformationByHandle(file.SafeFileHandle, out var identity))
            throw new IOException("Cannot establish database file identity.");
        var header = new byte[100]; file.Position = 0; file.ReadExactly(header);
        return $"{identity.Volume}:{identity.IndexHigh}:{identity.IndexLow}:{identity.WriteHigh}:{identity.WriteLow}:{file.Length}:{Hash(header)}";
    }
    public void Dispose() => _journal.Dispose(); // Disposal without Complete intentionally leaves Dirty.
    private sealed record Certificate(int Version, string State, string Store, string Database, string? Fingerprint, string? Session);
    private sealed record Envelope(string Payload, string Checksum);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdentity
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileIdentity information);
}

internal sealed record StartupValidationProgress(string Primary, string Supporting)
{
    internal static StartupValidationProgress For(string store, StartupValidationReason reason) => store == "Catalog"
        ? new(reason switch
        {
            StartupValidationReason.Migration => "Verifying upgraded Catalog…",
            StartupValidationReason.Restore => "Verifying restored Catalog…",
            StartupValidationReason.DatabaseAnomaly => "Verifying Catalog before opening…",
            StartupValidationReason.ExplicitValidation => "Verifying Catalog…",
            StartupValidationReason.UnexpectedShutdown => "Verifying Catalog after an interrupted shutdown…",
            _ => "Verifying Catalog before opening…"
        }, "Protecting your saved Lightflow work")
        : new(reason == StartupValidationReason.UnexpectedShutdown
            ? "Verifying Preview storage after an interrupted shutdown…" : "Verifying Preview storage…",
            "Making sure cached media is ready to use");
}
// Final cross-store commit record. Per-store certificates are not trusted unless both completed
// in the same session and this record was durably published last. Consume it first on next launch.
internal sealed class StartupSessionCompletion : IDisposable
{
    private readonly FileStream _file;
    internal string? Previous { get; }
    internal StartupValidationReason PriorReason { get; private set; } = StartupValidationReason.UncertainState;
    internal string Current { get; } = Guid.NewGuid().ToString("N");
    internal static string PathFor(string root) => Path.Combine(root, "startup-session.state");
    internal StartupSessionCompletion(string root)
    {
        Directory.CreateDirectory(root);
        _file = new FileStream(PathFor(root), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough);
        try
        {
            if (_file.Length == 32)
            {
                var bytes = new byte[32]; _file.ReadExactly(bytes);
                var value = Encoding.ASCII.GetString(bytes);
                if (Guid.TryParseExact(value, "N", out _))
                { Previous = value; PriorReason = StartupValidationReason.CleanShutdown; }
            }
            else if (_file.Length == 5)
            {
                var bytes = new byte[5]; _file.ReadExactly(bytes);
                if (Encoding.ASCII.GetString(bytes) == "Dirty") PriorReason = StartupValidationReason.UnexpectedShutdown;
            }
            Write("Dirty");
        }
        catch { _file.Dispose(); throw; }
    }
    internal void Complete() => Write(Current);
    private void Write(string value)
    {
        _file.Position = 0; _file.Write(Encoding.ASCII.GetBytes(value)); _file.SetLength(value.Length); _file.Flush(true);
    }
    public void Dispose() => _file.Dispose();
}
