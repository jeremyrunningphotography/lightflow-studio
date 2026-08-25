namespace LightflowStudio;

internal sealed record MediaMetadata(
    int Width,
    int Height,
    double FrameRate,
    double DurationSeconds,
    long FileSizeBytes,
    string VideoCodec,
    bool HasAudio,
    TimeSpan StartTimestamp = default,
    string Container = "",
    string? AudioCodec = null,
    int? AudioSampleRate = null,
    int? AudioChannels = null,
    string? AudioChannelLayout = null);
