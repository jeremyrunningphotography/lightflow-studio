using Lightflow.Application;
using LightflowStudio;
using Xunit;

namespace Lightflow.Application.Tests;

public sealed class BrowserLocationMappingTests
{
    [Fact]
    public void ExistingMostSpecificMappingWinsWithoutCreatingAnAnchor()
    {
        var parent=Guid.NewGuid(); var nested=Guid.NewGuid();
        MediaRootInfo[] roots=[new(parent,"Parent","/Media",MediaRootAvailability.Online),
            new(nested,"Nested","/Media/Trip",MediaRootAvailability.Online)];
        Assert.Equal(new BrowserCatalogScope(nested,"Day 2"),BrowserLocationMapping.Find("/Media/Trip/Day 2",roots,StringComparison.Ordinal));
        Assert.Equal(new BrowserCatalogScope(parent,""),BrowserLocationMapping.Find("/Media",roots,StringComparison.Ordinal));
        Assert.Null(BrowserLocationMapping.Find("/Media-other",roots,StringComparison.Ordinal));
        Assert.Null(BrowserLocationMapping.Find("/Uncataloged",roots,StringComparison.Ordinal));
        Assert.Equal(2,roots.Length);
    }
}
