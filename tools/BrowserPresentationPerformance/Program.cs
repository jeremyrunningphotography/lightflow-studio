using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Lightflow.Application;
using Lightflow.Platform.MacOS;
using Lightflow.UI;
using LightflowStudio;
if(args.Length!=3)throw new ArgumentException("Private folder list, NEW task data root, report path required.");
var allowed=Path.GetFullPath("artifacts/lf036-cached-presentation")+Path.DirectorySeparatorChar;
var data=Path.GetFullPath(args[1]);var report=Path.GetFullPath(args[2]);
if(!data.StartsWith(allowed,StringComparison.Ordinal)||!report.StartsWith(allowed,StringComparison.Ordinal)||Directory.Exists(data))throw new IOException("Use new task-local output roots.");
var sources=JsonSerializer.Deserialize<Source[]>(File.ReadAllText(args[0]))!;
using var authorization=JsonDocument.Parse(File.ReadAllText(Path.Combine(allowed,"private/source-authorization.json")));
var owner=authorization.RootElement.GetProperty("folder").GetString();
foreach(var s in sources)if(s.Path!=owner&&!Path.GetFullPath(s.Path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath("artifacts"))+Path.DirectorySeparatorChar,StringComparison.Ordinal))throw new IOException("Source outside authorized scope.");
Directory.CreateDirectory(Path.Combine(data,"Catalog"));Directory.CreateDirectory(Path.GetDirectoryName(report)!);
var clock=Stopwatch.StartNew();long fingerprints=0;var trace=new ConcurrentQueue<object>();
using var listener=new ActivityListener{ShouldListenTo=s=>s.Name is "LightflowStudio.Browser" or "LightflowStudio.Thumbnails",Sample=(ref ActivityCreationOptions<ActivityContext> _)=>ActivitySamplingResult.AllData,ActivityStopped=a=>{if(a.OperationName=="mac.source.fingerprint")Interlocked.Increment(ref fingerprints);trace.Enqueue(new{stage=a.OperationName,atMs=clock.Elapsed.TotalMilliseconds,elapsedMs=a.Duration.TotalMilliseconds,traceId=a.TraceId.ToHexString(),spanId=a.SpanId.ToHexString(),parentId=a.ParentSpanId.ToHexString(),tags=a.TagObjects.ToDictionary(t=>t.Key,t=>t.Value)});}};ActivitySource.AddActivityListener(listener);
var rows=new List<object>();var process=Process.GetCurrentProcess();long peak=0;using var sample=new Timer(_=>{process.Refresh();peak=Math.Max(peak,process.WorkingSet64);},null,0,50);
async Task Run(MacShellCatalogSession shell,string phase)
{
 var catalog=await shell.ReadBrowserCatalogAsync();var model=new BrowserViewModel(shell);await model.OpenAsync(catalog.CatalogId);
 foreach(var source in sources)
 {
  var start=clock.Elapsed.TotalMilliseconds;double? firstAsset=null,firstCache=null;var notifications=0;var priorAssets=model.Assets;
  void Changed(object? _,System.ComponentModel.PropertyChangedEventArgs e){notifications++;if(model.Assets.Count>0&&!ReferenceEquals(model.Assets,priorAssets))firstAsset??=clock.Elapsed.TotalMilliseconds-start;if(model.Thumbnails.Values.Any(x=>x.Path!=null))firstCache??=clock.Elapsed.TotalMilliseconds-start;}
  model.PropertyChanged+=Changed;await model.GoPathAsync(source.Path);var completed=clock.Elapsed.TotalMilliseconds-start;
  var readAt=clock.Elapsed.TotalMilliseconds;var demand=await shell.DemandThumbnailsAsync(model.Assets.Take(12).Select(a=>a.AssetId).ToArray(),DerivedWorkPriority.Visible,default);if(demand is not null)await demand.Completion;var thumbs=await shell.ReadThumbnailsAsync(model.Assets.Take(12).Select(a=>a.AssetId).ToArray(),default);var cacheRead=clock.Elapsed.TotalMilliseconds-readAt;if(thumbs.Values.Any(t=>t.Path!=null))firstCache??=clock.Elapsed.TotalMilliseconds-start;
  rows.Add(new{phase,source.Alias,startMs=start,firstAssetMs=firstAsset,firstCachedProjectionMs=firstCache,reconciliationCompleteMs=completed,postScanCacheReadMs=cacheRead,assets=model.Assets.Count,ready=thumbs.Values.Count(x=>x.Path!=null),notifications,rss=process.WorkingSet64});model.PropertyChanged-=Changed;Console.WriteLine($"{phase} {source.Alias}: first asset {firstAsset:F2} ms, cache {firstCache:F2} ms, complete {completed:F2} ms");
 }
 model.Suspend();
}
await using(var shell=new MacShellCatalogSession(data,new ThumbnailImageCodec()))
{
 if(!(await shell.CreateAsync(Path.Combine(data,"Catalog"))).IsReady)throw new IOException("Catalog create failed");
 foreach(var source in sources){var content=await shell.DiscoverBrowserFolderAsync(source.Path,Guid.Empty,"");var ids=content.Assets.Take(192).Select(a=>a.AssetId).ToArray();var batch=await shell.DemandThumbnailsAsync(ids,DerivedWorkPriority.Visible,default);await batch!.Completion.WaitAsync(TimeSpan.FromMinutes(4));}
 if(Environment.GetEnvironmentVariable("LF036_NAV_CANCEL")=="1")
 {
  var model=new BrowserViewModel(shell);await model.OpenAsync((await shell.ReadBrowserCatalogAsync()).CatalogId);
  for(var i=0;i<3;i++)
  {
   var before=Interlocked.Read(ref fingerprints);var old=model.GoPathAsync(sources[^1].Path);var wait=Stopwatch.StartNew();
   while(!old.IsCompleted&&Interlocked.Read(ref fingerprints)==before&&wait.Elapsed<TimeSpan.FromSeconds(10))await Task.Delay(1);
   var active=!old.IsCompleted&&Interlocked.Read(ref fingerprints)>before;var start=clock.Elapsed.TotalMilliseconds;
   await model.GoPathAsync(sources[0].Path);await old;
   rows.Add(new{phase="navigation-cancellation",observedActiveFingerprinting=active,replacementMs=clock.Elapsed.TotalMilliseconds-start,newScopeWins=model.Location?.AbsolutePath==sources[0].Path,assets=model.Assets.Count});
  }
  model.Suspend();
 }
 else for(int i=0;i<3;i++)await Run(shell,"warm-"+i);
}
if(Environment.GetEnvironmentVariable("LF036_NAV_CANCEL")!="1")await using(var shell=new MacShellCatalogSession(data,new ThumbnailImageCodec())){if(!(await shell.StartAsync()).IsReady)throw new IOException("Reopen failed");await Run(shell,"reopen");}
File.WriteAllText(report,JsonSerializer.Serialize(new{rows,maxSampledRss=peak,nativeReadersAfterClose=MacThumbnailRenderer.lf_preview_readers(),trace},new JsonSerializerOptions{WriteIndented=true}));
record Source(string Alias,string Path);
