namespace LightflowStudio;

internal sealed record PixelFormatFacts(int? ComponentDepth, string? ChromaSubsampling);

internal static partial class FfprobePixelFormats
{
    // Exact descriptor names only. Unknown/custom formats do not inherit facts from similar names.
    public static PixelFormatFacts? Find(string? name) => name is null ? null : Facts.GetValueOrDefault(name);
    public static int? ComponentDepth(string? name, int? explicitDepth) =>
        explicitDepth is > 0 and <= 64 ? explicitDepth : Find(name)?.ComponentDepth;
}
