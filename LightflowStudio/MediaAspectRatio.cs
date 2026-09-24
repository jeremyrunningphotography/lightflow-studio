using System.Globalization;
using System.Text.Json;

namespace LightflowStudio;

/// <summary>Exact rational identity, independent of formatted floating point values.</summary>
internal readonly record struct MediaAspectRatio
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public int Numerator { get; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public int Denominator { get; }
    [System.Text.Json.Serialization.JsonConstructor]
    public MediaAspectRatio(int numerator, int denominator)
    {
        if (numerator <= 0 || denominator <= 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        var a = numerator;
        var b = denominator;
        while (b != 0) (a, b) = (b, a % b);
        Numerator = numerator / a;
        Denominator = denominator / a;
    }
    public MediaAspectRatio Rotate(VideoRotation rotation)
    {
        var size = rotation.Dimensions(Numerator, Denominator);
        return new(size.Width, size.Height);
    }
}

internal static class MediaDisplayGeometry
{
    // FFprobe exposes libav's source display-matrix rotation. Dimension projection uses the same
    // quarter-turn primitive as playback (#287); direction/reflection do not change the aspect ratio.
    // Non-square pixels are deliberately unknown until Lightflow's playback/export display-aspect
    // contract is resolved. Do not mistake their encoded raster ratio for presentation geometry.
    public static MediaAspectRatio? FromProbe(JsonElement stream, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        var raster = new MediaAspectRatio(width, height);
        if (stream.TryGetProperty("sample_aspect_ratio", out var sar) &&
            sar.ToString() is { } sample && sample is not ("N/A" or "0:1") &&
            (!TryParse(sample, out var pixels) || pixels != new MediaAspectRatio(1, 1))) return null;
        if (stream.TryGetProperty("display_aspect_ratio", out var dar) &&
            dar.ToString() is { } display && display is not ("N/A" or "0:1") &&
            (!TryParse(display, out var ratio) || ratio != raster)) return null;

        double degrees = 0;
        var foundMatrix = false;
        if (stream.TryGetProperty("side_data_list", out var sideData) && sideData.ValueKind == JsonValueKind.Array)
            foreach (var side in sideData.EnumerateArray())
            {
                if (side.ValueKind != JsonValueKind.Object) return null;
                if (side.TryGetProperty("rotation", out var rotation))
                {
                    if (foundMatrix || !double.TryParse(rotation.ToString(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out degrees)) return null;
                    foundMatrix = true;
                }
            }
        if (!foundMatrix && stream.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object && tags.TryGetProperty("rotate", out var tag) &&
            !double.TryParse(tag.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out degrees)) return null;
        if (!double.IsFinite(degrees) || degrees % 90 != 0 || degrees < int.MinValue || degrees > int.MaxValue) return null;
        return raster.Rotate(new VideoRotation((int)degrees));
    }

    private static bool TryParse(string text, out MediaAspectRatio ratio)
    {
        ratio = default;
        var parts = text.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var n) || !int.TryParse(parts[1], out var d) || n <= 0 || d <= 0) return false;
        ratio = new(n, d);
        return true;
    }
}
