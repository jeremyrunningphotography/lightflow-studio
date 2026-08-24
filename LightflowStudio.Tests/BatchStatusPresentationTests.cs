using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BatchStatusPresentationTests
{
    [Theory]
    [InlineData(BatchStatus.Ready, "READY", "SuccessBrush")]
    [InlineData(BatchStatus.Encoding, "EXPORTING", "OrangeBrush")]
    [InlineData(BatchStatus.Paused, "PAUSED", "WarningBrush")]
    internal void For_ReturnsDistinctStatusPresentation(BatchStatus status, string text, string foreground)
    {
        var presentation = BatchStatusPresentation.For(status);

        Assert.Equal(text, presentation.Text);
        Assert.Equal(foreground, presentation.ForegroundResource);
        Assert.EndsWith("BackgroundBrush", presentation.BackgroundResource);
        Assert.EndsWith("BorderBrush", presentation.BorderResource);
    }

    [Fact]
    public void For_RejectsUnknownStatus()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BatchStatusPresentation.For((BatchStatus)99));
    }
}
