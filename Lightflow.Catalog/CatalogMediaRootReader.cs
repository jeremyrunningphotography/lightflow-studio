using Microsoft.Data.Sqlite;
namespace LightflowStudio;

/// <summary>The existing machine-local root projection, shared by browsing and root management.</summary>
public class CatalogMediaRootReader(Func<CatalogDatabaseSession?> session, IMachineIdentityProvider machine,
    IMediaRootFileSystem fileSystem) : IMediaRootReader
{
    protected Func<CatalogDatabaseSession?> SessionProvider { get; } = session;
    protected IMachineIdentityProvider Machine { get; } = machine;
    protected IMediaRootFileSystem FileSystem { get; } = fileSystem;
    public Task<IReadOnlyList<MediaRootInfo>> ListAsync(CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        var machineId = Machine.GetMachineId();
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
        var machineId = Machine.GetMachineId();
        using var connection = RequireSession().OpenConnection();
        return Read(connection, rootId, machineId);
    }, cancellationToken);

    protected MediaRootInfo Observe(Guid id, string name, string? path) => path is null
        ? new(id, name, null, MediaRootAvailability.Unmapped, "This Media Root is not connected on this computer.")
        : FileSystem.DirectoryExists(path)
            ? new(id, name, path, MediaRootAvailability.Online)
            : new(id, name, path, MediaRootAvailability.Unavailable, "The mapped folder is currently unavailable.");

    protected MediaRootInfo? Read(SqliteConnection connection, Guid id, string machineId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT r.DisplayName,m.PhysicalPath FROM MediaRoots r LEFT JOIN MediaRootMappings m ON m.RootId=r.RootId AND m.MachineId=$machine WHERE r.RootId=$id;";
        command.Parameters.AddWithValue("$machine", machineId); command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? Observe(id, reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)) : null;
    }

    private CatalogDatabaseSession RequireSession() => SessionProvider() ?? throw new InvalidOperationException("The Catalog is unavailable.");
    private static Task<T> RunAsync<T>(Func<T> operation, CancellationToken token) => Task.Run(() => { token.ThrowIfCancellationRequested(); return operation(); }, token);
}
