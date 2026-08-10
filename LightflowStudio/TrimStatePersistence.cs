namespace LightflowStudio;

internal static class TrimStatePersistence
{
    public static void Apply(BatchFileOption option, MediaRange? range, ITrimHistoryStore history)
    {
        option.ApplyTrim(range);
        if (range is not null) history.Save(option.FilePath, range);
        else history.Remove(option.FilePath);
    }
}
