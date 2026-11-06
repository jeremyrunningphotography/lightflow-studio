namespace LightflowStudio;

public sealed record BrowserThumbnail(Guid AssetId,string? Path,string State,int RotationDegrees=0,string? Diagnostic=null);
public interface IBrowserThumbnailSession
{
    Task<IDerivedWorkBatch?> DemandThumbnailsAsync(IReadOnlyList<Guid> ids,DerivedWorkPriority priority,CancellationToken token);
    Task<IReadOnlyDictionary<Guid,BrowserThumbnail>> ReadThumbnailsAsync(IReadOnlyList<Guid> ids,CancellationToken token);
}
