using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BatchStartReadinessTests
{
    [Theory]
    [InlineData(false, false, 0, 0, "Choose a video folder to begin")]
    [InlineData(true, false, 0, 0, "The selected folder could not be found")]
    [InlineData(true, true, 0, 0, "No supported video files found")]
    [InlineData(true, true, 3, 0, "Select at least one file to export")]
    public void Evaluate_DisablesStartWhenBatchRequirementsAreMissing(
        bool chosen, bool exists, int available, int selected, string guidance)
    {
        var result = BatchStartReadiness.Evaluate(chosen, exists, available, selected);

        Assert.False(result.CanStart);
        Assert.Equal(guidance, result.Guidance);
    }

    [Theory]
    [InlineData(1, "1 file ready to export")]
    [InlineData(4, "4 files ready to export")]
    public void Evaluate_EnablesStartWhenFolderExistsAndFilesAreSelected(int selected, string guidance)
    {
        var result = BatchStartReadiness.Evaluate(true, true, 5, selected);

        Assert.True(result.CanStart);
        Assert.Equal(guidance, result.Guidance);
    }
}
