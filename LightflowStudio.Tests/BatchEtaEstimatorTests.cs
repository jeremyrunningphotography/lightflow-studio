using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BatchEtaEstimatorTests
{
    [Fact]
    public void Estimate_UsesProgressWithinTheCurrentFile()
    {
        var estimate = BatchEtaEstimator.Estimate(TimeSpan.FromMinutes(1), 0, 4, 50);

        Assert.Equal(TimeSpan.FromMinutes(7), estimate);
    }

    [Fact]
    public void Estimate_UsesCompletedAndCurrentWorkTogether()
    {
        var estimate = BatchEtaEstimator.Estimate(TimeSpan.FromMinutes(3), 1, 4, 50);

        Assert.Equal(TimeSpan.FromMinutes(5), estimate);
    }

    [Fact]
    public void Estimate_ReturnsNullUntilProgressIsAvailable()
    {
        Assert.Null(BatchEtaEstimator.Estimate(TimeSpan.FromSeconds(10), 0, 3, 0));
    }

    [Fact]
    public void Estimate_ClampsReportedProgress()
    {
        Assert.Equal(TimeSpan.Zero, BatchEtaEstimator.Estimate(TimeSpan.FromMinutes(2), 2, 2, 500));
    }

    [Fact]
    public void Estimate_SupportsDurationWeightedWork()
    {
        var estimate = BatchEtaEstimator.Estimate(TimeSpan.FromMinutes(1), completedWork: 45, totalWork: 180);

        Assert.Equal(TimeSpan.FromMinutes(3), estimate);
    }
}
