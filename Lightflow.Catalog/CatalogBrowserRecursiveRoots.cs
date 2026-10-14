namespace LightflowStudio;
public sealed class CatalogBrowserRecursiveRootRepository(Func<CatalogDatabaseSession?> session, CatalogMutationLifecycle? mutations = null)
    : IBrowserRecursiveRootRepository, ICatalogMutationParticipant
{
    public CatalogMutationLifecycle Mutations => mutations ?? RequireSession().Mutations;
    public Task<IReadOnlyList<BrowserRecursiveRoot>> ListAsync(CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<BrowserRecursiveRoot>>(() =>
        {
            using var connection = RequireSession().OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ScopeId, RootId, RelativeFolder FROM BrowserRecursiveScopes ORDER BY RootId, RelativeFolderKey;";
            using var reader = command.ExecuteReader();
            var results = new List<BrowserRecursiveRoot>();
            while (reader.Read())
                results.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2)));
            return results;
        }, cancellationToken);

    public Task CreateAsync(Guid rootId, string relativeFolder, CancellationToken cancellationToken = default) {
        return RequireSession().Mutations.RunAsync(() => { return RunAsync(() =>
        {
            using var connection = RequireSession().OpenConnection();
            using var command = connection.CreateCommand();
            var now = Timestamp(DateTimeOffset.UtcNow);
            command.CommandText = """
                INSERT INTO BrowserRecursiveScopes (ScopeId,RootId,RelativeFolder,RelativeFolderKey,CreatedUtc,UpdatedUtc)
                VALUES ($scope,$root,$folder,$key,$now,$now)
                ON CONFLICT(RootId, RelativeFolderKey) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$scope", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$root", rootId.ToString("D"));
            command.Parameters.AddWithValue("$folder", relativeFolder);
            command.Parameters.AddWithValue("$key", relativeFolder.ToUpperInvariant());
            command.Parameters.AddWithValue("$now", now);
            command.ExecuteNonQuery();
        }, cancellationToken); }, cancellationToken);
    }

    public Task<int> DeleteAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken cancellationToken = default) {
        return RequireSession().Mutations.RunAsync<int>(() => { return RunAsync(() =>
        {
            if (scopeIds.Count == 0) return 0;
            using var connection = RequireSession().OpenConnection();
            using var transaction = connection.BeginTransaction();
            var deleted = 0;
            foreach (var scopeId in scopeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM BrowserRecursiveScopes WHERE ScopeId=$scope;";
                command.Parameters.AddWithValue("$scope", scopeId.ToString("D"));
                deleted += command.ExecuteNonQuery();
            }
            transaction.Commit();
            return deleted;
        }, cancellationToken); }, cancellationToken);
    }

    private CatalogDatabaseSession RequireSession() =>
        session() ?? throw new InvalidOperationException("The Catalog is unavailable.");

    private static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static Task RunAsync(Action operation, CancellationToken token) =>
        Task.Run(() => { token.ThrowIfCancellationRequested(); operation(); }, token);

    private static Task<T> RunAsync<T>(Func<T> operation, CancellationToken token) =>
        Task.Run(() => { token.ThrowIfCancellationRequested(); return operation(); }, token);
}
