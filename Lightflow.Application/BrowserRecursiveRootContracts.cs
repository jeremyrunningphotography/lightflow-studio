namespace LightflowStudio;
public sealed record BrowserRecursiveRoot(Guid ScopeId, Guid RootId, string RelativeFolder);
public interface IBrowserRecursiveRootRepository
{
    Task<IReadOnlyList<BrowserRecursiveRoot>> ListAsync(CancellationToken cancellationToken = default);
    Task CreateAsync(Guid rootId, string relativeFolder, CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(IReadOnlyCollection<Guid> scopeIds, CancellationToken cancellationToken = default);
}
