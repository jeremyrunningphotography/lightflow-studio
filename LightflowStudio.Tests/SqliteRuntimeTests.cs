using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class SqliteRuntimeTests
{
    [Theory]
    [InlineData("3.41.2", false)]
    [InlineData("3.50.1", false)]
    [InlineData("3.50.2", true)]
    [InlineData("3.53.3", true)]
    [InlineData("invalid", false)]
    public void RuntimeSecurityBoundary_RejectsAffectedVersions(string version, bool patched)
        => Assert.Equal(patched, CatalogPackageRuntimeVerifier.IsPatchedRuntime(version));

    [Fact]
    public async Task RealProvider_QualifiesCatalogPreviewAndLoadedNativeRuntime()
        => Assert.True(await CatalogPackageRuntimeVerifier.VerifyAsync());
}
