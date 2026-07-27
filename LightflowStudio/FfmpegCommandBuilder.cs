namespace LightflowStudio;

internal static class FfmpegCommandBuilder
{
    public static List<string> Encode(string input, string output, string? lut, RecoveryStrategy recovery,
        OutputResolution resolution, bool detailedOutput = false, EncodingOptions? encoding = null)
    {
        if (!Enum.IsDefined(recovery)) throw new ArgumentOutOfRangeException(nameof(recovery));
        if (!Enum.IsDefined(resolution)) throw new ArgumentOutOfRangeException(nameof(resolution));
        var options = EncodingOptions.Normalize(encoding);
        var errors = EncodingOptionValidator.Validate(options);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(encoding));

        var args = new List<string> { "-hide_banner", "-loglevel", detailedOutput ? "verbose" : "info", "-y" };
        if (recovery != RecoveryStrategy.Normal)
            args.AddRange(["-fflags", "+discardcorrupt+genpts", "-err_detect", "ignore_err"]);
        args.AddRange(["-i", input, "-map", "0:v:0"]);
        if (recovery != RecoveryStrategy.VideoOnly && options.AudioMode != AudioEncodingMode.None)
            args.AddRange(["-map", recovery == RecoveryStrategy.Salvage ? "0:a:0?" : "0:a?"]);

        var filters = new List<string>();
        if (!string.IsNullOrEmpty(lut)) filters.Add($"lut3d=file='{EscapeFilterPath(lut)}'");
        if (options.Deinterlace) filters.Add("bwdif");
        if (options.FrameRate > 0) filters.Add($"fps={options.FrameRate.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        filters.AddRange(resolution switch
        {
            OutputResolution.Sd480 => ["scale=-2:480"],
            OutputResolution.Hd720 => ["scale=-2:720"],
            OutputResolution.FullHd => ["scale=-2:1080"],
            OutputResolution.Qhd1440 => ["scale=-2:1440"],
            OutputResolution.UltraHd => ["scale=3840:2160:force_original_aspect_ratio=decrease", "pad=3840:2160:(ow-iw)/2:(oh-ih)/2"],
            OutputResolution.Source => [],
            _ => throw new ArgumentOutOfRangeException(nameof(resolution))
        });
        if (filters.Count > 0) args.AddRange(["-vf", string.Join(',', filters)]);

        args.AddRange(["-c:v", options.Codec == VideoCodec.H264 ? "h264_nvenc" : "hevc_nvenc"]);
        args.AddRange(["-preset", $"p{options.EncoderPreset}", "-tune", TuneName(options.Tune)]);
        AddRateControl(args, options);
        if (options.Multipass != MultipassMode.Disabled)
            args.AddRange(["-multipass", options.Multipass == MultipassMode.FullResolution ? "fullres" : "qres"]);
        args.AddRange(["-spatial-aq", options.SpatialAq ? "1" : "0", "-temporal-aq", options.TemporalAq ? "1" : "0"]);
        if (options.SpatialAq || options.TemporalAq) args.AddRange(["-aq-strength", options.AqStrength.ToString()]);
        args.AddRange(["-pix_fmt", options.PixelFormat == VideoPixelFormat.P010 ? "p010le" : "yuv420p"]);

        AddAudio(args, recovery, options);
        if (options.FastStart && options.Container is OutputContainer.Mp4 or OutputContainer.Mov)
            args.AddRange(["-movflags", "+faststart"]);
        args.AddRange(["-progress", "pipe:1", "-nostats", output]);
        return args;
    }

    private static void AddRateControl(List<string> args, EncodingOptions options)
    {
        var target = $"{options.TargetBitrateMbps}M";
        var maximum = $"{options.MaxBitrateMbps}M";
        switch (options.RateControl)
        {
            case RateControlMode.ConstantQuality:
                args.AddRange(["-rc", "vbr", "-cq", options.Quality.ToString(), "-b:v", "0"]);
                break;
            case RateControlMode.VariableBitrate:
                args.AddRange(["-rc", "vbr", "-b:v", target, "-maxrate", maximum, "-bufsize", $"{options.MaxBitrateMbps * 2}M"]);
                break;
            case RateControlMode.ConstantBitrate:
                args.AddRange(["-rc", "cbr", "-b:v", target, "-maxrate", target, "-bufsize", $"{options.TargetBitrateMbps * 2}M"]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.RateControl));
        }
    }

    private static void AddAudio(List<string> args, RecoveryStrategy recovery, EncodingOptions options)
    {
        if (recovery == RecoveryStrategy.VideoOnly || options.AudioMode == AudioEncodingMode.None)
        {
            args.Add("-an");
            return;
        }
        if (recovery == RecoveryStrategy.Normal && options.AudioMode == AudioEncodingMode.Copy)
        {
            args.AddRange(["-c:a", "copy"]);
            return;
        }

        args.AddRange(["-c:a", "aac", "-b:a", $"{options.AudioBitrateKbps}k"]);
        if (options.AudioSampleRate > 0) args.AddRange(["-ar", options.AudioSampleRate.ToString()]);
        if (options.AudioChannels > 0) args.AddRange(["-ac", options.AudioChannels.ToString()]);
        if (recovery == RecoveryStrategy.Salvage) args.AddRange(["-af", "aresample=async=1:first_pts=0"]);
    }

    private static string TuneName(EncoderTune tune) => tune switch
    {
        EncoderTune.HighQuality => "hq",
        EncoderTune.LowLatency => "ll",
        EncoderTune.UltraLowLatency => "ull",
        _ => throw new ArgumentOutOfRangeException(nameof(tune))
    };

    public static List<string> ProbeDuration(string file) => ["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", file];
    public static List<string> ProbeMetadata(string file) =>
        ["-v", "error", "-show_entries", "format=duration:stream=codec_type,codec_name,width,height,avg_frame_rate", "-of", "json", file];
    public static List<string> Inspect(string file) => ["-hide_banner", "-show_format", "-show_streams", file];
    public static List<string> Verify(string file) => ["-v", "warning", "-i", file, "-map", "0:v:0", "-f", "null", "NUL"];
    public static List<string> Rewrap(string input, string output) => ["-hide_banner", "-y", "-i", input, "-map", "0", "-c", "copy", "-movflags", "+faststart", output];
    public static List<string> Proxy(string input, string output) => ["-hide_banner", "-y", "-i", input, "-vf", "scale=-2:1080", "-c:v", "h264_nvenc", "-preset", "p4", "-cq", "24", "-b:v", "0", "-c:a", "aac", "-b:a", "128k", output];
    public static List<string> ContactSheet(string input, string output) => ["-hide_banner", "-y", "-i", input, "-vf", "fps=1/10,scale=480:-1,tile=4x4:padding=8:margin=8", "-frames:v", "1", output];

    internal static string EscapeFilterPath(string path) => path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
}