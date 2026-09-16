using System.IO;

namespace LightflowStudio;

internal sealed record PremiereJob(Guid JobId, PremiereProject Project, IReadOnlyList<PremiereSource> Sources,
    JobState State, int Completed, IReadOnlyList<PremiereReceipt> Receipts, string Message, DateTimeOffset CreatedUtc)
{
    public string Name => $"Send {Sources.Count} source(s) to Premiere";
    public double Progress => Sources.Count == 0 ? 0 : Completed * 100d / Sources.Count;
    public string Details => $"Project: {Project.Name}\n{Completed} of {Sources.Count} source(s) processed.\n{Message}\n"
        + string.Join("\n", Receipts.Select(receipt => $"{OutcomeText(receipt)}: {UserMessage(receipt)}"));
    internal static string OutcomeText(PremiereReceipt receipt) => receipt.Outcome switch
    {
        PremiereOutcome.Verified when receipt.Message.Contains("updated", StringComparison.OrdinalIgnoreCase) => "Updated",
        PremiereOutcome.Verified when receipt.Message.Contains("cleared", StringComparison.OrdinalIgnoreCase) => "Updated",
        PremiereOutcome.Verified when receipt.Message.Contains("imported", StringComparison.OrdinalIgnoreCase) => "Imported",
        PremiereOutcome.Verified => "Verified",
        PremiereOutcome.Conflict => "Not sent",
        PremiereOutcome.UnknownOutcome => "Needs reconciliation",
        _ => "Failed"
    };
    internal static string UserMessage(PremiereReceipt receipt) => receipt.Outcome switch
    {
        PremiereOutcome.Verified => receipt.Message,
        PremiereOutcome.UnknownOutcome => "Premiere may have started this import but it could not be verified. Return to the original project and send the same source again.",
        PremiereOutcome.Conflict when receipt.Message.Contains("missing or relinked", StringComparison.OrdinalIgnoreCase)
            => "The matching Premiere source was moved or relinked. Restore or relink it in the original project, then send again.",
        PremiereOutcome.Conflict when receipt.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
            => "A matching file already exists in Premiere but is not linked to this Catalog source. Resolve that item in the original project, then send again.",
        PremiereOutcome.Conflict when receipt.Message.Contains("identity changed", StringComparison.OrdinalIgnoreCase)
            => "The source changed since its earlier handoff. Refresh the Browser and resolve the existing Premiere source before sending again.",
        PremiereOutcome.Conflict => "Premiere could not safely reconcile this source. Resolve the existing source in the original project, then send again.",
        _ => "Premiere did not change this source. Check the connection and try again."
    };
    public JobCardPresentation Card(bool expanded) => new(JobId, Name, JobsPresentation.Glyph(State),
        State == JobState.Running ? "Sending" : JobsPresentation.StateText(State), Progress, State == JobState.Running,
        "", null, new JobMessageDetailsPresentation(Details), Message, expanded, false, false, false,
        State is JobState.Queued or JobState.Running, false);
    public JobsWorkspaceItem WorkspaceItem() => new(JobId, null, null, State is JobState.Queued or JobState.Running,
        false, Name, "Premiere handoff", State, Progress, CreatedUtc.ToLocalTime().ToString("MMM d, HH:mm"),
        Sources.FirstOrDefault()?.Path ?? "", Project.Path, Message, Details, CreatedUtc, long.MaxValue,
        new JobMessageDetailsPresentation(Details), SupportsQueueControls: false);
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
                job.State is JobState.Queued or JobState.Running && PremiereProtocol.DestinationId(job.Project) == command.Intent.DestinationId
                && job.Sources.Any(source => source.AssetId == command.Intent.Source.AssetId))));
        }
    }
    internal static IReadOnlyList<JobsWorkspaceItem> ProjectHistory(IEnumerable<PremiereCommand> history)
        => history.Select(command =>
            {
                var receipt = command.PreviousReceipt;
                var state = receipt?.Outcome == PremiereOutcome.Verified ? JobState.Completed : JobState.Failed;
                var message = receipt?.Message ?? (command.PreviouslyDispatched
                    ? "Interrupted handoff. Send the same source in the original project to reconcile."
                    : "Prepared but not dispatched. Send the source again to continue.");
                return new JobsWorkspaceItem(command.Intent.OperationId, null, null, false, false,
                    $"Premiere: {Path.GetFileName(command.Intent.Source.Path)}", "Premiere handoff", state,
                    receipt is null ? 0 : 100, command.Intent.CreatedUtc.ToLocalTime().ToString("MMM d, HH:mm"),
                    command.Intent.Source.Path, command.Intent.Project.Path, message, message,
                    command.Intent.CreatedUtc, long.MaxValue, new JobMessageDetailsPresentation(message));
            }).ToArray();

    public void Enqueue(PremiereProject project, string binId, string? createName, IReadOnlyList<PremiereSource> sources)
    {
        if (sources.Count is < 1 or > 500) throw new InvalidOperationException("Select between 1 and 500 source assets.");
        var job = new PremiereJob(Guid.NewGuid(), project, sources.ToArray(), JobState.Queued, 0, [], "", DateTimeOffset.UtcNow);
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
                job = job with { Completed = receipts.Count, Receipts = receipts.ToArray(), Message = $"{Path.GetFileName(source.Path)}: {PremiereJob.UserMessage(result)}" };
                Publish(job);
            }
            job = job with { State = receipts.All(receipt => receipt.Outcome == PremiereOutcome.Verified)
                ? JobState.Completed : JobState.CompletedWithWarnings };
        }
        catch (OperationCanceledException)
        { job = job with { State = JobState.Cancelled, Message = "Cancelled. Any import already in progress may remain in Premiere; resend the same selection to reconcile." }; }
        catch (Exception error)
        { job = job with { State = JobState.Failed, Message = error is TimeoutException ? "Companion did not finish in time. Reconnect and reconcile the original project." : error.Message }; }
        finally
        {
            if (entered) _serial.Release();
            lock (_sync) _cancellations.Remove(job.JobId);
            cts.Dispose(); Publish(job);
            try { await RefreshHistoryAsync().ConfigureAwait(false); } catch { /* Durable journal errors already surface on handoff; keep current result visible. */ }
        }
    }
}
