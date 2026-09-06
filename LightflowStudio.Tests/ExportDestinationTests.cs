using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExportDestinationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-destination-").FullName;

    [Fact]
    public void SpecificFolder_FlattensSourcesAndAppendsOptionalSubfolder()
    {
        var destination = Path.Combine(_root, "Exports");
        var policy = new ExportDestination(ExportDestinationMode.SpecificFolder, destination, "1080p");

        Assert.Equal(Path.Combine(destination, "1080p"),
            policy.ResolveDirectory(Path.Combine(_root, "Shoot A", "clip.mp4")));
        Assert.Equal(Path.Combine(destination, "1080p"),
            policy.ResolveDirectory(Path.Combine(_root, "Other", "Nested", "clip.mp4")));
    }

    [Fact]
    public void SameFolderAsOriginal_ResolvesEachSourceIndependently()
    {
        var policy = new ExportDestination(ExportDestinationMode.SameFolderAsOriginal, null, "1080p");
        var first = Path.Combine(_root, "Shoot A", "clip1.mp4");
        var second = Path.Combine(_root, "Shoot B", "clip2.mp4");

        Assert.Equal(Path.Combine(_root, "Shoot A", "1080p"), policy.ResolveDirectory(first));
        Assert.Equal(Path.Combine(_root, "Shoot B", "1080p"), policy.ResolveDirectory(second));
    }

    [Theory]
    [InlineData("nested/folder")]
    [InlineData("nested\\folder")]
    [InlineData("..")]
    internal void OptionalSubfolder_IsOneValidatedSegment(string subfolder)
    {
        var policy = new ExportDestination(ExportDestinationMode.SpecificFolder, _root, subfolder);

        Assert.Throws<ArgumentException>(() => policy.Normalize());
    }

    [Fact]
    public void SpecificFolder_RequiresAnAbsoluteFolderWhileSameFolderDoesNot()
    {
        Assert.Throws<ArgumentException>(() =>
            new ExportDestination(ExportDestinationMode.SpecificFolder, "relative", null).Normalize());
        var same = new ExportDestination(ExportDestinationMode.SameFolderAsOriginal, null, null).Normalize();
        Assert.Null(same.SpecificFolder);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
