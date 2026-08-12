using System.IO;
using Microsoft.Data.Sqlite;

namespace LightflowStudio;

internal enum MediaRootAvailability { Online, Unavailable, Unmapped }

internal sealed record MediaRootInfo(Guid RootId, string DisplayName, string? PhysicalPath,
    MediaRootAvailability Availability, string? Diagnostic = null);

internal sealed record MediaPathResolution(Guid RootId, string RelativePath, string RelativePathKey,
    string? PhysicalPath, MediaRootAvailability RootAvailability, bool Exists, string? Diagnostic = null);

internal sealed record MediaRootChangeResult(bool Succeeded, MediaRootInfo? Root = null, string? Diagnostic = null);

internal interface IMachineIdentityProvider
{
    string GetMachineId();
}

internal sealed class MachineIdentityProvider(string path) : IMachineIdentityProvider
{
    private static readonly object Gate = new();
    private string? _value;

    public string GetMachineId()
    {
        lock (Gate)
        {
            if (_value is not null) return _value;
            Guid existing;
            try
            {
                if (File.Exists(path) && Guid.TryParse(File.ReadAllText(path).Trim(), out existing))
                    return _value = existing.ToString("D");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var created = Guid.NewGuid().ToString("D");
            var temporary = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporary, created);
                File.Move(temporary, path, overwrite: true);
                return _value = created;
            }
            catch (IOException) when (File.Exists(path))
            {
                try { File.Delete(temporary); } catch { }
                if (Guid.TryParse(File.ReadAllText(path).Trim(), out existing))
                    return _value = existing.ToString("D");
                throw;
            }
            finally { try { File.Delete(temporary); } catch { } }
        }
    }
}

internal static class MediaPathSemantics
{
    public static string NormalizeRootPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Media Root paths must be absolute.", nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
    }

    public static string NormalizeRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var candidate = path.Trim();
        if (Path.IsPathFullyQualified(candidate) || candidate.StartsWith('/') || candidate.StartsWith('\\') ||
            (candidate.Length >= 2 && char.IsLetter(candidate[0]) && candidate[1] == ':'))
            throw new ArgumentException("Media paths must be relative to their Media Root.", nameof(path));
        var parts = candidate.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part == ".") continue;
            if (part == "..") throw new ArgumentException("Media paths cannot leave their Media Root.", nameof(path));
            normalized.Add(part);
        }
        if (normalized.Count == 0) throw new ArgumentException("A media path cannot be empty.", nameof(path));
        return string.Join('/', normalized);
    }

    public static string RelativePathKey(string path) => NormalizeRelativePath(path).ToUpperInvariant();

    public static string ResolveContained(string rootPath, string relativePath)
    {
        var root = NormalizeRootPath(rootPath);
        var relative = NormalizeRelativePath(relativePath);
        var resolved = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsSameOrAncestor(root, resolved)) throw new ArgumentException("The media path leaves its Media Root.", nameof(relativePath));
        return resolved;
    }

    public static bool Overlaps(string left, string right) =>
        IsSameOrAncestor(NormalizeRootPath(left), NormalizeRootPath(right)) ||
        IsSameOrAncestor(NormalizeRootPath(right), NormalizeRootPath(left));

    private static bool IsSameOrAncestor(string parent, string child)
    {
        if (string.Equals(parent, child, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
        return child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}

internal interface IMediaRootFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
}

internal sealed class MediaRootFileSystem : IMediaRootFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
}

internal interface IMediaRootService
{
    Task<IReadOnlyList<MediaRootInfo>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaRootInfo?> GetAsync(Guid rootId, CancellationToken cancellationToken = default);
    Task<MediaRootChangeResult> CreateAsync(string displayName, string physicalPath, CancellationToken cancellationToken = default);
    Task<MediaRootChangeResult> RenameAsync(Guid rootId, string displayName, CancellationToken cancellationToken = default);
    Task<MediaRootChangeResult> RemapAsync(Guid rootId, string physicalPath, CancellationToken cancellationToken = default);
    Task<MediaPathResolution> ResolveAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default);
}

internal sealed class MediaRootService(Func<CatalogDatabaseSession?> session, IMachineIdentityProvider machine,
    IMediaRootFileSystem fileSystem) : IMediaRootService
{
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    public Task<IReadOnlyList<MediaRootInfo>> ListAsync(CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.RootId, r.DisplayName, m.PhysicalPath
            FROM MediaRoots r
            LEFT JOIN MediaRootMappings m ON m.RootId = r.RootId AND m.MachineId = $machine
            ORDER BY r.DisplayName COLLATE NOCASE, r.RootId;
            """;
        command.Parameters.AddWithValue("$machine", machine.GetMachineId());
        using var reader = command.ExecuteReader();
        var roots = new List<MediaRootInfo>();
        while (reader.Read()) roots.Add(Observe(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return (IReadOnlyList<MediaRootInfo>)roots;
    }, cancellationToken);

    public Task<MediaRootInfo?> GetAsync(Guid rootId, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        using var connection = RequireSession().OpenConnection();
        return Read(connection, rootId);
    }, cancellationToken);

    public async Task<MediaRootChangeResult> CreateAsync(string displayName, string physicalPath, CancellationToken cancellationToken = default)
    {
        var name = NormalizeName(displayName);
        string path;
        try { path = await ProbeAsync(physicalPath, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return new(false, Diagnostic: ex.Message); }
        return await RunChangeAsync(() =>
        {
            using var connection = RequireSession().OpenConnection();
            using var transaction = connection.BeginTransaction();
            if (FindOverlap(connection, transaction, path, null) is { } overlap)
                return new MediaRootChangeResult(false, Diagnostic: $"That folder overlaps Media Root ‘{overlap}’ on this computer.");
            var rootId = Guid.NewGuid();
            var now = UtcTimestamp();
            Execute(connection, transaction, "INSERT INTO MediaRoots (RootId, DisplayName, SourceStatus, CreatedUtc, UpdatedUtc) VALUES ($id,$name,'online',$now,$now);",
                ("$id", rootId.ToString("D")), ("$name", name), ("$now", now));
            Execute(connection, transaction, "INSERT INTO MediaRootMappings (MappingId, RootId, MachineId, PhysicalPath, SourceStatus, CreatedUtc, UpdatedUtc) VALUES ($mapping,$id,$machine,$path,'online',$now,$now);",
                ("$mapping", Guid.NewGuid().ToString("D")), ("$id", rootId.ToString("D")), ("$machine", machine.GetMachineId()), ("$path", path), ("$now", now));
            transaction.Commit();
            return new MediaRootChangeResult(true, Observe(rootId, name, path));
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<MediaRootChangeResult> RenameAsync(Guid rootId, string displayName, CancellationToken cancellationToken = default) => RunChangeAsync(() =>
    {
        var name = NormalizeName(displayName);
        using var connection = RequireSession().OpenConnection();
        var changed = Execute(connection, null, "UPDATE MediaRoots SET DisplayName=$name, UpdatedUtc=$now WHERE RootId=$id;",
            ("$name", name), ("$now", UtcTimestamp()), ("$id", rootId.ToString("D")));
        return changed == 0 ? new(false, Diagnostic: "The Media Root no longer exists.") : new(true, Read(connection, rootId));
    }, cancellationToken);

    public async Task<MediaRootChangeResult> RemapAsync(Guid rootId, string physicalPath, CancellationToken cancellationToken = default)
    {
        string path;
        try { path = await ProbeAsync(physicalPath, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return new(false, Diagnostic: ex.Message); }
        return await RunChangeAsync(() =>
        {
            using var connection = RequireSession().OpenConnection();
            using var transaction = connection.BeginTransaction();
            if (!RootExists(connection, transaction, rootId)) return new(false, Diagnostic: "The Media Root no longer exists.");
            if (FindOverlap(connection, transaction, path, rootId) is { } overlap)
                return new(false, Diagnostic: $"That folder overlaps Media Root ‘{overlap}’ on this computer.");
            var now = UtcTimestamp();
            Execute(connection, transaction, """
                INSERT INTO MediaRootMappings (MappingId,RootId,MachineId,PhysicalPath,SourceStatus,CreatedUtc,UpdatedUtc)
                VALUES ($mapping,$id,$machine,$path,'online',$now,$now)
                ON CONFLICT(RootId,MachineId) DO UPDATE SET PhysicalPath=excluded.PhysicalPath, SourceStatus='online', UpdatedUtc=excluded.UpdatedUtc;
                """, ("$mapping", Guid.NewGuid().ToString("D")), ("$id", rootId.ToString("D")), ("$machine", machine.GetMachineId()), ("$path", path), ("$now", now));
            transaction.Commit();
            return new(true, Read(connection, rootId));
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<MediaPathResolution> ResolveAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => RunAsync<MediaPathResolution>(() =>
    {
        var normalized = MediaPathSemantics.NormalizeRelativePath(relativePath);
        using var connection = RequireSession().OpenConnection();
        var root = Read(connection, rootId) ?? throw new KeyNotFoundException("The Media Root does not exist.");
        if (root.Availability != MediaRootAvailability.Online)
            return new(rootId, normalized, MediaPathSemantics.RelativePathKey(normalized), null, root.Availability, false, root.Diagnostic);
        var resolved = MediaPathSemantics.ResolveContained(root.PhysicalPath!, normalized);
        var exists = fileSystem.FileExists(resolved);
        return new(rootId, normalized, MediaPathSemantics.RelativePathKey(normalized), resolved, root.Availability,
            exists, exists ? null : "The file is missing beneath an available Media Root.");
    }, cancellationToken);

    private CatalogDatabaseSession RequireSession() => session() ?? throw new InvalidOperationException("The Catalog is unavailable.");
    private MediaRootInfo Observe(Guid id, string name, string? path) => path is null
        ? new(id, name, null, MediaRootAvailability.Unmapped, "This Media Root is not connected on this computer.")
        : fileSystem.DirectoryExists(path)
            ? new(id, name, path, MediaRootAvailability.Online)
            : new(id, name, path, MediaRootAvailability.Unavailable, "The mapped folder is currently unavailable.");

    private MediaRootInfo? Read(SqliteConnection connection, Guid id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT r.DisplayName,m.PhysicalPath FROM MediaRoots r LEFT JOIN MediaRootMappings m ON m.RootId=r.RootId AND m.MachineId=$machine WHERE r.RootId=$id;";
        command.Parameters.AddWithValue("$machine", machine.GetMachineId()); command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? Observe(id, reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)) : null;
    }

    private async Task<string> ProbeAsync(string path, CancellationToken token)
    {
        var normalized = MediaPathSemantics.NormalizeRootPath(path);
        var exists = await Task.Run(() => fileSystem.DirectoryExists(normalized), token).ConfigureAwait(false);
        if (!exists) throw new IOException("The selected Media Root folder is not currently available.");
        return normalized;
    }

    private string? FindOverlap(SqliteConnection connection, SqliteTransaction transaction, string path, Guid? exclude)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT r.RootId,r.DisplayName,m.PhysicalPath FROM MediaRootMappings m JOIN MediaRoots r ON r.RootId=m.RootId WHERE m.MachineId=$machine;";
        command.Parameters.AddWithValue("$machine", machine.GetMachineId());
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (Guid.Parse(reader.GetString(0)) != exclude && MediaPathSemantics.Overlaps(path, reader.GetString(2))) return reader.GetString(1);
        return null;
    }

    private static bool RootExists(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM MediaRoots WHERE RootId=$id;"; command.Parameters.AddWithValue("$id", id.ToString("D"));
        return command.ExecuteScalar() is not null;
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }

    private static string UtcTimestamp() => DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static int Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object)[] values)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value);
        return command.ExecuteNonQuery();
    }

    private static Task<T> RunAsync<T>(Func<T> operation, CancellationToken token) => Task.Run(() => { token.ThrowIfCancellationRequested(); return operation(); }, token);

    private async Task<MediaRootChangeResult> RunChangeAsync(Func<MediaRootChangeResult> operation, CancellationToken token)
    {
        await _mutationGate.WaitAsync(token).ConfigureAwait(false);
        try { return await RunAsync(operation, token).ConfigureAwait(false); }
        finally { _mutationGate.Release(); }
    }
}
