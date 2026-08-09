namespace LightflowStudio;

internal static class BatchEtaEstimator
{
    public static TimeSpan? Estimate(TimeSpan elapsed, int completedFiles, int totalFiles, double currentFilePercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalFiles);
        var completedWork = Math.Clamp(completedFiles + Math.Clamp(currentFilePercent, 0, 100) / 100d, 0, totalFiles);
        return Estimate(elapsed, completedWork, totalFiles);
    }

    public static TimeSpan? Estimate(TimeSpan elapsed, double completedWork, double totalWork)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedWork);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalWork);
        completedWork = Math.Clamp(completedWork, 0, totalWork);
        if (completedWork <= 0 || elapsed <= TimeSpan.Zero) return null;
        var remainingWork = totalWork - completedWork;
        return TimeSpan.FromTicks((long)(elapsed.Ticks * remainingWork / completedWork));
    }
}
