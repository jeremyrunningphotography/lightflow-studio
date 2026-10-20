using System.ComponentModel;
using Lightflow.Application;
using LightflowStudio;

namespace Lightflow.UI;

public sealed record BrowserFolderNode(Guid RootId, string RelativeFolder, string Name, string Availability = "");

/// <summary>Shared transient presentation. Catalog/Folder services own data; extracted selection owns keys.</summary>
public sealed class BrowserViewModel(IBrowserFolderSession session) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _navigation, _selectionRead;
    private long _epoch, _generation, _selectionGeneration;
    private Guid? _catalogId;
    public bool CanRefresh => _catalogId is not null;
    private readonly BrowserGridSelection _selection = new();
    public IReadOnlyList<BrowserFolderNode> Roots { get; private set; } = [];
    public IReadOnlyList<MediaAsset> Assets { get; private set; } = [];
    public BrowserFolderNode? Location { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsAvailable { get; private set; }
    public bool Recursive { get; private set; }
    public string Message { get; private set; } = "Choose a Media Root to browse Catalog content.";
    public string SourceMessage { get; private set; } = "Select an asset to see its Catalog details.";
    public Guid? SelectedAssetId => _selection.Snapshot().Select(key => Guid.Parse(key)).Cast<Guid?>().FirstOrDefault();
    public MediaAsset? SelectedAsset => Assets.FirstOrDefault(asset => asset.AssetId == SelectedAssetId);
    public bool CanGoUp => Location is { RelativeFolder.Length: > 0 };
    public long Epoch => _epoch;

    public void Suspend()
    {
        _epoch++; _generation++; _selectionGeneration++;
        _lifetime.Cancel(); _lifetime.Dispose(); _lifetime = new();
        _navigation?.Cancel(); _selectionRead?.Cancel();
        _catalogId = null; IsAvailable = false; IsLoading = false; Roots = []; Assets = []; Location = null;
        _selection.Clear(); SourceMessage = "Select an asset to see its Catalog details."; Notify();
    }
    public async Task OpenAsync(Guid catalogId)
    {
        Suspend(); _catalogId = catalogId; var epoch = _epoch;
        using var demand = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        demand.CancelAfter(TimeSpan.FromSeconds(15)); var token = demand.Token;
        Message="Loading Catalog content…"; IsLoading=true; Notify();
        try
        {
            var content = await session.ReadBrowserCatalogAsync(cancellationToken:token).WaitAsync(token);
            if (epoch != _epoch || token.IsCancellationRequested || content.CatalogId != catalogId) return;
            Roots = content.Roots.Select(root => new BrowserFolderNode(root.RootId,"",root.DisplayName,
                root.Availability == MediaRootAvailability.Online ? "" : root.Availability == MediaRootAvailability.Unmapped ? "Not mapped" : "Unavailable")).ToArray();
            IsAvailable = true;
            Message = Roots.Count == 0 ? "This Catalog has no Media Roots. Open a Catalog with existing media to browse it." : "Choose a Media Root to browse Catalog content.";
        }
        catch (OperationCanceledException) { if(epoch == _epoch) Message="Catalog content loading stopped. Refresh to retry. Records are preserved."; }
        catch (Exception error) { Console.Error.WriteLine(error); if(epoch == _epoch) Message="Catalog content couldn't be loaded. Retry, or use Catalog to check its availability."; }
        if(epoch == _epoch) { IsLoading=false; Notify(); }
    }
    public async Task<IReadOnlyList<BrowserFolderNode>> ExpandAsync(BrowserFolderNode node)
    {
        if(!IsAvailable) return [];
        var epoch=_epoch; using var demand=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); demand.CancelAfter(TimeSpan.FromSeconds(15)); var token=demand.Token;
        try
        {
            var content=await session.ReadBrowserFoldersAsync(node.RootId,node.RelativeFolder,token).WaitAsync(token);
            if(epoch!=_epoch || token.IsCancellationRequested) return [];
            if(!content.Succeeded) return [new(node.RootId,node.RelativeFolder,Unavailable(content.Status),"unavailable")];
            return content.Entries.Where(entry=>entry.IsDirectory).Select(entry=>new BrowserFolderNode(node.RootId,entry.RelativePath,entry.Name)).ToArray();
        }
        catch(OperationCanceledException) { return []; }
        catch(Exception error) { Console.Error.WriteLine(error); return epoch==_epoch ? [new(node.RootId,node.RelativeFolder,"Couldn't load folders. Collapse and expand to retry.","unavailable")] : []; }
    }
    public async Task NavigateAsync(BrowserFolderNode node)
    {
        if(!IsAvailable) return;
        _navigation?.Cancel(); _navigation?.Dispose(); _navigation=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _navigation.CancelAfter(TimeSpan.FromSeconds(15)); var token=_navigation.Token; var generation=++_generation; _selectionGeneration++; _selectionRead?.Cancel();
        Location=node; IsLoading=true; Assets=[]; _selection.Clear(); SourceMessage="Select an asset to see its Catalog details.";
        Message="Loading Catalog content…"; Notify();
        try
        {
            var content=await session.ReadBrowserFolderAsync(node.RootId,node.RelativeFolder,token).WaitAsync(token);
            if(generation!=_generation || token.IsCancellationRequested) return;
            Assets=content.Assets; Recursive=content.Recursive;
            Message=content.Status!=MediaFolderEnumerationStatus.Succeeded ? Unavailable(content.Status) :
                Assets.Count==0 ? "No Catalog assets in this folder scope. New files are not imported by this Browser foundation." :
                $"{Assets.Count} Catalog asset{(Assets.Count==1 ? "" : "s")} · {(Recursive ? "Includes subfolders" : "This folder")} · Source availability is checked on selection.";
        }
        catch(OperationCanceledException) { if(generation==_generation) Message="Loading stopped. Retry or choose another folder. Catalog records are preserved."; }
        catch(Exception error) { Console.Error.WriteLine(error); if(generation==_generation) Message="This folder couldn't be loaded. Retry, reconnect its media location, or check Catalog availability. Records are preserved."; }
        finally { if(generation==_generation) { IsLoading=false; Notify(); } }
    }
    public Task UpAsync()
    {
        if(Location is not { } node || !CanGoUp) return Task.CompletedTask;
        var slash=node.RelativeFolder.LastIndexOf('/'); var parent=slash<0 ? "" : node.RelativeFolder[..slash];
        var root=Roots.FirstOrDefault(root=>root.RootId==node.RootId);
        return NavigateAsync(new(node.RootId,parent,parent.Length==0 ? root?.Name ?? "Media Root" : parent.Split('/')[^1]));
    }
    public async Task SelectAsync(Guid? assetId)
    {
        _selectionRead?.Cancel(); _selectionRead?.Dispose(); _selectionRead=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _selectionRead.CancelAfter(TimeSpan.FromSeconds(15)); var token=_selectionRead.Token; var generation=++_selectionGeneration; var scope=_generation;
        _selection.Clear(); var index=assetId is null ? -1 : Assets.ToList().FindIndex(asset=>asset.AssetId==assetId);
        if(index<0) { SourceMessage="Select an asset to see its Catalog details."; Notify(); return; }
        var asset=Assets[index]; _selection.SelectSingle(asset.AssetId.ToString("D"),index);
        SourceMessage="Checking source availability…"; Notify();
        try
        {
            var exists=await session.IsBrowserSourceAvailableAsync(asset.RootId,asset.AssetId,token).WaitAsync(token);
            if(generation==_selectionGeneration && scope==_generation && !token.IsCancellationRequested)
                SourceMessage=exists ? "Source available · Catalog metadata shown below." : "Source unavailable. Reconnect its media location or retry. The Catalog record is preserved.";
        }
        catch(OperationCanceledException) { if(generation==_selectionGeneration && scope==_generation) SourceMessage="Source check stopped. Retry selection or reconnect its media location."; }
        catch(Exception error) { Console.Error.WriteLine(error); if(generation==_selectionGeneration && scope==_generation) SourceMessage="Source availability couldn't be verified. Retry or reconnect the location; the Catalog record is preserved."; }
        if(generation==_selectionGeneration && scope==_generation) Notify();
    }
    public async Task RefreshAsync()
    {
        if (_catalogId is not { } id) return;
        var location = Location; var epoch = _epoch + 1;
        await OpenAsync(id);
        if (epoch == _epoch && IsAvailable && location is not null) await NavigateAsync(location);
    }
    private static string Unavailable(MediaFolderEnumerationStatus status) => status switch
    {
        MediaFolderEnumerationStatus.RootNotFound => "This Media Root is no longer in the Catalog. Refresh the Catalog view.",
        MediaFolderEnumerationStatus.RootUnavailable => "This Media Root is unavailable or not mapped on this computer. Reconnect its media location; its Catalog records are preserved.",
        MediaFolderEnumerationStatus.FolderNotFound => "This folder is missing. Return to its parent or reconnect the media location. Catalog records are preserved.",
        MediaFolderEnumerationStatus.AccessDenied => "This folder can't be accessed. Check its permissions, then retry.",
        _ => "This folder is unavailable or its path needs review. Retry or return to its parent. Catalog records are preserved."
    };
    private void Notify()=>PropertyChanged?.Invoke(this,new(null));
}
