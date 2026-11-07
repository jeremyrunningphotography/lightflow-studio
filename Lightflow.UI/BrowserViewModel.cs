using System.ComponentModel;
using Lightflow.Application;
using LightflowStudio;

namespace Lightflow.UI;

public sealed record BrowserFolderNode(Guid RootId, string RelativeFolder, string Name, string Availability = "", string? AbsolutePath = null, bool CanPrefetch = true,
    BrowserLocationSection Section = BrowserLocationSection.None,
    BrowserLocationKind Kind = BrowserLocationKind.Folder, string? Server = null)
{
    public string Key => AbsolutePath ?? $"{RootId}/{RelativeFolder}";
}

/// <summary>Shared transient presentation. Catalog/Folder services own data; extracted selection owns keys.</summary>
public sealed partial class BrowserViewModel(IBrowserFolderSession session, IBrowserLocationProvider? locations = null) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _navigation, _selectionRead, _locationUpdate;
    private long _epoch, _generation, _selectionGeneration;
    private Guid? _catalogId;
    private IReadOnlyList<MediaRootInfo> _catalogRoots = [];
    private IReadOnlyList<BrowserRecursiveRoot> _recursiveRoots = [];
    private bool? _hasSubfolders;
    private long _refreshGeneration;
    private HashSet<string> _liveVolumeMounts = new(StringComparer.Ordinal);
    private string? _unavailableVolume;
    public long FolderRefreshRevision { get; private set; }
    private readonly HashSet<string> _observedVolumeMounts = new(StringComparer.Ordinal);
    private readonly List<BrowserFolderNode> _back = [], _forward = [];
    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanChangeRecursive => session is IBrowserDiscoverySession && Location?.RootId != Guid.Empty && Location is not null && !IsLoading && !IsProvisional && (Recursive || _hasSubfolders != false);
    public bool CanRefresh => _catalogId is not null;
    public bool HasNoSubfolders => _hasSubfolders == false && !Recursive;
    private readonly BrowserGridSelection _selection = new();
    public IReadOnlyList<BrowserFolderNode> Roots { get; private set; } = [];
    public IReadOnlyList<MediaAsset> Assets { get; private set; } = [];
    public BrowserFolderNode? Location { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsProvisional { get; private set; }
    public bool IsAvailable { get; private set; }
    public bool Recursive { get; private set; }
    public string Message { get; private set; } = "Choose a location to browse Catalog content.";
    public string SourceMessage { get; private set; } = "Select an asset to see its Catalog details.";
    public Guid? SelectedAssetId => _selection.Snapshot().Select(key => Guid.Parse(key)).Cast<Guid?>().FirstOrDefault();
    public bool IsAssetSelected(Guid id) => _selection.IsSelected(id.ToString("D"));
    public IReadOnlySet<string> SelectedAssetKeys => _selection.Snapshot();
    public MediaAsset? SelectedAsset => Assets.FirstOrDefault(asset => asset.AssetId == SelectedAssetId);
    public bool CanGoUp => Location is { AbsolutePath: not null } file ? file.AbsolutePath != "/" : Location is { RelativeFolder.Length: > 0 };
    public long Epoch => _epoch;

    public bool IsRecursiveScope(BrowserFolderNode node)
    {
        var mapped = node.AbsolutePath is { } path ? BrowserLocationMapping.Find(path,_catalogRoots,StringComparison.Ordinal) : null;
        return BrowserRecursiveRootLogic.IsEffectivelyRecursive(_recursiveRoots,mapped?.RootId ?? node.RootId,mapped?.RelativeFolder ?? node.RelativeFolder);
    }
    public bool IsFolderIconFilled(BrowserFolderNode node) => Location?.Key == node.Key || IsRecursiveScope(node);

    public void Suspend()
    {
        ClearThumbnailDemand();
        _epoch++; _generation++; _selectionGeneration++; _refreshGeneration++; FolderRefreshRevision++;
        _locationUpdate?.Cancel(); _unavailableVolume=null; _liveVolumeMounts.Clear();
        _lifetime.Cancel(); _lifetime.Dispose(); _lifetime = new();
        _navigation?.Cancel(); _selectionRead?.Cancel();
        _back.Clear(); _forward.Clear(); _observedVolumeMounts.Clear();
        _recursiveRoots=[]; _hasSubfolders=null; Recursive=false;
        _catalogId = null; IsAvailable = false; IsLoading = false; IsProvisional = false; Roots = []; Assets = []; Location = null;
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
            _catalogRoots = content.Roots; _recursiveRoots = content.RecursiveRoots ?? [];
            var catalogNodes = content.Roots.Select(root => new BrowserFolderNode(root.RootId,"",root.DisplayName,
                root.Availability == MediaRootAvailability.Online ? "" : root.Availability == MediaRootAvailability.Unmapped ? "Not mapped" : "Unavailable", locations is null ? null : root.PhysicalPath)).ToArray();
            if (locations is not null)
            {
                var discovered = await locations.GetLocationsAsync(token).WaitAsync(token);
                if (epoch != _epoch || token.IsCancellationRequested) return;
                Roots = MergeRoots(discovered,catalogNodes);
            }
            else Roots = catalogNodes;
            IsAvailable = true;
            Message = Roots.Count == 0 ? "No locations are available. Refresh to retry." : "Choose a location to browse Catalog content.";
        }
        catch (OperationCanceledException) { if(epoch == _epoch) Message="Catalog content loading stopped. Refresh to retry. Records are preserved."; }
        catch (Exception error) { Console.Error.WriteLine(error); if(epoch == _epoch) Message="Catalog content couldn't be loaded. Retry, or use Catalog to check its availability."; }
        if(epoch == _epoch)
        {
            if(IsAvailable)await RestoreLocationAsync(epoch);
            if(epoch==_epoch) {IsLoading=false; Notify();}
        }
    }
    public async Task<IReadOnlyList<BrowserFolderNode>> ExpandAsync(BrowserFolderNode node, bool userInitiated = false)
    {
        if(!IsAvailable) return [];
        if(node.AbsolutePath is { } unavailable && IsKnownUnmountedPath(unavailable))
            return [new(node.RootId,node.RelativeFolder,VolumeUnavailableMessage,"unavailable",unavailable)];
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
        using var navigationTiming = ThumbnailPerformance.Measure("navigation.request");
        _navigation?.Cancel(); _navigation?.Dispose(); _navigation=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token=_navigation.Token; var generation=++_generation; _selectionGeneration++; _selectionRead?.Cancel();
        if (!refreshing && history && Location is { } prior && prior.Key != node.Key) { _back.Add(prior); _forward.Clear(); }
        _unavailableVolume=null;
        ClearThumbnailDemand();
        Location=PresentedNode(node); IsLoading=true; IsProvisional=false; Recursive=IsRecursiveScope(node); _hasSubfolders=null;
        if (!refreshing) { Assets=[]; _selection.Clear(); SourceMessage="Select an asset to see its Catalog details."; }
        Message="Checking folder…"; Notify();
        try
        {
            if(node.AbsolutePath is { } missingMount && IsKnownUnmountedPath(missingMount))
            { _unavailableVolume=missingMount; Message=VolumeUnavailableMessage; return; }
            if (session is IBrowserCachedFolderSession cached && node.RootId != Guid.Empty)
            {
                var known = await cached.ReadCachedBrowserFolderAsync(node.RootId, node.RelativeFolder, token).WaitAsync(token);
                if (generation != _generation || token.IsCancellationRequested) return;
                if (known.Assets.Count > 0)
                {
                    IsProvisional = true; Assets = known.Assets; Recursive = known.Recursive;
                    if (known.RecursiveRoots is { } scopes) _recursiveRoots = scopes;
                    Message = "Showing cached media · Checking for changes…";
                    using (var publication = ThumbnailPerformance.Measure("ui.assets.publish"))
                    { publication?.SetTag("provisional", true); publication?.SetTag("count", Assets.Count); Notify(); }
                    await HydrateNavigationCacheAsync(known.Assets, provisional: true, generation, token);
                    if (generation != _generation || token.IsCancellationRequested) return;
                }
            }
            if (locations is not null && node.AbsolutePath is { } path)
            {
                var listing = await locations.GetFoldersAsync(path, token, userInitiated: true).WaitAsync(token);
                if (generation != _generation || token.IsCancellationRequested) return;
                if (listing.Status != MediaFolderEnumerationStatus.Succeeded)
                { if (!refreshing) { Assets=[]; _selection.Clear(); } Message = Unavailable(listing.Status); return; }
                node = FileNode(new(path, node.Name, node.CanPrefetch, node.Section, node.Kind, node.Server));
                Location = PresentedNode(node);
                if (node.RootId == Guid.Empty && session is not IBrowserDiscoverySession)
                { Assets=[]; _selection.Clear(); Message = "This folder is not cataloged. Folder navigation does not import files."; return; }
            }
            var content = session is IBrowserDiscoverySession discovery
                ? await discovery.DiscoverBrowserFolderAsync(node.AbsolutePath, node.RootId, node.RelativeFolder, token).WaitAsync(token)
                : await session.ReadBrowserFolderAsync(node.RootId,node.RelativeFolder,token).WaitAsync(token);
            node = node with { RootId = content.RootId, RelativeFolder = content.RelativeFolder };
            if (generation == _generation && !token.IsCancellationRequested) Location = PresentedNode(node);
            if(generation!=_generation || token.IsCancellationRequested) return;
            var retainAssetId = refreshing || IsProvisional ? SelectedAssetId : null;
            var retainedKeys = _selection.Snapshot();
            if(content.CatalogRoots is { } catalogRoots)_catalogRoots=catalogRoots;
            if(content.RecursiveRoots is { } scopeRoots)_recursiveRoots=scopeRoots;
            if (IsProvisional)
            {
                // Re-read identities after reconciliation; changed/missing/Color-invalidated cached pixels
                // must not be promoted from provisional to current without the existing reuse checks.
                await HydrateNavigationCacheAsync(content.Assets, provisional: false, generation, token);
                if (generation != _generation || token.IsCancellationRequested) return;
            }
            IsProvisional=false; Assets=content.Assets; Recursive=content.Recursive; _hasSubfolders=content.Folders.Count>0;
            if (retainAssetId is not null && Assets.FirstOrDefault(asset => retainedKeys.Contains(asset.AssetId.ToString("D"))) is { } surviving)
            {
                await SelectAsync(surviving.AssetId);
                if (generation != _generation || token.IsCancellationRequested) return;
                _selection.Restore(retainedKeys.Intersect(Assets.Select(a => a.AssetId.ToString("D"))), null);
                _selection.ResetAnchor();
            }
            else { _selection.Clear(); SourceMessage="Select an asset to see its Catalog details."; }
            if (generation != _generation || token.IsCancellationRequested) return;
            Message=content.Status!=MediaFolderEnumerationStatus.Succeeded ? Unavailable(content.Status) : "";
            if (content.Diagnostic is { Length: > 0 } diagnostic)
            {
                Console.Error.WriteLine($"Browser scan details: {diagnostic}");
                Message = $"Scan incomplete · {Assets.Count} supported media shown. Some sources or folders could not be reconciled; Catalog records are preserved. Retry or check folder access.";
            }
            else if (content.UnavailableSourceCount > 0)
                Message = $"{Assets.Count} supported media shown · {content.UnavailableSourceCount} sources unavailable locally. Make them available outside Lightflow, then retry; no automatic download was requested.";
            if(content.Status==MediaFolderEnumerationStatus.Succeeded)RememberLocation();
        }
        catch(OperationCanceledException) { if(generation==_generation) Message="Loading stopped. Retry or choose another folder. Catalog records are preserved."; }
        catch(Exception error) { Console.Error.WriteLine(error); if(generation==_generation) Message=error is IOException ? $"{error.Message} Catalog records are preserved. Retry after resolving the source location." : "This folder couldn't be loaded. Retry, reconnect its media location, or check Catalog availability. Records are preserved."; }
        finally
        {
            if(generation==_generation)
            {
                if (IsProvisional) MarkNavigationCacheUnverified();
                IsLoading=false;
                using var publication = ThumbnailPerformance.Measure("ui.navigation.complete");
                publication?.SetTag("provisional", IsProvisional); publication?.SetTag("count", Assets.Count); Notify();
            }
        }
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
            if (generation != _generation) return;
            if (session is IBrowserRecursiveScopeSession scopes)
            {
                var roots=await scopes.ReadBrowserRecursiveRootsAsync(_lifetime.Token);
                if(generation!=_generation)return;
                _recursiveRoots=roots; Recursive=IsRecursiveScope(node); Notify();
            }
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
        if (IsProvisional) { SourceMessage="Cached Catalog details · source availability has not been verified."; Notify(); return; }
        SourceMessage="Checking source availability…"; Notify();
        try
        {
            var exists=await session.IsBrowserSourceAvailableAsync(asset.RootId,asset.AssetId,token).WaitAsync(token);
            if(generation==_selectionGeneration && scope==_generation && !token.IsCancellationRequested)
                SourceMessage=exists ? "Source available" : "Source unavailable. Reconnect its media location or retry. The Catalog record is preserved.";
        }
        catch(OperationCanceledException) { if(generation==_selectionGeneration && scope==_generation) SourceMessage="Source check stopped. Retry selection or reconnect its media location."; }
        catch(Exception error) { Console.Error.WriteLine(error); if(generation==_selectionGeneration && scope==_generation) SourceMessage="Source availability couldn't be verified. Retry or reconnect the location; the Catalog record is preserved."; }
        if(generation==_selectionGeneration && scope==_generation) Notify();
    }
    /// <summary>Desktop selection reuses the shared Windows selection owner; no card-local selection set.</summary>
    public async Task SelectGridAsync(Guid id, bool toggle = false, bool range = false)
    {
        var index = -1;
        for (var i = 0; i < Assets.Count; i++) if (Assets[i].AssetId == id) { index = i; break; }
        if (index < 0) return;
        if (!toggle && !range) { await SelectAsync(id); return; }
        _selectionRead?.Cancel(); _selectionGeneration++;
        if (range)
        {
            var anchor = Math.Clamp(_selection.AnchorIndex ?? index, 0, Assets.Count - 1);
            _selection.SelectRange(Assets.Skip(Math.Min(index, anchor)).Take(Math.Abs(index - anchor) + 1).Select(a => a.AssetId.ToString("D")).ToArray());
        }
        else _selection.ToggleCtrl(id.ToString("D"), index);
        SourceMessage = $"{_selection.Snapshot().Count} selected"; Notify();
    }
    public void SelectAllGrid()
    {
        _selectionRead?.Cancel(); _selectionGeneration++;
        _selection.SelectAll(Assets.Select(a => a.AssetId.ToString("D")));
        SourceMessage = $"{Assets.Count} selected"; Notify();
    }
    public async Task RefreshAsync()
    {
        if (_catalogId is not { } id) return;
        _locationUpdate?.Cancel();
        var refresh = ++_refreshGeneration; var epoch = _epoch; var navigation = _generation;
        var location = Location;
        using var demand = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        demand.CancelAfter(TimeSpan.FromSeconds(15)); var token = demand.Token;
        try
        {
            var content = await session.ReadBrowserCatalogAsync(cancellationToken: token).WaitAsync(token);
            var discovered = locations is null ? null : await locations.GetLocationsAsync(token).WaitAsync(token);
            if (refresh != _refreshGeneration || epoch != _epoch || navigation != _generation || content.CatalogId != id) return;
            _catalogRoots = content.Roots; _recursiveRoots = content.RecursiveRoots ?? [];
            var catalogNodes = content.Roots.Select(root => new BrowserFolderNode(root.RootId,"",root.DisplayName,
                root.Availability == MediaRootAvailability.Online ? "" : "Unavailable", locations is null ? null : root.PhysicalPath));
            Roots = discovered is null ? catalogNodes.ToArray() : MergeRoots(discovered,catalogNodes);
            IsAvailable = true; FolderRefreshRevision++;
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
    public void ReportLocationUpdatesUnavailable()
    {
        Message="Automatic volume updates are unavailable. Use Refresh to update Locations."; Notify();
    }
    private const string VolumeUnavailableMessage = "This volume is no longer mounted. Reconnect it, then retry or Refresh. Catalog records are preserved.";
    private bool IsKnownUnmountedPath(string path)
    {
        var mount=_observedVolumeMounts.Where(mount=>Under(path,mount)).OrderByDescending(mount=>mount.Length).FirstOrDefault();
        return mount is not null && !_liveVolumeMounts.Contains(mount);
    }
    private static bool Under(string path,string mount) => path==mount || path.StartsWith(mount=="/" ? "/" : mount+"/",StringComparison.Ordinal);

    /// <summary>UI-thread entry for native hints. Snapshot/read-only presentation only: never RefreshAsync/Discover/Navigate.</summary>
    public async Task ReconcileLocationsAsync(CancellationToken cancellationToken = default)
    {
        if(!IsAvailable || locations is null)return;
        _locationUpdate?.Cancel(); _locationUpdate?.Dispose();
        using var update=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token,cancellationToken);
        _locationUpdate=update; update.CancelAfter(TimeSpan.FromSeconds(15));
        var token=update.Token; var revision=++_refreshGeneration; var epoch=_epoch;
        try
        {
            var discovered=await locations.GetLocationsAsync(token).WaitAsync(token);
            if(epoch!=_epoch || revision!=_refreshGeneration || token.IsCancellationRequested)return;
            var catalogNodes=_catalogRoots.Select(root=>new BrowserFolderNode(root.RootId,"",root.DisplayName,
                root.Availability==MediaRootAvailability.Online ? "" : "Unavailable",root.PhysicalPath));
            var roots=MergeRoots(discovered,catalogNodes);
            var changed=!roots.SequenceEqual(Roots);
            if(changed)Roots=roots;
            if(Location is { AbsolutePath: { } path } current)
            {
                if(IsKnownUnmountedPath(path))
                {
                    _navigation?.Cancel(); _generation++; _selectionRead?.Cancel(); _selectionGeneration++;
                    IsLoading=false; _unavailableVolume=path; Message=VolumeUnavailableMessage;
                    if(SelectedAssetId is not null)SourceMessage="Source unavailable. Reconnect its volume, then retry selection or Refresh. Catalog records are preserved.";
                    changed=true;
                }
                else if(_unavailableVolume==path)
                {
                    _unavailableVolume=null;
                    Message="The volume is available again. Retry navigation or Refresh to check its contents.";
                    if(SelectedAssetId is not null)SourceMessage="Source availability needs rechecking. Retry selection or Refresh.";
                    changed=true;
                }
                // A display rename never replaces filesystem/Catalog/navigation identity.
                if(roots.FirstOrDefault(root=>root.Key==current.Key) is { } renamed && renamed.Name!=current.Name)
                { Location=current with { Name=renamed.Name }; changed=true; }
            }
            if(changed)Notify();
        }
        catch(OperationCanceledException)
        {
            // Supersession/teardown stays quiet; a native timeout keeps context but offers retry.
            if(epoch==_epoch && revision==_refreshGeneration && !cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
            { Message="The automatic Locations check stopped or timed out. Current context is preserved; use Refresh to retry."; Notify(); }
        }
        catch(Exception error)
        {
            Console.Error.WriteLine(error);
            if(epoch==_epoch && revision==_refreshGeneration && !token.IsCancellationRequested)
            { Message="Locations couldn't be updated automatically. Current context is preserved; use Refresh to retry."; Notify(); }
        }
        finally { if(ReferenceEquals(_locationUpdate,update))_locationUpdate=null; }
    }
    private IReadOnlyList<BrowserFolderNode> MergeRoots(IReadOnlyList<BrowserFileLocation> discovered, IEnumerable<BrowserFolderNode> catalogNodes)
    {
        _liveVolumeMounts=discovered.Where(node=>node.Section is BrowserLocationSection.Devices or BrowserLocationSection.Network)
            .Select(node=>node.Path).ToHashSet(StringComparer.Ordinal);
        foreach(var path in _liveVolumeMounts)_observedVolumeMounts.Add(path);
        // A Catalog mapping is not a live volume bookmark. Preserve records/history,
        // but do not reinsert a known disconnected local/external/network mount.
        var live=discovered.Select(node=>node.Path).ToHashSet(StringComparer.Ordinal);
        return discovered.Select(FileNode).Concat(catalogNodes.Where(node=>node.AbsolutePath is not { } path ||
            !_observedVolumeMounts.Any(mount=>!live.Contains(mount) &&
                Under(path,mount))))
            .DistinctBy(node=>node.Key).ToArray();
    }
    private BrowserFolderNode PresentedNode(BrowserFolderNode node)
    {
        var shown=Roots.FirstOrDefault(root=>root.Key==node.Key && root.Section!=BrowserLocationSection.None);
        return shown is null ? node : node with { Name=shown.Name,Section=shown.Section,Kind=shown.Kind,Server=shown.Server };
    }
    private BrowserFolderNode FileNode(BrowserFileLocation location)
    {
        if(location.Section==BrowserLocationSection.None && Roots.FirstOrDefault(node=>node.AbsolutePath==location.Path && node.Section!=BrowserLocationSection.None) is { } known)
            location=location with { Name=known.Name, Section=known.Section, Kind=known.Kind, Server=known.Server };
        var scope = BrowserLocationMapping.Find(location.Path, _catalogRoots, StringComparison.Ordinal);
        return new(scope?.RootId ?? Guid.Empty, scope?.RelativeFolder ?? "", location.Name, AbsolutePath: location.Path, CanPrefetch: location.CanPrefetch || (scope is not null && location.Section==BrowserLocationSection.None),
            Section: location.Section, Kind: location.Kind, Server: location.Server);
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
