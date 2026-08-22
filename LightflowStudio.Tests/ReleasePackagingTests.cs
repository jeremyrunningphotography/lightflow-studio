using System.Buffers.Binary;
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
        Assert.Equal("3.11.2", root.GetProperty("flyleafVersion").GetString());
        Assert.Equal("9.0.0", root.GetProperty("flyleafBindingsVersion").GetString());
        Assert.Matches("^[a-f0-9]{64}$", root.GetProperty("sha256").GetString()!);
        Assert.Contains("lgpl-shared", root.GetProperty("archiveName").GetString());

        var project = XDocument.Load(PathAtRoot("LightflowStudio", "LightflowStudio.csproj"));
        var packages = project.Descendants("PackageReference").ToDictionary(
            element => element.Attribute("Include")!.Value,
            element => element.Attribute("Version")!.Value);
        Assert.Equal("3.11.2", packages["FlyleafLib"]);
        Assert.Equal("9.0.0", packages["Flyleaf.FFmpeg.Bindings"]);
        Assert.Equal("2.3.0", packages["NAudio.WinMM"]);
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
    public void CatalogSqliteDependency_IsExactLockedLicensedAndEmbeddedForSelfContainedPackaging()
    {
        var project = XDocument.Load(PathAtRoot("LightflowStudio", "LightflowStudio.csproj"));
        var sqlite = project.Descendants("PackageReference").Single(element =>
            element.Attribute("Include")?.Value == "Microsoft.Data.Sqlite");
        Assert.Equal("[8.0.29]", sqlite.Attribute("Version")?.Value);

        using var lockDocument = JsonDocument.Parse(
            File.ReadAllText(PathAtRoot("LightflowStudio", "packages.lock.json")));
        var packages = lockDocument.RootElement.GetProperty("dependencies")
            .GetProperty("net8.0-windows7.0");
        Assert.Equal("8.0.29", packages.GetProperty("Microsoft.Data.Sqlite").GetProperty("resolved").GetString());
        Assert.Equal("8.0.29", packages.GetProperty("Microsoft.Data.Sqlite.Core").GetProperty("resolved").GetString());
        Assert.Equal("2.1.6", packages.GetProperty("SQLitePCLRaw.bundle_e_sqlite3").GetProperty("resolved").GetString());
        Assert.Equal("2.1.6", packages.GetProperty("SQLitePCLRaw.lib.e_sqlite3").GetProperty("resolved").GetString());

        using var testLockDocument = JsonDocument.Parse(
            File.ReadAllText(PathAtRoot("LightflowStudio.Tests", "packages.lock.json")));
        var testPackages = testLockDocument.RootElement.GetProperty("dependencies")
            .GetProperty("net8.0-windows7.0");
        Assert.Equal("8.0.29", testPackages.GetProperty("Microsoft.Data.Sqlite").GetProperty("resolved").GetString());
        Assert.Equal("2.1.6", testPackages.GetProperty("SQLitePCLRaw.lib.e_sqlite3").GetProperty("resolved").GetString());

        var release = File.ReadAllText(PathAtRoot("scripts", "Build-Release.ps1"));
        Assert.Contains("IncludeNativeLibrariesForSelfExtract=true", release);
        Assert.Contains("--verify-catalog-runtime", release);
        Assert.Contains("Start-Process", release);
        Assert.Contains("-WorkingDirectory $appDirectory", release);
        Assert.Contains("-Wait -PassThru -WindowStyle Hidden", release);
        Assert.Contains("Packaged Catalog SQLite runtime verification failed", release);
        Assert.True(release.IndexOf("--verify-catalog-runtime", StringComparison.Ordinal) <
            release.IndexOf("if ($Mode -eq \"Release\")", StringComparison.Ordinal));
        Assert.True(release.IndexOf("--verify-catalog-runtime", StringComparison.Ordinal) <
            release.IndexOf("if (-not $SkipInstaller)", StringComparison.Ordinal));
        var notices = File.ReadAllText(PathAtRoot("THIRD-PARTY-NOTICES.md"));
        Assert.Contains("Microsoft.Data.Sqlite 8.0.29", notices);
        Assert.Contains("SQLitePCLRaw 2.1.6", notices);
        Assert.Contains("sqlite.org/copyright", notices);
        Assert.Contains("Microsoft.Data.Sqlite", File.ReadAllText(
            PathAtRoot("scripts", "Test-PackageContents.ps1")));
    }

    [Fact]
    public void PullRequestPackaging_UsesSharedStagingButSkipsReleaseArchiveAndUsesFastInstallerCompression()
    {
        var script = File.ReadAllText(PathAtRoot("scripts", "Build-Release.ps1"));
        var installer = File.ReadAllText(PathAtRoot("installer", "LightflowStudio.iss"));

        Assert.Contains("[ValidateSet(\"Release\", \"PullRequest\")]", script);
        Assert.Contains("$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)", script);
        Assert.Contains("Test-PackageContents.ps1", script);
        Assert.Contains("if ($Mode -eq \"Release\")", script);
        Assert.Contains("release portable archive skipped", script);
        Assert.Contains("/DValidationBuild=1", script);
        Assert.Contains("#ifdef ValidationBuild", installer);
        Assert.Contains("Compression=zip", installer);
        Assert.Contains("Compression=lzma2/ultra64", installer);
    }

    [Fact]
    public void Packaging_WaitsForRealBrowserStartupAndFailsIfPackagedProcessExits()
    {
        var script = File.ReadAllText(PathAtRoot("scripts", "Build-Release.ps1"));

        Assert.Contains("--startup-smoke-test", script);
        Assert.Contains("WaitForExit(8000)", script);
        Assert.Contains("exited during the Browser startup smoke test", script);
        Assert.Contains("Stop-Process -Id $startupSmoke.Id", script);
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
    public void Installer_IsBrandedPerMachineAndRecursivelyPackagesTheStagedApplication()
    {
        var installer = File.ReadAllText(PathAtRoot("installer", "LightflowStudio.iss"));

        Assert.Contains("recursesubdirs", installer);
        Assert.Contains("PrivilegesRequired=admin", installer);
        Assert.DoesNotContain("PrivilegesRequired=lowest", installer);
        Assert.DoesNotContain("PrivilegesRequiredOverridesAllowed", installer);
        Assert.Contains(@"DefaultDirName={autopf}\Lightflow Studio", installer);
        Assert.DoesNotContain("{localappdata}", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WizardStyle=modern dark slate includetitlebar hidebevels", installer);
        Assert.Contains("WizardImageFile={#InstallerAssetsDir}\\LightflowWizard.png", installer);
        Assert.Contains("WizardSmallImageFile={#InstallerAssetsDir}\\LightflowWizardSmall.png", installer);
        Assert.Contains("Jeremy Running Photography", installer);
        Assert.Contains("UninstallDisplayIcon={app}\\{#MyAppExeName}", installer);
        Assert.Contains("THIRD-PARTY-NOTICES.md", installer);
        Assert.True(File.Exists(PathAtRoot("installer", "Assets", "LightflowWizard.png")));
        Assert.True(File.Exists(PathAtRoot("installer", "Assets", "LightflowWizardSmall.png")));
        Assert.Equal((480, 918), ReadPngDimensions(PathAtRoot("installer", "Assets", "LightflowWizard.png")));
        Assert.Equal((294, 294), ReadPngDimensions(PathAtRoot("installer", "Assets", "LightflowWizardSmall.png")));
        Assert.Contains("Test-InstallerArtifact.ps1", File.ReadAllText(PathAtRoot("scripts", "Build-Release.ps1")));
        var artifactValidation = File.ReadAllText(PathAtRoot("scripts", "Test-InstallerArtifact.ps1"));
        Assert.Contains("ProductName", artifactValidation);
        Assert.Contains("CompanyName", artifactValidation);
        Assert.Contains("ProductVersion", artifactValidation);
    }

    [Fact]
    public void Installer_DeliberatelyMigratesLegacyPerUserRegistrationWithoutTouchingUserData()
    {
        var installer = File.ReadAllText(PathAtRoot("installer", "LightflowStudio.iss"));

        Assert.Contains("HKCU64", installer);
        Assert.Contains("HKCU32", installer);
        Assert.Contains("PrepareToInstall", installer);
        Assert.Contains("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", installer);
        Assert.Contains("ExecutableFromCommandLine", installer);
        Assert.Contains("Your Catalog, Previews, settings, and logs will remain untouched", installer);
        Assert.DoesNotContain("[UninstallDelete]", installer);
        Assert.DoesNotContain("Jeremy Running Photography\\Lightflow Studio", installer);

        var locations = File.ReadAllText(PathAtRoot("LightflowStudio", "LightflowStorageLocations.cs"));
        Assert.Contains("Environment.SpecialFolder.LocalApplicationData", locations);
        Assert.DoesNotContain("Environment.SpecialFolder.ProgramFiles", locations);
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

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        var header = File.ReadAllBytes(path).AsSpan();
        Assert.True(header.Length >= 24 &&
            header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        return (BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4)));
    }

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
