namespace LightflowStudio;

/// <summary>
/// #124 (revised): one durable "Include Subfolders" recursive root, stored in the Catalog. Root-agnostic
/// identity — <see cref="RootId"/> + normalized <see cref="RelativeFolder"/> — never an absolute physical
/// path, so the setting survives Media Root remapping exactly like every other logical Catalog identity.
/// <see cref="RelativeFolder"/> is empty for a root established at a Media Root's own top level.
/// </summary>


/// <summary>
/// Pure inheritance/normalization rules over an already-fetched root list — independent of SQL, Catalog, and
/// WPF, and therefore directly unit-testable. Segment-aware ancestry is delegated to
/// <see cref="BrowserScope.IsWithinFolderScope"/>, the same authoritative "is folder A within folder B's
/// descendant scope" answer #101 monitoring relevance already uses (e.g. "Trip" is never an ancestor of
/// "Trips") — one answer, three consumers (monitoring, recursive-root membership, tree iconography).
/// </summary>
/// <summary>
/// SQLite-backed repository for <c>BrowserRecursiveScopes</c> (schema version 2). Mirrors
/// <see cref="CatalogMediaAssetRepository"/>'s conventions exactly: a <c>Func&lt;CatalogDatabaseSession?&gt;</c>
/// so the same instance keeps working correctly across Catalog reopen/relocation, and an
/// <see cref="InvalidOperationException"/> when the Catalog is unavailable — the same exception type
/// <see cref="BrowserNavigationSession"/>'s existing generic failure handling already converts into an honest
/// Browser failure state, so no second Catalog-unavailable UI path is needed for this feature.
/// </summary>
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
    private CatalogMutationLifecycle Mutations { get; } = CatalogMutationLifecycle.From(repository);
    public Task<IReadOnlyList<BrowserRecursiveRoot>> ListAsync(CancellationToken cancellationToken = default) =>
        repository.ListAsync(cancellationToken);

    public async Task EnableAsync(Guid rootId, string relativeFolder, CancellationToken cancellationToken = default)
    {
        await Mutations.RunAsync(async () => {
        var normalized = NormalizeFolder(relativeFolder);
        var existing = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        if (BrowserRecursiveRootLogic.IsEffectivelyRecursive(existing, rootId, normalized)) return;

        var redundant = BrowserRecursiveRootLogic.RedundantDescendants(existing, rootId, normalized);
        if (redundant.Count > 0)
            await repository.DeleteAsync([.. redundant.Select(root => root.ScopeId)], cancellationToken).ConfigureAwait(false);
        await repository.CreateAsync(rootId, normalized, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);
    }

    public async Task DisableAsync(Guid rootId, string relativeFolder, CancellationToken cancellationToken = default)
    {
        await Mutations.RunAsync(async () => {
        var normalized = NormalizeFolder(relativeFolder);
        var existing = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var governing = BrowserRecursiveRootLogic.GoverningRoots(existing, rootId, normalized);
        if (governing.Count == 0) return;
        await repository.DeleteAsync([.. governing.Select(root => root.ScopeId)], cancellationToken).ConfigureAwait(false);
    }, cancellationToken);
    }

    private static string NormalizeFolder(string relativeFolder) =>
        relativeFolder.Length == 0 ? "" : MediaPathSemantics.NormalizeRelativePath(relativeFolder);
}
