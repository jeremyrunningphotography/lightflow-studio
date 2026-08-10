namespace LightflowStudio;

internal sealed record MediaMetadata(
    int Width,
    int Height,
    double FrameRate,
    double DurationSeconds,
    long FileSizeBytes,
    string VideoCodec,
    bool HasAudio,
    TimeSpan StartTimestamp = default);
