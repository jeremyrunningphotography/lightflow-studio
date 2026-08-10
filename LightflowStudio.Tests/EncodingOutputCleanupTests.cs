using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class EncodingOutputCleanupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-cleanup-").FullName;

    [Fact]
    public void DeleteIncomplete_RemovesPartialMediaAndResumeIdentity()
    {
        var output = Path.Combine(_root, "cancelled.mp4");
        File.WriteAllText(output, "partial");
        File.WriteAllText(EncodingOutputIdentityStore.PathFor(output), "stale");

        EncodingOutputCleanup.DeleteIncomplete(output);

        Assert.False(File.Exists(output));
        Assert.False(File.Exists(EncodingOutputIdentityStore.PathFor(output)));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
