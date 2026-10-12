using LightflowStudio;
using Xunit;
using Xunit.Abstractions;
using System.Runtime.InteropServices;

namespace LightflowStudio.Tests;

public sealed class CatalogWriterOwnershipTests(ITestOutputHelper output)
{
    [Fact]
    public void HardLinkedOwnershipJournal_RefusesBeforeWritingRetainedBytes()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "lf012-native-lease", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var retained = Path.Combine(root, "retained-evidence");
        var bytes = "Synthetic evidence must remain unchanged"u8.ToArray();
        File.WriteAllBytes(retained, bytes);
        var database = Path.Combine(root, LightflowStorageLocations.CatalogFileName);
        Assert.True(CreateHardLinkW(StartupStoreEvidence.JournalPath(database), retained, IntPtr.Zero),
            "Task-owned native hard-link creation failed: " + Marshal.GetLastWin32Error());
        Assert.Throws<IOException>(() => StartupStoreEvidence.Begin(database, "Catalog", false));
        Assert.Equal(bytes, File.ReadAllBytes(retained));
        Assert.False(File.Exists(database));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string path, string existing, IntPtr security);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DistinctProfileOwners_RefuseSecondCatalogWriter(bool moveCatalog)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "artifacts", "lf012-writer-probe", Guid.NewGuid().ToString("N")));
        var outer = ApplicationDataProfile.Resolve(["--data-root", root]);
        var inner = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(root, "second-profile")]);
        using var firstInstance = new WindowsApplicationInstanceCoordinator(ApplicationDataProfile.InstanceIdentity(outer));
        using var secondInstance = new WindowsApplicationInstanceCoordinator(ApplicationDataProfile.InstanceIdentity(inner));
        Assert.Equal(ApplicationInstanceStatus.Primary, firstInstance.StartOrSignal(ApplicationLaunchRequest.Current([])).Status);
        Assert.Equal(ApplicationInstanceStatus.Primary, secondInstance.StartOrSignal(ApplicationLaunchRequest.Current([])).Status);
        var firstStartup = await LightflowStorageCoordinator.StartAsync(profile: inner);
        Assert.True(firstStartup.IsReady, firstStartup.Diagnostic);
        await using var first = firstStartup.Coordinator!;
        if (moveCatalog)
        {
            var move = await first.RelocateCatalogAsync(Path.Combine(inner.ApplicationDataDirectory, "moved-catalog"));
            Assert.True(move.Succeeded, move.Diagnostic);
        }
        ApplicationDataProfile.Initialize(outer);
        AppSettingsStore.Save(outer.SettingsPath, new AppSettings
        {
            CatalogDirectory = first.Locations.CatalogDirectory,
            CatalogId = first.CatalogSession.Identity.CatalogId,
            CameraLutFolder = "", CreativeLutFolder = ""
        }, isolated: true);
        var secondStartup = await LightflowStorageCoordinator.StartAsync(profile: outer);
        Assert.False(secondStartup.IsReady);
        Assert.Equal(CatalogRecoveryReason.CompetingOwner, secondStartup.RecoveryReason);
        await using var second = secondStartup.Coordinator!;
        Assert.False(second.CatalogAvailable);
        Assert.Contains("ownership", secondStartup.Diagnostic!, StringComparison.OrdinalIgnoreCase);
        var a = await first.Collections.CreateCollectionAsync("LF012 first active owner");
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.Collections.CreateCollectionAsync("Must refuse"));
        Assert.NotNull(await first.Collections.GetCollectionAsync(a.CollectionId));
        output.WriteLine("Second profile refused before SQLite activation; the first owner can still author.");
        output.WriteLine("Synthetic task-owned root: " + root);
        output.WriteLine("CatalogId: " + first.CatalogSession.Identity.CatalogId);
        first.PreventCleanShutdown(); second.PreventCleanShutdown();
    }
}
