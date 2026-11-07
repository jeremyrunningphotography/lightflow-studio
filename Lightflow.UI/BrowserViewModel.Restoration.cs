using Lightflow.Application;
using LightflowStudio;
namespace Lightflow.UI;

/// <summary>Profile preference bound to exact Catalog/root/path identity; never used to remap storage.</summary>
public sealed record BrowserLocationBookmark(Guid CatalogId, Guid RootId, string RelativeFolder, string? AbsolutePath);

public sealed partial class BrowserViewModel
{
    public BrowserLocationBookmark? RestoreLocation { get; set; }
    public event Action<BrowserLocationBookmark>? LocationRemembered;
    private void RememberLocation()
    {
        if (_catalogId is not { } catalog || Location is not { RootId: var root } location || root==Guid.Empty || IsProvisional) return;
        var bookmark=new BrowserLocationBookmark(catalog,root,location.RelativeFolder,location.AbsolutePath);
        RestoreLocation=bookmark; LocationRemembered?.Invoke(bookmark);
    }
    private async Task RestoreLocationAsync(long epoch)
    {
        if(RestoreLocation is not { } bookmark || bookmark.CatalogId!=_catalogId)return;
        if(bookmark.RelativeFolder is null || bookmark.RelativeFolder.Length>32768)
        {Message="Last location preferences are invalid. Choose a location.";return;}
        const string fallback="Last location is unavailable or changed. Choose a location; Catalog records are preserved.";
        var root=_catalogRoots.FirstOrDefault(r=>r.RootId==bookmark.RootId);
        // Only restore exact, accessible mappings in this Catalog. Never create natural anchors or discover.
        if(root is null || root.Availability!=MediaRootAvailability.Online || session is not IBrowserCachedFolderSession cached ||
            (bookmark.RelativeFolder.Length>0 && !PortablePathIdentityValidator.ValidateRelativePath(bookmark.RelativeFolder,CancellationToken.None).IsSafe))
        { Message=fallback;return; }
        var path=root.PhysicalPath is { } physical ? physical.TrimEnd('/')+(bookmark.RelativeFolder.Length==0 ? "" : "/"+bookmark.RelativeFolder) : null;
        if(!string.Equals(path,bookmark.AbsolutePath,StringComparison.Ordinal)) { Message=fallback;return; }
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var token=timeout.Token;var generation=_generation;
        try
        {
            // Existing contained filesystem adapter refuses linked/cloud-only paths; no scan/admission/hydration.
            if(locations is not null && path is not null)
            {
                var listing=await locations.GetFoldersAsync(path,token,userInitiated:false).WaitAsync(token);
                if(epoch!=_epoch || generation!=_generation || token.IsCancellationRequested)return;
                if(listing.Status!=MediaFolderEnumerationStatus.Succeeded) { Message=fallback;return; }
            }
            else if(path is not null) { Message=fallback;return; }
            var content=await cached.ReadCachedBrowserFolderAsync(root.RootId,bookmark.RelativeFolder,token).WaitAsync(token);
            if(epoch!=_epoch || generation!=_generation || token.IsCancellationRequested)return;
            if(content.RootId!=root.RootId || content.RelativeFolder!=bookmark.RelativeFolder || content.Status!=MediaFolderEnumerationStatus.Succeeded)
            {Message=fallback;return;}
            Location=new(root.RootId,bookmark.RelativeFolder,bookmark.RelativeFolder.Length==0 ? root.DisplayName : bookmark.RelativeFolder.Split('/')[^1],AbsolutePath:path,CanPrefetch:false);
            Assets=content.Assets;Recursive=content.Recursive;IsProvisional=true;
            Message="Restored Catalog view · Refresh to check for changes.";
            await HydrateNavigationCacheAsync(Assets,provisional:true,generation,token);
        }
        catch(OperationCanceledException) { if(epoch==_epoch && generation==_generation){Location=null;Assets=[];IsProvisional=false;Message=fallback;} }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {if(epoch==_epoch && generation==_generation){Location=null;Assets=[];IsProvisional=false;Message=fallback;}}
    }
}
