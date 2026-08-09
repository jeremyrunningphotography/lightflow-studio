namespace LightflowStudio;

internal sealed class JobCancellation : IDisposable
{
    private readonly CancellationTokenSource _source = new();

    public CancellationToken Token => _source.Token;
    public bool IsCancellationRequested => _source.IsCancellationRequested;
    public void Cancel() => _source.Cancel();
    public void Dispose() => _source.Dispose();
}

internal sealed class JobItemExecution<TData>
{
    private static readonly IReadOnlyDictionary<JobState, JobState[]> AllowedTransitions =
        new Dictionary<JobState, JobState[]>
        {
            [JobState.Planned] = [JobState.Queued, JobState.Skipped, JobState.Cancelled, JobState.Failed],
            [JobState.Queued] = [JobState.Running, JobState.Skipped, JobState.Cancelled, JobState.Failed],
            [JobState.Running] = [JobState.Completed, JobState.CompletedWithWarnings, JobState.Cancelled, JobState.Failed],
            [JobState.Failed] = [JobState.Queued],
            [JobState.Cancelled] = [JobState.Queued]
        };

    public JobItemExecution(JobPlanItem planItem)
    {
        PlanItem = planItem;
        Warnings.AddRange(planItem.Issues
            .Where(issue => issue.Severity == JobIssueSeverity.Warning)
            .Select(issue => issue.Message));
        Errors.AddRange(planItem.Issues
            .Where(issue => issue.Severity == JobIssueSeverity.Error)
            .Select(issue => issue.Message));
        State = planItem.Disposition == JobPlanDisposition.Skip ? JobState.Skipped : JobState.Planned;
        ProgressPercent = State == JobState.Skipped ? 100 : null;
    }

    public JobPlanItem PlanItem { get; }
    public JobState State { get; private set; }
    public double? ProgressPercent { get; private set; }
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
    public TData? Data { get; private set; }

    public void Queue() => TransitionTo(JobState.Queued);
    public void Start() => TransitionTo(JobState.Running);

    public void ReportProgress(double percent)
    {
        if (State != JobState.Running) throw new InvalidOperationException("Progress can only be reported for a running item.");
        ProgressPercent = Math.Clamp(percent, 0, 100);
    }

    public void Complete(TData? data = default)
    {
        Data = data;
        ProgressPercent = 100;
        TransitionTo(JobState.Completed);
    }

    public void CompleteWithWarnings(IEnumerable<string> warnings, TData? data = default)
    {
        Warnings.AddRange(warnings.Where(value => !string.IsNullOrWhiteSpace(value)));
        Data = data;
        ProgressPercent = 100;
        TransitionTo(JobState.CompletedWithWarnings);
    }

    public void Skip(string? warning = null)
    {
        if (!string.IsNullOrWhiteSpace(warning)) Warnings.Add(warning);
        ProgressPercent = 100;
        TransitionTo(JobState.Skipped);
    }

    public void Fail(string error, TData? data = default)
    {
        if (!string.IsNullOrWhiteSpace(error)) Errors.Add(error);
        Data = data;
        ProgressPercent = 100;
        TransitionTo(JobState.Failed);
    }

    public void Cancel()
    {
        ProgressPercent = 100;
        TransitionTo(JobState.Cancelled);
    }

    public void Retry()
    {
        Warnings.Clear();
        Errors.Clear();
        Data = default;
        ProgressPercent = null;
        TransitionTo(JobState.Queued);
    }

    public JobItemResult<TData> Result() => new(
        PlanItem.Definition.Id,
        State,
        PlanItem.OutputPaths,
        Warnings.ToList(),
        Errors.ToList(),
        Data);

    private void TransitionTo(JobState next)
    {
        if (State == next) return;
        if (!AllowedTransitions.TryGetValue(State, out var allowed) || !allowed.Contains(next))
            throw new InvalidOperationException($"A job item cannot transition from {State} to {next}.");
        State = next;
    }
}

internal sealed class JobExecution<TOptions, TData>
{
    public JobExecution(JobPlan<TOptions> plan, DateTimeOffset? startedAt = null)
    {
        if (!plan.IsValid) throw new ArgumentException("A job cannot execute an invalid plan.", nameof(plan));
        Plan = plan;
        StartedAt = startedAt;
        Items = plan.Items.Select(item => new JobItemExecution<TData>(item)).ToList();
    }

    public JobPlan<TOptions> Plan { get; }
    public IReadOnlyList<JobItemExecution<TData>> Items { get; }
    public DateTimeOffset? StartedAt { get; private set; }

    public JobState State
    {
        get
        {
            if (Items.Any(item => item.State == JobState.Running)) return JobState.Running;
            if (Items.Any(item => item.State == JobState.Queued)) return JobState.Queued;
            if (Items.All(item => item.State == JobState.Planned)) return JobState.Planned;
            if (Items.Any(item => item.State == JobState.Failed)) return JobState.Failed;
            if (Items.Any(item => item.State == JobState.Cancelled)) return JobState.Cancelled;
            if (Items.Any(item => item.State == JobState.CompletedWithWarnings)) return JobState.CompletedWithWarnings;
            if (Items.All(item => item.State == JobState.Skipped)) return JobState.Skipped;
            if (Items.All(item => IsTerminal(item.State))) return JobState.Completed;
            return JobState.Planned;
        }
    }

    public void Queue()
    {
        foreach (var item in Items.Where(item => item.State == JobState.Planned)) item.Queue();
    }

    public void MarkStarted(DateTimeOffset? startedAt = null) => StartedAt ??= startedAt ?? DateTimeOffset.Now;

    public void CancelPending()
    {
        foreach (var item in Items.Where(item => item.State is JobState.Planned or JobState.Queued)) item.Cancel();
    }

    public JobProgressSnapshot Progress(JobItemExecution<TData>? current = null)
    {
        var workItems = Items.Where(item => item.PlanItem.Disposition == JobPlanDisposition.Process).ToList();
        if (workItems.Count == 0)
            return new(100, current?.ProgressPercent, 0, 0, Plan.WorkUnit);

        var determinate = workItems.All(item => item.PlanItem.WorkEstimate.IsDeterminate);
        var total = determinate ? workItems.Sum(item => item.PlanItem.WorkEstimate.Value!.Value) : (double?)null;
        var completed = determinate
            ? workItems.Sum(item => item.PlanItem.WorkEstimate.Value!.Value * CompletionFraction(item))
            : workItems.Count(item => IsTerminal(item.State));
        double? percent = total is > 0 ? Math.Clamp(completed * 100 / total.Value, 0, 100) : null;
        if (workItems.All(item => IsTerminal(item.State))) percent = 100;
        return new(percent, current?.ProgressPercent, completed, total, Plan.WorkUnit);
    }

    public JobResult<TData> Result(DateTimeOffset? completedAt = null)
    {
        if (Items.Any(item => !IsTerminal(item.State)))
            throw new InvalidOperationException("A result is only available after every job item reaches a terminal state.");
        var results = Items.Select(item => item.Result()).ToList();
        var summary = new JobResultSummary(
            results.Count,
            results.Count(item => item.State == JobState.Completed),
            results.Count(item => item.State == JobState.CompletedWithWarnings),
            results.Count(item => item.State == JobState.Skipped),
            results.Count(item => item.State == JobState.Cancelled),
            results.Count(item => item.State == JobState.Failed));
        var warnings = Plan.Issues
            .Where(issue => issue.Severity == JobIssueSeverity.Warning)
            .Select(issue => issue.Message)
            .Concat(results.SelectMany(item => item.Warnings))
            .ToList();
        var errors = Plan.Issues
            .Where(issue => issue.Severity == JobIssueSeverity.Error)
            .Select(issue => issue.Message)
            .Concat(results.SelectMany(item => item.Errors))
            .ToList();
        return new(Plan.Definition.Id, State, StartedAt, completedAt ?? DateTimeOffset.Now,
            results, summary, warnings, errors);
    }

    private static double CompletionFraction(JobItemExecution<TData> item) => item.State switch
    {
        JobState.Running => Math.Clamp(item.ProgressPercent ?? 0, 0, 100) / 100,
        _ when IsTerminal(item.State) => 1,
        _ => 0
    };

    private static bool IsTerminal(JobState state) => state is
        JobState.Completed or JobState.CompletedWithWarnings or JobState.Skipped or JobState.Cancelled or JobState.Failed;
}

internal static class SequentialJobRunner
{
    public static async Task<JobResult<TData>> RunAsync<TOptions, TData>(
        JobExecution<TOptions, TData> execution,
        Func<JobPlanItem, IProgress<double>, CancellationToken, Task<JobItemResult<TData>>> execute,
        JobCancellation cancellation,
        IProgress<JobProgressSnapshot>? progress = null)
    {
        execution.MarkStarted();
        execution.Queue();
        foreach (var item in execution.Items.Where(item => item.State == JobState.Queued))
        {
            if (cancellation.IsCancellationRequested)
            {
                execution.CancelPending();
                break;
            }

            item.Start();
            var itemProgress = new Progress<double>(value =>
            {
                item.ReportProgress(value);
                progress?.Report(execution.Progress(item));
            });
            try
            {
                var result = await execute(item.PlanItem, itemProgress, cancellation.Token);
                ApplyResult(item, result);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                item.Cancel();
                execution.CancelPending();
                break;
            }
            catch (Exception ex)
            {
                item.Fail(ex.Message);
            }
            progress?.Report(execution.Progress(item));
        }

        execution.CancelPending();
        return execution.Result();
    }

    private static void ApplyResult<TData>(JobItemExecution<TData> item, JobItemResult<TData> result)
    {
        switch (result.State)
        {
            case JobState.Completed:
                item.Complete(result.Data);
                break;
            case JobState.CompletedWithWarnings:
                item.CompleteWithWarnings(result.Warnings, result.Data);
                break;
            case JobState.Skipped:
                item.Skip(result.Warnings.FirstOrDefault());
                break;
            case JobState.Cancelled:
                item.Cancel();
                break;
            case JobState.Failed:
                item.Fail(result.Errors.FirstOrDefault() ?? "The operation failed.", result.Data);
                break;
            default:
                throw new InvalidOperationException($"{result.State} is not a valid item result state.");
        }
    }
}
