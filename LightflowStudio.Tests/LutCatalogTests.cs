using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class LutCatalogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"LightflowStudio-{Guid.NewGuid():N}");

    public LutCatalogTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public void NoLut_HasAnEmptyFilePathSoItCanBeUsedAsASentinel()
    {
        Assert.Equal("No LUT", LutCatalog.NoLut.DisplayName);
        Assert.Equal("", LutCatalog.NoLut.FilePath);
    }

    [Fact]
    public void Options_UsesFolderResourcesInExistingEncodingLibrary()
    {
        var lutId = Guid.NewGuid();
        var path = Path.Combine(_folder, "Film.cube");
        var options = LutCatalog.Options([
            new ManagedLutResource(lutId, "Film", "Film.cube", new('a', 64),
                LutDimension.ThreeDimensional, 33, LutResourceAvailability.Available, path)
        ]);

        Assert.Equal(["No LUT", "Film"], options.Select(option => option.DisplayName));
        Assert.Equal(lutId, options[1].LutId);
        Assert.Equal(path, options[1].FilePath);
        Assert.True(options[1].IsManaged);
    }

    [Fact]
    public void SelectPreferred_PreservesNoLutAndFallsBackToItWhenSavedLutIsMissing()
    {
        var options = LutCatalog.Options(Array.Empty<ManagedLutResource>());

        Assert.Equal(LutCatalog.NoLut, LutCatalog.SelectPreferred(options, ""));
        Assert.Equal(LutCatalog.NoLut, LutCatalog.SelectPreferred(options, @"C:\missing.cube"));
    }

    [Fact]
    public void IsValidSelection_AcceptsNoLutAndExistingCubeOnly()
    {
        var cube = Path.Combine(_folder, "Film.cube");
        var text = Path.Combine(_folder, "Film.txt");
        File.WriteAllText(cube, "LUT");
        File.WriteAllText(text, "not a LUT");

        Assert.True(LutCatalog.IsValidSelection(LutCatalog.NoLut));
        Assert.True(LutCatalog.IsValidSelection(new LutOption("Film", cube)));
        Assert.False(LutCatalog.IsValidSelection(null));
        Assert.False(LutCatalog.IsValidSelection(new LutOption("Missing", Path.Combine(_folder, "Missing.cube"))));
        Assert.False(LutCatalog.IsValidSelection(new LutOption("Wrong type", text)));
    }

    [Fact]
    public void IsValidSelection_TrustsAlreadyValidatedFolderBackedCacheEntry()
    {
        var path = Path.Combine(_folder, "Film.cube");
        File.WriteAllText(path, "not a supported LUT");

        Assert.True(LutCatalog.IsValidSelection(new LutOption("Film", path, Guid.NewGuid(), IsManaged: true)));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
