namespace LightflowStudio;

internal static class BatchLogFormatter
{
    public static string Started(
        int fileCount,
        string outputRoot,
        OutputResolution resolution,
        RecoveryStrategy recovery,
        TimeSpan sourceDuration,
        DateTime startedAt)
    {
        var estimate = sourceDuration > TimeSpan.Zero
            ? $"{FormatDuration(sourceDuration)} (approximately {startedAt.Add(sourceDuration):t}, based on source duration)"
            : "unavailable until export progress is measured";
        return $"Export started — {fileCount} file{Plural(fileCount)} | {EncodingPathPlanner.ResolutionName(resolution)} | {RecoveryName(recovery)}" +
               Environment.NewLine +
               $"Outputs — {fileCount} MP4 file{Plural(fileCount)} in {outputRoot}" +
               Environment.NewLine +
               $"Initial estimated completion — {estimate}";
    }

    public static string Finished(
        string outcome,
        int total,
        int encoded,
        int failed,
        int skipped,
        TimeSpan elapsed,
        string outputRoot) =>
        $"Export {outcome} — {encoded} exported, {failed} failed, {skipped} skipped, {encoded + failed + skipped} of {total} processed in {FormatDuration(elapsed)}. Output: {outputRoot}";

    internal static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");

    private static string Plural(int count) => count == 1 ? "" : "s";

    private static string RecoveryName(RecoveryStrategy recovery) => recovery switch
    {
        RecoveryStrategy.Normal => "Normal",
        RecoveryStrategy.Salvage => "Salvage",
        RecoveryStrategy.VideoOnly => "Video only",
        _ => throw new ArgumentOutOfRangeException(nameof(recovery))
    };
}
