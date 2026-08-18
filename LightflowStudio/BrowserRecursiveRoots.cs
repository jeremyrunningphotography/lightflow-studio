namespace LightflowStudio;

/// <summary>
/// #124 (revised): one durable "Include Subfolders" recursive root, stored in the Catalog. Root-agnostic
/// identity — <see cref="RootId"/> + normalized <see cref="RelativeFolder"/> — never an absolute physical
/// path, so the setting survives Media Root remapping exactly like every other logical Catalog identity.
/// <see cref="RelativeFolder"/> is empty for a root established at a Media Root's own top level.
/// </summary>
internal sealed record BrowserRecursiveRoot(Guid ScopeId, Guid RootId, string RelativeFolder);

/// <summary>
/// Pure inheritance/normalization rules over an already-fetched root list — independent of SQL, Catalog, and
/// WPF, and therefore directly unit-testable. Segment-aware ancestry is delegated to
/// <see cref="BrowserScope.IsWithinFolderScope"/>, the same authoritative "is folder A within folder B's
/// descendant scope" answer #101 monitoring relevance already uses (e.g. "Trip" is never an ancestor of
/// "Trips") — one answer, three consumers (monitoring, recursive-root membership, tree iconography).
/// </summary>
internal static class BrowserRecursiveRootLogic
{
    /// <summary>True when <paramref name="relativeFolder"/> is governed by any stored root for <paramref name="rootId"/> — equal to it or a descendant.</summary>
    public static bool IsEffectivelyRecursive(IReadOnlyList<BrowserRecursiveRoot> roots, Guid rootId, string relativeFolder) =>
        GoverningRoots(roots, rootId, relativeFolder).Count > 0;

    /// <summary>
    /// The stored root(s) — normally at most one in a correctly normalized table — whose scope already covers
    /// <paramref name="relativeFolder"/>. Used both to answer "is this folder effectively recursive" and to
    /// find what a "disable" action at a descendant folder must remove.
    /// </summary>
    public static IReadOnlyList<BrowserRecursiveRoot> GoverningRoots(
        IReadOnlyList<BrowserRecursiveRoot> roots, Guid rootId, string relativeFolder) =>
        [.. roots.Where(root => root.RootId == rootId && BrowserScope.IsWithinFolderScope(relativeFolder, root.RelativeFolder))];

    /// <summary>
    /// Existing stored roots for <paramref name="rootId"/> that become redundant once <paramref name="relativeFolder"/>
    /// is established as a recursive root — its own strict-or-equal descendants. Normalization rule 2: an
    /// ancestor that newly becomes a recursive root absorbs any descendant roots it now already covers.
    /// </summary>
    public static IReadOnlyList<BrowserRecursiveRoot> RedundantDescendants(
        IReadOnlyList<BrowserRecursiveRoot> roots, Guid rootId, string relativeFolder) =>
        [.. roots.Where(root => root.RootId == rootId && BrowserScope.IsWithinFolderScope(root.RelativeFolder, relativeFolder))];
}

internal interface IBrowserRecursiveRootRepository
{
    Task<IReadOnlyList<BrowserRecursiveRoot>> ListAsync(CancellationToken cancellationToken = default);
    Task CreateAsync(Guid rootId, string relativeFolder, CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// SQLite-backed repository for <c>BrowserRecursiveScopes</c> (schema version 2). Mirrors
/// <see cref="CatalogMediaAssetRepository"/>'s conventions exactly: a <c>Func&lt;CatalogDatabaseSession?&gt;</c>
/// so the same instance keeps working correctly across Catalog reopen/relocation, and an
/// <see cref="InvalidOperationException"/> when the Catalog is unavailable — the same exception type
/// <see cref="BrowserNavigationSession"/>'s existing generic failure handling already converts into an honest
/// Browser failure state, so no second Catalog-unavailable UI path is needed for this feature.
/// </summary>
internal sealed class CatalogBrowserRecursiveRootRepository(Func<CatalogDatabaseSession?> session)
    : IBrowserRecursiveRootRepository
{
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

    public Task CreateAsync(Guid rootId, string relativeFolder, CancellationToken cancellationToken = default) =>
        RunAsync(() =>
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
        }, cancellationToken);

    public Task<int> DeleteAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken cancellationToken = default) =>
        RunAsync(() =>
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
        }, cancellationToken);

    private CatalogDatabaseSession RequireSession() =>
        session() ?? throw new InvalidOperationException("The Catalog is unavailable.");

    private static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static Task RunAsync(Action operation, CancellationToken token) =>
        Task.Run(() => { token.ThrowIfCancellationRequested(); operation(); }, token);

    private static Task<T> RunAsync<T>(Func<T> operation, CancellationToken token) =>
        Task.Run(() => { token.ThrowIfCancellationRequested(); return operation(); }, token);
}

internal interface IBrowserRecursiveRootService
{
    /// <summary>Every stored recursive root across every Media Root. The table is expected to stay small (a handful of user-established roots), so callers are free to fetch the full list rather than filtering server-side.</summary>
    Task<IReadOnlyList<BrowserRecursiveRoot>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Establishes <paramref name="relativeFolder"/> as a recursive root. A no-op if it is already covered by
    /// an existing root (normalization rule 1); otherwise removes any existing roots the new one now
    /// redundantly covers (rule 2) before creating it.
    /// </summary>
    Task EnableAsync(Guid rootId, string relativeFolder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes whichever stored root currently governs <paramref name="relativeFolder"/> — the folder itself
    /// if it is a root, or its ancestor root if the mode was inherited. A no-op if nothing currently governs
    /// it. Never creates a per-folder "off" override; recursive behavior is inherited from stored roots only.
    /// </summary>
    Task DisableAsync(Guid rootId, string relativeFolder, CancellationToken cancellationToken = default);
}

internal sealed class BrowserRecursiveRootService(IBrowserRecursiveRootRepository repository) : IBrowserRecursiveRootService
{
    public Task<IReadOnlyList<BrowserRecursiveRoot>> ListAsync(CancellationToken cancellationToken = default) =>
        repository.ListAsync(cancellationToken);

    public async Task EnableAsync(Guid rootId, string relativeFolder, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeFolder(relativeFolder);
        var existing = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        if (BrowserRecursiveRootLogic.IsEffectivelyRecursive(existing, rootId, normalized)) return;

        var redundant = BrowserRecursiveRootLogic.RedundantDescendants(existing, rootId, normalized);
        if (redundant.Count > 0)
            await repository.DeleteAsync([.. redundant.Select(root => root.ScopeId)], cancellationToken).ConfigureAwait(false);
        await repository.CreateAsync(rootId, normalized, cancellationToken).ConfigureAwait(false);
    }

    public async Task DisableAsync(Guid rootId, string relativeFolder, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeFolder(relativeFolder);
        var existing = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var governing = BrowserRecursiveRootLogic.GoverningRoots(existing, rootId, normalized);
        if (governing.Count == 0) return;
        await repository.DeleteAsync([.. governing.Select(root => root.ScopeId)], cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeFolder(string relativeFolder) =>
        relativeFolder.Length == 0 ? "" : MediaPathSemantics.NormalizeRelativePath(relativeFolder);
}
