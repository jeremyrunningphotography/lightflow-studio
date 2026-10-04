using Lightflow.Actions;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ReconciledActionInventoryTests
{
    [Fact]
    public void AcceptedActionFamiliesRemainDiscoverableAndNeutral()
    {
        var descriptors = PlayerActions.Descriptors.Concat(BrowserActions.Descriptors)
            .Concat(ReviewPresentationActions.Descriptors).Concat(ReviewShellActions.Descriptors).ToArray();
        string[] expected = ["player.play-pause", "player.step-frame", "player.color-bypass", "player.set-boundary",
            "player.traverse-review", "subclip.create-from-working-range", "marker.add", "marker.navigate",
            "browser.navigate-selection", "browser.open-current", "asset.set-rating", "asset.set-flag", "asset.step-flag", "asset.set-color-label",
            "player.volume", "player.review-speed", "viewer.zoom", "viewer.step-zoom", "player.presentation-toggle",
            "browser.step-thumbnail-size", "review.toggle-right-panel", "review.show-panel", "export.open"];
        Assert.Equal(expected.Order(), descriptors.Select(d => d.Id).Order());
        Assert.Equal(expected.Length, descriptors.Select(d => d.Id).Distinct().Count());
        foreach (var id in new[] { BrowserActions.SetRating, BrowserActions.SetFlag, BrowserActions.SetColorLabel })
            Assert.Equal(ActionExecutionPolicy.Serialized, descriptors.Single(d => d.Id == id).Execution);
        Assert.Equal(4, (int)ActionArgumentShape.BrowserNavigation);
        Assert.Equal(13, (int)ActionUnavailableReason.NoBrowser);
        Assert.DoesNotContain(typeof(ActionDescriptor).Assembly.GetReferencedAssemblies(), reference =>
            reference.Name!.Contains("Windows", StringComparison.OrdinalIgnoreCase) || reference.Name.StartsWith("Presentation", StringComparison.Ordinal));
    }
}
