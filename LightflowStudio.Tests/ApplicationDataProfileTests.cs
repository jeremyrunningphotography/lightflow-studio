using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ApplicationDataProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lightflow-profile-" + Guid.NewGuid().ToString("N"));
    private LightflowStorageLocations Profile(string name = "isolated") =>
        ApplicationDataProfile.Resolve(["--data-root", Path.Combine(_root, name)]);

    [Theory]
    [InlineData("--data-root")]
    [InlineData("--data-root", "")]
    [InlineData("--data-root", " ")]
    [InlineData("--data-root", "relative")]
    [InlineData("--data-root", "C:relative")]
    [InlineData("--data-root", "--startup-smoke-test")]
    [InlineData("--data-root=C:\\isolated")]
    [InlineData("--DATA-ROOT", "C:\\isolated")]
    [InlineData("--data-root", "C:\\isolated", "--data-root", "C:\\other")]
    [InlineData("--data-root", "C:\\invalid*root")]
    [InlineData("--data-root", "C:\\ambiguous.\\root")]
    [InlineData("--data-root", "C:\\root:stream")]
    [InlineData("--data-root", "\\\\server\\share\\root")]
    public void MalformedArgumentsFailClosed(params string[] args) =>
        Assert.ThrowsAny<ArgumentException>(() => ApplicationDataProfile.Resolve(args));

    [Fact]
    public void NoOverrideRetainsProductionPathsAndInstanceIdentity()
    {
        var profile = ApplicationDataProfile.Resolve(["--startup-smoke-test"]);
        Assert.Equal(LightflowStorageLocations.CreateDefault(), profile);
        Assert.False(profile.IsIsolated);
        Assert.Equal(WindowsApplicationInstanceCoordinator.ApplicationIdentity, ApplicationDataProfile.InstanceIdentity(profile));
    }

    [Fact]
    public void AbsoluteResolutionIsDeterministicAndAllMutablePathsAreContained()
    {
        var profile = Profile();
        var equivalent = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(_root, "child", "..", "isolated") + "\\"]);
        Assert.Equal(profile, equivalent);
        Assert.Equal(ApplicationDataProfile.InstanceIdentity(profile), ApplicationDataProfile.InstanceIdentity(equivalent));
        Assert.NotEqual(ApplicationDataProfile.InstanceIdentity(profile), ApplicationDataProfile.InstanceIdentity(Profile("second")));
        foreach (var property in typeof(LightflowStorageLocations).GetProperties()
                     .Where(p => p.PropertyType == typeof(string)))
        {
            var path = (string)property.GetValue(profile)!;
            Assert.True(Path.IsPathFullyQualified(path));
            ApplicationDataProfile.RequireContained(profile.ApplicationDataDirectory, path);
        }
        Assert.False(Directory.Exists(_root)); // Resolution itself does not initialize or read persisted settings.
    }

    [Fact]
    public void NormalProfileAndItsAncestorsCannotBeSelected()
    {
        var normal = LightflowStorageLocations.CreateDefault().ApplicationDataDirectory;
        foreach (var path in new[] { normal, Path.GetDirectoryName(normal)!, Path.Combine(normal, "test") })
            Assert.Throws<ArgumentException>(() => ApplicationDataProfile.Resolve(["--data-root", path]));
    }

    [Fact]
    public async Task EmptyRootsInitializeIndependentCatalogsPreviewsSettingsAndRecovery()
    {
        var first = Profile();
        var second = Profile("second");
        var resultA = await LightflowStorageCoordinator.StartAsync(profile: first);
        var resultB = await LightflowStorageCoordinator.StartAsync(profile: second);
        await using var a = resultA.Coordinator!;
        await using var b = resultB.Coordinator!;
        Assert.True(resultA.IsReady, resultA.Diagnostic);
        Assert.True(resultB.IsReady, resultB.Diagnostic);
        Assert.NotEqual(a.CatalogSession.Identity.CatalogId, b.CatalogSession.Identity.CatalogId);
        Assert.True(a.PreviewAvailable);
        Assert.True(b.PreviewAvailable);
        Assert.Empty(a.Settings.CameraLutFolder);
        Assert.Empty(a.Settings.CreativeLutFolder);
        Assert.StartsWith(first.ApplicationDataDirectory, a.Settings.ScreengrabDirectory);
        Assert.Empty(a.CatalogBackups);
        Assert.Empty(b.CatalogBackups);
        Assert.StartsWith(first.ApplicationDataDirectory, a.BackupDirectory);
        Assert.StartsWith(second.ApplicationDataDirectory, b.BackupDirectory);
        a.SaveSettings(a.Settings with { ScreengrabDirectory = "only-first-profile" });
        Assert.NotEqual("only-first-profile", AppSettingsStore.Load(second.SettingsPath).ScreengrabDirectory);

        foreach (var profile in new[] { first, second })
        {
            Assert.True(File.Exists(profile.CatalogDatabasePath));
            Assert.True(File.Exists(profile.PreviewsDatabasePath));
            Assert.True(File.Exists(profile.SettingsPath));
            Assert.DoesNotContain("J:\\\\Photography", File.ReadAllText(profile.SettingsPath));
        }
    }

    [Fact]
    public void IsolatedSettingsRoundTripNeverRestoresLegacyMachineLutDefault()
    {
        var profile = Profile();
        var store = new AppSettingsStorageConfigurationStore(profile.SettingsPath, isolated: true);
        Assert.True(store.TryLoad(out var initial, out _));
        Assert.Empty(initial.CameraLutFolder);
        store.Save(initial);
        Assert.True(store.TryLoad(out var restored, out _));
        Assert.Empty(restored.CameraLutFolder);
        Assert.Empty(restored.CreativeLutFolder);
        Assert.Equal(LutCatalog.DefaultFolder, AppSettings.Normalize(new AppSettings(), isolated: false).CameraLutFolder);
    }

    [Fact]
    public async Task PersistedStorageEscapeIsRejectedBeforeOpeningCatalog()
    {
        var profile = Profile();
        ApplicationDataProfile.Initialize(profile);
        var outside = Path.Combine(_root, "must-not-open");
        AppSettingsStore.Save(profile.SettingsPath, new AppSettings { CatalogDirectory = outside });
        var result = await LightflowStorageCoordinator.StartAsync(profile: profile);
        Assert.Equal(StorageStartupStatus.InvalidConfiguration, result.Status);
        Assert.Null(result.Coordinator);
        Assert.False(Directory.Exists(outside));
        Assert.False(File.Exists(profile.CatalogDatabasePath));
        Assert.Throws<ArgumentException>(() => profile.WithOverrides(new(PreviewsDirectory: outside)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Catalog")]
    [InlineData("Previews")]
    public async Task FileInsteadOfDirectoryFailsClosed(string child)
    {
        var profile = Profile();
        var blocked = string.IsNullOrEmpty(child) ? profile.ApplicationDataDirectory : Path.Combine(profile.ApplicationDataDirectory, child);
        Directory.CreateDirectory(Path.GetDirectoryName(blocked)!);
        File.WriteAllText(blocked, "sentinel");
        await Assert.ThrowsAnyAsync<IOException>(() => LightflowStorageCoordinator.StartAsync(profile: profile));
        Assert.Equal("sentinel", File.ReadAllText(blocked));
        Assert.False(File.Exists(profile.SettingsPath));
    }

    [Fact]
    public async Task LockedSettingsFailClosedWithoutCreatingCatalog()
    {
        var profile = Profile();
        ApplicationDataProfile.Initialize(profile);
        AppSettingsStore.Save(profile.SettingsPath, new AppSettings());
        using var locked = new FileStream(profile.SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await LightflowStorageCoordinator.StartAsync(profile: profile);
        Assert.Equal(StorageStartupStatus.InvalidConfiguration, result.Status);
        Assert.Null(result.Coordinator);
        Assert.False(File.Exists(profile.CatalogDatabasePath));
    }

    [Fact]
    public void PackagedSmokeRequiresAnExplicitDisposableProfile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "scripts", "Build-Release.ps1")))
            directory = directory.Parent;
        var script = File.ReadAllText(Path.Combine(directory!.FullName, "scripts", "Build-Release.ps1"));
        Assert.Contains("\"--data-root\", \"`\"$smokeDataRoot`\"\"", script);
        Assert.Contains("Catalog\\LightflowCatalog.db", script);
        Assert.Contains("Remove-Item -LiteralPath $resolvedSmokeRoot", script);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
