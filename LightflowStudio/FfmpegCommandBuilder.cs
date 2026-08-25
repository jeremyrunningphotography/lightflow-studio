namespace LightflowStudio;

internal static class FfmpegCommandBuilder
{
    public static List<string> Encode(string input, string output, string? lut, RecoveryStrategy recovery,
        OutputResolution resolution, bool detailedOutput = false, EncodingOptions? encoding = null,
        ResolvedMediaRange? trim = null, IReadOnlyList<string>? assignedLuts = null)
    {
        if (!Enum.IsDefined(recovery)) throw new ArgumentOutOfRangeException(nameof(recovery));
        if (!Enum.IsDefined(resolution)) throw new ArgumentOutOfRangeException(nameof(resolution));
        var options = EncodingOptions.Normalize(encoding);
        var errors = EncodingOptionValidator.Validate(options);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(encoding));

        var args = new List<string> { "-hide_banner", "-loglevel", detailedOutput ? "verbose" : "info", "-y" };
        if (recovery != RecoveryStrategy.Normal)
            args.AddRange(["-fflags", "+discardcorrupt+genpts", "-err_detect", "ignore_err"]);
        if (trim is not null)
        {
            if (trim.RequestedRange.EffectiveIn > TimeSpan.Zero)
                args.AddRange(["-ss", Seconds(trim.RequestedRange.EffectiveIn)]);
            args.Add("-copyts");
        }
        args.AddRange(["-i", input, "-map", "0:v:0"]);
        if (recovery != RecoveryStrategy.VideoOnly && options.AudioMode != AudioEncodingMode.None)
            args.AddRange(["-map", recovery == RecoveryStrategy.Salvage ? "0:a:0?" : "0:a?"]);

        var filters = new List<string>();
        if (trim is not null)
            filters.Add($"trim=start={Seconds(trim.AbsoluteIn)}:end={Seconds(trim.ExclusiveOut)},setpts=PTS-STARTPTS");
        if (!string.IsNullOrEmpty(lut) && assignedLuts?.Count > 0)
            throw new ArgumentException("A manual LUT cannot be combined with assigned Color.", nameof(lut));
        if (!string.IsNullOrEmpty(lut)) filters.Add($"lut3d=file='{EscapeFilterPath(lut)}'");
        if (assignedLuts is not null)
            filters.AddRange(assignedLuts.Select(path => $"lut3d=file='{EscapeFilterPath(path)}'"));
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

        AddAudio(args, recovery, options, trim);
        if (options.FastStart && options.Container is OutputContainer.Mp4 or OutputContainer.Mov)
            args.AddRange(["-movflags", "+faststart"]);
        args.AddRange(["-progress", "pipe:1", "-nostats", "-f", OutputMuxer(options.Container), output]);
        return args;
    }

    internal static string OutputMuxer(OutputContainer container) => container switch
    {
        OutputContainer.Mp4 => "mp4",
        OutputContainer.Mov => "mov",
        OutputContainer.Mkv => "matroska",
        _ => throw new ArgumentOutOfRangeException(nameof(container))
    };

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

    private static void AddAudio(List<string> args, RecoveryStrategy recovery, EncodingOptions options, ResolvedMediaRange? trim)
    {
        if (recovery == RecoveryStrategy.VideoOnly || options.AudioMode == AudioEncodingMode.None)
        {
            args.Add("-an");
            return;
        }
        if (recovery == RecoveryStrategy.Normal && options.AudioMode == AudioEncodingMode.Copy && trim is null)
        {
            args.AddRange(["-c:a", "copy"]);
            return;
        }

        args.AddRange(["-c:a", "aac", "-b:a", $"{options.AudioBitrateKbps}k"]);
        if (options.AudioSampleRate > 0) args.AddRange(["-ar", options.AudioSampleRate.ToString()]);
        if (options.AudioChannels > 0) args.AddRange(["-ac", options.AudioChannels.ToString()]);
        var audioFilters = new List<string>();
        if (trim is not null)
            audioFilters.Add($"atrim=start={Seconds(trim.AbsoluteIn)}:end={Seconds(trim.ExclusiveOut)},asetpts=PTS-STARTPTS");
        if (recovery == RecoveryStrategy.Salvage) audioFilters.Add("aresample=async=1:first_pts=0");
        if (audioFilters.Count > 0) args.AddRange(["-af", string.Join(',', audioFilters)]);
    }

    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture);

    private static string TuneName(EncoderTune tune) => tune switch
    {
        EncoderTune.HighQuality => "hq",
        EncoderTune.LowLatency => "ll",
        EncoderTune.UltraLowLatency => "ull",
        _ => throw new ArgumentOutOfRangeException(nameof(tune))
    };

    public static List<string> ProbeDuration(string file) => ["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", file];
    public static List<string> ProbeMetadata(string file) =>
        ["-v", "error", "-show_entries", "format=format_name,duration,start_time:stream=codec_type,codec_name,width,height,avg_frame_rate,start_time,duration,sample_rate,channels,channel_layout", "-of", "json", file];
    public static List<string> ProbeDerivedMetadata(string file) =>
        ["-v", "error", "-show_entries",
            "format=format_name,format_long_name,duration,start_time,size,bit_rate,tags:stream=index,codec_type,codec_name,codec_long_name,profile,width,height,pix_fmt,bits_per_raw_sample,color_space,color_transfer,color_primaries,avg_frame_rate,r_frame_rate,duration,start_time,sample_rate,channels,channel_layout,bit_rate,tags",
            "-of", "json", file];
    public static List<string> ProbeVideoFrames(string file) =>
        ["-v", "error", "-select_streams", "v:0", "-show_entries", "frame=best_effort_timestamp_time", "-of", "json", file];
    public static List<string> ProbeVideoFrames(string file, MediaRange range, TimeSpan sourceStartTimestamp = default) =>
        ["-v", "error", "-select_streams", "v:0", "-read_intervals", EncodingFrameProbeWindow.For(range, sourceStartTimestamp),
            "-show_entries", "frame=best_effort_timestamp_time", "-of", "json", file];
    public static List<string> ProbeVideoPackets(string file, MediaRange range, TimeSpan sourceStartTimestamp = default) =>
        ["-v", "error", "-select_streams", "v:0", "-read_intervals", EncodingFrameProbeWindow.For(range, sourceStartTimestamp),
            "-show_packets", "-show_entries", "packet=pts_time", "-of", "json", file];
    public static List<string> ProbeOutput(string file) =>
        ["-v", "error", "-show_entries", "format=duration:stream=codec_type,codec_name", "-of", "json", file];
    public static List<string> ExtractThumbnail(string file, TimeSpan position, int maximumPixelDimension, string output,
        IReadOnlyList<string>? assignedLuts = null)
    {
        var filters = assignedLuts?.Select(path => $"lut3d=file='{EscapeFilterPath(path)}'").ToList() ?? [];
        filters.Add($"scale={maximumPixelDimension}:{maximumPixelDimension}:force_original_aspect_ratio=decrease");
        return ["-hide_banner", "-loglevel", "error", "-y", "-ss", Seconds(position), "-i", file, "-an", "-frames:v", "1",
            "-vf", string.Join(',', filters), "-c:v", "mjpeg", "-q:v", "3", "-f", "image2", output];
    }
    public static List<string> Inspect(string file) => ["-hide_banner", "-show_format", "-show_streams", file];
    public static List<string> Verify(string file) => ["-v", "warning", "-i", file, "-map", "0:v:0", "-f", "null", "NUL"];
    public static List<string> Rewrap(string input, string output) => ["-hide_banner", "-y", "-i", input, "-map", "0", "-c", "copy", "-movflags", "+faststart", output];
    public static List<string> Proxy(string input, string output) => ["-hide_banner", "-y", "-i", input, "-vf", "scale=-2:1080", "-c:v", "h264_nvenc", "-preset", "p4", "-cq", "24", "-b:v", "0", "-c:a", "aac", "-b:a", "128k", output];
    public static List<string> ContactSheet(string input, string output) => ["-hide_banner", "-y", "-i", input, "-vf", "fps=1/10,scale=480:-1,tile=4x4:padding=8:margin=8", "-frames:v", "1", output];

    internal static string EscapeFilterPath(string path) => path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
}
