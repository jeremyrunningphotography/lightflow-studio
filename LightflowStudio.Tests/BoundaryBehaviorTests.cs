using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BoundaryBehaviorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"LightflowStudio-{Guid.NewGuid():N}");

    [Fact]
    public void EncodingBuildersRejectUnknownEnumValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FfmpegCommandBuilder.Encode("in", "out", "lut", (RecoveryStrategy)99, OutputResolution.Source));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FfmpegCommandBuilder.Encode("in", "out", "lut", RecoveryStrategy.Normal, (OutputResolution)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EncodingPathPlanner.OutputRoot("videos", OutputResolution.Source, (RecoveryStrategy)99));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(42.5, 42.5)]
    [InlineData(120, 100)]
    public void FileProgressIsClampedToValidRange(double reported, double expected)
    {
        var progress = new BatchProgressState();

        progress.ReportFileProgress(reported);

        Assert.Equal(expected, progress.FilePercent);
    }

    [Fact]
    public void BatchProgressRejectsNonPositiveTotals()
    {
        var progress = new BatchProgressState();

        Assert.Throws<ArgumentOutOfRangeException>(() => progress.StartBatch(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => progress.ReportBatchProgress(0, 0));
    }

    [Fact]
    public void BlankSavedLutFolderFallsBackToDefault()
    {
        var path = Path.Combine(_folder, "settings.json");
        AppSettingsStore.Save(path, new AppSettings("   "));

        Assert.Equal(LutCatalog.DefaultFolder, AppSettingsStore.Load(path).CameraLutFolder);
    }

    [Theory]
    [InlineData("warmFilmLook.cube", "warm Film Look")]
    [InlineData("!!!.cube", "Unnamed LUT")]
    [InlineData("clean name.cube", "clean name")]
    public void LutDisplayNamesHandleCommonAndDegenerateNames(string file, string expected)
    {
        Assert.Equal(expected, LutCatalog.MakeDisplayName(file));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
