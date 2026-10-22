using System.ComponentModel;
using Lightflow.Application;
using LightflowStudio;

namespace Lightflow.UI;

public sealed record BrowserFolderNode(Guid RootId, string RelativeFolder, string Name, string Availability = "", string? AbsolutePath = null, bool CanPrefetch = true)
{
    public string Key => AbsolutePath ?? $"{RootId}/{RelativeFolder}";
}

/// <summary>Shared transient presentation. Catalog/Folder services own data; extracted selection owns keys.</summary>
public sealed class BrowserViewModel(IBrowserFolderSession session, IBrowserLocationProvider? locations = null) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _navigation, _selectionRead;
    private long _epoch, _generation, _selectionGeneration;
    private Guid? _catalogId;
    private IReadOnlyList<MediaRootInfo> _catalogRoots = [];
    private long _refreshGeneration;
    private readonly List<BrowserFolderNode> _back = [], _forward = [];
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanChangeRecursive => session is IBrowserDiscoverySession && Location?.RootId != Guid.Empty && Location is not null && !IsLoading;
    public bool CanRefresh => _catalogId is not null;
    private readonly BrowserGridSelection _selection = new();
    public IReadOnlyList<BrowserFolderNode> Roots { get; private set; } = [];
    public IReadOnlyList<MediaAsset> Assets { get; private set; } = [];
    public BrowserFolderNode? Location { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsAvailable { get; private set; }
    public bool Recursive { get; private set; }
    public string Message { get; private set; } = "Choose a location to browse Catalog content.";
    public string SourceMessage { get; private set; } = "Select an asset to see its Catalog details.";
    public Guid? SelectedAssetId => _selection.Snapshot().Select(key => Guid.Parse(key)).Cast<Guid?>().FirstOrDefault();
    public MediaAsset? SelectedAsset => Assets.FirstOrDefault(asset => asset.AssetId == SelectedAssetId);
    public bool CanGoUp => Location is { AbsolutePath: not null } file ? file.AbsolutePath != "/" : Location is { RelativeFolder.Length: > 0 };
    public long Epoch => _epoch;

    public void Suspend()
    {
        _epoch++; _generation++; _selectionGeneration++;
        _lifetime.Cancel(); _lifetime.Dispose(); _lifetime = new();
        _navigation?.Cancel(); _selectionRead?.Cancel();
        _back.Clear(); _forward.Clear();
        _catalogId = null; IsAvailable = false; IsLoading = false; Roots = []; Assets = []; Location = null;
        _selection.Clear(); SourceMessage = "Select an asset to see its Catalog details."; Notify();
    }
    public async Task OpenAsync(Guid catalogId)
    {
        Suspend(); _catalogId = catalogId; var epoch = _epoch;
        using var demand = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        demand.CancelAfter(TimeSpan.FromSeconds(15)); var token = demand.Token;
        Message="Checking folder…"; IsLoading=true; Notify();
        try
        {
            var content = await session.ReadBrowserCatalogAsync(cancellationToken:token).WaitAsync(token);
            if (epoch != _epoch || token.IsCancellationRequested || content.CatalogId != catalogId) return;
            _catalogRoots = content.Roots;
            var catalogNodes = content.Roots.Select(root => new BrowserFolderNode(root.RootId,"",root.DisplayName,
                root.Availability == MediaRootAvailability.Online ? "" : root.Availability == MediaRootAvailability.Unmapped ? "Not mapped" : "Unavailable", locations is null ? null : root.PhysicalPath)).ToArray();
            if (locations is not null)
            {
                var discovered = await locations.GetLocationsAsync(token).WaitAsync(token);
                if (epoch != _epoch || token.IsCancellationRequested) return;
                Roots = discovered.Select(FileNode).Concat(catalogNodes).DistinctBy(node => node.Key).ToArray();
            }
            else Roots = catalogNodes;
            IsAvailable = true;
            Message = Roots.Count == 0 ? "No locations are available. Refresh to retry." : "Choose a location to browse Catalog content.";
        }
        catch (OperationCanceledException) { if(epoch == _epoch) Message="Catalog content loading stopped. Refresh to retry. Records are preserved."; }
        catch (Exception error) { Console.Error.WriteLine(error); if(epoch == _epoch) Message="Catalog content couldn't be loaded. Retry, or use Catalog to check its availability."; }
        if(epoch == _epoch) { IsLoading=false; Notify(); }
    }
    public async Task<IReadOnlyList<BrowserFolderNode>> ExpandAsync(BrowserFolderNode node, bool userInitiated = false)
    {
        if(!IsAvailable) return [];
        var epoch=_epoch; using var demand=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); demand.CancelAfter(TimeSpan.FromSeconds(15)); var token=demand.Token;
        try
        {
            if (locations is not null && node.AbsolutePath is { } path)
            {
                var listing = await locations.GetFoldersAsync(path, token, userInitiated).WaitAsync(token);
                if (epoch != _epoch || token.IsCancellationRequested) return [];
                return listing.Status == MediaFolderEnumerationStatus.Succeeded
                    ? listing.Folders.Select(FileNode).ToArray()
                    : [new(node.RootId,node.RelativeFolder,Unavailable(listing.Status),"unavailable",path)];
            }
            var content=await session.ReadBrowserFoldersAsync(node.RootId,node.RelativeFolder,token).WaitAsync(token);
            if(epoch!=_epoch || token.IsCancellationRequested) return [];
            if(!content.Succeeded) return [new(node.RootId,node.RelativeFolder,Unavailable(content.Status),"unavailable")];
            return content.Entries.Where(entry=>entry.IsDirectory).Select(entry=>new BrowserFolderNode(node.RootId,entry.RelativePath,entry.Name)).ToArray();
        }
        catch(OperationCanceledException) { return epoch == _epoch && !_lifetime.IsCancellationRequested ? [new(node.RootId,node.RelativeFolder,"Folder check stopped. Collapse and expand to retry.","unavailable")] : []; }
        catch(Exception error) { Console.Error.WriteLine(error); return epoch==_epoch ? [new(node.RootId,node.RelativeFolder,"Couldn't load folders. Collapse and expand to retry.","unavailable")] : []; }
    }
    public async Task NavigateAsync(BrowserFolderNode node, bool refreshing = false, bool history = true)
    {
        if(!IsAvailable) return;
        _navigation?.Cancel(); _navigation?.Dispose(); _navigation=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token=_navigation.Token; var generation=++_generation; _selectionGeneration++; _selectionRead?.Cancel();
        if (!refreshing && history && Location is { } prior && prior.Key != node.Key) { _back.Add(prior); _forward.Clear(); }
        Location=node; IsLoading=true; Recursive=false;
        if (!refreshing) { Assets=[]; _selection.Clear(); SourceMessage="Select an asset to see its Catalog details."; }
        Message="Checking folder…"; Notify();
        try
        {
            if (locations is not null && node.AbsolutePath is { } path)
            {
                var listing = await locations.GetFoldersAsync(path, token, userInitiated: true).WaitAsync(token);
                if (generation != _generation || token.IsCancellationRequested) return;
                if (listing.Status != MediaFolderEnumerationStatus.Succeeded)
                { Assets=[]; _selection.Clear(); Message = Unavailable(listing.Status); return; }
                node = FileNode(new(path, node.Name));
                Location = node;
                if (node.RootId == Guid.Empty && session is not IBrowserDiscoverySession)
                { Assets=[]; _selection.Clear(); Message = "This folder is not cataloged. Folder navigation does not import files."; return; }
            }
            var content = session is IBrowserDiscoverySession discovery
                ? await discovery.DiscoverBrowserFolderAsync(node.AbsolutePath, node.RootId, node.RelativeFolder, token).WaitAsync(token)
                : await session.ReadBrowserFolderAsync(node.RootId,node.RelativeFolder,token).WaitAsync(token);
            node = node with { RootId = content.RootId, RelativeFolder = content.RelativeFolder };
            if (generation == _generation && !token.IsCancellationRequested) Location = node;
            if(generation!=_generation || token.IsCancellationRequested) return;
            var retainAssetId = refreshing ? SelectedAssetId : null;
            Assets=content.Assets; Recursive=content.Recursive;
            if (retainAssetId is { } selected && Assets.Any(asset => asset.AssetId == selected)) await SelectAsync(selected);
            else { _selection.Clear(); SourceMessage="Select an asset to see its Catalog details."; }
            if (generation != _generation || token.IsCancellationRequested) return;
            Message=content.Status!=MediaFolderEnumerationStatus.Succeeded ? Unavailable(content.Status) :
                Assets.Count==0 ? "No supported media in this folder." :
                $"{Assets.Count} Catalog asset{(Assets.Count==1 ? "" : "s")} · {(Recursive ? "Includes subfolders" : "This folder")} · Source availability is checked on selection.";
            if (content.Diagnostic is { Length: > 0 } diagnostic) Message += " · " + diagnostic;
        }
        catch(OperationCanceledException) { if(generation==_generation) Message="Loading stopped. Retry or choose another folder. Catalog records are preserved."; }
        catch(Exception error) { Console.Error.WriteLine(error); if(generation==_generation) Message="This folder couldn't be loaded. Retry, reconnect its media location, or check Catalog availability. Records are preserved."; }
        finally { if(generation==_generation) { IsLoading=false; Notify(); } }
    }
    public void CancelNavigation() => _navigation?.Cancel();
    public Task GoPathAsync(string path) => NavigateAsync(FileNode(new(path, path == "/" ? "/" : path.TrimEnd('/').Split('/')[^1])));
    public async Task BackAsync()
    {
        if (!CanGoBack) return;
        var target = _back[^1]; _back.RemoveAt(_back.Count - 1);
        if (Location is { } current) _forward.Add(current);
        await NavigateAsync(target, history: false);
    }
    public async Task ForwardAsync()
    {
        if (!CanGoForward) return;
        var target = _forward[^1]; _forward.RemoveAt(_forward.Count - 1);
        if (Location is { } current) _back.Add(current);
        await NavigateAsync(target, history: false);
    }
    public async Task ToggleRecursiveAsync()
    {
        if (!CanChangeRecursive || session is not IBrowserDiscoverySession discovery || Location is not { } node) return;
        var generation = _generation;
        try
        {
            await discovery.SetBrowserRecursiveAsync(node.RootId, node.RelativeFolder, !Recursive, _lifetime.Token);
            if (generation == _generation) await NavigateAsync(node, refreshing: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Console.Error.WriteLine(error); Message = "Subfolder scope couldn't be changed. Retry."; Notify(); }
    }
    public Task UpAsync()
    {
        if(Location is not { } node || !CanGoUp) return Task.CompletedTask;
        if (node.AbsolutePath is { } path && locations is not null)
        {
            var slashIndex = path.TrimEnd('/').LastIndexOf('/');
            var parentPath = slashIndex <= 0 ? "/" : path[..slashIndex];
            return NavigateAsync(FileNode(new(parentPath, parentPath == "/" ? "/" : parentPath.Split('/')[^1])));
        }
        var slash=node.RelativeFolder.LastIndexOf('/'); var parent=slash<0 ? "" : node.RelativeFolder[..slash];
        var root=Roots.FirstOrDefault(root=>root.RootId==node.RootId);
        return NavigateAsync(new(node.RootId,parent,parent.Length==0 ? root?.Name ?? "Location" : parent.Split('/')[^1]));
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
        var refresh = ++_refreshGeneration; var epoch = _epoch; var navigation = _generation;
        var location = Location;
        using var demand = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        demand.CancelAfter(TimeSpan.FromSeconds(15)); var token = demand.Token;
        try
        {
            var content = await session.ReadBrowserCatalogAsync(cancellationToken: token).WaitAsync(token);
            var discovered = locations is null ? null : await locations.GetLocationsAsync(token).WaitAsync(token);
            if (refresh != _refreshGeneration || epoch != _epoch || navigation != _generation || content.CatalogId != id) return;
            _catalogRoots = content.Roots;
            var catalogNodes = content.Roots.Select(root => new BrowserFolderNode(root.RootId,"",root.DisplayName,
                root.Availability == MediaRootAvailability.Online ? "" : "Unavailable", locations is null ? null : root.PhysicalPath));
            Roots = (discovered is null ? catalogNodes : discovered.Select(FileNode).Concat(catalogNodes)).DistinctBy(node => node.Key).ToArray();
            IsAvailable = true;
            Notify();
            if (location is not null) await NavigateAsync(location, refreshing: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (epoch == _epoch && navigation == _generation)
            { Message = "Locations couldn't be refreshed. Current context is preserved; retry."; Notify(); }
        }
    }
    private BrowserFolderNode FileNode(BrowserFileLocation location)
    {
        var scope = BrowserLocationMapping.Find(location.Path, _catalogRoots, StringComparison.Ordinal);
        return new(scope?.RootId ?? Guid.Empty, scope?.RelativeFolder ?? "", location.Name, AbsolutePath: location.Path, CanPrefetch: scope is not null || location.CanPrefetch);
    }
    private static string Unavailable(MediaFolderEnumerationStatus status) => status switch
    {
        MediaFolderEnumerationStatus.RootNotFound => "This location is no longer in the Catalog. Refresh the Catalog view.",
        MediaFolderEnumerationStatus.RootUnavailable => "This location is unavailable or not mapped on this computer. Reconnect its media location; its Catalog records are preserved.",
        MediaFolderEnumerationStatus.FolderNotFound => "This folder is missing. Return to its parent or reconnect the media location. Catalog records are preserved.",
        MediaFolderEnumerationStatus.AccessDenied => "This folder can't be accessed. Check its permissions, then retry.",
        _ => "This folder is unavailable or its path needs review. Retry or return to its parent. Catalog records are preserved."
    };
    private void Notify()=>PropertyChanged?.Invoke(this,new(null));
}
