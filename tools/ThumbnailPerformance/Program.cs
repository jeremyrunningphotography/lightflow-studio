using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using LightflowStudio;
using Lightflow.Platform.MacOS;
using Lightflow.UI;

// This harness NEVER creates, writes, renames or deletes source media. The older disposable-vector harness is intentionally not used.
var repo=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
if(args.Length is not (3 or 4))throw new ArgumentException("Use absolute private-selection-json, NEW task-data-root, output-json paths.");
var selectionFile=Path.GetFullPath(args[0]);var data=Path.GetFullPath(args[1]);var report=Path.GetFullPath(args[2]);
var taskRoot=Path.GetFullPath(Path.Combine(repo,"artifacts/lf036-performance"));
void RequireOutput(string path){if(!path.StartsWith(taskRoot+"/",StringComparison.Ordinal)||path.StartsWith("/Volumes/JRPhoto4T/",StringComparison.Ordinal))throw new ArgumentException("Every generated path must remain in this task's local performance directory.");}
RequireOutput(data);RequireOutput(report);if(Directory.Exists(data))throw new ArgumentException("Use a new data root, preserving earlier evidence.");
var selected=JsonSerializer.Deserialize<Source[]>(File.ReadAllText(selectionFile),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
var media=Path.GetDirectoryName(selected[0].Path)!;
foreach(var item in selected){if(Path.GetDirectoryName(item.Path)!=media||!File.Exists(item.Path))throw new ArgumentException("Sources must already exist in one authorized folder.");}
Directory.CreateDirectory(Path.Combine(data,"Catalog"));Directory.CreateDirectory(Path.GetDirectoryName(report)!);
if(args.Length==4)
{
 var nativePath=Path.GetFullPath(args[3]);RequireOutput(nativePath);
 var handle=NativeLibrary.Load(nativePath);
 NativeLibrary.SetDllImportResolver(typeof(MacThumbnailRenderer).Assembly,(name,assembly,search)=>name=="libLightflowStorage.dylib"?handle:IntPtr.Zero);
}
var events=new ConcurrentQueue<object>();var clock=Stopwatch.StartNew();var process=Process.GetCurrentProcess();var cpuStart=process.TotalProcessorTime;
var stages=new ConcurrentQueue<object>();
using var stageWriter=new StreamWriter(report+".stages.jsonl"){AutoFlush=true};var stageLock=new object();
long maxRss=0;var maxReaders=0;var samples=new ConcurrentQueue<object>();
using var sampler=new Timer(_=>{process.Refresh();var rss=process.WorkingSet64;var readers=MacThumbnailRenderer.lf_preview_readers();maxRss=Math.Max(maxRss,rss);maxReaders=Math.Max(maxReaders,readers);samples.Enqueue(new{atMs=clock.Elapsed.TotalMilliseconds,rss,readers});},null,0,50);
using var listener=new ActivityListener{ShouldListenTo=source=>source.Name is "LightflowStudio.Browser" or "LightflowStudio.Thumbnails",Sample=(ref ActivityCreationOptions<ActivityContext> options)=>ActivitySamplingResult.AllData,ActivityStopped=a=>{var row=new{stage=a.OperationName,elapsedMs=a.Duration.TotalMilliseconds,atMs=clock.Elapsed.TotalMilliseconds,traceId=a.TraceId.ToHexString(),spanId=a.SpanId.ToHexString(),parentId=a.ParentSpanId.ToHexString(),tags=a.TagObjects.ToDictionary(t=>t.Key,t=>t.Value)};stages.Enqueue(row);lock(stageLock)stageWriter.WriteLine(JsonSerializer.Serialize(row));}};ActivitySource.AddActivityListener(listener);
var observations=new ConcurrentQueue<object>();var alias=selected.ToDictionary(x=>x.Path,x=>x.Alias,StringComparer.Ordinal);
await using var shell=new MacShellCatalogSession(data,new ThumbnailImageCodec(),o=>observations.Enqueue(new{alias=alias.GetValueOrDefault(o.Source,"other"),atMs=clock.Elapsed.TotalMilliseconds,o.RequestedSeconds,o.ActualValue,o.ActualTimescale,o.DurationSeconds,o.Status,o.Mechanism}));
var startup=await shell.CreateAsync(Path.Combine(data,"Catalog"));if(!startup.IsReady)throw new IOException("Isolated Catalog failed startup.");
var discoveryStart=clock.Elapsed.TotalMilliseconds;var scan=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");var discoveryMs=clock.Elapsed.TotalMilliseconds-discoveryStart;Console.WriteLine($"discovery {discoveryMs:F1} ms");
var assets=selected.Select(x=>(Source:x,Asset:scan.Assets.Single(a=>Path.GetFileName(a.RelativePath)==Path.GetFileName(x.Path)))).ToArray();var ids=assets.Select(x=>x.Asset.AssetId).ToArray();var idAliases=assets.ToDictionary(x=>x.Asset.AssetId,x=>x.Source.Alias);
if(Environment.GetEnvironmentVariable("LF036_CANCEL")=="1")
{
 var original=await shell.DemandThumbnailsAsync(ids,DerivedWorkPriority.Visible,default);
 var wait=Stopwatch.StartNew();while(MacThumbnailRenderer.lf_preview_readers()==0&&!original!.Completion.IsCompleted&&wait.Elapsed<TimeSpan.FromSeconds(5))await Task.Delay(1);
 var observedActive=MacThumbnailRenderer.lf_preview_readers()>0;var cancelAt=clock.Elapsed.TotalMilliseconds;original!.Cancel();
 var replacement=await shell.DemandThumbnailsAsync(ids.Reverse().ToArray(),DerivedWorkPriority.Visible,default);await replacement!.Completion.WaitAsync(TimeSpan.FromSeconds(30));
 await shell.DisposeAsync();
 File.WriteAllText(report,JsonSerializer.Serialize(new{observedActive,cancelToReplacementAndCloseMs=clock.Elapsed.TotalMilliseconds-cancelAt,originalStatus=original.Progress.Status,replacementResults=replacement.Results.Select(x=>new{alias=idAliases[x.AssetId],x.Outcome,x.Thumbnail}),nativeReadersAfterClose=MacThumbnailRenderer.lf_preview_readers(),temporaryFiles=Directory.EnumerateFiles(data,"*.lightflow",SearchOption.AllDirectories).Count(),stages,samples},new JsonSerializerOptions{WriteIndented=true}));
 return;
}
var rounds=new List<object>();
for(int round=0;round<2;round++)
{
 var start=clock.Elapsed.TotalMilliseconds;var batch=await shell.DemandThumbnailsAsync(ids,DerivedWorkPriority.Visible,default);var complete=new ConcurrentDictionary<Guid,double>();
 void Progress(object? sender,DerivedWorkProgress p){events.Enqueue(new{round,atMs=clock.Elapsed.TotalMilliseconds-start,p.Pending,p.Running,p.Completed});foreach(var r in batch!.Results)complete.TryAdd(r.AssetId,clock.Elapsed.TotalMilliseconds-start);}
 batch!.ProgressChanged+=Progress;await batch.Completion.WaitAsync(TimeSpan.FromSeconds(240));Progress(null,batch.Progress);batch.ProgressChanged-=Progress;
 var done=clock.Elapsed.TotalMilliseconds-start;var readStart=clock.Elapsed.TotalMilliseconds;var thumbnails=await shell.ReadThumbnailsAsync(ids,default);var projectionMs=clock.Elapsed.TotalMilliseconds-readStart;
 rounds.Add(new{round,elapsedMs=done,projectionMs,results=batch.Results.Select(r=>new{alias=idAliases[r.AssetId],r.Outcome,r.Metadata,r.Thumbnail,r.ThumbnailFailureReason,completedMs=complete.GetValueOrDefault(r.AssetId),thumbnailState=thumbnails.GetValueOrDefault(r.AssetId)?.State}),rss=process.WorkingSet64,maxSampledRss=maxRss,maxSampledReaders=maxReaders,nativeReaders=MacThumbnailRenderer.lf_preview_readers()});
 File.WriteAllText(report,JsonSerializer.Serialize(new{discoveryMs,discovered=scan.Assets.Count,rounds,events,observations,stages,samples,assetAliases=idAliases.ToDictionary(x=>x.Key.ToString("N"),x=>x.Value),cpuSeconds=(process.TotalProcessorTime-cpuStart).TotalSeconds},new JsonSerializerOptions{WriteIndented=true}));
 Console.WriteLine($"round {round}: {done:F1} ms",System.Globalization.CultureInfo.InvariantCulture);
}
record Source(string Alias,string Path,long Size);
