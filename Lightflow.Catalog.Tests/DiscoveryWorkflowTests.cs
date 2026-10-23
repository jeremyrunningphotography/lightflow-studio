using LightflowStudio;
using Xunit;

namespace Lightflow.Catalog.Tests;

public sealed class DiscoveryWorkflowTests
{
    [Fact]
    public async Task CancellationAfterOneObservationNeverMarksUnseenAssetsMissing()
    {
        var id = Guid.NewGuid(); using var cancel = new CancellationTokenSource();
        var assets = new Assets(id, cancel); var existing = assets.Rows[0];
        var folder = new Folders(new(MediaFolderEnumerationStatus.Succeeded, "", [Entry(id,"new.jpg")]));
        var result = await new CatalogReconciliationService(folder, assets).ReconcileAsync(new(id), cancel.Token);
        Assert.Equal(CatalogReconciliationStatus.Canceled, result.Status);
        Assert.Equal(1, result.NewCount); Assert.Equal(0, assets.MissingCalls);
        Assert.Equal(existing, assets.Rows[0]); Assert.Equal(2, assets.Rows.Count);
    }

    [Theory]
    [InlineData(MediaFolderEnumerationStatus.AccessDenied)]
    [InlineData(MediaFolderEnumerationStatus.RootUnavailable)]
    [InlineData(MediaFolderEnumerationStatus.FolderUnavailable)]
    public async Task FailedEnumerationNeverMutatesOrInfersMissing(MediaFolderEnumerationStatus status)
    {
        var id = Guid.NewGuid(); var assets = new Assets(id);
        var result = await new CatalogReconciliationService(new Folders(new(status,"",[])), assets).ReconcileAsync(new(id));
        Assert.False(result.Succeeded); Assert.Single(assets.Rows); Assert.Equal(0,assets.MissingCalls);
    }

    [Fact]
    public async Task AmbiguousPathsRefuseBeforeMutationAndUnsupportedFilesAreNotCreated()
    {
        var id = Guid.NewGuid(); var assets = new Assets(id);
        var collision = new Folders(new(MediaFolderEnumerationStatus.Succeeded,"",[Entry(id,"new.jpg"),Entry(id,"NEW.jpg")]));
        Assert.Equal(CatalogReconciliationStatus.InvalidRequest,(await new CatalogReconciliationService(collision,assets).ReconcileAsync(new(id))).Status);
        Assert.Single(assets.Rows); Assert.Equal(0,assets.MissingCalls);
        var unsupported = Entry(id,"notes.txt") with { MediaType=MediaTypeClassification.Unknown };
        var result = await new MediaDiscoveryRefreshService(new CatalogReconciliationService(new Folders(new(MediaFolderEnumerationStatus.Succeeded,"",[Entry(id,"kept.jpg"),unsupported])),assets),()=>null).RefreshAsync(new(id));
        Assert.True(result.Reconciliation.Succeeded); Assert.Null(result.DerivedWork);
        Assert.Equal(1,result.Reconciliation.UnsupportedCount); Assert.Single(assets.Rows);
    }

    [Fact]
    public async Task RecursiveDiscoveryReusesPerFolderPipelineHonorsCapAndReportsDescendantFailure()
    {
        var id=Guid.NewGuid(); var discovery=new Discovery();
        var walker=new RecursiveMediaDiscoveryService(new Tree(id),discovery,maximumConcurrentFolders:2,maximumFolders:3);
        var result=await walker.DiscoverAsync(new(id));
        Assert.True(result.Succeeded); Assert.InRange(discovery.Count,1,3);
        Assert.Contains("not scanned",result.Diagnostic!);
        Assert.Contains("denied",result.Diagnostic!);
        Assert.Null(result.DerivedWork);
        using var cancel=new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>walker.DiscoverAsync(new(id),enumerationToken:cancel.Token));
    }
    private static MediaFolderEntry Entry(Guid id,string name)=>new(id,name,name.ToUpperInvariant(),name,false,new(MediaTypeCategory.StillImage),4,DateTimeOffset.UtcNow);
    private sealed class Folders(MediaFolderEnumerationResult result):IMediaFolderEnumerator
    {
        public Task<MediaFolderEnumerationResult> EnumerateAsync(MediaFolderEnumerationRequest request,CancellationToken token=default) { token.ThrowIfCancellationRequested(); return Task.FromResult(result); }
    }
    private sealed class Tree(Guid id):IMediaFolderEnumerator
    {
        public Task<MediaFolderEnumerationResult> EnumerateAsync(MediaFolderEnumerationRequest request,CancellationToken token=default)
        {
            token.ThrowIfCancellationRequested(); var folder=request.RelativeFolder??"";
            return Task.FromResult(new MediaFolderEnumerationResult(MediaFolderEnumerationStatus.Succeeded,folder,folder.Length==0
                ? Enumerable.Range(0,8).Select(i=>Entry(id,"folder"+i) with {IsDirectory=true}).ToArray() : [Entry(id,folder+"/one.jpg")]));
        }
    }
    private sealed class Discovery:IMediaDiscoveryRefreshService
    {
        public int Count;
        public Task<MediaDiscoveryRefreshResult> RefreshAsync(MediaFolderEnumerationRequest request,DerivedWorkPriority priority=DerivedWorkPriority.Background,CancellationToken cancellationToken=default,CancellationToken derivedWorkCancellationToken=default)
        { Interlocked.Increment(ref Count); return Task.FromResult(new MediaDiscoveryRefreshResult(new(request.RelativeFolder == "folder1" ? CatalogReconciliationStatus.FolderUnavailable : CatalogReconciliationStatus.Succeeded,request.RootId,request.RelativeFolder??"",[],Diagnostic:request.RelativeFolder == "folder1" ? "denied" : null),null)); }
    }
    private sealed class Assets(Guid root,CancellationTokenSource? cancel=null):IMediaAssetService,ICatalogMutationParticipant
    {
        public CatalogMutationLifecycle Mutations {get;}=new();
        public List<MediaAsset> Rows=[new(Guid.NewGuid(),root,"kept.jpg","KEPT.JPG","image",4,0,null,MediaAssetSourceStatus.Available,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null)];
        public int MissingCalls;
        public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken token=default)=>Task.FromResult<IReadOnlyList<MediaAsset>>(Rows.ToArray());
        public Task<MediaAssetOperationResult> CreateAsync(Guid rootId,string path,string mediaType="unknown",CancellationToken token=default)
        {
            var asset=new MediaAsset(Guid.NewGuid(),rootId,path,path.ToUpperInvariant(),mediaType,4,0,null,MediaAssetSourceStatus.Available,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null);
            Rows.Add(asset);cancel?.Cancel();return Task.FromResult(new MediaAssetOperationResult(MediaAssetOperationStatus.Succeeded,new(asset,MediaRootAvailability.Online,path,true)));
        }
        public Task<MediaAssetOperationResult> ObserveAsync(Guid id,CancellationToken token=default)=>Task.FromResult(new MediaAssetOperationResult(MediaAssetOperationStatus.Succeeded,new(Rows.Single(a=>a.AssetId==id),MediaRootAvailability.Online,"source",true)));
        public Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> ids,CancellationToken token=default) { MissingCalls++; return Task.FromResult(ids.Count); }
        public Task<MediaAssetResolution?> GetAsync(Guid id,CancellationToken token=default)=>throw new NotSupportedException();
        public Task<MediaAssetResolution?> FindAsync(Guid id,string path,CancellationToken token=default)=>throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid,MediaAsset>> GetManyAsync(IReadOnlyCollection<Guid> ids,CancellationToken token=default)=>throw new NotSupportedException();
    }
}
