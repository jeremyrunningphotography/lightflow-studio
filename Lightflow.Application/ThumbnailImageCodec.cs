namespace LightflowStudio;

/// <summary>Encoded image mechanism only. Source admission, demand and cache publication remain shared.</summary>
public interface IThumbnailImageCodec
{
    bool Supports(string extension);
    ThumbnailRenderResult Render(string source, string destination, IReadOnlyList<string> orderedLutPaths,
        CancellationToken token);
    bool Validate(string path);
}
