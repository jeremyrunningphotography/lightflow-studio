using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class AppStateStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"LightflowStudio-{Guid.NewGuid():N}");
    private string StatePath => Path.Combine(_folder, "state.json");

    [Fact]
    public void StatePath_UsesLightflowStudioBrandFolder()
    {
        Assert.EndsWith(Path.Combine("Jeremy Running Photography", "Lightflow Studio", "state.json"), AppStateStore.StatePath);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsLastLutPath()
    {
        var expected = new AppState(@"D:\LUTs\Selected.cube")
        {
            HasBatchState = true,
            LastVideoFolder = @"D:\Videos",
            LastResolution = OutputResolution.UltraHd,
            LastRecovery = RecoveryStrategy.Salvage,
            LastIncludeSubfolders = true,
            LastPreserveFolderStructure = false,
            LastOverwriteExistingFiles = true,
            LastOutputMode = OutputDestinationMode.SpecificFolder,
            LastOutputSubfolder = "Deliverables",
            LastOutputSubfolderUsesResolutionDefault = false,
            LastSpecificOutputFolder = @"E:\Exports",
            LastFilenameSuffix = "_Web",
            LastFilenameSuffixUsesResolutionDefault = false
        };

        AppStateStore.Save(StatePath, expected);

        Assert.Equal(expected, AppStateStore.Load(StatePath));
    }

    [Fact]
    public void Load_ReturnsEmptyStateForMissingOrMalformedFile()
    {
        Assert.Equal(new AppState(), AppStateStore.Load(StatePath));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(StatePath, "not json");
        Assert.Equal(new AppState(), AppStateStore.Load(StatePath));
    }

    [Fact]
    public void SaveAndLoad_PreservesNoLutSelection()
    {
        var expected = new AppState("");

        AppStateStore.Save(StatePath, expected);

        Assert.Equal("", AppStateStore.Load(StatePath).LastLutPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}
