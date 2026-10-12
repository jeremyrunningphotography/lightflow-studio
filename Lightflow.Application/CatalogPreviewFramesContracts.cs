using System.Globalization;
namespace LightflowStudio;
public sealed record PreferredPreviewFrame(
    Guid AssetId,
    TimeSpan Position,
    long Revision,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public interface IPreferredPreviewFrameStore
{
    Task<PreferredPreviewFrame?> GetAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<PreferredPreviewFrame> SetAsync(Guid assetId, MediaPresentationTimestamp timestamp,
        TimeSpan sourceDuration, CancellationToken cancellationToken = default);
    Task ResetAsync(Guid assetId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Catalog-owned user intent for #205. Only the authoritative decoded source timestamp is durable; Preview
/// pixels remain rebuildable and continue through the ordinary thumbnail pipeline.
/// </summary>
