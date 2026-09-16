using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

public class PremiereUiContractTests
{
    private static string Source(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "LightflowStudio", "LightflowStudio.csproj")))
                return Path.Combine(directory.FullName, "LightflowStudio", name);
        throw new InvalidOperationException("Repository root not found.");
    }
    [Fact]
    public void DisconnectedSendUsesSharedStyledActionInsteadOfStockMessageBox()
    {
        var source = File.ReadAllText(Source("MainWindow.Premiere.cs"));
        Assert.DoesNotContain("MessageBox", source);
        Assert.Contains("NoticeDialog.OfferAction", source);
        Assert.Contains("Open Integration Settings", source);
        var dialog = XDocument.Load(Source("NoticeDialog.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var cancel = dialog.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "CancelButton");
        Assert.Equal("True", (string?)cancel.Attribute("IsCancel"));
        Assert.Equal("Not now", (string?)cancel.Attribute("Content"));
        Assert.Contains(dialog.Descendants(), e => (string?)e.Attribute("Style") == "{StaticResource Card}");
        var actionCode = File.ReadAllText(Source("NoticeDialog.xaml.cs"));
        Assert.Contains("FindResource(\"PrimaryButton\")", actionCode);
        Assert.Contains("WindowAppearance.EnableDarkTitleBar", actionCode);
    }
    [Fact]
    public void SettingsKeepsSetupDetailsExpandableAndDestinationChoicesInSend()
    {
        var settings = XDocument.Load(Source("PremiereIntegrationWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.DoesNotContain(settings.Descendants(), e => new[] { "Bins", "NewBinName", "SendButton", "PairingPath" }.Contains((string?)e.Attribute(x + "Name")));
        var copy = settings.Descendants().Single(e => (string?)e.Attribute("Click") == "CopySetup_Click");
        Assert.Contains(copy.Ancestors(), e => e.Name.LocalName == "Expander" && (string?)e.Attribute("Header") == "First-time setup");
        var reset = settings.Descendants().Single(e => (string?)e.Attribute("Click") == "Pair_Click");
        Assert.Contains(reset.Ancestors(), e => e.Name.LocalName == "Expander" && (string?)e.Attribute("Header") == "Troubleshooting");
        var send = XDocument.Load(Source("PremiereSendWindow.xaml"));
        foreach (var name in new[] { "Bins", "NewBinName", "SendButton" })
            Assert.Contains(send.Descendants(), e => (string?)e.Attribute(x + "Name") == name);
    }

    [Fact]
    public void SendUsesExportStyleSourceReviewWithGlobalAndPerItemRangeOptions()
    {
        var send = XDocument.Load(Source("PremiereSendWindow.xaml"));
        var text = File.ReadAllText(Source("PremiereSendWindow.xaml.cs"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var heading in new[] { "Premiere connection", "Premiere destination" })
            Assert.Contains(send.Descendants(), element => (string?)element.Attribute("Text") == heading);
        Assert.Contains(send.Descendants(), element => (string?)element.Attribute(x + "Name") == "Sources");
        Assert.Contains(send.Descendants(), element => (string?)element.Attribute(x + "Name") == "GlobalUseRangesCheck");
        Assert.Contains(send.Descendants(), element => (string?)element.Attribute(x + "Name") == "MediaHeading");
        Assert.Contains(send.Descendants(), element => (string?)element.Attribute(x + "Name") == "RangeCheck");
        Assert.Contains(send.Descendants(), element => (string?)element.Attribute(x + "Name") == "RangeTimeline");
        Assert.DoesNotContain(send.Descendants(), element => (string?)element.Attribute(x + "Name") == "PremiereItemText");
        var noRangeTrigger = send.Descendants().Single(element => element.Name.LocalName == "DataTrigger" &&
            (string?)element.Attribute("Value") == "False");
        Assert.Contains(noRangeTrigger.Elements(), element => (string?)element.Attribute("TargetName") == "RangeCheck" &&
            (string?)element.Attribute("Property") == "Visibility" && (string?)element.Attribute("Value") == "Collapsed");
        Assert.DoesNotContain(noRangeTrigger.Elements(), element => (string?)element.Attribute("TargetName") == "RangeRow");
        Assert.Contains(send.Descendants(), element => (string?)element.Attribute(x + "Name") == "ProjectText");
        var sourcesScroll = send.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "SourcesScroll");
        Assert.Equal("SourcesScroll_PreviewMouseWheel", (string?)sourcesScroll.Attribute("PreviewMouseWheel"));
        Assert.Equal("DialogScroll", (string?)send.Root!.Elements().Single(element => element.Name.LocalName == "ScrollViewer").Attribute(x + "Name"));
        Assert.Equal("720", (string?)send.Root!.Attribute("Height"));
        Assert.Contains(send.Descendants(), element => (string?)element.Attribute("Click") == "Refresh_Click");
        Assert.DoesNotContain("Retries verify existing", send.ToString());
        Assert.DoesNotContain("ApplyRangesCheck", text);
        Assert.DoesNotContain("this does not create Subclips", text);
        Assert.Contains("PremiereSendModel", text);
        Assert.Contains("GlobalUseRanges_Changed", text);
        Assert.Contains("RangeUse_Changed", text);
        Assert.Contains("ShouldTransferWheelToDialog", text);
        Assert.Contains("PremiereSendState.Present", text);
        Assert.Contains("RedBrush", text);
        Assert.DoesNotContain("nearest timing unit", text);
        Assert.DoesNotContain("cannot be transferred exactly", text);
    }

    [Fact]
    public void ShutdownProtectionUsesOnlyAnUnresolvedDispatchedBridgeCommand()
    {
        var window = File.ReadAllText(Source("MainWindow.xaml.cs"));
        var closing = window[window.IndexOf("private void Window_Closing", StringComparison.Ordinal)..];
        Assert.Contains("_premiereBridge?.HasUnresolvedDispatchedHandoff == true", closing);
        Assert.DoesNotContain("_premiereJobs?.Jobs.Any(job => job.State is JobState.Queued or JobState.Running)", closing);
    }
}
