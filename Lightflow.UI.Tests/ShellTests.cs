using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Lightflow.Application;
using Lightflow.UI;
using LightflowStudio;
using Xunit;
[assembly: AvaloniaTestApplication(typeof(Lightflow.UI.Tests.TestBootstrap))]
namespace Lightflow.UI.Tests;
public static class TestBootstrap
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
public sealed class TestApp : Avalonia.Application { public override void Initialize() => Styles.Add(new FluentTheme()); }
public sealed class ShellTests
{
    [AvaloniaFact]
    public async Task RealWindowNeverShowsWorkspaceUntilAuthoritativeReadyAndRevokesItOnLoss()
    {
        var service=new TestSession();var model=new ShellViewModel(service);var window=new ShellWindow(model);window.Show();
        var workspace=window.GetVisualDescendants().OfType<StackPanel>().Single(c=>AutomationProperties.GetAutomationId(c)=="ReadyWorkspace");
        Assert.False(workspace.IsVisible);Assert.True(model.IsBusy);
        service.Pending.SetResult(new(StorageStartupStatus.Ready,Directory:"/task/Catalog",CatalogId:Guid.NewGuid(),SchemaVersion:19));
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(()=>{},Avalonia.Threading.DispatcherPriority.Background);
        Assert.True(workspace.IsVisible);
        service.Verification=new(StorageStartupStatus.CatalogUnavailable,CatalogRecoveryReason.LocationUnavailable);
        await model.VerifyAsync();Assert.False(workspace.IsVisible);
        window.Close();await model.CloseAsync();Assert.Equal(1,service.Closes);
    }
    [Theory]
    [InlineData(CatalogRecoveryReason.UnsupportedStorage,"Catalog location not supported")]
    [InlineData(CatalogRecoveryReason.PendingRecovery,"Catalog needs attention")]
    [InlineData(CatalogRecoveryReason.CompetingOwner,"Catalog already in use")]
    public async Task FailedLifecycleResultGatesWorkspaceAndUsesPlainLanguage(CatalogRecoveryReason reason,string title)
    {
        var service=new TestSession();service.Pending.SetResult(new(StorageStartupStatus.CatalogUnavailable,reason,"native diagnostic"));
        var model=new ShellViewModel(service);await model.StartAsync();
        Assert.False(model.WorkspaceAvailable);Assert.Equal(title,model.Title);Assert.DoesNotContain("lease",model.Message);
        Assert.Equal(reason!=CatalogRecoveryReason.PendingRecovery,model.CanChoose);
    }
    [Fact]
    public async Task BusyAndClosingBlockDuplicateRequestsAndWorkspace()
    {
        var service=new TestSession();var model=new ShellViewModel(service);var pending=model.StartAsync();
        await model.OpenAsync("/task");Assert.Equal(0,service.Opens);Assert.False(model.WorkspaceAvailable);
        service.Pending.SetResult(new(StorageStartupStatus.Ready));await pending;Assert.True(model.WorkspaceAvailable);
        await model.CloseAsync();Assert.False(model.WorkspaceAvailable);Assert.False(model.CanChoose);
        await model.StartAsync();Assert.Equal(1,service.Starts);
    }
    [Fact]
    public async Task LateReadinessCheckCannotOverwriteNewerCatalogSelection()
    {
        var service=new TestSession();service.Pending.SetResult(new(StorageStartupStatus.Ready));
        var model=new ShellViewModel(service);await model.StartAsync();
        service.DelayedVerification=new();
        var oldCheck=model.VerifyAsync();
        await model.OpenAsync("/task/new");
        service.DelayedVerification.SetResult(new(StorageStartupStatus.CatalogUnavailable));
        await oldCheck;
        Assert.True(model.WorkspaceAvailable);
    }
    private sealed class TestSession : IShellCatalogSession
    {
        public string DataRoot=>"/task";
        public TaskCompletionSource<ShellCatalogSnapshot> Pending {get;}=new();
        public ShellCatalogSnapshot Verification=new(StorageStartupStatus.Ready);
        public TaskCompletionSource<ShellCatalogSnapshot>? DelayedVerification;
        public int Starts,Opens,Closes;
        public Task<ShellCatalogSnapshot> StartAsync(){Starts++;return Pending.Task;}
        public Task<ShellCatalogSnapshot> OpenAsync(string directory){Opens++;return Pending.Task;}
        public Task<ShellCatalogSnapshot> CreateAsync(string directory)=>Pending.Task;
        public Task<ShellCatalogSnapshot> VerifyAsync()=>DelayedVerification?.Task ?? Task.FromResult(Verification);
        public ValueTask DisposeAsync(){Closes++;return ValueTask.CompletedTask;}
    }
}
