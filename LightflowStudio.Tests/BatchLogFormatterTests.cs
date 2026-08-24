using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BatchLogFormatterTests
{
    [Fact]
    public void Started_IncludesPlanOutputAndInitialEstimate()
    {
        var started = new DateTime(2026, 7, 22, 10, 0, 0);

        var text = BatchLogFormatter.Started(3, @"D:\Output", OutputResolution.FullHd,
            RecoveryStrategy.Normal, TimeSpan.FromMinutes(15), started);

        Assert.Contains("Export started — 3 files | 1080p | Normal", text);
        Assert.Contains(@"3 MP4 files in D:\Output", text);
        Assert.Contains("15:00", text);
        Assert.Contains("10:15", text);
    }

    [Fact]
    public void Started_ExplainsWhenEstimateIsUnavailable()
    {
        var text = BatchLogFormatter.Started(1, "Output", OutputResolution.Source,
            RecoveryStrategy.VideoOnly, TimeSpan.Zero, DateTime.Now);

        Assert.Contains("1 file | Source | Video only", text);
        Assert.Contains("unavailable", text);
    }

    [Fact]
    public void Finished_IncludesOutcomeCountsElapsedTimeAndOutput()
    {
        var text = BatchLogFormatter.Finished("completed", 5, 3, 1, 1,
            TimeSpan.FromMinutes(2), @"D:\Output");

        Assert.Equal(@"Export completed — 3 exported, 1 failed, 1 skipped, 5 of 5 processed in 2:00. Output: D:\Output", text);
    }
}
