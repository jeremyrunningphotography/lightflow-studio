using Xunit;

namespace LightflowStudio.Tests;

public sealed class ShutdownLifecycleRegressionTests
{
    [Fact]
    public void FlyleafDisposal_ReleasesDispatcherOwnedPresentationBeforeItsFirstAsyncWait()
    {
        var source = File.ReadAllText(PathAtRoot("LightflowStudio", "FlyleafPlaybackBackend.cs"));
        var start = source.IndexOf("public async ValueTask DisposeAsync()", StringComparison.Ordinal);
        var end = source.IndexOf("internal static class PlaybackDependencyLocator", start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "FlyleafPlaybackBackend.DisposeAsync was not found.");
        var body = source[start..end];

        var closePlayer = body.IndexOf("var playerDisposal = ClosePlayerAsync();", StringComparison.Ordinal);
        var releasePresentation = body.IndexOf("CloseOffscreenWindow();", StringComparison.Ordinal);
        var firstAwait = body.IndexOf("await _audio.DisposeAsync()", StringComparison.Ordinal);
        Assert.True(closePlayer >= 0 && releasePresentation > closePlayer && firstAwait > releasePresentation,
            "All dispatcher-owned Flyleaf presentation must be detached before disposal first yields.");
        Assert.DoesNotContain("RunOnUi", body[firstAwait..]);
    }

    [Fact]
    public void ApplicationExit_RecordsPlaybackAndStorageDisposalStageBoundaries()
    {
        var source = File.ReadAllText(PathAtRoot("LightflowStudio", "App.xaml.cs"));
        Assert.Contains("Application.Exit entered; disposing playback", source);
        Assert.Contains("Playback disposal completed; disposing storage", source);
        Assert.Contains("Storage disposal completed", source);
    }

    private static string PathAtRoot(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(current, "Directory.Build.props")))
            current = Directory.GetParent(current)?.FullName
                ?? throw new DirectoryNotFoundException("Repository root not found.");
        return Path.Combine([current, .. parts]);
    }
}
