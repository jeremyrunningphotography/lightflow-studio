namespace LightflowStudio;

internal enum BatchStatus
{
    Ready,
    Encoding,
    Paused
}

internal sealed record BatchStatusPresentation(string Text, string ForegroundResource, string BackgroundResource, string BorderResource)
{
    public static BatchStatusPresentation For(BatchStatus status) => status switch
    {
        BatchStatus.Ready => new("READY", "SuccessBrush", "ReadyBadgeBackgroundBrush", "ReadyBadgeBorderBrush"),
        BatchStatus.Encoding => new("EXPORTING", "OrangeBrush", "EncodingBadgeBackgroundBrush", "EncodingBadgeBorderBrush"),
        BatchStatus.Paused => new("PAUSED", "WarningBrush", "PausedBadgeBackgroundBrush", "PausedBadgeBorderBrush"),
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
