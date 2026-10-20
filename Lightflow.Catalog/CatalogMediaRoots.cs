using Lightflow.Application;
using System.IO;
using Microsoft.Data.Sqlite;
namespace LightflowStudio;
public class CatalogMediaRootService(Func<CatalogDatabaseSession?> session, IMachineIdentityProvider machine,
    IMediaRootFileSystem fileSystem, IMediaRootPathEffects paths) : CatalogMediaRootReader(session, machine, fileSystem), IMediaRootService
{
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    public async Task<MediaRootChangeResult> CreateAsync(string displayName, string physicalPath, CancellationToken cancellationToken = default)
        => await CreateCoreAsync(displayName, physicalPath, allowManagedOverlap: false, cancellationToken).ConfigureAwait(false);

    public async Task<MediaRootChangeResult> CreateBrowserAnchorAsync(string displayName, string physicalPath,
        CancellationToken cancellationToken = default)
    {
        string normalized;
        try { normalized = paths.NormalizeRootPath(physicalPath); }
        catch (ArgumentException exception) { return new(false, Diagnostic: exception.Message); }
        if (!IsNaturalAnchor(normalized))
            return new(false, Diagnostic: "Automatic Browser roots must be anchored at a volume or network-share boundary.");
        return await CreateCoreAsync(displayName, normalized, allowManagedOverlap: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MediaRootChangeResult> CreateCoreAsync(string displayName, string physicalPath,
        bool allowManagedOverlap, CancellationToken cancellationToken)
    {
        return await RequireSession().Mutations.RunAsync<MediaRootChangeResult>(async () => {
        var name = NormalizeName(displayName);
        string path;
        try { path = await ProbeAsync(physicalPath, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return new(false, Diagnostic: ex.Message); }
        using var pathAdmission = paths.OpenAdmission(path);
        try { await pathAdmission.ValidateAsync(cancellationToken, Path.GetDirectoryName(RequireSession().ResolvedDatabasePath)).ConfigureAwait(false); }
        catch (IOException exception) { return new(false, Diagnostic: exception.Message); }
        return await RunChangeAsync(() =>
        {
            var machineId = Machine.GetMachineId();
            using var connection = RequireSession().OpenConnection();
            if (allowManagedOverlap && FindExactMappingWithoutTransaction(connection, path, machineId) is { } anchor)
                return new(true, Read(connection, anchor, machineId));
            var nativeConflict = FindNativeConflict(connection, pathAdmission, machineId, null, allowManagedOverlap, cancellationToken);
            if (nativeConflict is not null) return new(false, Diagnostic: nativeConflict);
            try { pathAdmission.ValidateAsync(cancellationToken, Path.GetDirectoryName(RequireSession().ResolvedDatabasePath)).GetAwaiter().GetResult(); }
            catch (IOException exception) { return new(false, Diagnostic: exception.Message); }
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
    }, cancellationToken);
    }

    private Guid? FindExactMapping(SqliteConnection connection, SqliteTransaction transaction,
        string path, string machineId, Guid? exclude = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT RootId,PhysicalPath FROM MediaRootMappings WHERE MachineId=$machine;";
        command.Parameters.AddWithValue("$machine", machineId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (Guid.Parse(reader.GetString(0)) != exclude &&
                string.Equals(paths.NormalizeRootPath(path),
                    paths.NormalizeRootPath(reader.GetString(1)), StringComparison.OrdinalIgnoreCase))
                return Guid.Parse(reader.GetString(0));
        return null;
    }

    public Task<MediaRootChangeResult> RenameAsync(Guid rootId, string displayName, CancellationToken cancellationToken = default) {
        return RequireSession().Mutations.RunAsync<MediaRootChangeResult>(() => { return RunChangeAsync(() =>
    {
        var name = NormalizeName(displayName);
        var machineId = Machine.GetMachineId();
        using var connection = RequireSession().OpenConnection();
        var changed = Execute(connection, null, "UPDATE MediaRoots SET DisplayName=$name, UpdatedUtc=$now WHERE RootId=$id;",
            ("$name", name), ("$now", UtcTimestamp()), ("$id", rootId.ToString("D")));
        return changed == 0 ? new(false, Diagnostic: "The Media Root no longer exists.") : new(true, Read(connection, rootId, machineId));
    }, cancellationToken); }, cancellationToken);
    }

    public async Task<MediaRootChangeResult> RemapAsync(Guid rootId, string physicalPath, CancellationToken cancellationToken = default)
    {
        return await RequireSession().Mutations.RunAsync<MediaRootChangeResult>(async () => {
        string path;
        try { path = await ProbeAsync(physicalPath, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return new(false, Diagnostic: ex.Message); }
        PathIdentityCandidate[] mappingCandidates;
        using (var readConnection = RequireSession().OpenConnection())
            mappingCandidates = ReadCandidates(readConnection, null, rootId);
        using var pathAdmission = paths.OpenAdmission(path);
        try
        {
            await pathAdmission.ValidateAsync(cancellationToken, Path.GetDirectoryName(RequireSession().ResolvedDatabasePath)).ConfigureAwait(false);
            await Task.Run(() => paths.ValidatePaths(path, mappingCandidates, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return new(false, Diagnostic: exception.Message); }
        return await RunChangeAsync(() =>
        {
            var machineId = Machine.GetMachineId();
            using var connection = RequireSession().OpenConnection();
            var nativeConflict = FindNativeConflict(connection, pathAdmission, machineId, rootId, false, cancellationToken);
            if (nativeConflict is not null) return new(false, Diagnostic: nativeConflict);
            try { pathAdmission.ValidateAsync(cancellationToken, Path.GetDirectoryName(RequireSession().ResolvedDatabasePath)).GetAwaiter().GetResult(); }
            catch (IOException exception) { return new(false, Diagnostic: exception.Message); }
            using var transaction = connection.BeginTransaction();
            var currentCandidates = ReadCandidates(connection, transaction, rootId);
            if (!currentCandidates.SequenceEqual(mappingCandidates))
                return new(false, Diagnostic: "Catalog paths changed during mapping preflight. Retry without changing the existing mapping.");
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
    }, cancellationToken);
    }

    public Task<MediaPathResolution> ResolveAsync(Guid rootId, string relativePath, CancellationToken cancellationToken = default) => RunAsync<MediaPathResolution>(() =>
    {
        var portable = relativePath.Replace('\\', '/'); // Explicit Windows separator compatibility, never a Mac filename conversion.
        var check = PortablePathIdentityValidator.ValidateRelativePath(portable, cancellationToken);
        if (!check.IsSafe)
            return new(rootId, relativePath, "", null, MediaRootAvailability.Online, false, check.Diagnostic, check.Status);
        var normalized = portable;
        var machineId = Machine.GetMachineId();
        using var connection = RequireSession().OpenConnection();
        var root = Read(connection, rootId, machineId) ?? throw new KeyNotFoundException("The Media Root does not exist.");
        if (root.Availability != MediaRootAvailability.Online)
            return new(rootId, normalized, paths.RelativePathKey(normalized), null, root.Availability, false, root.Diagnostic);
        try
        {
            using var admission = paths.OpenAdmission(root.PhysicalPath!);
            admission.ValidateAsync(cancellationToken, Path.GetDirectoryName(RequireSession().ResolvedDatabasePath)).GetAwaiter().GetResult();
            paths.ValidatePaths(root.PhysicalPath!, [new(rootId, Guid.Empty, normalized)], cancellationToken);
            admission.ValidateAsync(cancellationToken, Path.GetDirectoryName(RequireSession().ResolvedDatabasePath)).GetAwaiter().GetResult();
        }
        catch (IOException exception)
        {
            return new(rootId, normalized, paths.RelativePathKey(normalized), null, root.Availability, false,
                exception.Message, exception is MediaPathIdentityException refusal ? refusal.Status : PathIdentityStatus.NativeEvidenceUnavailable);
        }
        var resolved = paths.ResolveContained(root.PhysicalPath!, normalized);
        var exists = FileSystem.FileExists(resolved);
        return new(rootId, normalized, paths.RelativePathKey(normalized), resolved, root.Availability,
            exists, exists ? null : "The file is missing beneath an available Media Root.");
    }, cancellationToken);

    private CatalogDatabaseSession RequireSession() => SessionProvider() ?? throw new InvalidOperationException("The Catalog is unavailable.");
    private async Task<string> ProbeAsync(string path, CancellationToken token)
    {
        if (!string.Equals(path, path.Trim(), StringComparison.Ordinal)) throw new ArgumentException("Root mapping spelling must not require trimming.");
        var normalized = paths.NormalizeRootPath(path);
        var exists = await Task.Run(() => FileSystem.DirectoryExists(normalized), token).ConfigureAwait(false);
        if (!exists) throw new IOException("The selected Media Root folder is not currently available.");
        return normalized;
    }

    private string? FindOverlap(SqliteConnection connection, SqliteTransaction transaction, string path,
        Guid? exclude, string machineId, bool ignoreNaturalAnchors)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT r.RootId,r.DisplayName,m.PhysicalPath FROM MediaRootMappings m JOIN MediaRoots r ON r.RootId=m.RootId WHERE m.MachineId=$machine;";
        command.Parameters.AddWithValue("$machine", machineId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (Guid.Parse(reader.GetString(0)) != exclude &&
                (!ignoreNaturalAnchors || !IsNaturalAnchor(reader.GetString(2))) &&
                paths.Overlaps(path, reader.GetString(2))) return reader.GetString(1);
        return null;
    }

    private bool IsNaturalAnchor(string path)
    {
        var normalized = paths.NormalizeRootPath(path);
        var root = Path.GetPathRoot(normalized);
        return !string.IsNullOrWhiteSpace(root) && string.Equals(normalized,
            paths.NormalizeRootPath(root), StringComparison.OrdinalIgnoreCase);
    }

    private Guid? FindExactMappingWithoutTransaction(SqliteConnection connection, string path, string machineId)
    {
        using var transaction = connection.BeginTransaction();
        return FindExactMapping(connection, transaction, path, machineId);
    }

    private string? FindNativeConflict(SqliteConnection connection, IMediaRootPathAdmission admission,
        string machineId, Guid? exclude, bool allowOverlap, CancellationToken token)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT RootId,PhysicalPath FROM MediaRootMappings WHERE MachineId=$machine;";
        command.Parameters.AddWithValue("$machine", machineId);
        var mappings = new List<(Guid Id, string Path)>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) mappings.Add((Guid.Parse(reader.GetString(0)), reader.GetString(1)));
        foreach (var mapping in mappings.Where(m => m.Id != exclude))
        {
            try
            {
                var conflict = admission.ConflictingMapping(mapping.Id, exclude ?? Guid.Empty, mapping.Path, allowOverlap || IsNaturalAnchor(mapping.Path), token);
                if (conflict is not null) return conflict;
            }
            catch (IOException exception) { return exception.Message; }
        }
        return null;
    }

    private static PathIdentityCandidate[] ReadCandidates(SqliteConnection connection, SqliteTransaction? transaction, Guid rootId)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT AssetId,RelativePath,RelativePathKey FROM MediaAssets WHERE RootId=$root ORDER BY AssetId;";
        command.Parameters.AddWithValue("$root", rootId.ToString("D"));
        using var reader = command.ExecuteReader();
        var rows = new List<PathIdentityCandidate>();
        while (reader.Read()) rows.Add(new(rootId, Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)));
        return rows.ToArray();
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
