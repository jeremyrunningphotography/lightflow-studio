using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class LutCatalogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"LightflowStudio-{Guid.NewGuid():N}");

    public LutCatalogTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public void Discover_ReturnsCubeFilesWithReadableNamesAndOriginalPaths()
    {
        var expectedPath = Path.Combine(_folder, "Kodak-Portra_400 (warm)!.cube");
        File.WriteAllText(expectedPath, "LUT");
        File.WriteAllText(Path.Combine(_folder, "ignore.txt"), "not a LUT");

        var option = Assert.Single(LutCatalog.Discover(_folder));

        Assert.Equal("Kodak Portra 400 warm", option.DisplayName);
        Assert.Equal(expectedPath, option.FilePath);
    }

    [Fact]
    public void Discover_IsCaseInsensitiveAndSortsByDisplayName()
    {
        File.WriteAllText(Path.Combine(_folder, "Zulu.CUBE"), "LUT");
        File.WriteAllText(Path.Combine(_folder, "alpha.cube"), "LUT");

        var options = LutCatalog.Discover(_folder);

        Assert.Equal(["alpha", "Zulu"], options.Select(option => option.DisplayName));
    }

    [Fact]
    public void Discover_DisambiguatesNamesThatBecomeIdentical()
    {
        File.WriteAllText(Path.Combine(_folder, "Film-Look.cube"), "LUT");
        File.WriteAllText(Path.Combine(_folder, "Film_Look.cube"), "LUT");

        var options = LutCatalog.Discover(_folder);

        Assert.Equal(["Film Look (1)", "Film Look (2)"], options.Select(option => option.DisplayName));
        Assert.Equal(2, options.Select(option => option.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Discover_ReturnsEmptyForMissingFolder()
    {
        Assert.Empty(LutCatalog.Discover(Path.Combine(_folder, "missing")));
    }

    [Fact]
    public void NoLut_HasAnEmptyFilePathSoItCanBeUsedAsASentinel()
    {
        Assert.Equal("No LUT", LutCatalog.NoLut.DisplayName);
        Assert.Equal("", LutCatalog.NoLut.FilePath);
    }

    [Fact]
    public void Options_AlwaysPlacesNoLutFirst()
    {
        File.WriteAllText(Path.Combine(_folder, "Film.cube"), "LUT");

        var options = LutCatalog.Options(_folder);

        Assert.Equal(LutCatalog.NoLut, options[0]);
        Assert.Equal("Film", options[1].DisplayName);
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
        var options = LutCatalog.Options(Path.Combine(_folder, "missing"));

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
    public void IsValidSelection_RevalidatesFolderBackedOptionsBeforeEncoding()
    {
        var path = Path.Combine(_folder, "Film.cube");
        File.WriteAllText(path, "not a supported LUT");

        Assert.False(LutCatalog.IsValidSelection(new LutOption("Film", path, Guid.NewGuid(), IsManaged: true)));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
