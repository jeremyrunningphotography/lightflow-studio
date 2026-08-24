namespace LightflowStudio;

internal static class MediaWarningAnalyzer
{
    private static readonly double[] StandardFrameRates = [23.976, 24, 25, 29.97, 30, 50, 59.94, 60];
    private const double FrameRateTolerance = 0.02;

    public static bool IsStandardFrameRate(double frameRate) =>
        StandardFrameRates.Any(standard => Math.Abs(frameRate - standard) <= FrameRateTolerance);

    public static void Apply(IReadOnlyList<BatchFileOption> options)
    {
        foreach (var option in options)
        {
            if (option.MetadataError)
            {
                option.SetWarning("DETAILS UNAVAILABLE", "Media details could not be read. The file may be incomplete or unsupported.");
                continue;
            }
            if (option.Metadata is not { } metadata)
            {
                option.SetWarning("", "");
                continue;
            }

            var missingAudio = !metadata.HasAudio;
            var nonStandardFrameRate = !IsStandardFrameRate(metadata.FrameRate);
            var warnings = new List<string>();
            if (missingAudio) warnings.Add("No audio stream was found.");
            if (nonStandardFrameRate)
                warnings.Add($"{MediaMetadataPresentation.FormatFrameRate(metadata.FrameRate)} fps is not a standard output frame rate. Exporting at a standard rate may drop or duplicate frames.");

            var label = (missingAudio, nonStandardFrameRate) switch
            {
                (true, true) => "CHECK MEDIA",
                (true, false) => "NO AUDIO",
                (false, true) => "NON-STANDARD FPS",
                _ => ""
            };
            option.SetWarning(label, string.Join(" ", warnings));
        }
    }
}
