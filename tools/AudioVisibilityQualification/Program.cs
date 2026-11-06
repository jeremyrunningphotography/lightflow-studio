using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lightflow.Application;
using Lightflow.Platform.MacOS;
using Lightflow.UI;
using LightflowStudio;
if(args.Length!=3)throw new ArgumentException("Task fixture folder, NEW task data root, report path required.");
var allowed=Path.GetFullPath("artifacts/lf036-audio-visibility")+Path.DirectorySeparatorChar;
var media=Path.GetFullPath(args[0]);var data=Path.GetFullPath(args[1]);var report=Path.GetFullPath(args[2]);
if(new[]{media,data,report}.Any(p=>!p.StartsWith(allowed,StringComparison.Ordinal))||Directory.Exists(data))throw new IOException("Use new task-local output and immutable task fixtures only.");
Directory.CreateDirectory(Path.Combine(data,"Catalog"));
var types=MediaTypeRegistry.CreateDefault();var clock=Stopwatch.StartNew();long fingerprints=0;var trace=new ConcurrentQueue<object>();
using var listener=new ActivityListener{ShouldListenTo=s=>s.Name is "LightflowStudio.Browser" or "LightflowStudio.Thumbnails",Sample=(ref ActivityCreationOptions<ActivityContext> _)=>ActivitySamplingResult.AllData,ActivityStopped=a=>{if(a.OperationName=="mac.source.fingerprint")Interlocked.Increment(ref fingerprints);trace.Enqueue(new{stage=a.OperationName,atMs=clock.Elapsed.TotalMilliseconds,elapsedMs=a.Duration.TotalMilliseconds,traceId=a.TraceId.ToHexString(),tags=a.TagObjects.ToDictionary(t=>t.Key,t=>t.Value)});}};ActivitySource.AddActivityListener(listener);
var rows=new List<object>();var expected=new[]{"01.jpg","03.png","05.CR2","07.mp4","09.mov","11-corrupt.mp4"};int notifications=0;
void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
void Visible(IReadOnlyList<MediaAsset> assets,int count){Check(assets.Count==count,"Wrong visible count");Check(assets.All(a=>expected.Contains(Path.GetFileName(a.RelativePath))),"Audio or unknown asset published");Check(assets.Select(a=>a.AssetId).Distinct().Count()==count,"Duplicate identity");}
void Observe(object? sender,System.ComponentModel.PropertyChangedEventArgs e){var model=(BrowserViewModel)sender!;notifications++;Check(model.Assets.All(a=>types.Classify(new(a.RelativePath)).Category is MediaTypeCategory.StillImage or MediaTypeCategory.RawImage or MediaTypeCategory.Video),"Transient audio publication");}
async Task Navigate(BrowserViewModel model,string phase,int count)
{
 var start=clock.Elapsed.TotalMilliseconds;var prior=model.Assets;double? first=null,cache=null;bool provisional=false;
 void Changed(object? _,System.ComponentModel.PropertyChangedEventArgs e){if(model.Assets.Count>0&&!ReferenceEquals(model.Assets,prior))first??=clock.Elapsed.TotalMilliseconds-start;if(model.Thumbnails.Values.Any(t=>t.Path!=null))cache??=clock.Elapsed.TotalMilliseconds-start;provisional|=model.IsProvisional;}
 model.PropertyChanged+=Changed;await model.GoPathAsync(media);model.PropertyChanged-=Changed;Visible(model.Assets,count);rows.Add(new{phase,firstAssetMs=first,firstCacheMs=cache,completeMs=clock.Elapsed.TotalMilliseconds-start,provisional,visible=count});
}
async Task Generate(MacShellCatalogSession shell,IReadOnlyList<MediaAsset> assets)
{
 var batch=await shell.DemandThumbnailsAsync(assets.Select(a=>a.AssetId).ToArray(),DerivedWorkPriority.Visible,default);await batch!.Completion.WaitAsync(TimeSpan.FromSeconds(90));var thumbs=await shell.ReadThumbnailsAsync(assets.Select(a=>a.AssetId).ToArray(),default);
 Check(thumbs.Values.Count(t=>t.State=="Ready")==assets.Count/6*5,"Supported thumbnail regression");Check(thumbs.Values.Where(t=>t.Path==null).All(t=>t.State is "Unsupported" or "Preview failed"),"Genuine failure indicator missing");rows.Add(new{phase="thumbnail-states",values=assets.Select(a=>new{name=Path.GetFileName(a.RelativePath),state=thumbs[a.AssetId].State})});
}
Dictionary<string,string> CacheHashes()=>Directory.EnumerateFiles(Path.Combine(data,"Previews"),"*.jpg",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(data,p),p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))+":"+File.GetLastWriteTimeUtc(p).Ticks);
Guid catalog,root,collection;string folder;Dictionary<string,Guid> visibleIds;Guid[] audioIds;Dictionary<string,string> cacheBefore;
await using(var shell=new MacShellCatalogSession(data,new ThumbnailImageCodec()))
{
 var ready=await shell.CreateAsync(Path.Combine(data,"Catalog"));Check(ready.IsReady,"Catalog create");catalog=ready.CatalogId!.Value;
 var first=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");root=first.RootId;folder=first.RelativeFolder;Visible(first.Assets,6);await Generate(shell,first.Assets);
 var model=new BrowserViewModel(shell);await model.OpenAsync(catalog);model.PropertyChanged+=Observe;
 for(var i=0;i<3;i++)await Navigate(model,"direct-warm-"+i,6);
 for(var i=0;i<model.Assets.Count;i++){var id=model.Assets[i].AssetId;await model.SelectAsync(id);Check(model.SelectedAssetId==id&&model.SelectedAsset==model.Assets[i],"Visible index/selection mismatch");}
 var selected=model.SelectedAssetId;await model.RefreshAsync();Visible(model.Assets,6);Check(model.SelectedAssetId==selected,"Refresh selection loss");
 await model.ToggleRecursiveAsync();Visible(model.Assets,12);await Generate(shell,model.Assets);await Navigate(model,"recursive-warm",12);visibleIds=model.Assets.ToDictionary(a=>a.RelativePath,a=>a.AssetId);
 var before=Interlocked.Read(ref fingerprints);var old=model.GoPathAsync(media);var wait=Stopwatch.StartNew();while(!old.IsCompleted&&Interlocked.Read(ref fingerprints)==before&&wait.Elapsed<TimeSpan.FromSeconds(5))await Task.Delay(1);var active=!old.IsCompleted&&Interlocked.Read(ref fingerprints)>before;
 await model.GoPathAsync(Path.Combine(Path.GetDirectoryName(media)!,"Other"));await old;Check(model.Assets.Count==1&&model.Location!.AbsolutePath!.EndsWith("/Other",StringComparison.Ordinal),"Stale navigation overwrote replacement");rows.Add(new{phase="rapid-navigation",observedFingerprinting=active,newerScopeWins=true});
 await Navigate(model,"recursive-return",12);model.PropertyChanged-=Observe;model.Suspend();cacheBefore=CacheHashes();
}
var seed=await new CatalogLifecycle(new MacCatalogLifecycleEffects(data)).StartAsync(allowFirstCreation:false);
await using(var owner=seed.Coordinator!)
{
 var all=await new CatalogMediaAssetRepository(()=>owner.Session).ListScopeAsync(root,folder,true);Check(all.Count==18,"Catalog admission changed");audioIds=all.Where(a=>types.Classify(new(a.RelativePath)).Category==MediaTypeCategory.Audio).Select(a=>a.AssetId).ToArray();Check(audioIds.Length==6,"Audio records absent");
 var collections=new CatalogCollectionOrganizationStore(()=>owner.Session);collection=(await collections.CreateCollectionAsync("Audio preserved")).CollectionId;
 foreach(var id in audioIds){await new CatalogAssetClassificationStore(()=>owner.Session).UpdateAsync(id,p=>p with{Rating=4});await collections.AddMembershipAsync(collection,id);}
}
await using(var shell=new MacShellCatalogSession(data,new ThumbnailImageCodec()))
{
 Check((await shell.StartAsync()).CatalogId==catalog,"Catalog identity changed");var model=new BrowserViewModel(shell);await model.OpenAsync(catalog);model.PropertyChanged+=Observe;await Navigate(model,"reopen-legacy-audio",12);Check(model.Assets.All(a=>visibleIds[a.RelativePath]==a.AssetId),"AssetIds changed");
 foreach(var id in audioIds){await model.SelectAsync(id);Check(model.SelectedAssetId==null,"Hidden audio retained selection");}
 for(var i=0;i<model.Assets.Count;i++){await model.SelectAsync(model.Assets[i].AssetId);Check(model.SelectedAsset==model.Assets[i],"Reopen index mismatch");}
 await model.RefreshAsync();Visible(model.Assets,12);await Generate(shell,model.Assets);Check(cacheBefore.OrderBy(x=>x.Key).SequenceEqual(CacheHashes().OrderBy(x=>x.Key)),"Cache pixels/mtimes changed on reuse");model.PropertyChanged-=Observe;model.Suspend();
}
var check=await new CatalogLifecycle(new MacCatalogLifecycleEffects(data)).StartAsync(allowFirstCreation:false);
await using(var owner=check.Coordinator!)
{
 var repository=new CatalogMediaAssetRepository(()=>owner.Session);Check((await repository.ListScopeAsync(root,folder,true)).Count==18,"Audio deletion");foreach(var id in audioIds)Check((await repository.GetAsync(id)) is {SourceStatus:MediaAssetSourceStatus.Available},"Audio identity/status lost");
 var ratings=await new CatalogAssetClassificationStore(()=>owner.Session).GetAsync(audioIds);Check(audioIds.All(id=>ratings[id].Rating==4),"Authored audio metadata lost");Check((await new CatalogCollectionOrganizationStore(()=>owner.Session).ListMembershipsAsync(collection)).Select(m=>m.AssetId).OrderBy(x=>x).SequenceEqual(audioIds.OrderBy(x=>x)),"Collection membership lost");
}
Check(MacThumbnailRenderer.lf_preview_readers()==0,"Native readers leaked");File.WriteAllText(report,JsonSerializer.Serialize(new{passed=true,rows,checkedPublications=notifications,visibleDirect=6,visibleRecursive=12,retainedAudioRecords=6,retainedRating=4,collectionMemberships=6,cacheImagesUnchanged=cacheBefore.Count,nativeReadersAfterClose=0,trace},new JsonSerializerOptions{WriteIndented=true}));
