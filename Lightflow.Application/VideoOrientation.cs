namespace LightflowStudio;

/// <summary>Clockwise adjustment AFTER the source's display transform. Zero preserves source orientation.</summary>
public readonly record struct VideoRotation
{
    public int Degrees { get; }
    [System.Text.Json.Serialization.JsonConstructor]
    public VideoRotation(int degrees)
    {
        if (degrees % 90 != 0) throw new ArgumentOutOfRangeException(nameof(degrees));
        Degrees = ((degrees % 360) + 360) % 360;
    }
    public VideoRotation Turn(bool right) => new(Degrees + (right ? 90 : -90));
    public VideoRotation Compose(VideoRotation adjustment) => new(Degrees + adjustment.Degrees);
    public (int Width, int Height) Dimensions(int width, int height) =>
        Degrees is 90 or 270 ? (height, width) : (width, height);
}

public sealed record AssetVideoRotation(Guid AssetId, VideoRotation Rotation, long Revision = 0);

public interface IAssetVideoRotationStore
{
    event EventHandler<IReadOnlyList<AssetVideoRotation>>? Changed;
    event EventHandler? Invalidated { add { } remove { } }
    Task<IReadOnlyDictionary<Guid, AssetVideoRotation>> GetAsync(IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default);
    Task RotateAsync(IReadOnlyDictionary<Guid, long> expectedRevisions, bool right,
        CancellationToken cancellationToken = default);
}

