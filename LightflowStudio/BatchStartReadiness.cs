namespace LightflowStudio;

internal sealed record BatchStartPresentation(bool CanStart, string Guidance);

internal static class BatchStartReadiness
{
    public static BatchStartPresentation Evaluate(bool folderChosen, bool folderExists, int availableFiles, int selectedFiles)
    {
        if (!folderChosen) return new(false, "Choose a video folder to begin");
        if (!folderExists) return new(false, "The selected folder could not be found");
        if (availableFiles == 0) return new(false, "No supported video files found");
        if (selectedFiles == 0) return new(false, "Select at least one file to export");
        return new(true, $"{selectedFiles} file{(selectedFiles == 1 ? "" : "s")} ready to export");
    }
}
