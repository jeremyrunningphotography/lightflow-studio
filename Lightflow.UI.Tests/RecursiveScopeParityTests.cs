using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightflow.Application;
using Lightflow.UI;
using LightflowStudio;
using Xunit;

namespace Lightflow.UI.Tests;
public sealed class RecursiveScopeParityTests
{
    [Fact]
    public async Task ScopeUsesStoredRootsAndMaterializedIdentityWithoutScanningOrStalePublication()
    {
        var service=new ScopeSession();service.Scopes=[new(Guid.NewGuid(),service.Root,"A")];
        var model=new BrowserViewModel(service);await model.OpenAsync(service.Catalog);
        Assert.False(model.IsRecursiveScope(new(service.Root,"","root")));
        Assert.True(model.IsRecursiveScope(new(service.Root,"A","A")));
        Assert.True(model.IsRecursiveScope(new(service.Root,"A/Leaf","Leaf")));
        Assert.False(model.IsRecursiveScope(new(service.Root,"Another","Another")));
        Assert.False(model.IsRecursiveScope(new(Guid.NewGuid(),"A","other root")));
        Assert.True(model.IsRecursiveScope(new(Guid.Empty,"","path alias",AbsolutePath:"/task/A/Leaf")));
        var reads=service.Discoveries;
        for(var i=0;i<50;i++)Assert.True(model.IsFolderIconFilled(new(service.Root,"A","A")));
        Assert.Equal(reads,service.Discoveries);
        service.Delay=true;var old=model.NavigateAsync(new(service.Root,"A","A"));var pending=service.Pending!;
        service.Delay=false;service.Scopes=[];await model.NavigateAsync(new(service.Root,"Other","Other"));
        pending.SetResult();await old;
        Assert.Equal("Other",model.Location!.RelativeFolder);Assert.False(model.IsRecursiveScope(new(service.Root,"A","A")));
        service.Scopes=[new(Guid.NewGuid(),service.Root,"A")];await model.RefreshAsync();
        Assert.True(model.IsRecursiveScope(new(service.Root,"A/Leaf","Leaf")));Assert.False(model.Recursive);
        model.Suspend();Assert.False(model.IsRecursiveScope(new(service.Root,"A","A")));
    }

    [AvaloniaFact]
    public async Task FilledTreeAndLabeledToggleTrackInheritanceImmediatelyWithStableRowsAndKeyboard()
    {
        var service=new ScopeSession();service.Scopes=[new(Guid.NewGuid(),service.Root,"A")];
        var model=new BrowserViewModel(service);await model.OpenAsync(service.Catalog);
        var pane=new BrowserPane(model);var window=new Window{Content=pane,Width=1120,Height=720};LightflowTheme.Apply(window);window.Show();
        try
        {
            await Drain(()=>Buttons(pane).Any(b=>AutomationProperties.GetName(b)=="Expand root"));
            Click(Button(pane,"Expand root"));await Drain(()=>Buttons(pane).Any(b=>AutomationProperties.GetName(b)=="A"));
            var a=Button(pane,"A");var aContent=a.Content;var icon=Icon(a);
            Assert.Contains("active",icon.Classes);Assert.DoesNotContain("active",Icon(Button(pane,"root")).Classes);
            await model.NavigateAsync(new(service.Root,"Other","Other"));
            Assert.Contains("active",icon.Classes);Assert.Contains("active",Icon(Button(pane,"Other")).Classes);
            Click(Button(pane,"Expand A"));await Drain(()=>Buttons(pane).Any(b=>AutomationProperties.GetName(b)=="Leaf"));
            Assert.Contains("active",Icon(Button(pane,"Leaf")).Classes);
            await model.NavigateAsync(new(service.Root,"A/Leaf","Leaf"));
            var toggle=Assert.IsType<ToggleButton>(Button(pane,"Include Subfolders"));
            Assert.True(toggle.IsChecked);Assert.True(toggle.IsEnabled);Assert.Contains("Subfolders",toggle.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text));
            Assert.Contains("recursive scope",AutomationProperties.GetHelpText(Button(pane,"Leaf")));
            service.Delay=true;var off=model.ToggleRecursiveAsync();
            Assert.False(toggle.IsChecked);Assert.DoesNotContain("active",icon.Classes);
            Assert.Contains("active",Icon(Button(pane,"Leaf")).Classes); // Selected direct leaf stays filled.
            Assert.Same(aContent,a.Content);Assert.Same(icon,Icon(a));
            service.Pending!.SetResult();await off;
            Assert.False(toggle.IsEnabled);Assert.Equal("No subfolders",ToolTip.GetTip(toggle));
            service.Delay=false;await model.NavigateAsync(new(service.Root,"A","A"));Assert.True(toggle.IsEnabled);
            Assert.True(toggle.Focus(NavigationMethod.Tab));
            window.KeyPressQwerty(PhysicalKey.Space,0);window.KeyReleaseQwerty(PhysicalKey.Space,0);
            await Drain(()=>model.Recursive && !model.IsLoading);
            Assert.True(toggle.IsChecked);Assert.Contains("active",icon.Classes);Assert.Same(icon,Icon(a));
            await model.RefreshAsync();await Drain(()=>Buttons(pane).Any(b=>AutomationProperties.GetName(b)=="Leaf"));
            Assert.Contains("active",Icon(Button(pane,"Leaf")).Classes);Assert.True(toggle.IsChecked);
            Assert.Equal(window.FindResource("LightflowFocus"),icon.Fill);
            window.Width=720;window.UpdateLayout();Assert.True(toggle.Bounds.Width>70);Assert.True(toggle.Bounds.Height>=32);
            Assert.True(toggle.Bounds.Right<=pane.Bounds.Width);Assert.True(toggle.Focusable);
        }
        finally {window.Close();model.Suspend();}
    }
    private static IEnumerable<Button> Buttons(Control pane)=>pane.GetVisualDescendants().OfType<Button>();
    private static Button Button(Control pane,string name)=>Buttons(pane).Single(b=>AutomationProperties.GetName(b)==name);
    private static Avalonia.Controls.Shapes.Path Icon(Button button)=>button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
    private static void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static async Task Drain(Func<bool> ready){var end=DateTime.UtcNow.AddSeconds(5);while(!ready()){Dispatcher.UIThread.RunJobs();Assert.True(DateTime.UtcNow<end);await Task.Delay(5);}Dispatcher.UIThread.RunJobs();}

    private sealed class ScopeSession:IBrowserDiscoverySession,IBrowserRecursiveScopeSession
    {
        public Guid Catalog=Guid.NewGuid(),Root=Guid.NewGuid();public List<BrowserRecursiveRoot> Scopes=[];public bool Delay;public TaskCompletionSource? Pending;public int Discoveries;
        private IReadOnlyList<MediaRootInfo> Roots=>[new(Root,"root","/task",MediaRootAvailability.Online)];
        public Task<BrowserCatalogSnapshot> ReadBrowserCatalogAsync(Guid? rootId=null,string folder="",bool recursive=false,CancellationToken cancellationToken=default)=>Task.FromResult(new BrowserCatalogSnapshot(Catalog,Roots,[],Scopes.ToArray()));
        private MediaFolderEntry Entry(string folder)=>new(Root,folder,folder.ToUpperInvariant(),folder.Split('/')[^1],true,MediaTypeClassification.Unknown,null,default);
        private IReadOnlyList<MediaFolderEntry> Children(string folder)=>folder switch{""=>[Entry("A"),Entry("Other")],"A"=>[Entry("A/Leaf")],_=>[]};
        public Task<MediaFolderEnumerationResult> ReadBrowserFoldersAsync(Guid rootId,string folder,CancellationToken token=default)=>Task.FromResult(new MediaFolderEnumerationResult(MediaFolderEnumerationStatus.Succeeded,folder,Children(folder)));
        public Task<BrowserFolderSnapshot> ReadBrowserFolderAsync(Guid rootId,string folder,CancellationToken token=default)=>DiscoverBrowserFolderAsync(null,rootId,folder,token);
        public async Task<BrowserFolderSnapshot> DiscoverBrowserFolderAsync(string? path,Guid rootId,string folder,CancellationToken token=default)
        {
            Discoveries++;var scopes=Scopes.ToArray();var snapshot=new BrowserFolderSnapshot(Root,folder,BrowserRecursiveRootLogic.IsEffectivelyRecursive(scopes,Root,folder),MediaFolderEnumerationStatus.Succeeded,Children(folder),[],RecursiveRoots:scopes,CatalogRoots:Roots);
            if(Delay){var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);Pending=pending;await pending.Task;}return snapshot;
        }
        public Task SetBrowserRecursiveAsync(Guid rootId,string folder,bool enabled,CancellationToken token=default)
        {
            if(enabled){Scopes.RemoveAll(r=>r.RootId==rootId && BrowserScope.IsWithinFolderScope(r.RelativeFolder,folder));Scopes.Add(new(Guid.NewGuid(),rootId,folder));}
            else{var roots=BrowserRecursiveRootLogic.GoverningRoots(Scopes,rootId,folder);Scopes.RemoveAll(roots.Contains);}return Task.CompletedTask;
        }
        public Task<IReadOnlyList<BrowserRecursiveRoot>> ReadBrowserRecursiveRootsAsync(CancellationToken token=default)=>Task.FromResult<IReadOnlyList<BrowserRecursiveRoot>>(Scopes.ToArray());
        public Task<bool> IsBrowserSourceAvailableAsync(Guid rootId,Guid assetId,CancellationToken token=default)=>Task.FromResult(true);
    }
}
