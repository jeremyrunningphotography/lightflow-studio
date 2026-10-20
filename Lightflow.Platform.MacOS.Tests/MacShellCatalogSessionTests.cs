using Lightflow.Application;
using Lightflow.Platform.MacOS;
using LightflowStudio;
using Xunit;

namespace Lightflow.Platform.MacOS.Tests;

[Collection("Native storage descriptor ownership")]
public sealed class MacShellCatalogSessionTests : IDisposable
{
    private readonly string _root = Path.Combine("/private/tmp", "lf024-shell-" + Guid.NewGuid().ToString("N"));
    private string Prepared(string name) { var path = Path.Combine(_root, name); Directory.CreateDirectory(path); return path; }
    [Fact]
    public async Task FirstRunPreparedCreationDeepReopenAndAuthoredState()
    {
        var directory = Prepared("Selected Catalog");
        Guid? identity;
        await using (var shell = new MacShellCatalogSession(_root))
        {
            var first = await shell.StartAsync(); Assert.Equal(StorageStartupStatus.FirstRunChoiceRequired, first.Status);
            Assert.False(File.Exists(Path.Combine(directory, CatalogLayout.CatalogFileName)));
            var ready = await shell.CreateAsync(directory); Assert.True(ready.IsReady, ready.Diagnostic);
            identity = ready.CatalogId; Assert.NotNull(identity); Assert.Equal(directory, ready.Directory);
        }
        var result = await new CatalogLifecycle(new MacCatalogLifecycleEffects(_root)).StartAsync(allowFirstCreation:false);
        Assert.True(result.IsReady, result.Diagnostic);
        await using (var owner = result.Coordinator!)
            await new CatalogCollectionOrganizationStore(() => owner.Session).CreateCollectionAsync("LF024 authored state");
        await using (var shell = new MacShellCatalogSession(_root))
        {
            var reopened = await shell.StartAsync(); Assert.True(reopened.IsReady, reopened.Diagnostic); Assert.Equal(identity, reopened.CatalogId);
            Assert.True((await shell.VerifyAsync()).IsReady);
        }
        var final = await new CatalogLifecycle(new MacCatalogLifecycleEffects(_root)).StartAsync(allowFirstCreation:false);
        await using (var owner = final.Coordinator!)
        {
            Assert.False(owner.CatalogEvidence!.KnownClean);
            Assert.Equal("LF024 authored state", Assert.Single(await new CatalogCollectionOrganizationStore(() => owner.Session).ListCollectionsAsync()).Name);
            Assert.False(File.Exists(owner.Locations.PreviewsDatabasePath));
        }
        Assert.Equal("Dirty", File.ReadAllText(StartupSessionCompletion.PathFor(_root)));
    }
    [Fact]
    public async Task ExplicitExistingSelectionRetainsIdentityAndFailedSelectionRetainsConfiguration()
    {
        var first = Prepared("First"); var second = Prepared("Second");
        await using var shell = new MacShellCatalogSession(_root);
        await shell.StartAsync();
        var a = await shell.CreateAsync(first); Assert.True(a.IsReady, a.Diagnostic);
        var b = await shell.CreateAsync(second); Assert.True(b.IsReady, b.Diagnostic); Assert.NotEqual(a.CatalogId,b.CatalogId);
        var selected = await shell.OpenAsync(first); Assert.True(selected.IsReady, selected.Diagnostic); Assert.Equal(a.CatalogId, selected.CatalogId);
        var configuration = File.ReadAllBytes(Path.Combine(_root,"settings.json"));
        var refused = await shell.OpenAsync(Prepared("Invalid")); Assert.False(refused.IsReady);
        Assert.Equal(configuration,File.ReadAllBytes(Path.Combine(_root,"settings.json")));
        Assert.Equal(a.CatalogId,(await shell.StartAsync()).CatalogId);
    }
    [Fact]
    public async Task MissingCatalogRetainsIdentityAndNeverCreatesReplacement()
    {
        var directory = Prepared("Catalog");
        await using var shell = new MacShellCatalogSession(_root);
        await shell.StartAsync(); Assert.True((await shell.CreateAsync(directory)).IsReady);
        await shell.DisposeAsync();
        var database = Path.Combine(directory,CatalogLayout.CatalogFileName); File.Move(database,database+".retained");
        await using var reopened = new MacShellCatalogSession(_root);
        Assert.False((await reopened.StartAsync()).IsReady); Assert.False(File.Exists(database)); Assert.True(File.Exists(database+".retained"));
    }
    [Fact]
    public async Task InvalidCatalogAndOccupiedCreationRefuseWithoutReplacingBytes()
    {
        var directory=Prepared("Invalid"); var path=Path.Combine(directory,CatalogLayout.CatalogFileName); File.WriteAllText(path,"not a Catalog");
        await using var shell=new MacShellCatalogSession(_root); await shell.StartAsync();
        Assert.False((await shell.OpenAsync(directory)).IsReady); Assert.Equal("not a Catalog",File.ReadAllText(path));
        Assert.False((await shell.CreateAsync(directory)).IsReady); Assert.Equal("not a Catalog",File.ReadAllText(path));
    }
    [Fact]
    public async Task RecoveryCannotBeBypassedByChoosingOrCreatingAnotherCatalog()
    {
        var directory=Prepared("Catalog");
        await using(var shell=new MacShellCatalogSession(_root)) { await shell.StartAsync(); Assert.True((await shell.CreateAsync(directory)).IsReady); }
        var journal=Path.Combine(_root,"Catalog Transfers",Guid.NewGuid().ToString("N")+".journal");File.WriteAllBytes(journal,[7,0,0]);
        await using var recovery=new MacShellCatalogSession(_root);
        Assert.Equal(CatalogRecoveryReason.PendingRecovery,(await recovery.StartAsync()).Reason);
        Assert.Equal(CatalogRecoveryReason.PendingRecovery,(await recovery.CreateAsync(Path.Combine(_root,"Missing"))).Reason);
        var alternate=Prepared("Alternate");
        Assert.Equal(CatalogRecoveryReason.PendingRecovery,(await recovery.CreateAsync(alternate)).Reason);
        Assert.Equal(CatalogRecoveryReason.PendingRecovery,(await recovery.OpenAsync(directory)).Reason);
        Assert.Empty(Directory.EnumerateFileSystemEntries(alternate)); Assert.Equal(new byte[]{7,0,0},File.ReadAllBytes(journal));
    }
    [Fact]
    public async Task WriterRefusalAndRepeatedCloseReleaseOwnership()
    {
        var directory=Prepared("Catalog");
        var first=new MacShellCatalogSession(_root); await first.StartAsync(); Assert.True((await first.CreateAsync(directory)).IsReady);
        await using(var second=new MacShellCatalogSession(_root)) Assert.False((await second.StartAsync()).IsReady);
        await Task.WhenAll(first.DisposeAsync().AsTask(),first.DisposeAsync().AsTask());
        await using var reopened=new MacShellCatalogSession(_root); Assert.True((await reopened.StartAsync()).IsReady);
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>first.StartAsync());
    }
    [Fact]
    public async Task MissingPreparationOutsideRootAndLinkedStorageRefuse()
    {
        Prepared("Catalog"); await using var shell=new MacShellCatalogSession(_root); await shell.StartAsync();
        var missing=Path.Combine(_root,"Missing");Assert.False((await shell.CreateAsync(missing)).IsReady);Assert.False(Directory.Exists(missing));
        Assert.False((await shell.CreateAsync("/private/tmp")).IsReady);
        var target=Prepared("Target");var link=Path.Combine(_root,"Linked");Directory.CreateSymbolicLink(link,target);
        Assert.False((await shell.CreateAsync(link)).IsReady);Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }
    [Fact]
    public async Task ActiveCatalogLossRemovesReadiness()
    {
        var directory=Prepared("Catalog");await using var shell=new MacShellCatalogSession(_root);await shell.StartAsync();Assert.True((await shell.CreateAsync(directory)).IsReady);
        var path=Path.Combine(directory,CatalogLayout.CatalogFileName);File.Move(path,path+".retained");
        Assert.False((await shell.VerifyAsync()).IsReady);Assert.False(File.Exists(path));
    }
    public void Dispose() { if(Directory.Exists(_root))Directory.Delete(_root,true); }
}
