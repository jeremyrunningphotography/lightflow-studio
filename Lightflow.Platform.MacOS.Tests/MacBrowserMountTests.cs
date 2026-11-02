using Lightflow.Application;
using Lightflow.Platform.MacOS;
using Xunit;

namespace Lightflow.Platform.MacOS.Tests;
public sealed class MacBrowserMountTests
{
    [Fact]
    public void NativeFactsClassifyWithoutInferringFromNamesAndDeduplicateOnlyEstablishedIdentity()
    {
        var mounts=new MacBrowserMount[] {
            new("/","Macintosh HD","system","/dev/disk3s1s1","apfs",true,true),
            new("/Volumes/Startup","Alias","system","/dev/disk3s1s1","apfs",true,true),
            new("/Volumes/Data","Macintosh HD - Data","distinct","/dev/disk3s5","apfs",true,true),
            new("/Volumes/NAS","JRP","local","/dev/disk9s1","apfs",true,false),
            new("/Volumes/JRP","JRP","","//person@JRVault/JRP","smbfs",false,null),
            new("/Volumes/JRP-1","JRP","","opaque","nfs",false,null) };
        var locations=MacBrowserMounts.Locations("/Users/test-account",mounts);
        Assert.Equal("test-account",locations[0].Name);
        Assert.Equal(4,locations.Count(location=>location.Section==BrowserLocationSection.Favorites));
        Assert.All(locations,location=>Assert.False(location.CanPrefetch));
        Assert.DoesNotContain(locations,location=>location.Path=="/Volumes/Startup");
        Assert.Contains(locations,location=>location.Path=="/Volumes/Data");
        Assert.Equal(BrowserLocationKind.ExternalVolume,locations.Single(location=>location.Path=="/Volumes/NAS").Kind);
        var share=locations.Single(location=>location.Path=="/Volumes/JRP");
        Assert.Equal(BrowserLocationSection.Network,share.Section); Assert.Equal("jrvault",share.Server);
        Assert.Null(locations.Single(location=>location.Path=="/Volumes/JRP-1").Server);
        Assert.Equal(3,locations.Count(location=>location.Name=="JRP"));
    }
    [Theory]
    [InlineData("//user:secret@server/share","smbfs",false,"server")]
    [InlineData("//[fe80::1]/share","smbfs",false,"fe80::1")]
    [InlineData("//server/share","nfs",false,null)]
    [InlineData("//server/share","smbfs",true,null)]
    [InlineData("JRVault/JRP","smbfs",false,null)]
    [InlineData("//server/","smbfs",false,null)]
    [InlineData("//bad host/share","smbfs",false,null)]
    public void ServerGroupingRequiresValidatedNativeSmbSource(string source,string fs,bool local,string? expected)
        => Assert.Equal(expected,MacBrowserMounts.VerifiedServer(new("/Volumes/test","test","",source,fs,local,null)));
    [Fact]
    public async Task RealNativeSnapshotHasOneUsableSystemIdentityAndCancellationIsHonored()
    {
        if(!OperatingSystem.IsMacOS())return;
        var facts=MacBrowserMounts.Read(); Assert.Contains(facts,mount=>mount.Path=="/" && mount.Local);
        var provider=new MacBrowserLocationProvider(); var locations=await provider.GetLocationsAsync(default);
        var root=Assert.Single(locations.Where(location=>location.Kind==BrowserLocationKind.SystemVolume));
        Assert.Equal("/",root.Path); Assert.NotEqual("/",root.Name);
        using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>provider.GetLocationsAsync(cancelled.Token));
    }
}
