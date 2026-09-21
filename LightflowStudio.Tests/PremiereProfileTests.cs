using System.Net;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("Premiere bridge")]
public sealed class PremiereProfileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lightflow-premiere-profile-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PairingProtectsCredentialsWithoutRequiringPermissionToChangeOwner()
    {
        var profile = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(_root, "restricted")]);
        ApplicationDataProfile.Initialize(profile);
        var directory = Directory.CreateDirectory(profile.PremierePairingDirectory);
        var user = WindowsIdentity.GetCurrent().User!;
        var owner = directory.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        Assert.Equal(user, owner);
        // Match an ordinary user-owned workspace granting Modify, not WRITE_OWNER.
        var limited = new DirectorySecurity();
        limited.SetAccessRuleProtection(true, false);
        limited.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(limited);
        await using var bridge = new PremiereBridge(new CatalogPremiereHandoffs(() => null), profile);
        await bridge.StartAsync();
        Assert.Equal(PremiereConnectionState.Ready, bridge.Connection.State);
        var secured = directory.GetAccessControl();
        Assert.Equal(owner, secured.GetOwner(typeof(SecurityIdentifier)));
        Assert.True(secured.AreAccessRulesProtected);
        var rule = Assert.Single(secured.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>());
        Assert.Equal(user, rule.IdentityReference);
        Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
        Assert.False(rule.IsInherited);
        var pairing = new FileInfo(Path.Combine(directory.FullName, "lightflow-pairing.json"));
        Assert.True(pairing.Exists);
        Assert.All(pairing.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
            fileRule => Assert.Equal(user, fileRule.IdentityReference));
        Assert.True(Authenticates(bridge, Authorization(await File.ReadAllBytesAsync(pairing.FullName))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfilesOwnCatalogPairingAndHandoffsAndNeverAdoptOtherListener(bool firstIsIsolated)
    {
        // The non-isolated case models normal-profile behavior entirely in disposable storage.
        var first = firstIsIsolated
            ? ApplicationDataProfile.Resolve(["--data-root", Path.Combine(_root, "first")])
            : LightflowStorageLocations.Create(Path.Combine(_root, "normal"));
        var second = ApplicationDataProfile.Resolve(["--data-root", Path.Combine(_root, "second")]);
        await using var a = (await LightflowStorageCoordinator.StartAsync(profile: first)).Coordinator!;
        await using var b = (await LightflowStorageCoordinator.StartAsync(profile: second)).Coordinator!;
        Assert.NotNull(a); Assert.NotNull(b);
        a.SaveSettings(a.Settings with { DefaultVideoFolder = "first-only" });
        Assert.NotEqual("first-only", b.Settings.DefaultVideoFolder);
        Assert.NotEqual(a.CatalogSession.Identity.CatalogId, b.CatalogSession.Identity.CatalogId);
        var journalA = new CatalogPremiereHandoffs(() => a.CatalogSession);
        var journalB = new CatalogPremiereHandoffs(() => b.CatalogSession);
        var media = Path.Combine(_root, "fixture.mov");
        await File.WriteAllTextAsync(media, "synthetic fixture");
        var sourceA = await RegisterSource(a.CatalogSession, media);
        var sourceB = await RegisterSource(b.CatalogSession, media);
        var project = new PremiereProject("fixture-project", Path.Combine(_root, "fixture.prproj"), "fixture");
        var handoffA = await journalA.PrepareAsync(project, "root", null, sourceA);
        Assert.Empty(await journalB.ListAsync());
        var handoffB = await journalB.PrepareAsync(project, "root", null, sourceB);
        Assert.NotEqual(handoffA.Intent.CatalogId, handoffB.Intent.CatalogId);
        Assert.NotEqual(handoffA.Intent.OperationId, handoffB.Intent.OperationId);
        Assert.Equal(handoffA.Intent.OperationId, Assert.Single(await journalA.ListAsync()).Intent.OperationId);
        await using var bridgeA = new PremiereBridge(journalA, a.Locations);
        await using var bridgeB = new PremiereBridge(journalB, b.Locations);
        await Task.WhenAll(bridgeA.StartAsync(), bridgeA.StartAsync());
        var pairingA = Path.Combine(first.PremierePairingDirectory, "lightflow-pairing.json");
        var pairingB = Path.Combine(second.PremierePairingDirectory, "lightflow-pairing.json");
        var bytesA = await File.ReadAllBytesAsync(pairingA);
        var tokenA = Authorization(bytesA);
        Assert.Equal(first.PremierePairingDirectory, bridgeA.PairingDirectory);
        Assert.Equal(second.PremierePairingDirectory, bridgeB.PairingDirectory);
        Assert.True(Authenticates(bridgeA, tokenA));
        Assert.NotNull(await Record.ExceptionAsync(bridgeB.StartAsync));
        Assert.NotNull(await Record.ExceptionAsync(bridgeB.RotatePairingAsync));
        Assert.Equal(PremiereConnectionState.ConnectionProblem, bridgeB.Connection.State);
        Assert.Contains("Refresh Connection", bridgeB.Connection.Message);
        Assert.False(File.Exists(pairingB)); // No credentials for the other profile's listener.
        Assert.Equal(bytesA, await File.ReadAllBytesAsync(pairingA));
        Assert.True(Authenticates(bridgeA, tokenA));
        await bridgeA.DisposeAsync();
        await bridgeB.StartAsync(); // Explicit retry after the other owner exits.
        var tokenB = Authorization(await File.ReadAllBytesAsync(pairingB));
        Assert.True(Authenticates(bridgeB, tokenB));
        Assert.False(Authenticates(bridgeB, tokenA));
        Assert.False(Authenticates(bridgeA, tokenB));
        Assert.Equal(PremiereSendRoute.Settings, PremiereSendState.Route(bridgeB.Connection));
        await bridgeB.RotatePairingAsync();
        Assert.False(Authenticates(bridgeB, tokenB));
        Assert.True(Authenticates(bridgeB, Authorization(await File.ReadAllBytesAsync(pairingB))));
        Assert.Equal(bytesA, await File.ReadAllBytesAsync(pairingA));
        Assert.Equal(handoffA.Intent.OperationId, Assert.Single(await journalA.ListAsync()).Intent.OperationId);
        Assert.Equal(handoffB.Intent.OperationId, Assert.Single(await journalB.ListAsync()).Intent.OperationId);
        Assert.Equal("first-only", AppSettingsStore.Load(first.SettingsPath).DefaultVideoFolder);
    }

    private static string Authorization(byte[] json)
    {
        using var doc = JsonDocument.Parse(json);
        return "Bearer " + doc.RootElement.GetProperty("token").GetString();
    }
    private sealed class Machine : IMachineIdentityProvider { public string GetMachineId() => "profile-test"; }
    private static async Task<PremiereSource> RegisterSource(CatalogDatabaseSession session, string path)
    {
        var roots = new MediaRootService(() => session, new Machine(), new MediaRootFileSystem());
        var root = (await roots.CreateAsync("Fixture", Path.GetDirectoryName(path)!)).Root!;
        var assets = new MediaAssetService(new CatalogMediaAssetRepository(() => session), roots, new SampledSourceFingerprintService());
        var asset = (await assets.CreateAsync(root.RootId, Path.GetFileName(path), "video")).Asset!.Asset;
        return new(asset.AssetId, path, asset.FileSizeBytes.ToString(), asset.LastWriteUtcTicks.ToString());
    }
    private static bool Authenticates(PremiereBridge bridge, string token) =>
        bridge.Authenticate($"localhost:{PremiereProtocol.Port}", token, false, IPAddress.Loopback);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
