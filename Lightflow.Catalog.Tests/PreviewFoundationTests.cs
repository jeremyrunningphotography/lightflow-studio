using LightflowStudio;
using Microsoft.Data.Sqlite;
using Xunit;
namespace Lightflow.Catalog.Tests;

public sealed class PreviewFoundationTests
{
    [Fact]
    public async Task HostProbeVersionRefreshesMetadataOnceWithoutReplacingCurrentThumbnail()
    {
        var root=Root();var locations=new Locations(root);var assets=new Assets(root);var id=assets.Add();var renderer=new Renderer();
        await using var store=new CatalogPreviewStoreService(locations);
        using var generator=Generator(assets,store,locations,renderer);
        await generator.GenerateAsync(new(id));var before=(await store.GetAsync(id))!;
        var probe=new DimensionProbe();
        using(var old=new DerivedMediaMetadataService(assets,store,probe))await old.ProbeAsync(id);
        using var updated=new DerivedMediaMetadataService(assets,store,probe,probeVersion:_=>3);
        Assert.Equal(DerivedMetadataStatus.Succeeded,(await updated.ProbeAsync(id)).Status);
        Assert.Equal(DerivedMetadataStatus.Current,(await updated.ProbeAsync(id)).Status);
        Assert.Equal(2,probe.Calls);
        var after=(await store.GetAsync(id))!;Assert.Equal(3,after.MetadataProbeVersion);Assert.Equal(before.ThumbnailRelativePath,after.ThumbnailRelativePath);
        await using var scheduler=new SharedDerivedWorkScheduler(assets,store,updated,generator,artifactExists:_=>true,probeVersion:_=>3);
        await scheduler.TrySchedule(Reconcile(id)).Batch!.Completion;
        Assert.Equal(2,probe.Calls);Assert.Equal(1,renderer.Calls);
    }
    sealed class DimensionProbe:IMediaMetadataProbe
    {
        public int Calls;
        public Task<MediaProbeResult> ProbeAsync(string path,string type,long size,CancellationToken cancellationToken=default)
        {Calls++;return Task.FromResult(new MediaProbeResult(DerivedMetadataStatus.Succeeded,new(DerivedMediaKind.Image,"jpg",null,null,size,null,null,null,new("jpg",100,50,null,null,null,null,null,null))));}
    }
    [Fact]
    public async Task RealSchemaFourSurvivesReopenAndSourceInvalidation()
    {
        var root=Root();var locations=new Locations(root);var assets=new Assets(root);var id=assets.Add();var renderer=new Renderer();
        string path;
        await using(var store=new CatalogPreviewStoreService(locations))
        {
            using var generator=Generator(assets,store,locations,renderer);
            path=(await generator.GenerateAsync(new(id))).ThumbnailPath!;
            Assert.True(File.Exists(path));Assert.Equal(1,renderer.Calls);
            Assert.Equal(ThumbnailGenerationStatus.Current,(await generator.GenerateAsync(new(id))).Status);Assert.Equal(1,renderer.Calls);
            var before=await store.GetAsync(id);Assert.Equal(1,before!.ThumbnailGeneratorVersion);
        }
        await using(var store=new CatalogPreviewStoreService(locations))
        {
            using var generator=Generator(assets,store,locations,renderer);
            Assert.Equal(path,(await generator.GenerateAsync(new(id))).ThumbnailPath);Assert.Equal(1,renderer.Calls);
            assets.Change(id);
            Assert.Equal(ThumbnailGenerationStatus.Succeeded,(await generator.GenerateAsync(new(id))).Status);Assert.Equal(2,renderer.Calls);
            Assert.NotEqual(path,(await store.GetAsync(id))!.ThumbnailRelativePath);
        }
        using var connection=new SqliteConnection($"Data Source={locations.PreviewsDatabasePath};Pooling=False");connection.Open();using var query=connection.CreateCommand();query.CommandText="PRAGMA user_version";Assert.Equal(4L,query.ExecuteScalar());
        Assert.Equal(10,(int)ThumbnailGenerationStatus.Failed);Assert.Equal(3,(int)PreviewComponentState.Failed);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SourceOrColorChangeDuringRenderCannotPublish(bool colorChange)
    {
        var root=Root();var locations=new Locations(root);var assets=new Assets(root);var id=assets.Add();var colors=new Colors();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer=new Renderer(async token=>{entered.SetResult();await release.Task.WaitAsync(token);});
        await using var store=new CatalogPreviewStoreService(locations);using var generator=new SharedThumbnailGenerationService(assets,store,locations,renderer,colorSource:colors,validate:File.Exists);
        var work=generator.GenerateAsync(new(id));await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if(colorChange)colors.Identity="changed";else assets.Change(id);release.SetResult();
        Assert.Equal(ThumbnailGenerationStatus.SourceChanged,(await work).Status);
        Assert.NotEqual(PreviewComponentState.Current,(await store.GetAsync(id))!.ThumbnailState);
        Assert.Empty(Directory.EnumerateFiles(root,"*.lightflow",SearchOption.AllDirectories));
    }
    [Fact]
    public async Task FailedRetriesPersistAndForceBypassesCooldown()
    {
        var root=Root();var locations=new Locations(root);var assets=new Assets(root);var id=assets.Add();var renderer=new Renderer(fail:true);
        await using(var store=new CatalogPreviewStoreService(locations)){using var generator=Generator(assets,store,locations,renderer);Assert.Equal(ThumbnailGenerationStatus.Unsupported,(await generator.GenerateAsync(new(id))).Status);}
        await using(var store=new CatalogPreviewStoreService(locations))
        {
            using var generator=Generator(assets,store,locations,renderer);Assert.Equal(ThumbnailGenerationStatus.Deferred,(await generator.GenerateAsync(new(id))).Status);Assert.Equal(1,renderer.Calls);
            await generator.GenerateAsync(new(id,ForceRefresh:true));Assert.Equal(2,renderer.Calls);
        }
    }
    [Fact]
    public async Task VisiblePromotionDeduplicatesAndCancellationStormKeepsQueueBounded()
    {
        var root=Root();var locations=new Locations(root);var assets=new Assets(root);var first=assets.Add();var second=assets.Add();var third=assets.Add();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var order=new List<Guid>();
        var renderer=new Renderer(async token=>{if(!entered.Task.IsCompleted){entered.SetResult();await release.Task.WaitAsync(token);}});
        await using var store=new CatalogPreviewStoreService(locations);using var metadata=new Metadata();using var generator=Generator(assets,store,locations,renderer);
        await using var scheduler=new SharedDerivedWorkScheduler(assets,store,metadata,generator,maximumConcurrency:1);
        var a=scheduler.TrySchedule(Reconcile(first),DerivedWorkPriority.Background).Batch!;await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var b=scheduler.TrySchedule(Reconcile(second),DerivedWorkPriority.Background).Batch!;
        var c=scheduler.TrySchedule(Reconcile(third),DerivedWorkPriority.Background).Batch!;
        var promoted=scheduler.TrySchedule(Reconcile(third),DerivedWorkPriority.Visible).Batch!;
        c.ProgressChanged+=(_,p)=>{if(p.Running>0)lock(order)order.Add(third);};b.ProgressChanged+=(_,p)=>{if(p.Running>0)lock(order)order.Add(second);};
        for(int i=0;i<2000;i++){var id=assets.Add();var batch=scheduler.TrySchedule(Reconcile(id)).Batch!;batch.Cancel();}
        Assert.InRange(scheduler.QueuedEntryCount,0,64);Assert.InRange(scheduler.Diagnostics.Outstanding,1,3);
        release.SetResult();await Task.WhenAll(a.Completion,b.Completion,c.Completion,promoted.Completion).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[]{third,second},order);Assert.Equal(3,renderer.Calls);
    }
    [Fact]
    public async Task CancelledInflightDemandCanBeReplacedWithoutLosingNewSubscriber()
    {
        var root=Root();var locations=new Locations(root);var assets=new Assets(root);var id=assets.Add();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls=0;var renderer=new Renderer(async token=>{if(Interlocked.Increment(ref calls)==1){entered.SetResult();await Task.Delay(Timeout.Infinite,token);}});
        await using var store=new CatalogPreviewStoreService(locations);using var metadata=new Metadata();using var generator=Generator(assets,store,locations,renderer);
        await using var scheduler=new SharedDerivedWorkScheduler(assets,store,metadata,generator,maximumConcurrency:2);
        var first=scheduler.TrySchedule(Reconcile(id)).Batch!;await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));first.Cancel();
        var next=scheduler.TrySchedule(Reconcile(id),DerivedWorkPriority.Visible).Batch!;
        await next.Completion.WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal(DerivedWorkItemOutcome.Generated,Assert.Single(next.Results).Outcome);
    }
    [Fact]
    public async Task PreferredFrameChangeDuringRenderCannotPublish()
    {
        var root=Root();var locations=new Locations(root);var assets=new Assets(root);var id=assets.Add();var preferred=new Frames();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer=new Renderer(async token=>{entered.SetResult();await release.Task.WaitAsync(token);});
        await using var store=new CatalogPreviewStoreService(locations);using var generator=new SharedThumbnailGenerationService(assets,store,locations,renderer,validate:File.Exists,preferredFrames:preferred);
        var work=generator.GenerateAsync(new(id));await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));preferred.Value=new(id,TimeSpan.FromSeconds(2),1,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);release.SetResult();
        Assert.Equal(ThumbnailGenerationStatus.SourceChanged,(await work).Status);Assert.NotEqual(PreviewComponentState.Current,(await store.GetAsync(id))!.ThumbnailState);
    }
    [Fact]
    public async Task MissingAuthoredLutNeverRendersUntreatedAndRetainsOnlyMatchingArtifact()
    {
        var root=Root();var locations=new Locations(root);var assets=new Assets(root);var id=assets.Add();var renderer=new Renderer();var colors=new Colors{Identity="authored-color"};
        await using var store=new CatalogPreviewStoreService(locations);using var generator=new SharedThumbnailGenerationService(assets,store,locations,renderer,validate:File.Exists,colorSource:colors);
        var first=await generator.GenerateAsync(new(id));colors.Missing=true;
        var failed=await generator.GenerateAsync(new(id,ForceRefresh:true));Assert.Equal(ThumbnailGenerationStatus.Failed,failed.Status);Assert.Equal(PreviewFailureReason.DependencyUnavailable,(await store.GetAsync(id))!.ThumbnailFailureReason);Assert.Equal(1,renderer.Calls);
        Assert.True(BrowserPreviewReuse.HasRetainedThumbnail((await store.GetAsync(id))!,store));
        colors.Identity="different-authored-color";await generator.GenerateAsync(new(id,ForceRefresh:true));Assert.False(BrowserPreviewReuse.HasRetainedThumbnail((await store.GetAsync(id))!,store));Assert.Equal(1,renderer.Calls);
    }
    sealed class Frames:IPreferredPreviewFrameStore
    {
        public PreferredPreviewFrame? Value;
        public Task<PreferredPreviewFrame?> GetAsync(Guid id,CancellationToken cancellationToken=default)=>Task.FromResult(Value);
        public Task<PreferredPreviewFrame> SetAsync(Guid id,MediaPresentationTimestamp timestamp,TimeSpan duration,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task ResetAsync(Guid id,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
    [Fact]
    public async Task GuardAndContainmentRejectForeignOrEscapingStorage()
    {
        var root=Root();var locations=new Locations(root);var guards=0;
        await using var store=new CatalogPreviewStoreService(locations,guard:path=>{guards++;if(!Path.GetFullPath(path).StartsWith(root+Path.DirectorySeparatorChar)&&path!=root)throw new IOException("outside");return path;});
        await store.InitializeAsync();Assert.True(guards>0);Assert.Throws<ArgumentException>(()=>store.ResolveArtifactPath("../outside.jpg"));
        var foreign=new Locations(Root());Directory.CreateDirectory(foreign.PreviewsDirectory);await File.WriteAllTextAsync(foreign.PreviewsDatabasePath,"foreign database");
        await using var refused=new CatalogPreviewStoreService(foreign);await Assert.ThrowsAnyAsync<Exception>(()=>refused.InitializeAsync());Assert.Equal("foreign database",await File.ReadAllTextAsync(foreign.PreviewsDatabasePath));
    }
    [Theory] [InlineData(null,1)] [InlineData(.4,.2)] [InlineData(1.0,1.0)] [InlineData(5.0,1.0)] [InlineData(20.0,2.0)] [InlineData(600.0,30.0)]
    public void PosterPolicyPreservesWindowsBoundaries(double? duration,double seconds)=>Assert.Equal(TimeSpan.FromSeconds(seconds),ThumbnailPosterPolicy.Position(duration));

    static string Root(){var p=Path.Combine(AppContext.BaseDirectory,"PreviewFixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    static SharedThumbnailGenerationService Generator(Assets assets,IPreviewStoreService store,Locations locations,Renderer renderer)=>new(assets,store,locations,renderer,validate:File.Exists);
    static CatalogReconciliationResult Reconcile(Guid id)=>new(CatalogReconciliationStatus.Succeeded,Guid.Empty,"",[new(id,"source.jpg",CatalogReconciliationItemStatus.Unchanged)]);
    sealed class Renderer(Func<CancellationToken,Task>? wait=null,bool fail=false):IThumbnailRenderer
    {
        public int Calls;
        public async Task<ThumbnailRenderResult> RenderAsync(string source,string type,TimeSpan time,string destination,CancellationToken token=default)
        {Interlocked.Increment(ref Calls);if(wait is not null)await wait(token);if(fail)return new(ThumbnailGenerationStatus.Unsupported,"unsupported",PreviewFailureReason.CodecUnavailable);await File.WriteAllTextAsync(destination,"pixels",token);return new(ThumbnailGenerationStatus.Succeeded);}
    }
    sealed class Colors:IThumbnailColorSource
    {
        public string Identity="original";public bool Missing;
        public Task<string> IdentityAsync(Guid id,CancellationToken token)=>Task.FromResult(Identity);
        public Task<ThumbnailColorRender> ResolveAsync(Guid id,CancellationToken token)=>Missing?throw new FileNotFoundException("authored LUT missing"):Task.FromResult(new ThumbnailColorRender(Identity,[]));
    }
    sealed class Metadata:IDerivedMediaMetadataService
    {public void Dispose(){}public Task<DerivedMetadataResult> ProbeAsync(Guid id,bool forceRefresh=false,CancellationToken cancellationToken=default)=>Task.FromResult(new DerivedMetadataResult(DerivedMetadataStatus.Current));}
    sealed class Assets(string root):IMediaAssetService
    {
        readonly Dictionary<Guid,MediaAsset> _items=[];
        public Guid Add(){var id=Guid.NewGuid();_items[id]=new(id,Guid.NewGuid(),"source.jpg",id.ToString(),"image",1,1,new(1,"aa"),MediaAssetSourceStatus.Available,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null);return id;}
        public void Change(Guid id)=>_items[id]=_items[id] with{FileSizeBytes=_items[id].FileSizeBytes+1,Fingerprint=new(1,"bb")};
        public Task<MediaAssetResolution?> GetAsync(Guid id,CancellationToken cancellationToken=default)=>Task.FromResult<MediaAssetResolution?>(new(_items[id],MediaRootAvailability.Online,Path.Combine(root,"source.jpg"),true));
        public async Task<MediaAssetOperationResult> ObserveAsync(Guid id,CancellationToken cancellationToken=default)=>new(MediaAssetOperationStatus.Succeeded,await GetAsync(id,cancellationToken));
        public Task<IReadOnlyDictionary<Guid,MediaAsset>> GetManyAsync(IReadOnlyCollection<Guid> ids,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyDictionary<Guid,MediaAsset>>(ids.ToDictionary(id=>id,id=>_items[id]));
        public Task<IReadOnlyList<MediaAsset>> ListAsync(CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<MediaAsset>>(_items.Values.ToArray());
        public Task<MediaAssetOperationResult> CreateAsync(Guid rootId,string relativePath,string mediaType="unknown",CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<MediaAssetResolution?> FindAsync(Guid rootId,string relativePath,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<int> MarkMissingAsync(IReadOnlyCollection<Guid> ids,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }

    sealed class Locations(string root):ILightflowStorageLocations
    {
        public string ApplicationDataDirectory=>root;
        public string SettingsPath=>Path.Combine(root,"SettingsPath");
        public string StatePath=>Path.Combine(root,"StatePath");
        public string WorkspaceStatePath=>Path.Combine(root,"WorkspaceStatePath");
        public string JobHistoryPath=>Path.Combine(root,"JobHistoryPath");
        public string FileOperationHistoryPath=>Path.Combine(root,"FileOperationHistoryPath");
        public string TrimHistoryPath=>Path.Combine(root,"TrimHistoryPath");
        public string OutputIdentityDirectory=>Path.Combine(root,"OutputIdentityDirectory");
        public string CatalogDirectory=>Path.Combine(root,"Catalog");
        public string CatalogDatabasePath=>Path.Combine(root,"CatalogDatabasePath");
        public string CatalogBackupsDirectory=>Path.Combine(root,"CatalogBackupsDirectory");
        public string PreviewsDirectory=>Path.Combine(root,"Previews");
        public string PreviewsDatabasePath=>Path.Combine(PreviewsDirectory,"previews.db");
        public string ThumbnailCacheDirectory=>Path.Combine(PreviewsDirectory,"thumbnails");
        public string StandardPreviewCacheDirectory=>Path.Combine(PreviewsDirectory,"previews");
        public string TemporaryDirectory=>Path.Combine(root,"TemporaryDirectory");
        public string LogsDirectory=>Path.Combine(root,"LogsDirectory");
        public string ActivityLogPath=>Path.Combine(root,"ActivityLogPath");
        public string MachineIdentityPath=>Path.Combine(root,"MachineIdentityPath");
    }
}
