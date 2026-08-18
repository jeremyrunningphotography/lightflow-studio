using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserStatusPresentationTests
{
    [Fact]
    public void Describe_EmptyFolderReportsNoMedia() =>
        Assert.Equal("No media in this folder", BrowserStatusPresentation.Describe(0, 0, 0, 0, false, 0));

    [Fact]
    public void Describe_UnfilteredFolderReportsAPlainItemCount() =>
        Assert.Equal("42 items", BrowserStatusPresentation.Describe(42, 42, 0, 0, false, 0));

    [Fact]
    public void Describe_SingleItemUsesSingularNoun() =>
        Assert.Equal("1 item", BrowserStatusPresentation.Describe(1, 1, 0, 0, false, 0));

    [Fact]
    public void Describe_FilteredFolderReportsVisibleOfTotal() =>
        Assert.Equal("3 of 42 items", BrowserStatusPresentation.Describe(3, 42, 0, 0, false, 0));

    [Fact]
    public void Describe_SelectionWithoutKnownSizeOmitsTheSizeParenthetical() =>
        Assert.Equal("10 items · 2 selected", BrowserStatusPresentation.Describe(10, 10, 2, 0, false, 0));

    [Fact]
    public void Describe_SelectionWithSizeIncludesFormattedSize() =>
        Assert.Equal("10 items · 2 selected (1 KB)", BrowserStatusPresentation.Describe(10, 10, 2, 1024, false, 0));

    [Fact]
    public void Describe_ActiveGenerationWithoutARemainingCountUsesAPlainEllipsis() =>
        Assert.Equal("10 items · Generating previews…", BrowserStatusPresentation.Describe(10, 10, 0, 0, true, 0));

    [Fact]
    public void Describe_ActiveGenerationWithARemainingCountIncludesIt() =>
        Assert.Equal("10 items · Generating previews… (4 left)", BrowserStatusPresentation.Describe(10, 10, 0, 0, true, 4));

    [Fact]
    public void Describe_CombinesFilteredCountSelectionAndActivityInOneRestrainedLine() =>
        Assert.Equal("3 of 42 items · 2 selected (2.5 MB) · Generating previews… (5 left)",
            BrowserStatusPresentation.Describe(3, 42, 2, (long)(2.5 * 1024 * 1024), true, 5));

    [Fact]
    public void Describe_NotGeneratingNeverMentionsActivityEvenWithARemainingCount() =>
        Assert.Equal("10 items", BrowserStatusPresentation.Describe(10, 10, 0, 0, false, 4));
}
