namespace LightflowStudio;

internal sealed record ExportChoice<T>(T Value, string Label);
internal sealed record ExportNamePartChip(int Index, NamePart Part, string Label)
{
    public string AutomationName => $"{Label} name part, position {Index + 1}";
    public string RemoveAutomationName => $"Remove {Label} name part";
    public string MoveEarlierAutomationName => $"Move {Label} name part earlier";
    public string MoveLaterAutomationName => $"Move {Label} name part later";
    public bool IsCustomText => Part.Kind == NamePartKind.CustomText;
}

internal sealed record ExportHardwareStatus(string Heading, string Detail, bool Available, string Diagnostic);

internal static class ExportPresentation
{
    public static IReadOnlyList<ExportChoice<NamePartKind>> NameParts { get; } =
    [
        Choice(NamePartKind.OriginalName, "Original name"), Choice(NamePartKind.CustomText, "Custom text"),
        Choice(NamePartKind.Date, "Date"), Choice(NamePartKind.Time, "Time"),
        Choice(NamePartKind.Sequence1, "Sequence 1"), Choice(NamePartKind.Sequence01, "Sequence 01"),
        Choice(NamePartKind.Sequence001, "Sequence 001"), Choice(NamePartKind.Sequence0001, "Sequence 0001"),
        Choice(NamePartKind.Sequence00001, "Sequence 00001"), Choice(NamePartKind.IndexNumber, "Index Number")
    ];
    public static IReadOnlyList<ExportChoice<NamePartSeparator>> Separators { get; } =
    [Choice(NamePartSeparator.Underscore, "Underscore ( _ )"), Choice(NamePartSeparator.Hyphen, "Hyphen ( - )"), Choice(NamePartSeparator.Space, "Space"), Choice(NamePartSeparator.None, "None")];
    public static IReadOnlyList<ExportChoice<ExportContainerChoice>> Containers { get; } =
    [Choice(ExportContainerChoice.SameAsSource, "Same as Source"), Choice(ExportContainerChoice.Mp4, "MP4"), Choice(ExportContainerChoice.Mov, "MOV"), Choice(ExportContainerChoice.Mkv, "MKV")];
    public static IReadOnlyList<ExportChoice<ExportCodecChoice>> Codecs { get; } =
    [Choice(ExportCodecChoice.SameAsSource, "Same as Source"), Choice(ExportCodecChoice.H264, "H.264"), Choice(ExportCodecChoice.Hevc, "HEVC (H.265)")];
    public static IReadOnlyList<ExportChoice<RateControlMode>> RateControls { get; } =
    [Choice(RateControlMode.ConstantQuality, "Constant Quality"), Choice(RateControlMode.VariableBitrate, "Variable Bitrate"), Choice(RateControlMode.ConstantBitrate, "Constant Bitrate")];
    public static IReadOnlyList<ExportChoice<OutputResolution>> Resolutions { get; } =
    [Choice(OutputResolution.Source, "Same as Source"), Choice(OutputResolution.UltraHd, "4K UHD (3840 × 2160)"), Choice(OutputResolution.Qhd1440, "1440p (2560 × 1440)"), Choice(OutputResolution.FullHd, "1080p (1920 × 1080)"), Choice(OutputResolution.Hd720, "720p (1280 × 720)"), Choice(OutputResolution.Sd480, "480p (854 × 480)")];
    public static IReadOnlyList<ExportChoice<EncoderTune>> Tunes { get; } =
    [Choice(EncoderTune.HighQuality, "High Quality"), Choice(EncoderTune.LowLatency, "Low Latency"), Choice(EncoderTune.UltraLowLatency, "Ultra-low Latency")];
    public static IReadOnlyList<ExportChoice<MultipassMode>> MultipassModes { get; } =
    [Choice(MultipassMode.Disabled, "Disabled"), Choice(MultipassMode.QuarterResolution, "Quarter Resolution"), Choice(MultipassMode.FullResolution, "Full Resolution")];
    public static IReadOnlyList<ExportChoice<VideoPixelFormat>> PixelFormats { get; } =
    [Choice(VideoPixelFormat.Yuv420p, "YUV 4:2:0 (8-bit)"), Choice(VideoPixelFormat.P010, "P010 (10-bit)")];
    public static IReadOnlyList<ExportChoice<EncoderBackend>> Encoders { get; } = [Choice(EncoderBackend.NvidiaNvenc, "NVIDIA NVENC")];

    public static string NamePartLabel(NamePartKind kind) => NameParts.Single(x => x.Value == kind).Label;
    public static IReadOnlyList<ExportNamePartChip> Composer(IReadOnlyList<NamePart> parts) => parts.Select((part, index) => new ExportNamePartChip(index, part, NamePartLabel(part.Kind))).ToArray();
    public static ExportHardwareStatus Hardware(EncoderCapability capability) => capability.IsUsable
        ? new("✓ Hardware acceleration available", "NVIDIA NVENC", true, capability.Diagnostic)
        : new("Hardware acceleration unavailable", "NVIDIA NVENC could not be initialized.", false, capability.Diagnostic);
    private static ExportChoice<T> Choice<T>(T value, string label) => new(value, label);
}
