namespace LightflowStudio;

internal enum EncoderBackend { NvidiaNvenc, Cpu, AmdAmf, IntelQuickSync }
internal enum VideoCodec { H264, Hevc }
internal enum VideoCodecPolicy { Explicit, SameAsSource }
internal enum OutputContainerPolicy { Explicit, SameAsSource }
internal enum EncodingQualityPolicy { Automatic, Explicit }
internal enum RateControlMode { ConstantQuality, VariableBitrate, ConstantBitrate }
internal enum EncoderTune { HighQuality, LowLatency, UltraLowLatency }
internal enum MultipassMode { Disabled, QuarterResolution, FullResolution }
internal enum VideoPixelFormat { Yuv420p, P010 }
internal enum AudioEncodingMode { Copy, Aac, None }
internal enum OutputContainer { Mp4, Mkv, Mov }
internal enum EncodingPreset { Recommended, MaximumQuality, FastPreview, EfficientHevc, Custom }

internal sealed record EncodingOptions
{
    public EncoderBackend Backend { get; init; } = EncoderBackend.NvidiaNvenc;
    public VideoCodec Codec { get; init; } = VideoCodec.H264;
    public int EncoderPreset { get; init; } = 7;
    public EncoderTune Tune { get; init; } = EncoderTune.HighQuality;
    public RateControlMode RateControl { get; init; } = RateControlMode.ConstantQuality;
    public int Quality { get; init; } = 18;
    public int TargetBitrateMbps { get; init; } = 40;
    public int MaxBitrateMbps { get; init; } = 80;
    public MultipassMode Multipass { get; init; } = MultipassMode.FullResolution;
    public bool SpatialAq { get; init; } = true;
    public bool TemporalAq { get; init; } = true;
    public int AqStrength { get; init; } = 8;
    public VideoPixelFormat PixelFormat { get; init; } = VideoPixelFormat.Yuv420p;
    public double FrameRate { get; init; }
    public bool Deinterlace { get; init; }
    public AudioEncodingMode AudioMode { get; init; } = AudioEncodingMode.Copy;
    public int AudioBitrateKbps { get; init; } = 192;
    public int AudioSampleRate { get; init; }
    public int AudioChannels { get; init; }
    public OutputContainer Container { get; init; } = OutputContainer.Mp4;
    public bool FastStart { get; init; } = true;

    public static EncodingOptions Normalize(EncodingOptions? options)
    {
        var value = options ?? EncodingPresetCatalog.Recommended;
        return value with
        {
            Backend = Enum.IsDefined(value.Backend) ? value.Backend : EncoderBackend.NvidiaNvenc,
            Codec = Enum.IsDefined(value.Codec) ? value.Codec : VideoCodec.H264,
            EncoderPreset = Math.Clamp(value.EncoderPreset, 1, 7),
            Tune = Enum.IsDefined(value.Tune) ? value.Tune : EncoderTune.HighQuality,
            RateControl = Enum.IsDefined(value.RateControl) ? value.RateControl : RateControlMode.ConstantQuality,
            Quality = Math.Clamp(value.Quality, 0, 51),
            TargetBitrateMbps = Math.Clamp(value.TargetBitrateMbps, 1, 500),
            MaxBitrateMbps = Math.Clamp(value.MaxBitrateMbps, 1, 1000),
            Multipass = Enum.IsDefined(value.Multipass) ? value.Multipass : MultipassMode.FullResolution,
            AqStrength = Math.Clamp(value.AqStrength, 1, 15),
            PixelFormat = Enum.IsDefined(value.PixelFormat) ? value.PixelFormat : VideoPixelFormat.Yuv420p,
            FrameRate = value.FrameRate is >= 0 and <= 240 ? value.FrameRate : 0,
            AudioMode = Enum.IsDefined(value.AudioMode) ? value.AudioMode : AudioEncodingMode.Copy,
            AudioBitrateKbps = Math.Clamp(value.AudioBitrateKbps, 32, 512),
            AudioSampleRate = value.AudioSampleRate is 0 or 44100 or 48000 or 96000 ? value.AudioSampleRate : 0,
            AudioChannels = value.AudioChannels is >= 0 and <= 8 ? value.AudioChannels : 0,
            Container = Enum.IsDefined(value.Container) ? value.Container : OutputContainer.Mp4
        };
    }
}

internal static class EncodingOptionValidator
{
    public static IReadOnlyList<string> Validate(EncodingOptions options)
    {
        var errors = new List<string>();
        if (options.Backend != EncoderBackend.NvidiaNvenc) errors.Add("Only NVIDIA NVENC is available in this release.");
        if (options.EncoderPreset is < 1 or > 7) errors.Add("Encoder preset must be between P1 and P7.");
        if (options.Quality is < 0 or > 51) errors.Add("Quality must be between 0 and 51.");
        if (options.TargetBitrateMbps is < 1 or > 500) errors.Add("Target bitrate must be between 1 and 500 Mbps.");
        if (options.MaxBitrateMbps is < 1 or > 1000) errors.Add("Maximum bitrate must be between 1 and 1000 Mbps.");
        if (options.RateControl == RateControlMode.VariableBitrate && options.MaxBitrateMbps < options.TargetBitrateMbps)
            errors.Add("Maximum bitrate must be at least the target bitrate for variable bitrate encoding.");
        if (options.AqStrength is < 1 or > 15) errors.Add("AQ strength must be between 1 and 15.");
        if (options.PixelFormat == VideoPixelFormat.P010 && options.Codec != VideoCodec.Hevc)
            errors.Add("10-bit P010 output requires HEVC.");
        if (options.FrameRate is < 0 or > 240) errors.Add("Frame rate must be Source or between 1 and 240 fps.");
        if (options.AudioBitrateKbps is < 32 or > 512) errors.Add("AAC bitrate must be between 32 and 512 kbps.");
        if (options.AudioSampleRate is not (0 or 44100 or 48000 or 96000)) errors.Add("Audio sample rate is not supported.");
        if (options.AudioChannels is < 0 or > 8) errors.Add("Audio channels must be Source or between 1 and 8 channels.");
        return errors;
    }
}

internal static class EncoderBackendCatalog
{
    public static bool IsImplemented(EncoderBackend backend) => backend == EncoderBackend.NvidiaNvenc;
}

internal sealed record AudioFallbackEncoding(
    int BitrateKbps = 192,
    int SampleRate = 0,
    int Channels = 0);

internal enum EncoderCapabilityState { ImplementedAndAvailable, ImplementedButUnavailable, NotImplemented }

internal sealed record EncoderCapability(EncoderBackend Backend, EncoderCapabilityState State, string Diagnostic)
{
    public bool IsUsable => State == EncoderCapabilityState.ImplementedAndAvailable;
}
