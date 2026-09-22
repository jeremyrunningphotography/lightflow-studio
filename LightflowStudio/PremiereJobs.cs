using System.IO;

namespace LightflowStudio;

internal static class PremiereGrammar
{
    public static string Count(int count, string singular) => $"{count} {(count == 1 ? singular : singular + "s")}";
    public static string Mixed(int nativeSubclips, int completeVideos) => nativeSubclips == 0
        ? Count(completeVideos, "video")
        : completeVideos == 0 ? Count(nativeSubclips, "Subclip") : Count(nativeSubclips + completeVideos, "item");
}

internal enum PremiereJobItemState { Pending, Sending, Sent, Failed, Conflict }

internal sealed record PremiereJobItem(string Key, string Name, PremiereJobItemState State,
    PremiereReceipt? Receipt = null)
{
    public bool IsTerminal => State is PremiereJobItemState.Sent or PremiereJobItemState.Failed or PremiereJobItemState.Conflict;
    public string StatusText => State switch
    {
        PremiereJobItemState.Pending => "Pending",
        PremiereJobItemState.Sending => "Sending",
        PremiereJobItemState.Sent => "Sent",
        PremiereJobItemState.Conflict => "Conflict",
        _ => "Failed"
    };
}

internal sealed record PremiereJob(Guid JobId, PremiereProject Project, IReadOnlyList<PremiereSource> Sources,
    JobState State, int Completed, IReadOnlyList<PremiereReceipt> Receipts, string Message, DateTimeOffset CreatedUtc,
    IReadOnlyList<PremierePlannedSubclip>? Subclips = null, IReadOnlyList<PremiereJobItem>? Items = null)
{
    public int ItemCount => Subclips?.Count ?? Sources.Count;
    public string CountText => Subclips is null ? PremiereGrammar.Count(Sources.Count, "video")
        : PremiereGrammar.Mixed(Subclips.Count(item => !item.Projection.IsSourceFallback),
            Subclips.Count(item => item.Projection.IsSourceFallback));
    public string Name => $"Send {CountText} to Premiere";
    public double Progress => ItemCount == 0 ? 0 : Completed * 100d / ItemCount;
    public string Details => Items is null
        ? $"Project: {Project.Name}\n{Completed} of {CountText} processed.\n{Message}\n"
            + string.Join("\n", Receipts.Select(receipt => $"{OutcomeText(receipt)}: {UserMessage(receipt)}"))
        : $"Project: {Project.Name}\n{Completed} of {CountText} processed.\n"
            + string.Join("\n", Items.Select(item => $"{item.Name} — {item.StatusText}"
                + (item.Receipt is { Outcome: not PremiereOutcome.Verified } receipt ? $": {UserMessage(receipt)}" : "")));
    private JobDetailsPresentation DetailPresentation => Items is null
        ? new JobMessageDetailsPresentation(Details)
        : new PremiereJobDetailsPresentation(Project.Name, $"{Completed} of {CountText} processed.",
            Items.Select(item => new PremiereJobItemDetailsPresentation(item.Name, item.StatusText,
                item.Receipt is { Outcome: not PremiereOutcome.Verified } receipt ? $": {UserMessage(receipt)}" : "",
                item.State)).ToArray());
    private int AttentionCount => Items?.Count(item => item.State is PremiereJobItemState.Failed
        or PremiereJobItemState.Conflict) ?? 0;
    private string? Issue => Items is null ? Message : AttentionCount switch
    {
        0 => null,
        1 => "1 item needs attention.",
        var count => $"{count} items need attention."
    };
    internal static string OutcomeText(PremiereReceipt receipt) => receipt.Outcome switch
    {
        PremiereOutcome.Verified when receipt.Message.Contains("updated", StringComparison.OrdinalIgnoreCase) => "Updated",
        PremiereOutcome.Verified when receipt.Message.Contains("cleared", StringComparison.OrdinalIgnoreCase) => "Updated",
        PremiereOutcome.Verified when receipt.Message.Contains("imported", StringComparison.OrdinalIgnoreCase) => "Imported",
        PremiereOutcome.Verified when receipt.Message.Contains("created", StringComparison.OrdinalIgnoreCase) => "Created",
        PremiereOutcome.Verified => "Verified",
        PremiereOutcome.Conflict => "Not sent",
        PremiereOutcome.UnknownOutcome => "Needs reconciliation",
        _ => "Failed"
    };
    internal static string UserMessage(PremiereReceipt receipt) => receipt.Message.StartsWith("Marker:", StringComparison.Ordinal)
        ? receipt.Message : receipt.Outcome switch
    {
        PremiereOutcome.Verified => receipt.Message,
        PremiereOutcome.UnknownOutcome => "Premiere may have started this import but it could not be verified. Return to the original project and send the same source again.",
        PremiereOutcome.Conflict when receipt.Message.Contains("missing or relinked", StringComparison.OrdinalIgnoreCase)
            => "The matching Premiere source was moved or relinked. Restore or relink it in the original project, then send again.",
        PremiereOutcome.Conflict when receipt.Message.Contains("source was relinked", StringComparison.OrdinalIgnoreCase)
            => "The matching Premiere source uses different media. Restore its original media or delete that item, then send again.",
        PremiereOutcome.Conflict when receipt.Message.Contains("unmapped replacement", StringComparison.OrdinalIgnoreCase)
            => "An unlinked replacement uses the same media. Delete it or restore the original mapped source, then send again.",
        PremiereOutcome.Conflict when receipt.Message.Contains("replacement Premiere item", StringComparison.OrdinalIgnoreCase)
            => "An unlinked Premiere item already matches this Subclip. Delete it or restore the original mapped Subclip, then send again.",
        PremiereOutcome.Conflict when receipt.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
            => "A matching file already exists in Premiere but is not linked to this Catalog source. Resolve that item in the original project, then send again.",
        PremiereOutcome.Conflict when receipt.Message.Contains("Subclip changed", StringComparison.OrdinalIgnoreCase)
            || receipt.Message.Contains("Catalog Subclip", StringComparison.OrdinalIgnoreCase)
            => "This Lightflow Subclip changed after it was sent. Review the existing Premiere Subclip, then resolve the conflict before retrying.",
        PremiereOutcome.Conflict when receipt.Message.Contains("outside the selected destination", StringComparison.OrdinalIgnoreCase)
            => "This Premiere Subclip exists outside the selected destination. Move it back or send to its current bin, then retry.",
        PremiereOutcome.Conflict when receipt.Message.Contains("native Subclip", StringComparison.OrdinalIgnoreCase)
            => "The matching native Premiere Subclip is missing or changed. Review the original project before retrying.",
        PremiereOutcome.Conflict when receipt.Message.Contains("identity changed", StringComparison.OrdinalIgnoreCase)
            => "The source changed since its earlier handoff. Refresh the Browser and resolve the existing Premiere source before sending again.",
        PremiereOutcome.Conflict => "Premiere could not safely reconcile this source. Resolve the existing source in the original project, then send again.",
        _ => "Premiere did not change this source. Check the connection and try again."
    };
    public JobCardPresentation Card(bool expanded) => new(JobId, Name, JobsPresentation.Glyph(State),
        State == JobState.Running ? "Sending" : JobsPresentation.StateText(State), Progress, State == JobState.Running,
        "", null, DetailPresentation, Issue, expanded, JobActionState.For(State));
    public JobsWorkspaceItem WorkspaceItem() => new(JobId, null, null, State is JobState.Queued or JobState.Running,
        false, Name, "Premiere handoff", State, Progress, CreatedUtc.ToLocalTime().ToString("MMM d, HH:mm"),
        Sources.FirstOrDefault()?.Path ?? "", Project.Path, Issue ?? "", Details, CreatedUtc, long.MaxValue,
        DetailPresentation, SupportsQueueControls: false, RemovalKind: JobRemovalKind.RetainedProvenance);
}

/// <summary>Typed non-encoding executor; current progress is projected into the existing Jobs product.</summary>
internal sealed class PremiereJobs(CatalogPremiereHandoffs journal, PremiereBridge bridge)
{
    private readonly object _sync = new();
    private readonly List<PremiereJob> _jobs = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _cancellations = [];
    private readonly SemaphoreSlim _serial = new(1, 1);
    public event Action? Changed;
    private IReadOnlyList<PremiereCommand> _history = [];
    public IReadOnlyList<PremiereJob> Jobs { get { lock (_sync) return _jobs.ToArray(); } }
    public async Task RefreshHistoryAsync()
    {
        var history = await journal.ListAsync().ConfigureAwait(false);
        lock (_sync) _history = history;
        Changed?.Invoke();
    }
    public IReadOnlyList<JobsWorkspaceItem> History
    {
        get
        {
            lock (_sync) return ProjectHistory(_history.Where(command => !_jobs.Any(job =>
                (job.Subclips is not null || job.State is JobState.Queued or JobState.Running)
                && PremiereProtocol.DestinationId(job.Project) == command.Intent.DestinationId
                && job.Sources.Any(source => source.AssetId == command.Intent.Source.AssetId))));
        }
    }
    internal static IReadOnlyList<JobsWorkspaceItem> ProjectHistory(IEnumerable<PremiereCommand> history)
    {
        var commands = history.ToArray();
        var markerIssues = commands.Where(command => command.Intent.Marker is not null
            && command.PreviousReceipt?.Outcome != PremiereOutcome.Verified)
            .ToLookup(command => (command.Intent.DestinationId, command.Intent.Marker!.TargetKey));
        return commands.Where(command => command.Intent.Marker is null).Select(command =>
            {
                var receipt = command.PreviousReceipt;
                var state = receipt?.Outcome == PremiereOutcome.Verified ? JobState.Completed : JobState.Failed;
                var message = receipt?.Message ?? (command.PreviouslyDispatched
                    ? "Interrupted handoff. Send the same source in the original project to reconcile."
                    : "Prepared but not dispatched. Send the source again to continue.");
                var targetKey = command.Intent.Subclip?.SubclipId is { } id
                    ? $"subclip:{id:D}" : $"asset:{command.Intent.Source.AssetId:D}";
                if (receipt?.Outcome == PremiereOutcome.Verified
                    && markerIssues[(command.Intent.DestinationId, targetKey)].FirstOrDefault() is { } markerIssue)
                {
                    state = JobState.CompletedWithWarnings;
                    message += " Attached marker transfer needs attention. " + (markerIssue.PreviousReceipt?.Message
                        ?? "Interrupted marker transfer; resend this item to reconcile.");
                }
                return new JobsWorkspaceItem(command.Intent.OperationId, null, null, false, false,
                    command.Intent.Subclip is { } subclip ? $"Premiere Subclip: {subclip.Name}" : $"Premiere: {Path.GetFileName(command.Intent.Source.Path)}", "Premiere handoff", state,
                    receipt is null ? 0 : 100, command.Intent.CreatedUtc.ToLocalTime().ToString("MMM d, HH:mm"),
                    command.Intent.Source.Path, command.Intent.Project.Path, message, message,
                    command.Intent.CreatedUtc, long.MaxValue, new JobMessageDetailsPresentation(message), SupportsQueueControls: false,
                    RemovalKind: JobRemovalKind.RetainedProvenance);
            }).ToArray();
    }

    public void Enqueue(PremiereProject project, string binId, string? createName, IReadOnlyList<PremiereSource> sources)
    {
        if (sources.Count is < 1 or > 500) throw new InvalidOperationException("Select between 1 and 500 source assets.");
        var job = new PremiereJob(Guid.NewGuid(), project, sources.ToArray(), JobState.Queued, 0, [], "", DateTimeOffset.UtcNow,
            Items: sources.Select(source => new PremiereJobItem($"asset:{source.AssetId:D}", source.Name, PremiereJobItemState.Pending)).ToArray());
        var cts = new CancellationTokenSource();
        lock (_sync) { _jobs.Add(job); _cancellations[job.JobId] = cts; }
        Changed?.Invoke();
        _ = RunAsync(job, binId, createName, cts);
    }
    public void EnqueueSubclips(PremiereProject project, string binId, string? createName,
        IReadOnlyList<PremierePlannedSubclip> subclips)
    {
        if (subclips.Count is < 1 or > 500) throw new InvalidOperationException("Select between 1 and 500 Subclips.");
        var sources = subclips.Select(item => item.Source).DistinctBy(source => source.AssetId).ToArray();
        var planned = subclips.ToArray();
        var items = planned.Select(item => new PremiereJobItem(ItemKey(item), item.Projection.Name,
            PremiereJobItemState.Pending)).ToArray();
        var job = new PremiereJob(Guid.NewGuid(), project, sources, JobState.Queued, 0, [], "", DateTimeOffset.UtcNow,
            planned, items);
        var cts = new CancellationTokenSource();
        lock (_sync) { _jobs.Add(job); _cancellations[job.JobId] = cts; }
        Changed?.Invoke();
        _ = RunAsync(job, binId, createName, cts);
    }
    public void Cancel(Guid id) { lock (_sync) if (_cancellations.TryGetValue(id, out var cts)) cts.Cancel(); }
    public void CancelAll() { lock (_sync) foreach (var cts in _cancellations.Values) cts.Cancel(); }
    private void Publish(PremiereJob job)
    {
        lock (_sync) { var index = _jobs.FindIndex(item => item.JobId == job.JobId); _jobs[index] = job; }
        Changed?.Invoke();
    }
    private async Task RunAsync(PremiereJob job, string binId, string? createName, CancellationTokenSource cts)
    {
        var entered = false;
        try
        {
            await _serial.WaitAsync(cts.Token).ConfigureAwait(false);
            entered = true;
            job = job with { State = JobState.Running }; Publish(job);
            var receipts = new List<PremiereReceipt>();
            if (job.Subclips is not null)
            {
                await RunSubclipsAsync(job, binId, createName, receipts, cts).ConfigureAwait(false);
                job = Jobs.Single(current => current.JobId == job.JobId);
                job = job with { State = receipts.All(receipt => receipt.Outcome == PremiereOutcome.Verified)
                    ? JobState.Completed : JobState.CompletedWithWarnings };
                return;
            }
            foreach (var source in job.Sources)
            {
                cts.Token.ThrowIfCancellationRequested();
                PremiereReceipt result;
                try
                {
                    var command = await journal.PrepareAsync(job.Project, binId, createName, source, cts.Token).ConfigureAwait(false);
                    result = await bridge.SendAsync(command, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    // Each Catalog source is independently reconcilable. A bad source must not
                    // abandon unrelated sources in a multi-source handoff.
                    result = new(Guid.Empty, PremiereOutcome.Failed, null, error.Message);
                }
                receipts.Add(result);
                job = job with { Completed = job.Completed + 1, Receipts = receipts.ToArray(),
                    Items = job.Items!.Select(item => item.Key == $"asset:{source.AssetId:D}" ? item with { State = ItemState(result), Receipt = result } : item).ToArray(),
                    Message = $"{Path.GetFileName(source.Path)}: {PremiereJob.UserMessage(result)}" };
                Publish(job);
                job = await RunMarkersAsync(job, source, null, result, binId, receipts, cts.Token).ConfigureAwait(false);
            }
            job = job with { State = receipts.All(receipt => receipt.Outcome == PremiereOutcome.Verified)
                ? JobState.Completed : JobState.CompletedWithWarnings };
        }
        catch (OperationCanceledException)
        { job = Jobs.Single(current => current.JobId == job.JobId) with { State = JobState.Cancelled, Message = "Cancelled. Any import already in progress may remain in Premiere; resend the same selection to reconcile." }; }
        catch (Exception error)
        { job = Jobs.Single(current => current.JobId == job.JobId) with { State = JobState.Failed, Message = error is TimeoutException ? "Companion did not finish in time. Reconnect and reconcile the original project." : error.Message }; }
        finally
        {
            if (entered) _serial.Release();
            lock (_sync) _cancellations.Remove(job.JobId);
            cts.Dispose(); Publish(job);
            try { await RefreshHistoryAsync().ConfigureAwait(false); } catch { /* Durable journal errors already surface on handoff; keep current result visible. */ }
        }
    }
    private static string ItemKey(PremierePlannedSubclip item) => item.Projection.SubclipId is { } subclipId
        ? $"subclip:{subclipId:D}" : $"asset:{item.Source.AssetId:D}";
    private static PremiereJobItemState ItemState(PremiereReceipt receipt) => receipt.Outcome switch
    {
        PremiereOutcome.Verified => PremiereJobItemState.Sent,
        PremiereOutcome.Conflict => PremiereJobItemState.Conflict,
        _ => PremiereJobItemState.Failed
    };
    private static PremiereJob UpdateItem(PremiereJob job, PremierePlannedSubclip planned,
        PremiereJobItemState state, PremiereReceipt? receipt = null)
    {
        var key = ItemKey(planned);
        var items = job.Items!.Select(item => item.Key == key ? item with { State = state, Receipt = receipt } : item).ToArray();
        var completed = items.Count(item => item.IsTerminal && !item.Key.StartsWith("marker:", StringComparison.Ordinal));
        return job with { Items = items, Completed = completed,
            Message = $"{planned.Projection.Name} — {items.Single(item => item.Key == key).StatusText}" };
    }

    private async Task RunSubclipsAsync(PremiereJob job, string binId, string? createName,
        List<PremiereReceipt> receipts, CancellationTokenSource cts)
    {
        foreach (var group in job.Subclips!.GroupBy(item => item.Source.AssetId))
        {
            var plannedItems = group.ToArray();
            cts.Token.ThrowIfCancellationRequested();
            foreach (var item in plannedItems) job = UpdateItem(job, item, PremiereJobItemState.Sending);
            Publish(job);
            var nativeSubclips = plannedItems.All(item => !item.Projection.IsSourceFallback);
            var source = plannedItems[0].Source with
            {
                Range = null,
                RangeIssue = null,
                PreserveRange = true,
                IsSubclipPrerequisite = nativeSubclips
            };
            PremiereReceipt sourceReceipt;
            try
            {
                var sourceCommand = await journal.PrepareAsync(job.Project, binId, createName, source, cts.Token).ConfigureAwait(false);
                sourceReceipt = await bridge.SendAsync(sourceCommand, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { sourceReceipt = new(Guid.Empty, PremiereOutcome.Failed, null, error.Message); }
            var earlierItemsVerified = true;
            for (var itemIndex = 0; itemIndex < plannedItems.Length; itemIndex++)
            {
                var item = plannedItems[itemIndex];
                cts.Token.ThrowIfCancellationRequested();
                PremiereReceipt result;
                if (sourceReceipt.Outcome != PremiereOutcome.Verified || string.IsNullOrWhiteSpace(sourceReceipt.ItemId))
                    result = new(Guid.Empty, sourceReceipt.Outcome, sourceReceipt.ItemId,
                        item.Projection.IsSourceFallback
                            ? $"Video preparation failed before sending the complete video: {sourceReceipt.Message}"
                            : $"Source reconciliation failed before creating {item.Projection.Name}: {sourceReceipt.Message}");
                else if (item.Projection.IsSourceFallback)
                    result = sourceReceipt with
                    {
                        Message = sourceReceipt.Message.Contains("imported", StringComparison.OrdinalIgnoreCase)
                            ? "Complete video imported and verified. Save your Premiere project to preserve the import."
                            : "Existing video verified; editor name and organization preserved."
                    };
                else
                {
                    try
                    {
                        var projection = item.Projection with
                        {
                            SourceItemId = sourceReceipt.ItemId,
                            RemoveSourceAfter = nativeSubclips && earlierItemsVerified
                                && sourceReceipt.Verification == PremiereProtocol.TemporarySubclipSourceVerification
                                && itemIndex == plannedItems.Length - 1
                        };
                        var command = await journal.PrepareSubclipAsync(job.Project, binId, createName,
                            item.Source with { PreserveRange = true }, projection, cts.Token).ConfigureAwait(false);
                        result = await bridge.SendAsync(command, cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) { result = new(Guid.Empty, PremiereOutcome.Failed, null, error.Message); }
                }
                receipts.Add(result);
                earlierItemsVerified &= result.Outcome == PremiereOutcome.Verified;
                job = UpdateItem(job with { Receipts = receipts.ToArray() }, item, ItemState(result), result);
                Publish(job);
                job = await RunMarkersAsync(job, item.Source, item.Projection, result, binId, receipts, cts.Token).ConfigureAwait(false);
            }
        }
    }

    private async Task<PremiereJob> RunMarkersAsync(PremiereJob job, PremiereSource source,
        PremiereSubclipProjection? subclip, PremiereReceipt target, string binId, List<PremiereReceipt> receipts, CancellationToken token)
    {
        var markers = await journal.PlanMarkersAsync(source, target.ItemId ?? "unavailable", subclip, token).ConfigureAwait(false);
        foreach (var marker in markers)
        {
            token.ThrowIfCancellationRequested();
            PremiereReceipt receipt;
            if (target.Outcome != PremiereOutcome.Verified || string.IsNullOrEmpty(target.ItemId))
                receipt = new(Guid.Empty, target.Outcome, null, "Marker: target handoff did not verify; marker was not sent.");
            else
            {
                var submitted = false;
                try
                {
                    var command = await journal.PrepareMarkerAsync(job.Project, binId, source, marker, token).ConfigureAwait(false);
                    submitted = true;
                    receipt = await bridge.SendAsync(command, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { receipt = new(Guid.Empty, submitted ? PremiereOutcome.UnknownOutcome : PremiereOutcome.Failed,
                    target.ItemId, "Marker: " + error.Message); }
            }
            receipts.Add(receipt);
            // Markers remain durable handoffs, but Jobs presents only requested media items.
            // Preserve an earlier parent failure; attach the first marker failure to a sent parent.
            job = job with { Receipts = receipts.ToArray(), Items = job.Items!.Select(item =>
                item.Key == marker.TargetKey && item.State == PremiereJobItemState.Sent
                    && receipt.Outcome != PremiereOutcome.Verified
                    ? item with { State = ItemState(receipt), Receipt = receipt } : item).ToArray() };
            Publish(job);
        }
        return job;
    }
}
