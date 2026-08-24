using System.Globalization;
using System.Text.Json;

namespace LightflowStudio;

internal static class EncodedOutputValidator
{
    public static bool TryValidate(string probeJson, TimeSpan expectedDuration, bool expectsAudio, out string error)
    {
        error = "";
        try
        {
            using var document = JsonDocument.Parse(probeJson);
            var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToList();
            if (!streams.Any(IsVideo)) { error = "The exported file has no readable video stream."; return false; }
            if (expectsAudio && !streams.Any(IsAudio)) { error = "The exported file has no readable audio stream."; return false; }
            var durationText = document.RootElement.GetProperty("format").GetProperty("duration").GetString();
            if (!double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
            { error = "The exported file has no valid duration."; return false; }
            var tolerance = Math.Max(0.15, expectedDuration.TotalSeconds * .02);
            if (expectedDuration > TimeSpan.Zero && Math.Abs(seconds - expectedDuration.TotalSeconds) > tolerance)
            { error = $"Exported duration {seconds:0.###}s differs from expected {expectedDuration.TotalSeconds:0.###}s."; return false; }
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            error = $"FFprobe could not validate the exported file: {exception.Message}";
            return false;
        }
    }

    private static bool IsVideo(JsonElement stream) => Type(stream) == "video";
    private static bool IsAudio(JsonElement stream) => Type(stream) == "audio";
    private static string? Type(JsonElement stream) => stream.TryGetProperty("codec_type", out var type) ? type.GetString() : null;
}
