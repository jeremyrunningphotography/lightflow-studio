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

internal enum MachineIdentityFailure { Malformed, Unavailable }

internal sealed class MachineIdentityException(MachineIdentityFailure failure, string diagnostic, Exception? innerException = null)
    : Exception(diagnostic, innerException)
{
    public MachineIdentityFailure Failure { get; } = failure;
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
            if (File.Exists(path)) return _value = ReadEstablishedIdentity();

            var created = Guid.NewGuid().ToString("D");
            var temporary = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(temporary, created);
                File.Move(temporary, path, overwrite: false);
                return _value = created;
            }
            catch (IOException exception) when (File.Exists(path))
            {
                return _value = ReadEstablishedIdentity(exception);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new MachineIdentityException(MachineIdentityFailure.Unavailable,
                    "Lightflow could not establish this installation's Media Root identity.", exception);
            }
            finally { try { File.Delete(temporary); } catch { } }
        }
    }

    private string ReadEstablishedIdentity(Exception? raceException = null)
    {
        string contents;
        try { contents = File.ReadAllText(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new MachineIdentityException(MachineIdentityFailure.Unavailable,
                "Lightflow could not read this installation's established Media Root identity.", exception);
        }
        if (!Guid.TryParse(contents.Trim(), out var identity))
            throw new MachineIdentityException(MachineIdentityFailure.Malformed,
                "This installation's Media Root identity is malformed. Lightflow preserved it for diagnosis.", raceException);
        return identity.ToString("D");
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

    public static bool Contains(string rootPath, string candidatePath) =>
        IsSameOrAncestor(NormalizeRootPath(rootPath), NormalizeRootPath(candidatePath));

    public static string RelativeFolder(string rootPath, string candidatePath)
    {
        var root = NormalizeRootPath(rootPath);
        var candidate = NormalizeRootPath(candidatePath);
        if (!IsSameOrAncestor(root, candidate))
            throw new ArgumentException("The folder is outside its Media Root.", nameof(candidatePath));
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." ? "" : NormalizeRelativePath(relative);
    }

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
    Task<MediaRootChangeResult> CreateBrowserAnchorAsync(string displayName, string physicalPath,
        CancellationToken cancellationToken = default) => CreateAsync(displayName, physicalPath, cancellationToken);
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
        var machineId = machine.GetMachineId();
        using var connection = RequireSession().OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.RootId, r.DisplayName, m.PhysicalPath
            FROM MediaRoots r
            LEFT JOIN MediaRootMappings m ON m.RootId = r.RootId AND m.MachineId = $machine
            ORDER BY r.DisplayName COLLATE NOCASE, r.RootId;
            """;
        command.Parameters.AddWithValue("$machine", machineId);
        using var reader = command.ExecuteReader();
        var roots = new List<MediaRootInfo>();
        while (reader.Read()) roots.Add(Observe(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return (IReadOnlyList<MediaRootInfo>)roots;
    }, cancellationToken);

    public Task<MediaRootInfo?> GetAsync(Guid rootId, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        var machineId = machine.GetMachineId();
        using var connection = RequireSession().OpenConnection();
        return Read(connection, rootId, machineId);
    }, cancellationToken);

    public async Task<MediaRootChangeResult> CreateAsync(string displayName, string physicalPath, CancellationToken cancellationToken = default)
        => await CreateCoreAsync(displayName, physicalPath, allowManagedOverlap: false, cancellationToken).ConfigureAwait(false);

    public async Task<MediaRootChangeResult> CreateBrowserAnchorAsync(string displayName, string physicalPath,
        CancellationToken cancellationToken = default)
    {
        string normalized;
        try { normalized = MediaPathSemantics.NormalizeRootPath(physicalPath); }
        catch (ArgumentException exception) { return new(false, Diagnostic: exception.Message); }
        if (!IsNaturalAnchor(normalized))
            return new(false, Diagnostic: "Automatic Browser roots must be anchored at a volume or network-share boundary.");
        return await CreateCoreAsync(displayName, normalized, allowManagedOverlap: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MediaRootChangeResult> CreateCoreAsync(string displayName, string physicalPath,
        bool allowManagedOverlap, CancellationToken cancellationToken)
    {
        var name = NormalizeName(displayName);
        string path;
        try { path = await ProbeAsync(physicalPath, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return new(false, Diagnostic: ex.Message); }
        return await RunChangeAsync(() =>
        {
            var machineId = machine.GetMachineId();
            using var connection = RequireSession().OpenConnection();
            using var transaction = connection.BeginTransaction();
            if (FindExactMapping(connection, transaction, path, machineId) is { } existing)
                return allowManagedOverlap
                    ? new MediaRootChangeResult(true, Read(connection, existing, machineId))
                    : new MediaRootChangeResult(false,
                        Diagnostic: "That folder is already mapped by another Media Root on this computer.");
            if (!allowManagedOverlap && FindOverlap(connection, transaction, path, null, machineId,
                    ignoreNaturalAnchors: true) is { } overlap)
                return new MediaRootChangeResult(false, Diagnostic: $"That folder overlaps Media Root ‘{overlap}’ on this computer.");
            var rootId = Guid.NewGuid();
            var now = UtcTimestamp();
            Execute(connection, transaction, "INSERT INTO MediaRoots (RootId, DisplayName, SourceStatus, CreatedUtc, UpdatedUtc) VALUES ($id,$name,'online',$now,$now);",
                ("$id", rootId.ToString("D")), ("$name", name), ("$now", now));
            Execute(connection, transaction, "INSERT INTO MediaRootMappings (MappingId, RootId, MachineId, PhysicalPath, SourceStatus, CreatedUtc, UpdatedUtc) VALUES ($mapping,$id,$machine,$path,'online',$now,$now);",
                ("$mapping", Guid.NewGuid().ToString("D")), ("$id", rootId.ToString("D")), ("$machine", machineId), ("$path", path), ("$now", now));
            transaction.Commit();
            return new MediaRootChangeResult(true, Observe(rootId, name, path));
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Guid? FindExactMapping(SqliteConnection connection, SqliteTransaction transaction,
        string path, string machineId, Guid? exclude = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT RootId,PhysicalPath FROM MediaRootMappings WHERE MachineId=$machine;";
        command.Parameters.AddWithValue("$machine", machineId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (Guid.Parse(reader.GetString(0)) != exclude &&
                string.Equals(MediaPathSemantics.NormalizeRootPath(path),
                    MediaPathSemantics.NormalizeRootPath(reader.GetString(1)), StringComparison.OrdinalIgnoreCase))
                return Guid.Parse(reader.GetString(0));
        return null;
    }

    public Task<MediaRootChangeResult> RenameAsync(Guid rootId, string displayName, CancellationToken cancellationToken = default) => RunChangeAsync(() =>
    {
        var name = NormalizeName(displayName);
        var machineId = machine.GetMachineId();
        using var connection = RequireSession().OpenConnection();
        var changed = Execute(connection, null, "UPDATE MediaRoots SET DisplayName=$name, UpdatedUtc=$now WHERE RootId=$id;",
            ("$name", name), ("$now", UtcTimestamp()), ("$id", rootId.ToString("D")));
        return changed == 0 ? new(false, Diagnostic: "The Media Root no longer exists.") : new(true, Read(connection, rootId, machineId));
    }, cancellationToken);

    public async Task<MediaRootChangeResult> RemapAsync(Guid rootId, string physicalPath, CancellationToken cancellationToken = default)
    {
        string path;
        try { path = await ProbeAsync(physicalPath, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return new(false, Diagnostic: ex.Message); }
        return await RunChangeAsync(() =>
        {
            var machineId = machine.GetMachineId();
            using var connection = RequireSession().OpenConnection();
            using var transaction = connection.BeginTransaction();
            if (!RootExists(connection, transaction, rootId)) return new(false, Diagnostic: "The Media Root no longer exists.");
            if (FindExactMapping(connection, transaction, path, machineId, rootId) is not null)
                return new(false, Diagnostic: "That folder is already mapped by another Media Root on this computer.");
            if (FindOverlap(connection, transaction, path, rootId, machineId,
                    ignoreNaturalAnchors: true) is { } overlap)
                return new(false, Diagnostic: $"That folder overlaps Media Root ‘{overlap}’ on this computer.");
            var now = UtcTimestamp();
            Execute(connection, transaction, """
                INSERT INTO MediaRootMappings (MappingId,RootId,MachineId,PhysicalPath,SourceStatus,CreatedUtc,UpdatedUtc)
                VALUES ($mapping,$id,$machine,$path,'online',$now,$now)
                ON CONFLICT(RootId,MachineId) DO UPDATE SET PhysicalPath=excluded.PhysicalPath, SourceStatus='online', UpdatedUtc=excluded.UpdatedUtc;
                """, ("$mapping", Guid.NewGuid().ToString("D")), ("$id", rootId.ToString("D")), ("$machine", machineId), ("$path", path), ("$now", now));
            transaction.Commit();
            return new(true, Read(connection, rootId, machineId));
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<MediaPathResolution> ResolveAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => RunAsync<MediaPathResolution>(() =>
    {
        var normalized = MediaPathSemantics.NormalizeRelativePath(relativePath);
        var machineId = machine.GetMachineId();
        using var connection = RequireSession().OpenConnection();
        var root = Read(connection, rootId, machineId) ?? throw new KeyNotFoundException("The Media Root does not exist.");
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

    private MediaRootInfo? Read(SqliteConnection connection, Guid id, string machineId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT r.DisplayName,m.PhysicalPath FROM MediaRoots r LEFT JOIN MediaRootMappings m ON m.RootId=r.RootId AND m.MachineId=$machine WHERE r.RootId=$id;";
        command.Parameters.AddWithValue("$machine", machineId); command.Parameters.AddWithValue("$id", id.ToString("D"));
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

    private static string? FindOverlap(SqliteConnection connection, SqliteTransaction transaction, string path,
        Guid? exclude, string machineId, bool ignoreNaturalAnchors)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT r.RootId,r.DisplayName,m.PhysicalPath FROM MediaRootMappings m JOIN MediaRoots r ON r.RootId=m.RootId WHERE m.MachineId=$machine;";
        command.Parameters.AddWithValue("$machine", machineId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (Guid.Parse(reader.GetString(0)) != exclude &&
                (!ignoreNaturalAnchors || !IsNaturalAnchor(reader.GetString(2))) &&
                MediaPathSemantics.Overlaps(path, reader.GetString(2))) return reader.GetString(1);
        return null;
    }

    private static bool IsNaturalAnchor(string path)
    {
        var normalized = MediaPathSemantics.NormalizeRootPath(path);
        var root = Path.GetPathRoot(normalized);
        return !string.IsNullOrWhiteSpace(root) && string.Equals(normalized,
            MediaPathSemantics.NormalizeRootPath(root), StringComparison.OrdinalIgnoreCase);
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
        try
        {
            try { return await RunAsync(operation, token).ConfigureAwait(false); }
            catch (MachineIdentityException exception) { return new(false, Diagnostic: exception.Message); }
        }
        finally { _mutationGate.Release(); }
    }
}
