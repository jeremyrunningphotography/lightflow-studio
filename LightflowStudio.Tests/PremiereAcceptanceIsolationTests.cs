using Xunit;

namespace LightflowStudio.Tests;

public sealed class PremiereAcceptanceIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(PremiereAcceptanceIsolation.FixtureArea, "policy-" + Guid.NewGuid().ToString("N"));
    private string Project => Path.Combine(_root, "disposable.prproj");
    private string Media => Path.Combine(_root, "media", "source.mov");
    public PremiereAcceptanceIsolationTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Media)!);
        File.WriteAllText(Project, "disposable test project");
        File.WriteAllText(Media, "disposable test media");
    }
    [Fact]
    public void ValidFixturesProduceFreshTaskOwnedCatalogAndPairingWithoutMutation()
    {
        var value = PremiereAcceptanceIsolation.Validate(_root, Project, Media);
        Assert.True(value.Locations.IsIsolated);
        ApplicationDataProfile.RequireContained(_root, value.Locations.CatalogDatabasePath);
        ApplicationDataProfile.RequireContained(_root, value.PairingDirectory);
        Assert.False(Directory.Exists(value.RunDirectory));
        Assert.NotEqual(value.RunDirectory, PremiereAcceptanceIsolation.Validate(_root, Project, Media).RunDirectory);
        Assert.DoesNotContain("Lightflow-Premiere-acceptance-pairing", value.PairingDirectory);
        value.Revalidate(Project);
        value.Revalidate(@"\\?\" + Project);
    }
    [Fact]
    public void NormalProfileAndParentChildAndNormalizedAliasesFailBeforeCreatingCatalogOrBridge()
    {
        var normal = LightflowStorageLocations.CreateDefault().ApplicationDataDirectory;
        foreach (var root in new[] { normal, Path.GetDirectoryName(normal)!, Path.Combine(normal, "Catalog"),
            Path.Combine(normal, "child", ".."), Path.Combine(_root, "..", "..", "..", "..") })
            Assert.ThrowsAny<ArgumentException>(() => PremiereAcceptanceIsolation.Validate(root, Project, Media));
        Assert.False(Directory.Exists(Path.Combine(_root, "runs")));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OutsideProjectOrMediaRejected(bool project)
    {
        var outside = Path.Combine(PremiereAcceptanceIsolation.FixtureArea, project ? "outside.prproj" : "outside.mov");
        Assert.ThrowsAny<ArgumentException>(() => PremiereAcceptanceIsolation.Validate(_root,
            project ? outside : Project, project ? Media : outside));
        Assert.False(Directory.Exists(Path.Combine(_root, "runs")));
    }
    [Fact]
    public void ActiveProjectMustRemainExactContainedFixture()
    {
        var value = PremiereAcceptanceIsolation.Validate(_root, Project, Media);
        var other = Path.Combine(_root, "other.prproj");
        File.WriteAllText(other, "other disposable project");
        Assert.Throws<InvalidOperationException>(() => value.Revalidate(other));
        Assert.ThrowsAny<ArgumentException>(() => value.Revalidate(Path.Combine(_root, "..", "outside.prproj")));
    }
    [Fact]
    public void ReparseDirectoryFailsClosed()
    {
        var link = Path.Combine(_root, "linked");
        var target = Path.Combine(_root, "media");
        // NTFS junctions need no symlink privilege and never launch an interactive process.
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { "-NoProfile", "-Command", $"New-Item -ItemType Junction -Path '{link}' -Target '{target}' | Out-Null" }
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        try { Assert.Throws<IOException>(() => PremiereAcceptanceIsolation.Validate(_root, Project, Media)); }
        finally { Directory.Delete(link); }
    }
    [Fact]
    public void HardLinkedMediaFailsClosed()
    {
        var alias = Path.Combine(_root, "media", "alias.mov");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { "-NoProfile", "-Command", $"New-Item -ItemType HardLink -Path '{alias}' -Target '{Media}' | Out-Null" }
        })!;
        process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        Assert.Throws<IOException>(() => PremiereAcceptanceIsolation.Validate(_root, Project, Media));
    }
    public void Dispose()
    {
        ApplicationDataProfile.RequireContained(PremiereAcceptanceIsolation.FixtureArea, _root);
        Directory.Delete(_root, true);
    }
}
