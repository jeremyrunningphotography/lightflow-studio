using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
namespace Lightflow.UI;

public sealed partial class BrowserPane
{
    private Task _treeRefresh=Task.CompletedTask;
    private CancellationTokenSource? _treeReveal;
    private string? _treeRevealKey, _treeRevealDiagnostic, _suppressedRestoreReveal;
    private void UpdateBrowserMessage()
    {
        _message.Text=_model.Message+(_treeRevealDiagnostic is null ? "" : " · "+_treeRevealDiagnostic);
        _message.IsVisible=!string.IsNullOrEmpty(_message.Text);ToolTip.SetTip(_message,_message.Text);
    }
    private void SetTreeRevealDiagnostic(string? diagnostic) { _treeRevealDiagnostic=diagnostic;UpdateBrowserMessage(); }
    private void CancelRestoredFolderRevealForUser()
    {
        if(_model.RestoredLocation is { } restored)_suppressedRestoreReveal=$"{_model.Epoch}/{restored.Key}";
        _treeRevealKey=null;_treeReveal?.Cancel();SetTreeRevealDiagnostic(null);
    }
    private void RequestRestoredFolderReveal()
    {
        if(_model.RestoredLocation is not { } restored || !_model.IsAvailable)
        { _treeReveal?.Cancel();_treeRevealKey=null;_suppressedRestoreReveal=null;if(_treeRevealDiagnostic is not null)SetTreeRevealDiagnostic(null);return; }
        if(_model.IsLoading || _suppressedRestoreReveal==$"{_model.Epoch}/{restored.Key}")return;
        var key=$"{_model.Epoch}/{_rootGeneration}/{restored.Key}";
        if(_treeRevealKey==key)return;
        SetTreeRevealDiagnostic(null);_treeRevealKey=key;_treeReveal?.Cancel();_treeReveal?.Dispose();
        var cancellation=_treeReveal=new();cancellation.CancelAfter(TimeSpan.FromSeconds(5));
        _=RevealRestoredFolderAsync(restored,key,cancellation.Token);
    }
    private async Task RevealRestoredFolderAsync(BrowserFolderNode restored,string request,CancellationToken token)
    {
        bool IdentityCurrent()=>_treeRevealKey==request && _model.RestoredLocation==restored && _model.Location?.Key==restored.Key;
        bool Current()=>!token.IsCancellationRequested && IdentityCurrent();
        void Incomplete() {if(IdentityCurrent())SetTreeRevealDiagnostic("Locations path could not be fully revealed. Expand its location to retry.");}
        static bool Contains(BrowserFolderNode ancestor,BrowserFolderNode target) =>
            ancestor.AbsolutePath is { } path && target.AbsolutePath is { } destination
                ? destination==path || destination.StartsWith(path.TrimEnd('/')+"/",StringComparison.Ordinal)
                : ancestor.RootId==target.RootId && (ancestor.RelativeFolder.Length==0 || ancestor.RelativeFolder==target.RelativeFolder || target.RelativeFolder.StartsWith(ancestor.RelativeFolder+"/",StringComparison.Ordinal));
        try
        {
            await _treeRefresh.WaitAsync(token);
            if(!Current())return;
            // Prefer the most specific existing favorite/volume/share, never expand unrelated branches.
            var row=_rows.Values.Where(r=>r.TreeKey==">"+r.Node.Key && r.Node.Availability.Length==0 && Contains(r.Node,restored))
                .OrderByDescending(r=>(r.Node.AbsolutePath ?? r.Node.RelativeFolder).Length).FirstOrDefault();
            if(row is null){Incomplete();return;}
            if(row.Node.Server is { } server && _groups.TryGetValue("server:"+server,out var group))group.Reveal?.Invoke();
            for(var depth=0;depth<128 && Current();depth++)
            {
                if(row.Node.Key==restored.Key)
                {
                    SetFolderContent(row);
                    var selected=row;
                    Dispatcher.UIThread.Post(()=> { if(Current() && selected.Choose.IsEffectivelyVisible)selected.Choose.BringIntoView(); },DispatcherPriority.Loaded);
                    return;
                }
                row.Expanded=true;row.Children.IsVisible=true;SetDisclosure(row);
                // Existing directory-only, contained expansion; never user navigation, discovery, or hydration.
                await LoadRowAsync(row,revealing:true,token:token).WaitAsync(token);
                if(!Current())return;
                ShowFolderChildren(row,allowPrefetch:false);
                var child=row.Folders.Where(n=>n.Availability.Length==0 && Contains(n,restored)).OrderByDescending(n=>(n.AbsolutePath ?? n.RelativeFolder).Length).FirstOrDefault();
                if(child is null){Incomplete();return;}
                row=Folder(child,row.TreeKey,allowPrefetch:false);
            }
            Incomplete();
        }
        catch(OperationCanceledException) { Incomplete(); }
        catch(Exception error) when(error is IOException or InvalidOperationException or UnauthorizedAccessException)
        { Incomplete();Console.Error.WriteLine($"Restored Locations presentation could not be revealed: {error.Message}"); }
    }
}
