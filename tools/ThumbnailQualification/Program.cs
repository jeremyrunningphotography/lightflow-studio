using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;
using LightflowStudio;
using Lightflow.UI;
using Lightflow.Platform.MacOS;

if(args.Length!=3||!args.All(Path.IsPathFullyQualified))throw new ArgumentException("Use absolute isolated-data, disposable-media, report-json paths.");
var data=args[0];var media=args[1];var report=args[2];Directory.CreateDirectory(data);Directory.CreateDirectory(media);Directory.CreateDirectory(Path.GetDirectoryName(report)!);
var codec=new ThumbnailImageCodec();var observations=new ConcurrentQueue<MacThumbnailObservation>();
// Generated source vectors only, no owner/personal media.
foreach(var (name,space,alpha) in new[]{("still.png",SKColorSpace.CreateSrgb(),false),("linear-alpha.png",SKColorSpace.CreateSrgbLinear(),true),("still.jpg",SKColorSpace.CreateSrgb(),false),("still.webp",SKColorSpace.CreateSrgb(),false)})
{
    var path=Path.Combine(media,name);if(File.Exists(path)){space.Dispose();continue;}
    using(space)using(var bitmap=new SKBitmap(new SKImageInfo(120,80,SKColorType.Rgba8888,alpha?SKAlphaType.Unpremul:SKAlphaType.Opaque,space)))
    {
        for(int y=0;y<80;y++)for(int x=0;x<120;x++)bitmap.SetPixel(x,y,new SKColor((byte)(x*2),(byte)(y*3),32,alpha?(byte)128:(byte)255));
        using var image=SKImage.FromBitmap(bitmap);using var encoded=image.Encode(name.EndsWith("jpg")?SKEncodedImageFormat.Jpeg:name.EndsWith("webp")?SKEncodedImageFormat.Webp:SKEncodedImageFormat.Png,95);File.WriteAllBytes(path,encoded.ToArray());
    }
}
var results=new List<object>();Guid catalogId;var worker=new Stopwatch();
async Task Run(bool reopen,bool rebuild=false)
{
    var stamp=Stopwatch.StartNew();
    await using var shell=new MacShellCatalogSession(data,codec,observations.Enqueue);
    var startup=reopen?await shell.StartAsync():await shell.CreateAsync(Path.Combine(data,"Catalog"));
    if(!startup.IsReady)throw new IOException(startup.Diagnostic);catalogId=startup.CatalogId!.Value;
    var scan=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");
    var ids=scan.Assets.Take(192).Select(x=>x.AssetId).ToArray();
    worker.Restart();var batch=await shell.DemandThumbnailsAsync(ids,DerivedWorkPriority.Visible,CancellationToken.None);
    await batch!.Completion.WaitAsync(TimeSpan.FromSeconds(90));worker.Stop();
    var thumbnails=await shell.ReadThumbnailsAsync(ids,CancellationToken.None);
    foreach(var asset in scan.Assets.Take(192))
    {
        var thumbnail=thumbnails.GetValueOrDefault(asset.AssetId);int width=0,height=0;
        if(thumbnail?.Path is { } path){using var pixels=SKBitmap.Decode(path);width=pixels.Width;height=pixels.Height;}
        var source=Path.Combine(media,Path.GetFileName(asset.RelativePath));
        results.Add(new{reopen,rebuild,catalogId,asset.AssetId,name=Path.GetFileName(asset.RelativePath),sourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant(),thumbnail,width,height});
    }
    results.Add(new{reopen,rebuild,elapsedMs=worker.Elapsed.TotalMilliseconds,totalMs=stamp.Elapsed.TotalMilliseconds,discovered=scan.Assets.Count,demanded=ids.Length,batch.Progress,componentResults=batch.Results,processRss=Process.GetCurrentProcess().WorkingSet64,nativeReaders=MacThumbnailRenderer.lf_preview_readers()});
}
Directory.CreateDirectory(Path.Combine(data,"Catalog"));await Run(false);await Run(true);
if(Directory.GetFiles(media).Length<100){Directory.Delete(Path.Combine(data,"Previews"),true);await Run(true,true);}
// Native source replacement and unavailable-state checks use only a generated disposable fixture.
var sourceChecks=new List<object>();var replacement=Path.Combine(media,"still.jpg");var original=File.ReadAllBytes(replacement);
try
{
    await using var shell=new MacShellCatalogSession(data,codec,observations.Enqueue);var startup=await shell.StartAsync();
    var scan=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");var asset=scan.Assets.Single(x=>Path.GetFileName(x.RelativePath)=="still.jpg");
    var initial=await shell.DemandThumbnailsAsync([asset.AssetId],DerivedWorkPriority.Visible,default);await initial!.Completion;
    var before=(await shell.ReadThumbnailsAsync([asset.AssetId],default))[asset.AssetId];
    using(var bitmap=new SKBitmap(90,60)){bitmap.Erase(SKColors.Coral);using var image=SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SKEncodedImageFormat.Jpeg,85);File.WriteAllBytes(replacement,encoded.ToArray());}
    await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");
    var stale=(await shell.ReadThumbnailsAsync([asset.AssetId],default))[asset.AssetId];
    if(stale.Path is not null)throw new InvalidDataException("Stale source pixels escaped identity validation.");
    var batch=await shell.DemandThumbnailsAsync([asset.AssetId],DerivedWorkPriority.Visible,default);await batch!.Completion;
    var after=(await shell.ReadThumbnailsAsync([asset.AssetId],default))[asset.AssetId];
    if(after.Path is null||after.Path==before.Path)throw new InvalidDataException("Source replacement did not rebuild its artifact.");
    File.Move(replacement,replacement+".hidden");
    try
    {
        await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");var unavailable=await shell.DemandThumbnailsAsync([asset.AssetId],DerivedWorkPriority.Visible,default);await unavailable!.Completion;
        var missing=(await shell.ReadThumbnailsAsync([asset.AssetId],default))[asset.AssetId];
        if(missing.State!="Unavailable")throw new InvalidDataException("Missing source was not truthfully labeled.");
        sourceChecks.Add(new{asset.AssetId,beforePath=before.Path,stale,after,missing,readers=MacThumbnailRenderer.lf_preview_readers()});
    }finally{File.Move(replacement+".hidden",replacement);}
}finally{File.WriteAllBytes(replacement,original);}
var cancellations=new List<object>();
var preferredVideo=Path.Combine(media,"cfr-h264.mp4");
var video=File.Exists(preferredVideo)?preferredVideo:Directory.EnumerateFiles(media,"*.mp4").FirstOrDefault();
if(video is not null)
{
    var renderer=new MacThumbnailRenderer(codec,observations.Enqueue);
    for(int i=0;i<12;i++)
    {
        using var cancel=new CancellationTokenSource();var target=Path.Combine(data,$"cancel-{i}.jpg");var elapsed=Stopwatch.StartNew();var cancelled=false;
        var work=renderer.RenderAsync(video,"video",TimeSpan.FromSeconds(.3),target,cancel.Token);
        while(!work.IsCompleted&&MacThumbnailRenderer.lf_preview_readers()==0&&elapsed.Elapsed<TimeSpan.FromSeconds(2))await Task.Delay(1);
        var admitted=MacThumbnailRenderer.lf_preview_readers()>0;cancel.Cancel();
        try{await work;}catch(OperationCanceledException){cancelled=true;}
        cancellations.Add(new{i,admitted,cancelled,elapsedMs=elapsed.Elapsed.TotalMilliseconds,readers=MacThumbnailRenderer.lf_preview_readers()});File.Delete(target);
    }
}
File.WriteAllText(report,JsonSerializer.Serialize(new{results,sourceChecks,cancellations,observations=observations.ToArray(),nativeReadersAfterClose=MacThumbnailRenderer.lf_preview_readers()},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(report);
