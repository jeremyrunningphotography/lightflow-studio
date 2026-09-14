using System.IO;

namespace LightflowStudio;

internal sealed record PremiereJob(Guid JobId, PremiereProject Project, IReadOnlyList<PremiereSource> Sources,
    JobState State, int Completed, IReadOnlyList<PremiereReceipt> Receipts, string Message, DateTimeOffset CreatedUtc)
{
    public string Name => $"Send {Sources.Count} source(s) to Premiere";
    public double Progress => Sources.Count == 0 ? 0 : Completed * 100d / Sources.Count;
    public string Details => $"Project: {Project.Name}\n{Completed} of {Sources.Count} source(s) processed.\n{Message}\n"
        + string.Join("\n", Receipts.Select(receipt => $"{receipt.Outcome}: {receipt.Message}"));
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
                var command = await journal.PrepareAsync(job.Project, binId, createName, source, cts.Token).ConfigureAwait(false);
                var result = await bridge.SendAsync(command, cts.Token).ConfigureAwait(false);
                receipts.Add(result);
                job = job with { Completed = receipts.Count, Receipts = receipts.ToArray(), Message = $"{Path.GetFileName(source.Path)}: {result.Message}" };
                Publish(job);
                if (result.Outcome == PremiereOutcome.UnknownOutcome)
                    throw new InvalidOperationException("Import outcome is uncertain. Reconnect to the original project and send the same Catalog selection to reconcile.");
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
