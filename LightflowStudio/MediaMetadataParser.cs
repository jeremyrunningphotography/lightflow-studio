using System.Globalization;
using System.Text.Json;

namespace LightflowStudio;

internal static class MediaMetadataParser
{
    public static bool TryParse(string json, long fileSizeBytes, out MediaMetadata metadata)
    {
        metadata = new MediaMetadata(0, 0, 0, 0, fileSizeBytes, "", false);
        try
        {
            using var document = JsonDocument.Parse(json);
            var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToList();
            var video = streams.FirstOrDefault(stream =>
                stream.TryGetProperty("codec_type", out var type) && type.GetString() == "video");
            if (video.ValueKind == JsonValueKind.Undefined) return false;

            var width = ReadInt(video, "width");
            var height = ReadInt(video, "height");
            var frameRate = ReadFrameRate(ReadString(video, "avg_frame_rate"));
            var codec = ReadString(video, "codec_name");
            var hasAudio = streams.Any(stream =>
                stream.TryGetProperty("codec_type", out var type) && type.GetString() == "audio");
            var audio = streams.FirstOrDefault(stream =>
                stream.TryGetProperty("codec_type", out var type) && type.GetString() == "audio");
            var formatDuration = document.RootElement.TryGetProperty("format", out var format)
                ? ReadDouble(format, "duration")
                : 0;
            var startTimestamp = ReadDouble(video, "start_time");
            if (startTimestamp == 0 && format.ValueKind != JsonValueKind.Undefined)
                startTimestamp = ReadDouble(format, "start_time");
            var streamDuration = ReadDouble(video, "duration");
            var duration = streamDuration > 0 ? streamDuration
                : startTimestamp > 0 && formatDuration > startTimestamp ? formatDuration - startTimestamp
                : formatDuration;
            metadata = new MediaMetadata(width, height, frameRate, duration, fileSizeBytes, codec, hasAudio,
                TimeSpan.FromSeconds(Math.Max(0, startTimestamp)), ReadString(format, "format_name"),
                hasAudio ? ReadString(audio, "codec_name") : null,
                hasAudio ? ReadNullableInt(audio, "sample_rate") : null,
                hasAudio ? ReadNullableInt(audio, "channels") : null,
                hasAudio ? ReadString(audio, "channel_layout") : null);
            return width > 0 && height > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static double ReadFrameRate(string value)
    {
        var parts = value.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            && denominator != 0)
            return numerator / denominator;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) ? rate : 0;
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString()
            : "";

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static double ReadDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;

    private static int? ReadNullableInt(JsonElement element, string name) =>
        int.TryParse(ReadString(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
}
