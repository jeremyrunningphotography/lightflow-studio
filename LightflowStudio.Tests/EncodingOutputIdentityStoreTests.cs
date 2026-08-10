using System.Text.Json;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class EncodingOutputIdentityStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-output-identity-").FullName;
    private string Cache => Path.Combine(_root, "cache");
    private string Output => Path.Combine(_root, "exports", "clip.mp4");
    private static EncodingOutputIdentity Identity => new("source.mp4", 123, 456, 10, 20, "OPTIONS");

    [Fact]
    public void Save_PersistsInCentralCacheWithoutCreatingOutputSidecar()
    {
        EncodingOutputIdentityStore.Save(Output, Identity, Cache);

        var cachePath = EncodingOutputIdentityStore.PathFor(Output, Cache);
        Assert.True(File.Exists(cachePath));
        Assert.StartsWith(Path.GetFullPath(Cache), Path.GetFullPath(cachePath));
        Assert.False(File.Exists(EncodingOutputIdentityStore.LegacyPathFor(Output)));
        Assert.True(EncodingOutputIdentityStore.Matches(Output, Identity, Cache));
    }

    [Fact]
    public void DifferentOutputPaths_UseDifferentDeterministicCacheEntries()
    {
        var first = EncodingOutputIdentityStore.PathFor(Output, Cache);
        var sameIgnoringCase = EncodingOutputIdentityStore.PathFor(Output.ToUpperInvariant(), Cache);
        var other = EncodingOutputIdentityStore.PathFor(Path.Combine(_root, "other", "clip.mp4"), Cache);

        Assert.Equal(first, sameIgnoringCase, ignoreCase: true);
        Assert.NotEqual(first, other);
        Assert.EndsWith(".json", first);
    }

    [Fact]
    public void Matches_MigratesAndRemovesLegacySidecar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
        var legacy = EncodingOutputIdentityStore.LegacyPathFor(Output);
        File.WriteAllText(legacy, JsonSerializer.Serialize(Identity));

        Assert.True(EncodingOutputIdentityStore.Matches(Output, Identity, Cache));
        Assert.False(File.Exists(legacy));
        Assert.True(File.Exists(EncodingOutputIdentityStore.PathFor(Output, Cache)));
    }

    [Fact]
    public void MalformedOrMismatchedCacheDoesNotMatch()
    {
        Directory.CreateDirectory(Cache);
        File.WriteAllText(EncodingOutputIdentityStore.PathFor(Output, Cache), "not json");
        Assert.False(EncodingOutputIdentityStore.Matches(Output, Identity, Cache));

        EncodingOutputIdentityStore.Save(Output, Identity, Cache);
        Assert.False(EncodingOutputIdentityStore.Matches(Output, Identity with { OutTicks = 30 }, Cache));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
