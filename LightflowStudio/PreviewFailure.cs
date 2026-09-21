namespace LightflowStudio;

// Persist only this allowlisted classification in Previews; engine diagnostics stay in Activity Log.
internal enum PreviewFailureReason { Unknown, NoVideoFrame, SourceUnreadable, CodecUnavailable, TimedOut }

internal static class PreviewFailure
{
    public static string Message(PreviewFailureReason reason) => reason switch
    {
        PreviewFailureReason.NoVideoFrame => "No decodable video frame was found.",
        PreviewFailureReason.SourceUnreadable => "The source file could not be read.",
        PreviewFailureReason.CodecUnavailable => "The video codec could not be decoded.",
        PreviewFailureReason.TimedOut => "Preview generation timed out.",
        _ => "Preview could not be generated."
    };

    // Only the decoder boundary calls this, never Browser presentation.
    public static PreviewFailureReason ClassifyDecoder(string diagnostic)
    {
        if (diagnostic.Contains("Output file is empty", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase))
            return PreviewFailureReason.NoVideoFrame;
        if (diagnostic.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase))
            return PreviewFailureReason.SourceUnreadable;
        if (diagnostic.Contains("Decoder not found", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Contains("Unknown decoder", StringComparison.OrdinalIgnoreCase))
            return PreviewFailureReason.CodecUnavailable;
        return PreviewFailureReason.Unknown;
    }
}
