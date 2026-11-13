using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExportPreflightStateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("export-preflight-state-").FullName;
    private readonly EncoderCapability _available = new(EncoderBackend.NvidiaNvenc, EncoderCapabilityState.ImplementedAndAvailable, "Execution probe succeeded");

    [Fact]
    public void RowsFooterAndExportFollowIndependentPendingCheckingReadyAndErrorStates()
    {
        var model = Model(2);
        var generation = model.BeginPreflight();
        Assert.Equal("Preparing 2 videos…", model.ReadySummary);
        Assert.All(model.SubmissionItems, item => Assert.False(item.HasError));
        model.ApplyEncoderCapability(generation, _available);
        model.SetChecking(generation, 0);
        model.SetChecking(generation, 1);
        model.ApplySourceProbe(generation, 0, Probe(model, 0));
        Assert.DoesNotContain(model.Errors, issue => issue.Code == "export.range-unresolved");
        Assert.False(model.CanExport);
        model.ApplySourceRange(generation, 0, Range(model, 0));
        Assert.Equal(ExportFilePreflightState.Ready, model.SubmissionItems[0].State);
        Assert.Equal(ExportFilePreflightState.Checking, model.SubmissionItems[1].State);
        Assert.Equal("Analyzing videos… 1 of 2 ready", model.ReadySummary);
        model.ApplySourceProbe(generation, 1, Probe(model, 1));
        model.ApplySourceRange(generation, 1, null, "The source EOF changed. Reopen the Subclip.");
        Assert.Equal("1 of 2 videos needs attention", model.ReadySummary);
        Assert.True(model.SubmissionItems[1].HasError);
        Assert.Contains("EOF changed", model.SubmissionItems[1].IssueToolTip);
        Assert.False(model.CanExport);
        Assert.DoesNotContain(model.Errors, issue => issue.Code == "export.encoder-unavailable");
    }

    [Theory]
    [InlineData(1, "1 video ready to export")]
    [InlineData(2, "2 videos ready to export")]
    public void VerifiedEofIsNormalReadyAndTechnicalEvidenceIsNotUserWarning(int count, string summary)
    {
        var model = Model(count);
        var generation = model.BeginPreflight();
        model.ApplyEncoderCapability(generation, _available);
        for (var index = 0; index < count; index++)
        {
            model.ApplySourceProbe(generation, index, Probe(model, index));
            model.ApplySourceRange(generation, index, Range(model, index));
        }
        Assert.Equal(summary, model.ReadySummary);
        Assert.True(model.CanExport);
        Assert.All(model.SubmissionItems, item =>
        {
            Assert.Equal(ExportFilePreflightState.Ready, item.State);
            Assert.False(item.HasIssues);
            Assert.DoesNotContain("Technical EOF", item.IssueToolTip + item.RangeToolTip);
        });
        Assert.DoesNotContain(model.Issues, issue => issue.Message.Contains("Technical EOF"));
        Assert.All(model.CurrentPlan!.Items, item => Assert.Contains("Technical EOF", item.Definition.ResolvedRange!.BoundaryDiagnostic));
    }

    [Fact]
    public void OldRunCannotOverwriteCurrentFactsSettingsOrSelection()
    {
        var model = Model(1);
        var old = model.BeginPreflight();
        var current = model.BeginPreflight();
        model.Codec = ExportCodecChoice.H264;
        Assert.False(model.ApplySourceProbe(old, 0, Probe(model, 0)));
        Assert.False(model.ApplySourceRange(old, 0, Range(model, 0)));
        Assert.False(model.ApplyEncoderCapability(old, _available));
        Assert.False(model.CanExport);
        model.ApplyEncoderCapability(current, _available);
        model.ApplySourceProbe(current, 0, Probe(model, 0));
        model.ApplySourceRange(current, 0, Range(model, 0));
        Assert.Equal(VideoCodec.H264, model.CurrentPlan!.Items[0].Definition.MaterializedExport!.Encoding.Codec);
        Assert.True(model.CanExport);
        model.CancelPreflight(current);
        Assert.False(model.ApplySourceRange(current, 0, null, "Stale failure"));
        Assert.True(model.CanExport);
    }

    [Fact]
    public void SameAsSourcePendingDoesNotValidateTenBitAgainstPlaceholderCodec()
    {
        var model = Model(1);
        model.Encoding = model.Encoding with { PixelFormat = VideoPixelFormat.P010 };
        var generation = model.BeginPreflight();
        model.ApplyEncoderCapability(generation, _available);
        Assert.DoesNotContain(model.Errors, issue => issue.Code == "export.options");
        Assert.False(model.SubmissionItems[0].HasError);
        model.ApplySourceProbe(generation, 0, Probe(model, 0));
        model.ApplySourceRange(generation, 0, Range(model, 0));
        Assert.True(model.CanExport);
    }

    [Fact]
    public void MeaningfulInteriorCorrectionRemainsAnAllowedVisibleWarning()
    {
        var original = Model(1).PreflightInputs[0];
        var input = original with { InitialTrim = new(TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.04), TimeSpan.FromSeconds(.135)) };
        var model = new ExportDialogModel(new([input], [], _root), new(), [], [],
            new EncodingLutResourceStore(Path.Combine(_root, "luts")), _ => new(false, 0));
        var generation = model.BeginPreflight();
        model.ApplyEncoderCapability(generation, _available);
        model.ApplySourceProbe(generation, 0, Probe(model, 0));
        model.ApplySourceRange(generation, 0, new(input.InitialTrim!, TimeSpan.Zero, TimeSpan.FromSeconds(.04), TimeSpan.FromSeconds(.16), TimeSpan.FromSeconds(.12))
            { BoundaryDiagnostic = "Saved Out 0.135 resolved to frame 0.12 within its local interval." });
        Assert.Equal(ExportFilePreflightState.Warning, model.SubmissionItems[0].State);
        Assert.True(model.SubmissionItems[0].HasIssues);
        Assert.False(model.SubmissionItems[0].HasError);
        Assert.Contains("0.135", model.SubmissionItems[0].IssueToolTip);
        Assert.True(model.CanExport);
    }

    [Fact]
    public async Task SettingsChangedDuringAcceptedPlanValidationCannotQueueStaleSnapshot()
    {
        var gate = new TaskCompletionSource<EncodingHandoffResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = Model(1, (_, _) => gate.Task);
        var generation = model.BeginPreflight();
        model.ApplyEncoderCapability(generation, _available);
        model.ApplySourceProbe(generation, 0, Probe(model, 0));
        model.ApplySourceRange(generation, 0, Range(model, 0));
        var pending = model.MaterializeAcceptedPlanAsync();
        model.Container = ExportContainerChoice.Mkv;
        gate.SetResult(new(model.PreflightInputs, [], _root));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Contains("changed during final validation", error.Message);
    }

    [Fact]
    public async Task ChangedSourceAfterPreflightIsRejectedAtSubmission()
    {
        var model = Model(1);
        var generation = model.BeginPreflight();
        model.ApplyEncoderCapability(generation, _available);
        model.ApplySourceProbe(generation, 0, Probe(model, 0));
        model.ApplySourceRange(generation, 0, Range(model, 0));
        File.AppendAllText(model.Inputs[0].SourcePath, "changed");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => model.MaterializeAcceptedPlanAsync());
        Assert.Contains("source changed", error.Message);
    }

    [Fact]
    public void LateResultForExcludedFallbackDoesNotReintroduceItAndCanBeReusedWhenIncluded()
    {
        var originals = Model(2).PreflightInputs;
        var first = originals[0] with { ExportProvenance = new(ExportItemKind.Subclip, originals[0].AssetId, Guid.NewGuid(), "Subclip", 1), RangeIsFixed = true };
        var fallback = originals[1] with { InitialTrim = null, ExportProvenance = new(ExportItemKind.NoSubclipFullSourceFallback, originals[1].AssetId), RangeIsFixed = true };
        var model = new ExportDialogModel(new([first, fallback], [], _root), new(), [], [],
            new EncodingLutResourceStore(Path.Combine(_root, "luts")), _ => new(false, 0));
        var generation = model.BeginPreflight();
        model.ApplyEncoderCapability(generation, _available);
        model.IncludeNoSubclipSources = false;
        model.ApplySourceProbe(generation, 0, new(new(256, 256, 25, .2, 7, "hevc", false, Container: "mp4"), first.SourceIdentity));
        model.ApplySourceRange(generation, 0, new(first.InitialTrim!, TimeSpan.Zero, TimeSpan.FromSeconds(.04), TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.16)) { RetainsSourceEof = true });
        Assert.Equal("1 video ready to export", model.ReadySummary);
        model.ApplySourceProbe(generation, 1, new(new(256, 256, 25, .2, 7, "hevc", false, Container: "mp4"), fallback.SourceIdentity));
        model.ApplySourceRange(generation, 1, null);
        Assert.Single(model.Inputs);
        Assert.Equal("1 video ready to export", model.ReadySummary);
        model.IncludeNoSubclipSources = true;
        Assert.Equal("2 videos ready to export", model.ReadySummary);
        Assert.True(model.CanExport);
    }

    [Fact]
    public async Task ConcurrentSourceWorkIsBoundedAndCloseCancellationCannotApplyLateHardwareResult()
    {
        var originals = Model(3).PreflightInputs.Select(input => input with { InitialTrim = null }).ToArray();
        var model = new ExportDialogModel(new(originals, [], _root), new(), [], [],
            new EncodingLutResourceStore(Path.Combine(_root, "luts")), _ => new(false, 0));
        var runner = new BlockingRunner();
        var hardware = new TaskCompletionSource<EncoderCapability>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new ExportPreflightCoordinator(new("ffprobe", runner), _ => hardware.Task);
        using var cancellation = new CancellationTokenSource();
        var pending = coordinator.RunAsync(model, cancellation.Token);
        await runner.TwoEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, runner.Active);
        Assert.Equal(2, model.SubmissionItems.Count(item => item.State == ExportFilePreflightState.Checking));
        Assert.Equal(1, model.SubmissionItems.Count(item => item.State == ExportFilePreflightState.Pending));
        cancellation.Cancel();
        hardware.SetResult(_available); // Deliberately ignores cancellation to test the stale-result guard.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, runner.Active);
        Assert.Null(model.EncoderCapability);
        Assert.False(model.CanExport);
    }

    private sealed class BlockingRunner : IProbeProcessRunner
    {
        private int _active;
        public int Active => Volatile.Read(ref _active);
        public TaskCompletionSource TwoEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ProbeProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _active) == 2) TwoEntered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); return new(0, "", ""); }
            finally { Interlocked.Decrement(ref _active); }
        }
    }

    private ExportDialogModel Model(int count, Func<bool, CancellationToken, Task<EncodingHandoffResult>>? revalidate = null)
    {
        var inputs = Enumerable.Range(0, count).Select(index =>
        {
            var path = Path.Combine(_root, $"source-{index}.mp4");
            File.WriteAllText(path, "fixture");
            return new EncodingHandoffInput(Guid.NewGuid(), Guid.NewGuid(), path, $"source-{index}", 7,
                new(TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.04), TimeSpan.FromSeconds(.2)),
                SourceIdentity: TrimSourceIdentity.Read(path));
        }).ToArray();
        return new(new(inputs, [], _root), new(), [], [], new EncodingLutResourceStore(Path.Combine(_root, "luts")),
            _ => new(false, 0), revalidate);
    }
    private static ExportSourceProbe Probe(ExportDialogModel model, int index) => new(
        new(256, 256, 25, .2, 7, "hevc", false, Container: "mp4"), model.Inputs[index].SourceIdentity);
    private static ResolvedMediaRange Range(ExportDialogModel model, int index) => new(model.Inputs[index].InitialTrim!, TimeSpan.Zero,
        TimeSpan.FromSeconds(.04), TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.16))
    { RetainsSourceEof = true, BoundaryDiagnostic = "Technical EOF: requested, frame start, frame end, duration and tolerance retained." };
    public void Dispose() => Directory.Delete(_root, true);
}
