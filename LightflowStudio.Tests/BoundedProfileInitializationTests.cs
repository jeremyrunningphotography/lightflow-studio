using System.Diagnostics;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BoundedProfileInitializationTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "bounded-profile-tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctions = [];
    private LightflowStorageLocations Profile(string child = "profile") => ApplicationDataProfile.Resolve(["--data-root", Path.Combine(_root, child)]);

    private sealed class CountingFileSystem : IProfileInitializationFileSystem
    {
        public readonly List<string> Inspections = [];
        public int Probes;
        public string? ReparsePath;
        public string? ForbiddenDescendants;
        public FileAttributes? Attributes(string path)
        {
            if (ForbiddenDescendants is { } forbidden)
                Assert.False(MediaPathSemantics.Contains(forbidden, path) && !string.Equals(forbidden, path, StringComparison.OrdinalIgnoreCase), path);
            Inspections.Add(path);
            return string.Equals(path, ReparsePath, StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.ReparsePoint : ProfileInitializationFileSystem.ReadAttributes(path);
        }
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void ProbeWrite(string path) { Probes++; new ProfileInitializationFileSystem().ProbeWrite(path); }
    }

    [Fact]
    public void FixedOperationBudgetNeverVisitsDerivedDescendants()
    {
        var profile = Profile();
        var fs = new CountingFileSystem { ForbiddenDescendants = profile.ThumbnailCacheDirectory };
        var first = ApplicationDataProfile.Initialize(profile, fs);
        var expected = fs.Inspections.ToArray();
        // A descendant sentinel (including arbitrary depth) must not affect the startup inventory.
        var nested = Path.Combine(profile.ThumbnailCacheDirectory, "aa", "bb", "many", "files");
        Directory.CreateDirectory(nested);
        for (var i = 0; i < 20; i++) File.WriteAllText(Path.Combine(nested, i + ".jpg"), "cache");
        fs.Inspections.Clear(); fs.Probes = 0;
        var second = ApplicationDataProfile.Initialize(profile, fs);
        Assert.Equal(expected, fs.Inspections);
        Assert.Equal(expected.Length, second.InspectedPaths);
        Assert.Equal(first.InspectedPaths, second.InspectedPaths);
        Assert.InRange(second.InspectedPaths, 30, 65);
        Assert.Equal(5, fs.Probes);
        Assert.Equal(fs.Inspections.Count, fs.Inspections.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task StorageReusesAppValidationWithoutAnotherInvocation()
    {
        var profile = Profile(); var fs = new CountingFileSystem();
        var initialized = ApplicationDataProfile.Initialize(profile, fs);
        var inspections = fs.Inspections.Count;
        var diagnostics = new List<string>();
        using var tracing = new StartupDiagnostics(diagnostics.Add);
        var result = await LightflowStorageCoordinator.StartAsync(profile: profile, initializedProfile: initialized);
        await using var storage = result.Coordinator!;
        Assert.True(result.IsReady, result.Diagnostic);
        Assert.Equal(inspections, fs.Inspections.Count);
        Assert.Equal(5, fs.Probes);
        Assert.DoesNotContain(diagnostics, text => text.Contains("Isolated profile validation: completed"));
    }

    [Fact]
    public async Task HeadlessStorageOwnsOneValidationWhenNoResultIsSupplied()
    {
        var messages = new List<string>();
        using var diagnostics = new StartupDiagnostics(messages.Add);
        var result = await LightflowStorageCoordinator.StartAsync(profile: Profile());
        await using var storage = result.Coordinator!;
        Assert.True(result.IsReady, result.Diagnostic);
        Assert.Single(messages, text => text.Contains("Isolated profile validation: completed"));
    }

    [Fact]
    public async Task ValidatedResultCannotBeUsedForAnotherProfile()
    {
        var initialized = ApplicationDataProfile.Initialize(Profile());
        await Assert.ThrowsAsync<ArgumentException>(() => LightflowStorageCoordinator.StartAsync(profile: Profile("other"), initializedProfile: initialized));
        Assert.False(Directory.Exists(Profile("other").ApplicationDataDirectory));
    }

    [Fact]
    public void NormalProfilePerformsZeroIsolationOperations()
    {
        var fs = new CountingFileSystem();
        var result = ApplicationDataProfile.Initialize(LightflowStorageLocations.CreateDefault(), fs);
        Assert.Empty(fs.Inspections); Assert.Equal(0, fs.Probes); Assert.Equal(0, result.InspectedPaths);
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("workspace-state.json")]
    [InlineData("Catalog/LightflowCatalog.db")]
    [InlineData("Catalog/LightflowCatalog.db-wal")]
    [InlineData("Catalog/LightflowCatalog.db.startup-state")]
    [InlineData("Previews/previews.db-shm")]
    [InlineData("Previews/thumbnails")]
    [InlineData("output-identities")]
    [InlineData("encoding-resources/luts")]
    public void FixedBoundaryAndFileLinksFailBeforeWrites(string relative)
    {
        var profile = Profile();
        var fs = new CountingFileSystem { ReparsePath = Path.GetFullPath(Path.Combine(profile.ApplicationDataDirectory, relative)) };
        Assert.Throws<IOException>(() => ApplicationDataProfile.Initialize(profile, fs));
        Assert.Equal(0, fs.Probes); Assert.False(Directory.Exists(profile.ApplicationDataDirectory));
    }

    [Theory]
    [InlineData("Catalog", true)]
    [InlineData("Previews", true)]
    [InlineData("Previews/thumbnails", true)]
    [InlineData("Previews/thumbnails/aa", false)]
    [InlineData("Previews/previews/visual-index/asset", false)]
    [InlineData("Previews/previews/markers/asset", false)]
    [InlineData("Previews/previews/subclips/asset", false)]
    [InlineData("output-identities/nested", false)]
    public void ActualJunctionsAreRejectedAtBoundaryOrBeforeDescendantAccess(string relative, bool startupRejects)
    {
        var profile = Profile(); var target = Path.Combine(_root, "outside"); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "sentinel"), "unchanged");
        var link = Path.GetFullPath(Path.Combine(profile.ApplicationDataDirectory, relative));
        Junction(link, target);
        if (startupRejects) Assert.Throws<IOException>(() => ApplicationDataProfile.Initialize(profile));
        else
        {
            ApplicationDataProfile.Initialize(profile);
            Assert.Throws<IOException>(() => ApplicationDataProfile.GuardAccess(Path.Combine(link, "sentinel")));
            if (MediaPathSemantics.Contains(profile.PreviewsDirectory, link))
            {
                var relativeFile = Path.GetRelativePath(profile.PreviewsDirectory, Path.Combine(link, "sentinel"));
                Assert.Throws<IOException>(() => MediaPathSemantics.ResolveContained(profile.PreviewsDirectory, relativeFile));
                Assert.Throws<IOException>(() => ApplicationDataProfile.EnumerateOwnedFiles(profile.PreviewsDirectory, "*", SearchOption.AllDirectories).ToArray());
            }
        }
        Assert.Equal("unchanged", File.ReadAllText(Path.Combine(target, "sentinel")));
    }

    [Fact]
    public void RootAncestorJunctionFailsWithoutTouchingTarget()
    {
        var target = Path.Combine(_root, "target"); Directory.CreateDirectory(target);
        var link = Path.Combine(_root, "alias"); Junction(link, target);
        var profile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(link, "profile")]);
        Assert.Throws<IOException>(() => ApplicationDataProfile.Initialize(profile));
        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    [Fact]
    public void LinkIntroducedAfterStartupIsRejectedAtAccess()
    {
        var profile = Profile(); ApplicationDataProfile.Initialize(profile);
        var outside = Path.Combine(_root, "outside"); Directory.CreateDirectory(outside);
        var link = Path.Combine(profile.ThumbnailCacheDirectory, "late"); Junction(link, outside);
        Assert.Throws<IOException>(() => ApplicationDataProfile.GuardAccess(Path.Combine(link, "new.jpg")));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }

    [Fact]
    public async Task ConfiguredDatabaseLeafCannotEscapeThroughLink()
    {
        var profile = Profile(); ApplicationDataProfile.Initialize(profile);
        var directory = Path.Combine(profile.ApplicationDataDirectory, "custom");
        Directory.CreateDirectory(directory);
        // A directory junction at the configured database leaf is rejected before SQLite/evidence opens.
        var outside = Path.Combine(_root, "outside"); Directory.CreateDirectory(outside);
        Junction(Path.Combine(directory, LightflowStorageLocations.CatalogFileName), outside);
        AppSettingsStore.Save(profile.SettingsPath, new AppSettings { CatalogDirectory = directory });
        var result = await LightflowStorageCoordinator.StartAsync(profile: profile);
        Assert.Equal(StorageStartupStatus.InvalidConfiguration, result.Status);
        Assert.Null(result.Coordinator); Assert.Empty(Directory.GetFileSystemEntries(outside));
    }

    [Fact]
    public void LongNormalizedCaseEquivalentPathsAndSiblingBoundaries()
    {
        var path = Path.Combine(_root, string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat(new string('a', 45), 6)));
        var profile = ApplicationDataProfile.Resolve(["--data-root", path]);
        ApplicationDataProfile.Initialize(profile);
        Assert.True(path.Length > 260);
        ApplicationDataProfile.RequireContained(path.ToUpperInvariant(), Path.Combine(path, "child", "..", "file"));
        ApplicationDataProfile.RequireContained(path, path);
        Assert.Throws<ArgumentException>(() => ApplicationDataProfile.RequireContained(path, path + "-sibling"));
        Assert.Throws<ArgumentException>(() => ApplicationDataProfile.RequireContained(path, Path.GetDirectoryName(path)!));
        Assert.Throws<ArgumentException>(() => ApplicationDataProfile.RequireContained(path, "Z:\\outside"));
        Assert.Throws<ArgumentException>(() => ApplicationDataProfile.RequireContained(path, Path.Combine(path, "file:stream")));
    }

    [Fact]
    public void DefaultProfileAliasesAndAncestorsRemainForbidden()
    {
        var normal = LightflowStorageLocations.CreateDefault().ApplicationDataDirectory;
        foreach (var candidate in new[] { normal.ToUpperInvariant(), Path.Combine(normal, "child", ".."),
                     Path.Combine(normal, "child"), Path.GetDirectoryName(normal)! })
            Assert.Throws<ArgumentException>(() => ApplicationDataProfile.Resolve(["--data-root", candidate]));
        Assert.True(ApplicationDataProfile.Resolve(["--data-root", normal + "-isolated-sibling"]).IsIsolated);
    }

    [Fact]
    public void OwnedStorageExclusionStillAllowsSourceRootsButRejectsSelfDiscovery()
    {
        var profile = Profile();
        var owned = new OwnedStoragePaths(() => profile);
        Assert.False(owned.Contains(_root)); // A media root may contain the isolated profile.
        Assert.False(owned.Contains(Path.Combine(_root, "source.jpg")));
        Assert.True(owned.Contains(profile.ApplicationDataDirectory));
        Assert.True(owned.Contains(Path.Combine(profile.ThumbnailCacheDirectory, "aa", "frame.jpg")));
        Assert.False(owned.Contains(profile.ApplicationDataDirectory + "-sibling"));
    }

    [Fact]
    public async Task GeneratedThumbnailPathsRejectLinkedCacheBuckets()
    {
        var profile = Profile(); ApplicationDataProfile.Initialize(profile);
        var assetId = Guid.NewGuid(); var target = Path.Combine(_root, "outside"); Directory.CreateDirectory(target);
        Junction(Path.Combine(profile.ThumbnailCacheDirectory, assetId.ToString("N")[..2]), target);
        await using var store = new PreviewStoreService(profile);
        Assert.Throws<IOException>(() => store.GetArtifactPath(assetId, PreviewArtifactKind.Thumbnail, 1,
            new PreviewSourceIdentity(1, 1, 1, "abcdef"), "jpg"));
        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    private void Junction(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(link); start.ArgumentList.Add(target);
        using var process = Process.Start(start)!; process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd());
        _junctions.Add(link);
    }

    public void Dispose()
    {
        foreach (var link in _junctions.AsEnumerable().Reverse()) if (Directory.Exists(link)) Directory.Delete(link);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
