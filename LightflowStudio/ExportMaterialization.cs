namespace LightflowStudio;

internal sealed record SourceMediaTraits(
    string VideoCodec,
    int Width,
    int Height,
    double FrameRate,
    string Container,
    string? AudioCodec = null,
    int? AudioSampleRate = null,
    int? AudioChannels = null,
    string? AudioChannelLayout = null);

internal enum ColorStagePolicyMode { AsSelectedInLightflow, NoLut, Override }

internal sealed record ColorStagePolicy(
    ColorStagePolicyMode Mode = ColorStagePolicyMode.AsSelectedInLightflow,
    MaterializedLutResource? Override = null);

internal sealed record ExportMaterializationPolicy(
    VideoCodecPolicy VideoCodec = VideoCodecPolicy.Explicit,
    OutputContainerPolicy Container = OutputContainerPolicy.Explicit,
    EncodingQualityPolicy Quality = EncodingQualityPolicy.Automatic,
    ColorStagePolicy? Camera = null,
    ColorStagePolicy? Creative = null,
    AudioFallbackEncoding? SourceAudioFallback = null);

internal enum MaterializedAudioMode { SourceCopyPreferred, EncodedAac, None }

internal sealed record MaterializedAudioIntent(
    MaterializedAudioMode Mode,
    AudioFallbackEncoding? Fallback = null);

internal sealed record MaterializedExportSettings(
    EncodingOptions Encoding,
    OutputResolution Resolution,
    MaterializedAudioIntent Audio,
    MaterializedColorPipeline? Color,
    SourceMediaTraits? SourceTraits,
    EncodingQualityPolicy QualityPolicy = EncodingQualityPolicy.Automatic,
    string? MaterializationProblem = null);

internal static class ExportSettingsMaterializer
{
    public static MaterializedExportSettings Materialize(EncodingJobOptions options, EncodingSource source)
    {
        if (source.RestoredExport is not null) return source.RestoredExport;
        var policy = options.MaterializationPolicy;
        var encoding = EncodingOptions.Normalize(options.Encoding);
        string? problem = null;
        if (policy?.VideoCodec == VideoCodecPolicy.SameAsSource)
            if (TryResolveCodec(source.MediaTraits?.VideoCodec, out var codec)) encoding = encoding with { Codec = codec };
            else problem = $"Source video codec '{source.MediaTraits?.VideoCodec ?? "unknown"}' is not supported for Same as Source encoding.";
        if (policy?.Container == OutputContainerPolicy.SameAsSource)
            if (TryResolveContainer(source.MediaTraits?.Container, out var container)) encoding = encoding with { Container = container };
            else problem = Join(problem, $"Source container '{source.MediaTraits?.Container ?? "unknown"}' is not supported for Same as Source output.");

        var fallback = policy?.SourceAudioFallback ?? new AudioFallbackEncoding(
            encoding.AudioBitrateKbps, encoding.AudioSampleRate, encoding.AudioChannels);
        if (encoding.AudioMode == AudioEncodingMode.Copy)
            fallback = fallback with
            {
                SampleRate = fallback.SampleRate > 0 ? fallback.SampleRate : source.MediaTraits?.AudioSampleRate ?? 0,
                Channels = fallback.Channels > 0 ? fallback.Channels : source.MediaTraits?.AudioChannels ?? 0
            };
        var audio = encoding.AudioMode switch
        {
            AudioEncodingMode.Copy => new MaterializedAudioIntent(MaterializedAudioMode.SourceCopyPreferred, fallback),
            AudioEncodingMode.Aac => new MaterializedAudioIntent(MaterializedAudioMode.EncodedAac,
                new(encoding.AudioBitrateKbps, encoding.AudioSampleRate, encoding.AudioChannels)),
            _ => new MaterializedAudioIntent(MaterializedAudioMode.None)
        };
        encoding = encoding with
        {
            AudioBitrateKbps = fallback.BitrateKbps,
            AudioSampleRate = fallback.SampleRate,
            AudioChannels = fallback.Channels
        };
        var camera = policy is null ? source.AssignedColor?.Camera : ResolveStage(ColorLutStage.Camera, policy.Camera, source.AssignedColor?.Camera);
        var creative = policy is null ? source.AssignedColor?.Creative : ResolveStage(ColorLutStage.Creative, policy.Creative, source.AssignedColor?.Creative);
        var color = policy is null ? source.AssignedColor : new MaterializedColorPipeline(
            camera is not null || creative is not null, camera, creative);
        return new(encoding, options.Resolution, audio, color, source.MediaTraits,
            policy?.Quality ?? EncodingQualityPolicy.Explicit, problem);
    }

    private static bool TryResolveCodec(string? value, out VideoCodec codec)
    {
        codec = value?.Trim().ToLowerInvariant() switch
        {
            "hevc" or "h265" or "hev1" or "hvc1" => VideoCodec.Hevc,
            _ => VideoCodec.H264
        };
        return value?.Trim().ToLowerInvariant() is "h264" or "avc" or "avc1" or "hevc" or "h265" or "hev1" or "hvc1";
    }

    private static bool TryResolveContainer(string? value, out OutputContainer container)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        container = normalized switch { "mov" => OutputContainer.Mov, "matroska" or "matroska,webm" or "mkv" => OutputContainer.Mkv, _ => OutputContainer.Mp4 };
        return normalized is "mp4" or "mov,mp4,m4a,3gp,3g2,mj2" or "mov" or "matroska" or "matroska,webm" or "mkv";
    }

    private static string Join(string? first, string second) => string.IsNullOrEmpty(first) ? second : first + " " + second;

    private static MaterializedLutResource? ResolveStage(ColorLutStage stage, ColorStagePolicy? policy,
        MaterializedLutResource? selected)
    {
        policy ??= new();
        return policy.Mode switch
        {
            ColorStagePolicyMode.AsSelectedInLightflow => selected,
            ColorStagePolicyMode.NoLut => null,
            ColorStagePolicyMode.Override when policy.Override?.Stage == stage => policy.Override,
            ColorStagePolicyMode.Override => throw new InvalidOperationException($"The {stage} override is missing or belongs to the wrong Color stage."),
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };
    }
}
