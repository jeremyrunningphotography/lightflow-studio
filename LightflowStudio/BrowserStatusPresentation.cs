namespace LightflowStudio;

/// <summary>
/// Pure formatting for the Browser media-area status line: visible/total item counts, selection count and
/// total size, and current Preview-generation activity. Kept separate from Dispatcher/UI glue for
/// testability, matching existing presentation-formatter conventions in this codebase.
/// </summary>
internal static class BrowserStatusPresentation
{
    public static string Describe(int visibleCount, int totalCount, int selectedCount, long selectedTotalSizeBytes,
        bool isGeneratingPreviews, int generatingRemaining)
    {
        var parts = new List<string> { DescribeScope(visibleCount, totalCount) };

        if (selectedCount > 0)
            parts.Add(selectedTotalSizeBytes > 0
                ? $"{selectedCount} selected ({MediaMetadataPresentation.FormatSize(selectedTotalSizeBytes)})"
                : $"{selectedCount} selected");

        if (isGeneratingPreviews)
            parts.Add(generatingRemaining > 0 ? $"Generating previews… ({generatingRemaining} left)" : "Generating previews…");

        return string.Join(" · ", parts);
    }

    private static string DescribeScope(int visibleCount, int totalCount)
    {
        if (totalCount == 0) return "No media in this folder";
        if (visibleCount == totalCount) return Pluralize(totalCount, "item");
        return $"{visibleCount} of {Pluralize(totalCount, "item")}";
    }

    private static string Pluralize(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
