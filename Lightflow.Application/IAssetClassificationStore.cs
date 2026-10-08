using Lightflow.Domain;

namespace Lightflow.Application;

public interface IAssetClassificationStore
{
    Task<IReadOnlyDictionary<Guid, AssetClassification>> GetAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);
    Task SaveAsync(AssetClassification classification, CancellationToken cancellationToken = default);
    /// <summary>Fresh-value mutation under the Catalog-owned classification serialization boundary.</summary>
    Task<AssetClassification> UpdateAsync(Guid assetId, Func<AssetClassification, AssetClassification> mutate,
        CancellationToken token = default) => throw new NotSupportedException("This read-only classification adapter cannot mutate.");
}
