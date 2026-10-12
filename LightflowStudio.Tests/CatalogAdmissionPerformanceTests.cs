using System.Diagnostics;
using LightflowStudio;
using Xunit;
using Xunit.Abstractions;

namespace LightflowStudio.Tests;

public sealed class CatalogAdmissionPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task MeasureIsolatedCreateAndExistingStartup()
    {
        var root = Path.Combine(Path.GetTempPath(), "lf005-timing-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Two warmups followed by eight independently initialized profiles. The identical
            // fixture is run against b63f98c and the correction; no product budget is asserted.
            for (var i = 0; i < 10; i++)
            {
                var profile = Path.Combine(root, i.ToString());
                var watch = Stopwatch.StartNew();
                var created = await LightflowStorageCoordinator.StartAsync(profile);
                var createMs = watch.Elapsed.TotalMilliseconds;
                Assert.True(created.IsReady, created.Diagnostic);
                var id = created.Coordinator!.CatalogSession.Identity.CatalogId;
                watch.Restart();
                await created.Coordinator.DisposeAsync();
                var createShutdownMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                var reopened = await LightflowStorageCoordinator.StartAsync(profile);
                var reopenMs = watch.Elapsed.TotalMilliseconds;
                Assert.True(reopened.IsReady, reopened.Diagnostic);
                Assert.Equal(id, reopened.Coordinator!.CatalogSession.Identity.CatalogId);
                watch.Restart();
                await reopened.Coordinator.DisposeAsync();
                var reopenShutdownMs = watch.Elapsed.TotalMilliseconds;
                output.WriteLine($"sample={i}; warmup={i < 2}; createMs={createMs:F3}; reopenMs={reopenMs:F3}; createShutdownMs={createShutdownMs:F3}; reopenShutdownMs={reopenShutdownMs:F3}");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
