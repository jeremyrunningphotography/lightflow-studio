using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class EncodingOutputCleanupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-cleanup-").FullName;
    private string Cache => Path.Combine(_root, "cache");

    [Fact]
    public void DeleteIncomplete_RemovesPartialMediaAndResumeIdentity()
    {
        var output = Path.Combine(_root, "cancelled.mp4");
        File.WriteAllText(output, "partial");
        Directory.CreateDirectory(Cache);
        File.WriteAllText(EncodingOutputIdentityStore.PathFor(output, Cache), "stale");
        File.WriteAllText(EncodingOutputIdentityStore.LegacyPathFor(output), "legacy");

        EncodingOutputCleanup.DeleteIncomplete(output, Cache);

        Assert.False(File.Exists(output));
        Assert.False(File.Exists(EncodingOutputIdentityStore.PathFor(output, Cache)));
        Assert.False(File.Exists(EncodingOutputIdentityStore.LegacyPathFor(output)));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
