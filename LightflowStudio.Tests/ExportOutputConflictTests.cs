using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class ExportOutputConflictTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("export-conflicts-").FullName;
    private static readonly EncoderCapability Available = new(EncoderBackend.NvidiaNvenc,
        EncoderCapabilityState.ImplementedAndAvailable, "Execution probe succeeded");
    private static readonly MediaMetadata Metadata = new(256, 256, 25, 1, 14, "h264", false, Container: "mp4");

    [Fact]
    public async Task ExistingOutputToggleRebuildsDispositionWarningCountsAndEligibilityWithoutProbes()
    {
        var model = Model("existing", "new");
        var output = model.CurrentPlan!.Items[0].OutputPaths.Single();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllText(output, "keep me");
        Ready(model);
        Assert.True(model.CanExport);
        Assert.Equal("1 ready · 1 will be skipped", model.ReadySummary);
        Assert.Equal(JobPlanDisposition.Skip, model.CurrentPlan!.Items[0].Disposition);
        var row = model.SubmissionItems[0];
        Assert.Equal(ExportFilePreflightState.Warning, row.State);
        Assert.True(row.WillSkip); Assert.False(row.HasError);
        Assert.StartsWith("Existing file will be skipped", row.IssueToolTip);
        Assert.DoesNotContain(_root, row.IssueToolTip);
        Assert.DoesNotContain("existing.mp4", row.IssueToolTip);
        model.OverwriteExisting = true;
        Assert.True(model.CanExport);
        Assert.Equal("1 ready · 1 will be overwritten", model.ReadySummary);
        Assert.Equal(JobPlanDisposition.Process, model.CurrentPlan!.Items[0].Disposition);
        Assert.Equal("Existing file will be replaced\nThis export will overwrite the existing output file.", model.SubmissionItems[0].IssueToolTip);
        var accepted = await model.MaterializeAcceptedPlanAsync();
        Assert.True(accepted.Definition.Options.OverwriteExistingFiles);
        Assert.All(accepted.Items, item => Assert.Equal(JobPlanDisposition.Process, item.Disposition));
        model.OverwriteExisting = false;
        Assert.Equal("1 ready · 1 will be skipped", model.ReadySummary);
        Assert.True(model.CanExport);
        Assert.Equal("keep me", File.ReadAllText(output));
    }

    [Fact]
    public async Task AllSkippedIsNotReportedReadyOrSubmittedAsSuccessfulExport()
    {
        var model = Model("existing"); var path = model.CurrentPlan!.Items.Single().OutputPaths.Single();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "preserve");
        Ready(model);
        Assert.Equal("1 will be skipped · Nothing to export", model.ReadySummary);
        Assert.False(model.CanExport); Assert.False(model.SubmissionItems[0].HasError);
        Assert.Contains("will be skipped", (await Assert.ThrowsAsync<InvalidOperationException>(() => model.MaterializeAcceptedPlanAsync())).Message);
        model.OverwriteExisting = true;
        Assert.True(model.CanExport); Assert.Equal("1 will be overwritten", model.ReadySummary);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SelectedSourceAndDuplicatePathsRemainBlockingAndHighestPriority(bool overwrite)
    {
        var model = Model("source", "other");
        model.OverwriteExisting = overwrite; model.CreateSubfolder = false;
        model.Destination = Path.Combine(_root, "input");
        Ready(model);
        Assert.False(model.CanExport);
        Assert.All(model.SubmissionItems, row =>
        {
            Assert.True(row.HasError);
            Assert.StartsWith("Output conflicts with source", row.IssueToolTip);
            Assert.DoesNotContain(_root, row.IssueToolTip);
            Assert.DoesNotContain("will be", row.IssueToolTip);
            Assert.Contains(row.Issues, issue => issue.Code == "encoding.source-overwrite" && issue.Message.Contains(_root));
        });
        Assert.False((await model.MaterializeAcceptedPlanAsync()).IsValid);
        model.Destination = Path.Combine(_root, "output");
        model.NameParts.Clear(); model.AddPart(NamePartKind.CustomText); model.UpdateCustomText(0, "duplicate");
        Assert.False(model.CanExport);
        Assert.All(model.SubmissionItems, row => Assert.StartsWith("Duplicate output filename", row.IssueToolTip));
        Assert.Equal("2 of 2 videos need attention", model.ReadySummary);
        Assert.All(model.PreflightInputs, input => Assert.Equal("source fixture", File.ReadAllText(input.SourcePath)));
    }

    [Fact]
    public void MixedBatchErrorsWinOverSkipWarningsAndSettingsAreReevaluatedAfterLateMetadata()
    {
        var model = Model("new", "existing", "broken");
        var output = model.CurrentPlan!.Items[1].OutputPaths.Single();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllText(output, "keep");
        var old = model.BeginPreflight(); var current = model.BeginPreflight();
        model.ApplyEncoderCapability(current, Available);
        model.OverwriteExisting = true;
        for (var i = 0; i < 2; i++) model.ApplySourceProbe(current, i, new(Metadata, TrimSourceIdentity.Read(model.PreflightInputs[i].SourcePath)));
        Assert.False(model.CanExport);
        Assert.Contains("1 will be overwritten", model.ReadySummary);
        Assert.False(model.ApplySourceProbe(old, 1, new(null, null, "stale failure")));
        model.OverwriteExisting = false;
        model.ApplySourceProbe(current, 2, new(null, null, "Read source failed"));
        Assert.Equal("1 of 3 videos needs attention", model.ReadySummary);
        Assert.False(model.CanExport);
        Assert.True(model.SubmissionItems[1].WillSkip);
        model.NameParts.Add(new(NamePartKind.Sequence001));
        Assert.False(model.SubmissionItems[1].WillSkip);
        Assert.DoesNotContain(model.SubmissionItems, row => row.IssueToolTip.Contains("stale failure"));
    }

    [Fact]
    public async Task OverwriteChangeDuringFinalValidationRejectsObsoleteSubmission()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var baseModel = Model("existing");
        var handoff = new EncodingHandoffResult(baseModel.PreflightInputs, [], _root);
        var model = new ExportDialogModel(handoff, new(), [], [], new EncodingLutResourceStore(Path.Combine(_root, "luts")),
            revalidate: async (_, _) => { entered.TrySetResult(); await release.Task; return handoff; },
            namingDefault: ExportNamingDefault.Subclip);
        Ready(model);
        var accepting = model.MaterializeAcceptedPlanAsync();
        await entered.Task;
        model.OverwriteExisting = true;
        release.TrySetResult();
        Assert.Contains("settings or selection changed", (await Assert.ThrowsAsync<InvalidOperationException>(() => accepting)).Message);
        Assert.True((await model.MaterializeAcceptedPlanAsync()).Definition.Options.OverwriteExistingFiles);
    }

    [Fact]
    public void TwentyTwoFileBatchCountsOnlyNewOutputsAsReadyAndPreservesWarningsOnToggle()
    {
        var model = Model(Enumerable.Range(1, 22).Select(i => "video-" + i).ToArray());
        foreach (var item in model.CurrentPlan!.Items.Take(2))
        {
            var path = item.OutputPaths.Single(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "unrelated existing output");
        }
        Ready(model);
        Assert.Equal("20 ready · 2 will be skipped", model.ReadySummary);
        Assert.True(model.CanExport);
        model.OverwriteExisting = true;
        Assert.Equal("20 ready · 2 will be overwritten", model.ReadySummary);
        Assert.True(model.CanExport);
        Assert.Equal(2, model.SubmissionItems.Count(row => row.State == ExportFilePreflightState.Warning));
        Assert.DoesNotContain(model.SubmissionItems, row => row.HasError);
        model.OverwriteExisting = false;
        Assert.Equal("20 ready · 2 will be skipped", model.ReadySummary);
    }

    [Fact]
    public async Task RealCheckboxUpdatesModelFooterButtonAndAmberIconImmediately()
    {
        var model = Model("existing", "new");
        var output = model.CurrentPlan!.Items[0].OutputPaths.Single();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.WriteAllText(output, "keep");
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var runner = new MetadataRunner();
            var preflight = new ExportPreflightCoordinator(new("ffprobe", runner), _ => Task.FromResult(Available));
            var window = new ExportDialog(model, null!, null, preflight)
            { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000 };
            try
            {
                window.Show();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (model.IsAnalyzing && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.False(model.IsAnalyzing); Assert.Equal(2, runner.Calls);
                foreach (var overwrite in new[] { false, true, false })
                {
                    window.OverwriteExistingCheck.IsChecked = overwrite;
                    window.UpdateLayout();
                    Assert.Equal(overwrite, model.OverwriteExisting);
                    Assert.Equal(overwrite ? "1 ready · 1 will be overwritten" : "1 ready · 1 will be skipped", window.ReadySummaryText.Text);
                    Assert.True(window.ExportButton.IsEnabled);
                    Assert.Equal(2, runner.Calls);
                    var icon = Descendants<Button>(window).Single(button => button.Name == "IssueIndicator" && button.Visibility == Visibility.Visible);
                    Assert.Equal(18, icon.Width); Assert.Equal(18, icon.Height);
                    Assert.Equal(model.SubmissionItems[0].IssueToolTip, icon.ToolTip);
                    Assert.Equal(window.FindResource("WarningBrush"), icon.Foreground);
                }
            }
            finally { window.Close(); }
        });
    }

    private ExportDialogModel Model(params string[] names)
    {
        var inputRoot = Path.Combine(_root, "input"); Directory.CreateDirectory(inputRoot);
        var inputs = names.Select(name =>
        {
            var path = Path.Combine(inputRoot, name + ".mp4"); File.WriteAllText(path, "source fixture");
            return new EncodingHandoffInput(Guid.NewGuid(), Guid.NewGuid(), path, name, 14, null, NamingOriginalName: name, SourceIdentity: TrimSourceIdentity.Read(path));
        }).ToArray();
        return new(new(inputs, [], _root), new(), [], [], new EncodingLutResourceStore(Path.Combine(_root, "luts")), namingDefault: ExportNamingDefault.Subclip);
    }
    private static void Ready(ExportDialogModel model)
    {
        model.ApplyMetadata(model.PreflightInputs.Select(_ => (MediaMetadata?)Metadata).ToArray());
        model.ApplyEncoderCapability(Available);
    }
    private sealed class MetadataRunner : IProbeProcessRunner
    {
        public int Calls;
        public Task<ProbeProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new ProbeProcessResult(0, "{\"streams\":[{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":256,\"height\":256,\"avg_frame_rate\":\"25/1\"}],\"format\":{\"duration\":\"1.0\",\"format_name\":\"mp4\"}}", ""));
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    public void Dispose() => Directory.Delete(_root, true);
}
