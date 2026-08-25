namespace LightflowStudio;

/// <summary>Application lifetime owner for accepted Export plans and their transient executors.</summary>
internal sealed class LegacyExportJobCoordinator : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly ApplicationJobsRuntime<EncodingJobOptions, EncodingItemResult> _runtime;
    private readonly IJobHistoryStore _history;
    private readonly Func<ExportExecutorLease> _executorFactory;
    private readonly Dictionary<Guid, ExportExecutorLease> _executors = [];
    private readonly HashSet<Guid> _historyRecorded = [];

    public LegacyExportJobCoordinator(ApplicationJobsRuntime<EncodingJobOptions, EncodingItemResult> runtime,
        IJobHistoryStore history, Func<ExportExecutorLease> executorFactory)
    {
        _runtime = runtime;
        _history = history;
        _executorFactory = executorFactory;
    }

    public event Action<EncodingJobHistoryRecord>? Completed;
    public int ActiveCount { get { lock (_sync) return _executors.Count; } }

    public JobRuntime<EncodingJobOptions, EncodingItemResult> Queue(JobPlan<EncodingJobOptions> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsValid) throw new ArgumentException("Only a valid immutable Export plan can be queued.", nameof(plan));
        var executor = _executorFactory();
        lock (_sync)
        {
            if (!_executors.TryAdd(plan.Definition.Id, executor))
                throw new InvalidOperationException($"Export job {plan.Definition.Id} is already active.");
        }
        try
        {
            var runtime = _runtime.Queue(plan, plan.Definition.Options.ParallelExports,
                (item, progress, token) => executor.Execute(item, plan.Definition.Options, progress, token));
            _ = ObserveCompletionAsync(plan, runtime);
            return runtime;
        }
        catch
        {
            lock (_sync) _executors.Remove(plan.Definition.Id);
            executor.Terminate();
            throw;
        }
    }

    public void TerminateAll()
    {
        ExportExecutorLease[] executors;
        lock (_sync) executors = _executors.Values.ToArray();
        foreach (var executor in executors) executor.Terminate();
    }

    private async Task ObserveCompletionAsync(JobPlan<EncodingJobOptions> plan,
        JobRuntime<EncodingJobOptions, EncodingItemResult> runtime)
    {
        try
        {
            var result = await runtime.Completion.ConfigureAwait(false);
            EncodingJobHistoryRecord? record = null;
            lock (_sync)
            {
                if (_historyRecorded.Add(plan.Definition.Id))
                    record = new(plan.Definition.Id, plan.Definition.Capability, plan.Definition.CreatedAt,
                        result.StartedAt, result.CompletedAt, result.State, plan.Definition, plan, result);
            }
            if (record is not null)
            {
                _history.Add(record);
                Completed?.Invoke(record);
            }
        }
        finally { lock (_sync) _executors.Remove(plan.Definition.Id); }
    }

    public async ValueTask DisposeAsync()
    {
        TerminateAll();
        await _runtime.DisposeAsync().ConfigureAwait(false);
        lock (_sync) _executors.Clear();
    }
}

internal sealed record ExportExecutorLease(
    Func<JobPlanItem, EncodingJobOptions, IProgress<double>, CancellationToken, Task<JobItemResult<EncodingItemResult>>> Execute,
    Action Terminate);
