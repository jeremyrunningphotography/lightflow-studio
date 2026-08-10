using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ReleasePackagingTests
{
    [Fact]
    public void FfmpegManifest_IsPinnedAndHasAValidSha256()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PathAtRoot("dependencies", "ffmpeg.json")));
        var root = document.RootElement;

        Assert.DoesNotContain("latest", root.GetProperty("downloadUrl").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("autobuild-", root.GetProperty("releaseTag").GetString());
        Assert.Matches("^[a-f0-9]{64}$", root.GetProperty("sha256").GetString()!);
        Assert.Equal("LGPL-2.1-or-later", root.GetProperty("license").GetString());
        Assert.StartsWith("https://", root.GetProperty("sourceUrl").GetString());
    }

    [Fact]
    public void PlaybackDependencies_AreExactPinnedAndUseTheLgplSharedVariant()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PathAtRoot("dependencies", "ffmpeg-playback.json")));
        var root = document.RootElement;
        Assert.Equal("lgpl-shared", root.GetProperty("variant").GetString());
        Assert.Equal("LGPL-2.1-or-later", root.GetProperty("license").GetString());
        Assert.Equal("3.10.4", root.GetProperty("flyleafVersion").GetString());
        Assert.Equal("8.1.0", root.GetProperty("flyleafBindingsVersion").GetString());
        Assert.Matches("^[a-f0-9]{64}$", root.GetProperty("sha256").GetString()!);
        Assert.Contains("lgpl-shared", root.GetProperty("archiveName").GetString());

        var project = XDocument.Load(PathAtRoot("LightflowStudio", "LightflowStudio.csproj"));
        var packages = project.Descendants("PackageReference").ToDictionary(
            element => element.Attribute("Include")!.Value,
            element => element.Attribute("Version")!.Value);
        Assert.Equal("3.10.4", packages["FlyleafLib"]);
        Assert.Equal("8.1.0", packages["Flyleaf.FFmpeg.Bindings"]);
    }

    [Fact]
    public void ReleasePackaging_IncludesPlaybackLibrariesAndComplianceRecords()
    {
        var script = File.ReadAllText(PathAtRoot("scripts", "Build-Release.ps1"));
        var dependencyScript = File.ReadAllText(PathAtRoot("scripts", "Get-PlaybackDependencies.ps1"));
        Assert.Contains("Get-PlaybackDependencies.ps1", script);
        Assert.Contains("playback\\ffmpeg", script);
        Assert.Contains("avcodec-*.dll", dependencyScript);
        Assert.Contains("SOURCE-AND-LICENSE.txt", dependencyScript);
        Assert.Contains("lgpl-shared", dependencyScript);
    }

    [Fact]
    public void PullRequestPackaging_UsesSharedStagingButSkipsReleaseArchiveAndUsesFastInstallerCompression()
    {
        var script = File.ReadAllText(PathAtRoot("scripts", "Build-Release.ps1"));
        var installer = File.ReadAllText(PathAtRoot("installer", "LightflowStudio.iss"));

        Assert.Contains("[ValidateSet(\"Release\", \"PullRequest\")]", script);
        Assert.Contains("Test-PackageContents.ps1", script);
        Assert.Contains("if ($Mode -eq \"Release\")", script);
        Assert.Contains("release portable archive skipped", script);
        Assert.Contains("/DValidationBuild=1", script);
        Assert.Contains("#ifdef ValidationBuild", installer);
        Assert.Contains("Compression=zip", installer);
        Assert.Contains("Compression=lzma2/ultra64", installer);
    }

    [Fact]
    public void PackageValidation_ChecksNativeDependenciesManifestsLicensesAndNotices()
    {
        var validation = File.ReadAllText(PathAtRoot("scripts", "Test-PackageContents.ps1"));

        Assert.Contains("THIRD-PARTY-NOTICES.md", validation);
        Assert.Contains("avcodec-*.dll", validation);
        Assert.Contains("ffmpeg-playback-package.json", validation);
        Assert.Contains("Get-FileHash", validation);
        Assert.Contains("licenses", validation);
    }

    [Fact]
    public void Workflow_CachesPinnedDependenciesByBothManifestContentsAndStillRunsVerification()
    {
        var workflow = File.ReadAllText(PathAtRoot(".github", "workflows", "ci-release.yml"));

        Assert.Contains("actions/cache@v5", workflow);
        Assert.Contains("path: .cache/ffmpeg/*.zip", workflow);
        Assert.Contains("hashFiles('dependencies/ffmpeg.json', 'dependencies/ffmpeg-playback.json')", workflow);
        Assert.Contains("Get-Ffmpeg.ps1", workflow);
        Assert.Contains("Get-PlaybackDependencies.ps1", workflow);
        Assert.Contains("-Mode $mode", workflow);
    }

    [Fact]
    public void ReleaseWorkflow_GatesPackagingOnTests()
    {
        var workflow = File.ReadAllText(PathAtRoot(".github", "workflows", "ci-release.yml"));

        Assert.Contains("needs: test", workflow);
        Assert.Contains("dotnet test", workflow);
        Assert.Contains("Build-Release.ps1", workflow);
        Assert.Contains("GITHUB_REF_NAME", workflow);
        Assert.Contains("SHA256SUMS.txt", File.ReadAllText(PathAtRoot("scripts", "Build-Release.ps1")));
    }

    [Fact]
    public void Installer_RecursivelyPackagesTheStagedApplication()
    {
        var installer = File.ReadAllText(PathAtRoot("installer", "LightflowStudio.iss"));

        Assert.Contains("recursesubdirs", installer);
        Assert.Contains("PrivilegesRequired=lowest", installer);
        Assert.Contains("THIRD-PARTY-NOTICES.md", installer);
    }

    [Fact]
    public void ProductVersion_RemainsTheReleaseScriptAuthority()
    {
        var props = XDocument.Load(PathAtRoot("Directory.Build.props"));
        var version = props.Descendants("VersionPrefix").Single().Value;
        var releaseScript = File.ReadAllText(PathAtRoot("scripts", "Build-Release.ps1"));

        Assert.Contains("Directory.Build.props", releaseScript);
        Assert.Contains("VersionPrefix", releaseScript);
        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
    }

    private static string PathAtRoot(params string[] parts) =>
        Path.Combine(new[] { FindRepositoryRoot() }.Concat(parts).ToArray());

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the Lightflow Studio repository root.");
    }
}
