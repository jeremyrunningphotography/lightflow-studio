using Lightflow.Application;
using Lightflow.Domain;
using Lightflow.Platform.MacOS;
using Xunit;
using System.Runtime.InteropServices;

namespace Lightflow.Platform.MacOS.Tests;

public sealed class MacStorageLocationAssessorTests
{
    private static StorageAssessmentRequest Request(string path) => new(Guid.NewGuid(), 17,
        StorageRole.ActiveCatalog, StorageOperation.Create, path);

    [Theory]
    [InlineData("relative/path")]
    [InlineData("/absolute\0truncated")]
    [InlineData("")]
    public async Task InvalidPathsFailWithoutNativeAdmission(string path)
    {
        using var assessor = new MacStorageLocationAssessor([]);
        var request = Request(path);
        var result = await assessor.AssessAsync(request);
        Assert.Same(request, result.Request);
        Assert.Equal(StorageAssessmentStatus.Failed, result.Status);
        Assert.Null(result.Identity);
    }

    [Fact]
    public async Task CancellationPreservesRequestAndCannotAdmit()
    {
        using var assessor = new MacStorageLocationAssessor([]);
        var request = Request("/never-created-lf006");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await assessor.AssessAsync(request, cancellation.Token);
        Assert.Same(request, result.Request);
        Assert.Equal(StorageAssessmentStatus.Cancelled, result.Status);
        Assert.Equal(StorageLocationDecision.Cancelled,
            StorageLocationPolicy.Evaluate(request, result, DateTimeOffset.UtcNow).Decision);
    }

    [Fact]
    public async Task DisposedProviderFailsClosed()
    {
        var assessor = new MacStorageLocationAssessor([]);
        assessor.Dispose();
        assessor.Dispose();
        var result = await assessor.AssessAsync(Request("/never-created-lf006"));
        Assert.Equal(StorageAssessmentStatus.Failed, result.Status);
    }

    [Fact]
    public void LifetimeCannotBeUnbounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacStorageLocationAssessor([], lifetime: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MacStorageLocationAssessor([], lifetime: TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void BoundaryConfigurationRejectsRelativeOrTruncatedPaths()
    {
        Assert.Throws<ArgumentException>(() => new MacStorageLocationAssessor(["relative"]));
        Assert.Throws<ArgumentException>(() => new MacStorageLocationAssessor(["/absolute\0truncated"]));
    }

    [DllImport("libLightflowStorage.dylib", EntryPoint = "lf_storage_active_descriptors")]
    private static extern int ActiveDescriptors();

    [Fact]
    public async Task NativeMountAnchorsAreStableAndReleasedWithOwner()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var protectedRoot = Directory.CreateDirectory(Path.Combine(root, "protected")).FullName;
        var catalog = Directory.CreateDirectory(Path.Combine(root, "catalog")).FullName;
        try
        {
            var baseline = ActiveDescriptors();
            for (var cycle = 0; cycle < 20; cycle++)
            {
                using (var assessor = new MacStorageLocationAssessor([protectedRoot]))
                {
                    var request = Request(Path.Combine(catalog, "not-created.db"));
                    var first = await assessor.AssessAsync(request);
                    var second = await assessor.AssessAsync(request);
                    Assert.Equal(StorageAssessmentStatus.Complete, first.Status);
                    Assert.Equal(first.Identity, second.Identity);
                    Assert.NotEqual(first.AssessmentId, second.AssessmentId);
                    Assert.Equal(baseline + 1, ActiveDescriptors());
                    Assert.False(File.Exists(request.RequestedLocation));
                }
                Assert.Equal(baseline, ActiveDescriptors());
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
