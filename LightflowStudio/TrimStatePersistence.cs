namespace LightflowStudio;

internal static class TrimStatePersistence
{
    public static bool ApplyDialogResult(bool? dialogResult, BatchFileOption option, MediaRange? range, ITrimHistoryStore history)
    {
        if (dialogResult != true) return false;
        Apply(option, range, history);
        return true;
    }

    public static void Apply(BatchFileOption option, MediaRange? range, ITrimHistoryStore history)
    {
        option.ApplyTrim(range);
        if (range is not null) history.Save(option.FilePath, range);
        else history.Remove(option.FilePath);
    }
}
