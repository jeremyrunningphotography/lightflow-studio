namespace LightflowStudio;

internal sealed class BatchProgressState
{
    public double BatchPercent { get; private set; }
    public double FilePercent { get; private set; }
    public string StatusText { get; private set; } = "";

    public void StartBatch(int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(total);
        BatchPercent = 0;
        FilePercent = 0;
        StatusText = $"Completed 0 of {total} — estimated remaining: calculating…";
    }

    public void StartFile() => FilePercent = 0;

    public void ReportFileProgress(double percent) => FilePercent = Math.Clamp(percent, 0, 100);

    public void ReportBatchProgress(double completed, int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(total);
        BatchPercent = Math.Clamp(completed * 100d / total, 0, 100);
    }

    public void ReportBatchPercent(double percent) => BatchPercent = Math.Clamp(percent, 0, 100);
}
