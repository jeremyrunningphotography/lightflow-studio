using System.Collections.Concurrent;
using System.Diagnostics;
using Lightflow.Platform.MacOS;
using LightflowStudio;
using Xunit;
namespace Lightflow.Platform.MacOS.Tests;
[Collection("Native storage descriptor ownership")]
public sealed class CachedBrowserCatalogTests
{
    [Fact]
    public async Task CachedScopeIsSourceIoFreeAndReconciliationStillDetectsSameTimestampChangeAddAndRemove()
    {
        var data=Path.Combine(Environment.GetEnvironmentVariable("LIGHTFLOW_TEST_ROOT")??Path.Combine(AppContext.BaseDirectory,"BrowserFixtures"),"lf036-cached-"+Guid.NewGuid().ToString("N"));
        var media=Path.Combine(data,"Media");Directory.CreateDirectory(Path.Combine(data,"Catalog"));Directory.CreateDirectory(Path.Combine(media,"Child"));
        var changed=Path.Combine(media,"changed.jpg");var removed=Path.Combine(media,"removed.jpg");
        await File.WriteAllTextAsync(changed,"before");await File.WriteAllTextAsync(removed,"remove");await File.WriteAllTextAsync(Path.Combine(media,"Child","nested.jpg"),"nested");
        var stages=new ConcurrentQueue<string>();using var listener=new ActivityListener{ShouldListenTo=s=>s.Name=="LightflowStudio.Browser",Sample=(ref ActivityCreationOptions<ActivityContext> _)=>ActivitySamplingResult.AllData,ActivityStopped=a=>stages.Enqueue(a.OperationName)};ActivitySource.AddActivityListener(listener);
        Guid root,id;string folder;SourceFingerprint? fingerprint;
        await using(var shell=new MacShellCatalogSession(data))
        {
            Assert.True((await shell.CreateAsync(Path.Combine(data,"Catalog"))).IsReady);var scan=await shell.DiscoverBrowserFolderAsync(media,Guid.Empty,"");Assert.Equal(2,scan.Assets.Count);root=scan.RootId;folder=scan.RelativeFolder;
            var original=scan.Assets.Single(a=>a.RelativePath.EndsWith("changed.jpg",StringComparison.Ordinal));id=original.AssetId;fingerprint=original.Fingerprint;
            File.Delete(removed);var modified=File.GetLastWriteTimeUtc(changed);await File.WriteAllTextAsync(changed,"after!");File.SetLastWriteTimeUtc(changed,modified);await File.WriteAllTextAsync(Path.Combine(media,"added.jpg"),"added");
            while(stages.TryDequeue(out _)){}
            var known=await shell.ReadCachedBrowserFolderAsync(root,folder);Assert.Equal(2,known.Assets.Count);Assert.Equal(fingerprint,known.Assets.Single(a=>a.AssetId==id).Fingerprint);Assert.DoesNotContain(stages,s=>s.Contains("filesystem")||s.Contains("fingerprint")||s=="reconciliation");
            var verified=await shell.DiscoverBrowserFolderAsync(media,root,folder);Assert.Equal(2,verified.Assets.Count);Assert.DoesNotContain(verified.Assets,a=>a.RelativePath.EndsWith("removed.jpg",StringComparison.Ordinal));var current=verified.Assets.Single(a=>a.AssetId==id);Assert.NotEqual(fingerprint,current.Fingerprint);Assert.Equal(original.LastWriteUtcTicks,current.LastWriteUtcTicks);Assert.Contains(verified.Assets,a=>a.RelativePath.EndsWith("added.jpg",StringComparison.Ordinal));
            Assert.DoesNotContain((await shell.ReadCachedBrowserFolderAsync(root,folder)).Assets,a=>a.RelativePath.EndsWith("removed.jpg",StringComparison.Ordinal));
            await shell.SetBrowserRecursiveAsync(root,folder,true);await shell.DiscoverBrowserFolderAsync(media,root,folder);while(stages.TryDequeue(out _)){}var recursive=await shell.ReadCachedBrowserFolderAsync(root,folder);Assert.True(recursive.Recursive);Assert.Equal(3,recursive.Assets.Count);
            await Assert.ThrowsAsync<ArgumentException>(()=>shell.ReadCachedBrowserFolderAsync(root,"../escape"));
            Directory.Move(media,media+"-offline");Assert.Equal(3,(await shell.ReadCachedBrowserFolderAsync(root,folder)).Assets.Count);await Assert.ThrowsAsync<IOException>(()=>shell.DiscoverBrowserFolderAsync(media,root,folder));Directory.Move(media+"-offline",media);
        }
        await using(var shell=new MacShellCatalogSession(data)){Assert.True((await shell.StartAsync()).IsReady);var known=await shell.ReadCachedBrowserFolderAsync(root,folder);Assert.True(known.Recursive);Assert.Contains(known.Assets,a=>a.AssetId==id&&a.Fingerprint!=fingerprint);}
    }
}
