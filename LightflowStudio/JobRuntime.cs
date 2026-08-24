using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightflowStudio;

internal interface IJobRuntimeObserver<TData>
{
    void OnChanged(JobRuntimeSnapshot<TData> snapshot);
}

internal interface IJobActiveClock
{
    bool IsRunning { get; }
    TimeSpan Elapsed { get; }
    void Start();
    void Stop();
}

internal sealed class StopwatchJobActiveClock : IJobActiveClock
{
    private readonly Stopwatch _stopwatch = new();
    public bool IsRunning => _stopwatch.IsRunning;
    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public void Start() => _stopwatch.Start();
    public void Stop() => _stopwatch.Stop();
}

internal sealed class ApplicationJobsRuntime<TOptions, TData> : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Entry> _jobs = [];
    private readonly Action<JobPlan<TOptions>, JobRuntimeSnapshot<TData>, bool>? _checkpoint;

    public ApplicationJobsRuntime(Action<JobPlan<TOptions>, JobRuntimeSnapshot<TData>, bool>? checkpoint = null) =>
        _checkpoint = checkpoint;

    public event Action<IReadOnlyList<JobRuntimeSnapshot<TData>>>? Changed;

    public IReadOnlyList<JobRuntimeSnapshot<TData>> Jobs
    {
        get { lock (_sync) return _jobs.Values.Select(entry => entry.Runtime.Snapshot()).OrderBy(job => job.CreatedAt).ToList(); }
    }

    public JobRuntime<TOptions, TData> Queue(JobPlan<TOptions> plan, int parallelism,
        Func<JobPlanItem, IProgress<double>, CancellationToken, Task<JobItemResult<TData>>> executor)
    {
        var runtime = new JobRuntime<TOptions, TData>(plan, parallelism, executor);
        var observer = new Observer(this, runtime);
        var subscription = runtime.Subscribe(observer);
        lock (_sync)
        {
            if (!_jobs.TryAdd(plan.Definition.Id, new(runtime, subscription)))
            {
                subscription.Dispose();
                throw new InvalidOperationException($"Job {plan.Definition.Id} is already registered.");
            }
        }
        runtime.StartAsync();
        Publish(runtime);
        return runtime;
    }

    public bool Pause(Guid jobId) => Act(jobId, runtime => runtime.Pause());
    public bool Resume(Guid jobId) => Act(jobId, runtime => runtime.Resume());
    public bool Cancel(Guid jobId) => Act(jobId, runtime => runtime.Cancel());

    public async ValueTask DisposeAsync()
    {
        Entry[] entries;
        lock (_sync) entries = _jobs.Values.ToArray();
        foreach (var entry in entries) entry.Runtime.Cancel();
        foreach (var entry in entries)
        {
            await entry.Runtime.DisposeAsync();
            entry.Subscription.Dispose();
        }
        lock (_sync) _jobs.Clear();
    }

    private bool Act(Guid id, Action<JobRuntime<TOptions, TData>> action)
    {
        JobRuntime<TOptions, TData>? runtime;
        lock (_sync) runtime = _jobs.GetValueOrDefault(id)?.Runtime;
        if (runtime is null) return false;
        action(runtime);
        return true;
    }

    private void Publish(JobRuntime<TOptions, TData> runtime)
    {
        var snapshot = runtime.Snapshot();
        var checkpoint = true;
        lock (_sync)
        {
            if (_jobs.TryGetValue(snapshot.JobId, out var entry))
            {
                var progress = (int)Math.Floor(snapshot.Progress.OverallPercent ?? -1);
                checkpoint = entry.LastState != snapshot.State || entry.LastProgress != progress;
                entry.LastState = snapshot.State;
                entry.LastProgress = progress;
            }
        }
        if (checkpoint) _checkpoint?.Invoke(runtime.Plan, snapshot, runtime.IsPauseRequested);
        Changed?.Invoke(Jobs);
    }

    private sealed class Entry(JobRuntime<TOptions, TData> runtime, IDisposable subscription)
    {
        public JobRuntime<TOptions, TData> Runtime { get; } = runtime;
        public IDisposable Subscription { get; } = subscription;
        public JobState? LastState { get; set; }
        public int LastProgress { get; set; } = -2;
    }
    private sealed class Observer(ApplicationJobsRuntime<TOptions, TData> owner, JobRuntime<TOptions, TData> runtime)
        : IJobRuntimeObserver<TData>
    {
        public void OnChanged(JobRuntimeSnapshot<TData> snapshot) => owner.Publish(runtime);
    }
}

internal sealed class JobRuntime<TOptions, TData> : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly JobExecution<TOptions, TData> _execution;
    private readonly Func<JobPlanItem, IProgress<double>, CancellationToken, Task<JobItemResult<TData>>> _executor;
    private readonly int _parallelism;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _resumeSignal;
    private readonly IJobActiveClock _activeClock;
    private readonly DateTimeOffset?[] _itemStarted;
    private readonly DateTimeOffset?[] _itemCompleted;
    private readonly List<IJobRuntimeObserver<TData>> _observers = [];
    private Task<JobResult<TData>>? _run;
    private bool _pauseRequested;
    private int _activeWorkers;
    private int _nextItem;
    private DateTimeOffset? _completedAt;
    private double _lastOverall;

    public JobRuntime(JobPlan<TOptions> plan, int parallelism,
        Func<JobPlanItem, IProgress<double>, CancellationToken, Task<JobItemResult<TData>>> executor,
        IJobActiveClock? activeClock = null)
    {
        _parallelism = EncodingJobConcurrency.Validate(parallelism);
        _resumeSignal = new SemaphoreSlim(0, _parallelism);
        _execution = new JobExecution<TOptions, TData>(plan);
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _activeClock = activeClock ?? new StopwatchJobActiveClock();
        _itemStarted = new DateTimeOffset?[plan.Items.Count];
        _itemCompleted = new DateTimeOffset?[plan.Items.Count];
    }

    public JobPlan<TOptions> Plan => _execution.Plan;
    public bool IsPauseRequested { get { lock (_sync) return _pauseRequested; } }
    public Task<JobResult<TData>> Completion => _run ?? throw new InvalidOperationException("The job has not started.");

    public IDisposable Subscribe(IJobRuntimeObserver<TData> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_sync) _observers.Add(observer);
        observer.OnChanged(Snapshot());
        return new Subscription(this, observer);
    }

    public Task<JobResult<TData>> StartAsync()
    {
        lock (_sync)
        {
            if (_run is not null) return _run;
            _execution.MarkStarted(DateTimeOffset.Now);
            _execution.Queue();
            _activeClock.Start();
            _run = RunCoreAsync();
        }
        Publish();
        return _run;
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_cancellation.IsCancellationRequested || IsFinished()) return;
            _pauseRequested = true;
            if (_activeWorkers == 0 && _activeClock.IsRunning) _activeClock.Stop();
        }
        Publish();
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (!_pauseRequested || _cancellation.IsCancellationRequested || IsFinished()) return;
            _pauseRequested = false;
            _activeClock.Start();
            var release = _parallelism - _resumeSignal.CurrentCount;
            if (release > 0) _resumeSignal.Release(release);
        }
        Publish();
    }

    public void Cancel()
    {
        lock (_sync)
        {
            if (_cancellation.IsCancellationRequested) return;
            _pauseRequested = false;
            _cancellation.Cancel();
            _execution.CancelPending();
            var release = _parallelism - _resumeSignal.CurrentCount;
            if (release > 0) _resumeSignal.Release(release);
        }
        Publish();
    }

    public JobRuntimeSnapshot<TData> Snapshot()
    {
        lock (_sync)
        {
            var progress = _execution.Progress();
            var overall = Math.Max(_lastOverall, progress.OverallPercent ?? 0);
            _lastOverall = overall;
            progress = progress with { OverallPercent = progress.OverallPercent is null ? null : overall };
            var items = _execution.Items.Select((item, index) => new JobItemRuntimeSnapshot<TData>(
                item.PlanItem.Definition.Id, index, item.State, item.ProgressPercent, _itemStarted[index],
                _itemCompleted[index], item.Warnings.ToList(), item.Errors.ToList(), item.Data)).ToList();
            var terminal = items.Count(item => IsTerminal(item.State));
            var state = RuntimeState(terminal);
            var eta = EstimateEta(progress, state);
            return new(_execution.Plan.Definition.Id, state, _execution.Plan.Definition.CreatedAt,
                _execution.StartedAt, _completedAt, _activeClock.Elapsed, eta, progress,
                new(items.Count,
                    items.Count(item => item.State is JobState.Planned or JobState.Queued),
                    items.Count(item => item.State == JobState.Running),
                    items.Count(item => item.State is JobState.Completed or JobState.CompletedWithWarnings),
                    items.Count(item => item.State == JobState.Failed),
                    items.Count(item => item.State == JobState.Cancelled),
                    items.Count(item => item.State == JobState.Skipped)),
                items, items.SelectMany(item => item.Warnings).ToList(), items.SelectMany(item => item.Errors).ToList());
        }
    }

    public async ValueTask DisposeAsync()
    {
        Cancel();
        if (_run is not null) try { await _run.ConfigureAwait(false); } catch { }
        _cancellation.Dispose();
        _resumeSignal.Dispose();
    }

    private async Task<JobResult<TData>> RunCoreAsync()
    {
        var workers = Enumerable.Range(0, _parallelism).Select(_ => WorkerAsync()).ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);
        lock (_sync)
        {
            _execution.CancelPending();
            _completedAt = DateTimeOffset.Now;
            if (_activeClock.IsRunning) _activeClock.Stop();
        }
        Publish();
        return _execution.Result(_completedAt);
    }

    private async Task WorkerAsync()
    {
        while (true)
        {
            JobItemExecution<TData>? item;
            int index;
            while (true)
            {
                lock (_sync)
                {
                    if (_cancellation.IsCancellationRequested) return;
                    while (_nextItem < _execution.Items.Count && _execution.Items[_nextItem].State != JobState.Queued) _nextItem++;
                    if (_nextItem >= _execution.Items.Count) return;
                    if (!_pauseRequested)
                    {
                        index = _nextItem++;
                        item = _execution.Items[index];
                        item.Start();
                        _itemStarted[index] = DateTimeOffset.Now;
                        _activeWorkers++;
                        break;
                    }
                    item = null;
                    index = -1;
                }
                Publish();
                try { await _resumeSignal.WaitAsync(_cancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { return; }
            }

            Publish();
            try
            {
                var progress = new InlineProgress(value =>
                {
                    lock (_sync) if (item.State == JobState.Running) item.ReportProgress(value);
                    Publish();
                });
                var result = await _executor(item.PlanItem, progress, _cancellation.Token).ConfigureAwait(false);
                lock (_sync) ApplyResult(item, result);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                lock (_sync) if (item.State == JobState.Running) item.Cancel();
            }
            catch (Exception exception)
            {
                lock (_sync) if (item.State == JobState.Running) item.Fail(exception.Message);
            }
            finally
            {
                lock (_sync)
                {
                    _itemCompleted[index] = DateTimeOffset.Now;
                    _activeWorkers--;
                    if (_pauseRequested && _activeWorkers == 0 && _activeClock.IsRunning) _activeClock.Stop();
                }
                Publish();
            }
        }
    }

    private JobState RuntimeState(int terminal)
    {
        if (_cancellation.IsCancellationRequested) return _activeWorkers > 0 ? JobState.Cancelling : JobState.Cancelled;
        if (_pauseRequested) return _activeWorkers > 0 ? JobState.Pausing : JobState.Paused;
        if (terminal == _execution.Items.Count) return _execution.State;
        return _activeWorkers > 0 ? JobState.Running : JobState.Queued;
    }

    private TimeSpan? EstimateEta(JobProgressSnapshot progress, JobState state)
    {
        if (state is JobState.Paused or JobState.Pausing or JobState.Cancelling || !_activeClock.IsRunning || progress.TotalWork is not > 0
            || progress.CompletedWork <= 0 || progress.CompletedWork >= progress.TotalWork) return null;
        var seconds = _activeClock.Elapsed.TotalSeconds * (progress.TotalWork.Value - progress.CompletedWork) / progress.CompletedWork;
        return double.IsFinite(seconds) && seconds >= 0 ? TimeSpan.FromSeconds(seconds) : null;
    }

    private void Publish()
    {
        JobRuntimeSnapshot<TData> snapshot;
        IJobRuntimeObserver<TData>[] observers;
        lock (_sync) { snapshot = Snapshot(); observers = _observers.ToArray(); }
        foreach (var observer in observers) observer.OnChanged(snapshot);
    }

    private static void ApplyResult(JobItemExecution<TData> item, JobItemResult<TData> result)
    {
        switch (result.State)
        {
            case JobState.Completed: item.Complete(result.Data); break;
            case JobState.CompletedWithWarnings: item.CompleteWithWarnings(result.Warnings, result.Data); break;
            case JobState.Skipped: item.Skip(result.Warnings.FirstOrDefault()); break;
            case JobState.Cancelled: item.Cancel(); break;
            case JobState.Failed: item.Fail(result.Errors.FirstOrDefault() ?? "The operation failed.", result.Data); break;
            default: throw new InvalidOperationException($"{result.State} is not a terminal item state.");
        }
    }

    private bool IsFinished() => _completedAt is not null;
    private static bool IsTerminal(JobState state) => state is JobState.Completed or JobState.CompletedWithWarnings
        or JobState.Skipped or JobState.Cancelled or JobState.Failed;

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class Subscription(JobRuntime<TOptions, TData> owner, IJobRuntimeObserver<TData> observer) : IDisposable
    {
        public void Dispose() { lock (owner._sync) owner._observers.Remove(observer); }
    }
}

internal enum JobRecoveryDisposition { Waiting, Paused, NeedsAttention, Terminal }

internal sealed record JobRuntimeCheckpoint<TOptions, TData>(
    int Version, JobPlan<TOptions> Plan, JobRuntimeSnapshot<TData> Runtime, bool PauseRequested);

internal sealed record RecoveredJob<TOptions, TData>(
    JobRuntimeCheckpoint<TOptions, TData> Checkpoint, JobRecoveryDisposition Disposition, IReadOnlyList<JobIssue> Issues);

internal sealed class JobRuntimeStore<TOptions, TData>(string path)
{
    private readonly object _sync = new();
    public const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public void Save(JobPlan<TOptions> plan, JobRuntimeSnapshot<TData> runtime, bool pauseRequested)
    {
        lock (_sync)
        {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        try
        {
            var checkpoint = new JobRuntimeCheckpoint<TOptions, TData>(SchemaVersion, plan, runtime, pauseRequested);
            var jobs = ReadDocument().Where(job => job.Plan.Definition.Id != plan.Definition.Id).Append(checkpoint).ToList();
            File.WriteAllText(temporary, JsonSerializer.Serialize(new RuntimeDocument<TOptions, TData>(SchemaVersion, jobs), Options));
            File.Move(temporary, path, true);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }
    }

    public RecoveredJob<TOptions, TData>? Load(Func<JobPlanItem, TOptions, IReadOnlyList<JobIssue>> revalidate)
        => LoadAll(revalidate).OrderByDescending(job => job.Checkpoint.Runtime.CreatedAt).FirstOrDefault();

    public IReadOnlyList<RecoveredJob<TOptions, TData>> LoadAll(Func<JobPlanItem, TOptions, IReadOnlyList<JobIssue>> revalidate)
    {
        lock (_sync)
        {
        try
        {
            var recovered = new List<RecoveredJob<TOptions, TData>>();
            foreach (var checkpoint in ReadDocument())
            {
                if (checkpoint.Version != SchemaVersion || checkpoint.Plan is null || checkpoint.Runtime is null) continue;
                var issues = checkpoint.Plan.Items.Where((_, index) =>
                    checkpoint.Runtime.Items.ElementAtOrDefault(index)?.State is JobState.Planned or JobState.Queued or JobState.Running or JobState.Pausing or JobState.Paused)
                    .SelectMany(item => revalidate(item, checkpoint.Plan.Definition.Options)).ToList();
                var wasRunning = checkpoint.Runtime.Items.Any(item => item.State == JobState.Running)
                             || checkpoint.Runtime.State is JobState.Running or JobState.Pausing or JobState.Cancelling;
                var disposition = issues.Any(issue => issue.Severity == JobIssueSeverity.Error) || wasRunning
                    ? JobRecoveryDisposition.NeedsAttention
                    : checkpoint.PauseRequested || checkpoint.Runtime.State == JobState.Paused
                        ? JobRecoveryDisposition.Paused
                        : checkpoint.Runtime.Items.All(item => IsTerminal(item.State))
                            ? JobRecoveryDisposition.Terminal : JobRecoveryDisposition.Waiting;
                recovered.Add(new(checkpoint, disposition, issues));
            }
            return recovered;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { return []; }
        }
    }

    private IReadOnlyList<JobRuntimeCheckpoint<TOptions, TData>> ReadDocument()
    {
        if (!File.Exists(path)) return [];
        var document = JsonSerializer.Deserialize<RuntimeDocument<TOptions, TData>>(File.ReadAllText(path), Options);
        return document is { Version: SchemaVersion, Jobs: not null } ? document.Jobs : [];
    }

    private static bool IsTerminal(JobState state) => state is JobState.Completed or JobState.CompletedWithWarnings
        or JobState.Skipped or JobState.Cancelled or JobState.Failed;

    private sealed record RuntimeDocument<TStoredOptions, TStoredData>(
        int Version, IReadOnlyList<JobRuntimeCheckpoint<TStoredOptions, TStoredData>> Jobs);
}
