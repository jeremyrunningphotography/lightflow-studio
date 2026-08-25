namespace LightflowStudio;

internal sealed class ExportJobCoordinator : IAsyncDisposable
{
    private readonly GlobalExportScheduler _scheduler;
    private readonly IJobHistoryStore _history;
    private readonly HashSet<Guid> _historyRecorded = [];
    private readonly object _sync = new();

    public ExportJobCoordinator(GlobalExportScheduler scheduler, IJobHistoryStore history)
    {
        _scheduler = scheduler;
        _history = history;
        _scheduler.Completed += Scheduler_Completed;
    }

    public event Action<EncodingJobHistoryRecord>? Completed;
    public int ActiveCount => _scheduler.Jobs.Count(job => job.State == JobState.Running);
    public GlobalExportScheduler Scheduler => _scheduler;

    public ExportQueueAdmission Queue(JobPlan<EncodingJobOptions> plan)
    {
        var admission = _scheduler.Admit(ExportSubmissionProposal.FromPlan(plan));
        if (!admission.Accepted)
            throw new InvalidOperationException(string.Join(Environment.NewLine, admission.Issues.Select(issue => issue.Message)));
        return admission;
    }

    public void TerminateAll()
    {
        foreach (var job in _scheduler.Jobs.Where(job => job.State == JobState.Running)) _scheduler.Cancel(job.JobId);
    }

    public async ValueTask DisposeAsync()
    {
        _scheduler.Completed -= Scheduler_Completed;
        await _scheduler.DisposeAsync().ConfigureAwait(false);
    }

    private void Scheduler_Completed(ExportJobSnapshot snapshot)
    {
        lock (_sync) if (!_historyRecorded.Add(snapshot.JobId)) return;
        var definition = new JobDefinition<EncodingJobOptions>(snapshot.JobId, "video.encode",
            snapshot.Definition.AcceptedAt, snapshot.Definition.Recipe, [snapshot.Definition.PlanItem.Definition]);
        var plan = new JobPlan<EncodingJobOptions>(definition, snapshot.Definition.AcceptedAt,
            [snapshot.Definition.PlanItem], snapshot.Definition.PlanItem.Issues,
            snapshot.Definition.PlanItem.WorkEstimate.Unit);
        var itemResult = new JobItemResult<EncodingItemResult>(snapshot.JobId, snapshot.State,
            snapshot.Definition.PlanItem.OutputPaths, snapshot.Warnings, snapshot.Errors, snapshot.Result);
        var summary = new JobResultSummary(1, snapshot.State == JobState.Completed ? 1 : 0,
            snapshot.State == JobState.CompletedWithWarnings ? 1 : 0, snapshot.State == JobState.Skipped ? 1 : 0,
            snapshot.State == JobState.Cancelled ? 1 : 0, snapshot.State == JobState.Failed ? 1 : 0);
        var completedAt = snapshot.CompletedAt ?? DateTimeOffset.Now;
        var result = new JobResult<EncodingItemResult>(snapshot.JobId, snapshot.State, snapshot.StartedAt,
            completedAt, [itemResult], summary, snapshot.Warnings, snapshot.Errors);
        var record = new EncodingJobHistoryRecord(snapshot.JobId, "video.encode", snapshot.Definition.AcceptedAt,
            snapshot.StartedAt, completedAt, snapshot.State, definition, plan, result);
        lock (_sync) _history.Add(record);
        Completed?.Invoke(record);
    }
}
