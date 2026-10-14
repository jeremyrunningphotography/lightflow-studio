namespace LightflowStudio;

public interface ISubclipService
{
    Task<SubclipCreateResult> CreateAsync(Guid assetId, MediaRange workingRange, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Subclip>> ListAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<Subclip> RenameAsync(Guid subclipId, long expectedRevision, string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid subclipId, long expectedRevision, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid assetId, IReadOnlyList<SubclipOrder> subclips, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Subclip>> ReorderAsync(Guid assetId, IReadOnlyList<SubclipOrder> order,
        CancellationToken cancellationToken = default);
}
