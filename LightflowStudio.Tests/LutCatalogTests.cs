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

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
