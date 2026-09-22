namespace LightflowStudio;

internal sealed record VisualIndexJobOptions(Guid AssetId, string Name);
internal sealed record VisualIndexJobResult(int Frames, int Failed);
internal sealed record VisualIndexJob(VisualIndexJobOptions Options, JobRuntimeSnapshot<VisualIndexJobResult> Runtime)
{
    internal Guid JobId => Runtime.JobId;
    internal JobState State => Runtime.State;
    internal bool CanRetry => State is JobState.Failed or JobState.Cancelled or JobState.CompletedWithWarnings;
    internal string Issue => Runtime.Errors.FirstOrDefault() ?? "";
    internal string Detail => $"Visual Index · {Options.Name}\nPreparing 12, 24, and 48 frame indexes\n" +
        (Runtime.Items.FirstOrDefault()?.Data is { } result ? $"{result.Frames} unique frames; {result.Failed} unavailable." : "");
    internal string StateText => State == JobState.Running ? "Generating" : JobsPresentation.StateText(State);
    internal JobCardPresentation Card(bool expanded) => new(JobId, $"Visual Index · {Options.Name}",
        JobsPresentation.Glyph(State), StateText, Runtime.Progress.OverallPercent ?? 0, State == JobState.Running,
        Runtime.Elapsed.ToString(@"m\:ss"), null, new JobMessageDetailsPresentation(Detail, State == JobState.Completed ? "Visual Index complete" : null), Issue, expanded,
        JobActionState.For(State, retry: CanRetry));
    internal JobsWorkspaceItem WorkspaceItem() => new(JobId, null, null, true, false, Options.Name,
        "Visual Index", State, Runtime.Progress.OverallPercent, Runtime.Elapsed.ToString(@"m\:ss"),
        Options.Name, "", Issue, Detail, Runtime.CreatedAt, 0, new JobMessageDetailsPresentation(Detail, State == JobState.Completed ? "Visual Index complete" : null),
        SupportsQueueControls: false, SupportsRetry: CanRetry);
}

/// <summary>Capability adapter; lifecycle, progress and cancellation belong to the shared Jobs runtime.</summary>
internal sealed class VisualIndexJobs(IPositionFrameService frames,
    Func<Guid, CancellationToken, Task<DerivedMetadataResult>> metadata) : IAsyncDisposable
{
    internal const string Capability = "video.visual-index";
    private readonly ApplicationJobsRuntime<VisualIndexJobOptions, VisualIndexJobResult> _runtime = new();
    private readonly Dictionary<Guid, VisualIndexJobOptions> _options = [];
    private readonly object _sync = new();
    internal event Action? Changed;
    internal IReadOnlyList<VisualIndexJob> Jobs
    {
        get { lock (_sync) return _runtime.Jobs.Select(job => new VisualIndexJob(_options[job.JobId], job)).ToArray(); }
    }
    internal void Initialize() => _runtime.Changed += _ => Changed?.Invoke();
    internal void Queue(CapabilityInvocation invocation, Func<Guid, string> name)
    {
        if (invocation.Capability != Capability) throw new ArgumentException("Unsupported capability.", nameof(invocation));
        foreach (var id in invocation.AssetIds.Distinct()) Queue(new VisualIndexJobOptions(id, name(id)));
    }
    private void Queue(VisualIndexJobOptions options)
    {
        var item = new JobItemDefinition(Guid.NewGuid(), options.Name);
        var definition = new JobDefinition<VisualIndexJobOptions>(Guid.NewGuid(), Capability, DateTimeOffset.UtcNow, options, [item]);
        var plan = new JobPlan<VisualIndexJobOptions>(definition, DateTimeOffset.UtcNow,
            [new(item, [], JobPlanDisposition.Process, new(JobWorkUnit.Items, 1), [])], [], JobWorkUnit.Items);
        lock (_sync) _options.Add(definition.Id, options);
        _runtime.Queue(plan, 1, (_, progress, token) => Task.Run(() => ExecuteAsync(options.AssetId, item.Id, progress, token), token));
    }
    internal bool Cancel(Guid id) => _runtime.Cancel(id);
    internal bool Retry(Guid id)
    {
        var job = Jobs.FirstOrDefault(job => job.JobId == id && job.CanRetry);
        if (job is null) return false;
        Queue(job.Options);
        return true;
    }
    private async Task<JobItemResult<VisualIndexJobResult>> ExecuteAsync(Guid assetId, Guid itemId,
        IProgress<double> progress, CancellationToken token)
    {
        try
        {
            var context = await frames.PrepareContextAsync(assetId, token).ConfigureAwait(false);
            if (context.Source?.Asset.MediaType != "video") return Failed("The selected video is unavailable.");
            var info = await metadata(assetId, token).ConfigureAwait(false);
            if (!info.Succeeded || info.Metadata?.DurationSeconds is not double seconds ||
                !double.IsFinite(seconds) || seconds <= 0 || seconds >= TimeSpan.MaxValue.TotalSeconds)
                return Failed("Video duration is unavailable. Check the source and retry.");
            var positions = VisualIndexSampling.PlanAll(TimeSpan.FromSeconds(seconds), info.Metadata.Video?.FrameRate ?? 0);
            var completed = 0;
            var failed = 0;
            // Each video yields between frames. The shared demand queue admits foreground frames first.
            foreach (var position in positions)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (await frames.GetAsync(context, position, ThumbnailPriority.Background, token).ConfigureAwait(false) is null) failed++;
                }
                catch (OperationCanceledException) { throw; }
                catch { failed++; }
                progress.Report(++completed * 100d / positions.Count);
            }
            if (!await frames.IsCurrentAsync(context, token).ConfigureAwait(false))
                return Failed("The source or color changed during preparation. Retry to prepare its current presentation.");
            return new(itemId, failed == 0 ? JobState.Completed : JobState.Failed, [], [],
                failed == 0 ? [] : [$"{failed} Visual Index {(failed == 1 ? "frame is" : "frames are")} unavailable. Check the source and color resources, then retry."],
                new(positions.Count, failed));
        }
        catch (OperationCanceledException) { throw; }
        catch { return Failed("Visual Index could not be prepared. Check the source and color resources, then retry."); }
        JobItemResult<VisualIndexJobResult> Failed(string message) => new(itemId, JobState.Failed, [], [], [message]);
    }
    public ValueTask DisposeAsync() => _runtime.DisposeAsync();
}
