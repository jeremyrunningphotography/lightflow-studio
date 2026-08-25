using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightflowStudio;

internal sealed record ExportJobDefinition(
    Guid JobId,
    Guid? SubmissionId,
    long QueueOrder,
    DateTimeOffset AcceptedAt,
    EncodingJobOptions Recipe,
    JobPlanItem PlanItem)
{
    public string OutputPath => PlanItem.OutputPaths.Single();
    public string DisplayName => Path.GetFileName(OutputPath);
}

internal sealed record ExportSubmissionProposal(Guid SubmissionId, IReadOnlyList<ExportJobDefinition> Jobs)
{
    public static ExportSubmissionProposal FromPlan(JobPlan<EncodingJobOptions> plan, Guid? submissionId = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsValid) throw new ArgumentException("Only a valid, completely materialized submission can be proposed.", nameof(plan));
        var id = submissionId ?? Guid.NewGuid();
        var accepted = DateTimeOffset.Now;
        var recipe = plan.Definition.Options with { ParallelExports = EncodingJobConcurrency.Default };
        return new(id, plan.Items.Select(item => new ExportJobDefinition(
            item.Definition.Id, id, 0, accepted, recipe, item)).ToList());
    }
}

internal sealed record ExportQueueAdmission(bool Accepted, IReadOnlyList<ExportJobSnapshot> Jobs,
    IReadOnlyList<JobIssue> Issues)
{
    public static ExportQueueAdmission Rejected(params JobIssue[] issues) => new(false, [], issues);
}

internal sealed record ExportJobSnapshot(
    ExportJobDefinition Definition,
    JobState State,
    double? ProgressPercent,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    TimeSpan Elapsed,
    TimeSpan? Eta,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    EncodingItemResult? Result)
{
    public Guid JobId => Definition.JobId;
    public long QueueOrder => Definition.QueueOrder;
    public string OutputPath => Definition.OutputPath;
    public string DisplayName => Definition.DisplayName;
}

internal interface IExportQueueStore
{
    IReadOnlyList<ExportJobCheckpoint> Load();
    void Save(IReadOnlyList<ExportJobCheckpoint> jobs);
}

internal sealed record ExportJobCheckpoint(ExportJobDefinition Definition, JobState State, double? ProgressPercent,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, TimeSpan Elapsed,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors, EncodingItemResult? Result);

internal sealed class ExportQueueStore(string path) : IExportQueueStore
{
    public const int SchemaVersion = 2;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public IReadOnlyList<ExportJobCheckpoint> Load()
    {
        try
        {
            if (!File.Exists(path)) return [];
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (!json.RootElement.TryGetProperty("version", out var version) || version.GetInt32() != SchemaVersion
                || !json.RootElement.TryGetProperty("jobs", out var jobs)) return [];
            return jobs.Deserialize<List<ExportJobCheckpoint>>(Options) ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { return []; }
    }

    public void Save(IReadOnlyList<ExportJobCheckpoint> jobs)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new QueueDocument(SchemaVersion, jobs), Options));
            File.Move(temporary, path, true);
        }
        finally { try { File.Delete(temporary); } catch { } }
    }

    private sealed record QueueDocument(int Version, IReadOnlyList<ExportJobCheckpoint> Jobs);
}

/// <summary>One application-wide reservation, queue-order, concurrency, lifecycle, and recovery boundary.</summary>
internal sealed class GlobalExportScheduler : IAsyncDisposable
{
    private const int ProgressCheckpointStep = 5;
    private readonly object _sync = new();
    private readonly List<Entry> _jobs = [];
    private readonly Dictionary<Guid, ActiveExecution> _active = [];
    private readonly HashSet<string> _reservations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<ExportExecutorLease> _executorFactory;
    private readonly IExportQueueStore? _store;
    private readonly Func<ExportJobDefinition, IReadOnlyList<JobIssue>>? _revalidateRecovered;
    private readonly Func<string, OutputFileSnapshot> _inspectOutput;
    private readonly Action<int>? _persistMaximum;
    private long _nextOrder;
    private int _maximum;
    private bool _disposed;
    private bool _shuttingDown;

    public GlobalExportScheduler(int maximum, Func<ExportExecutorLease> executorFactory,
        IExportQueueStore? store = null,
        Func<ExportJobDefinition, IReadOnlyList<JobIssue>>? revalidateRecovered = null,
        Func<string, OutputFileSnapshot>? inspectOutput = null,
        Action<int>? persistMaximum = null)
    {
        _maximum = EncodingJobConcurrency.Validate(maximum);
        _executorFactory = executorFactory ?? throw new ArgumentNullException(nameof(executorFactory));
        _store = store;
        _revalidateRecovered = revalidateRecovered;
        _inspectOutput = inspectOutput ?? OutputFileSnapshot.Read;
        _persistMaximum = persistMaximum;
        Restore(store?.Load() ?? []);
    }

    public event Action<IReadOnlyList<ExportJobSnapshot>>? Changed;
    public event Action<ExportJobSnapshot>? Completed;
    public event Action<Guid>? SubmissionAccepted;

    public int MaxSimultaneousExports
    {
        get { lock (_sync) return _maximum; }
        set
        {
            lock (_sync) { ThrowIfDisposed(); _maximum = EncodingJobConcurrency.Validate(value); PersistLocked(); }
            _persistMaximum?.Invoke(value);
            PublishAndSchedule();
        }
    }

    public IReadOnlyList<ExportJobSnapshot> Jobs
    {
        get { lock (_sync) return _jobs.OrderBy(job => job.Definition.QueueOrder).Select(SnapshotLocked).ToList(); }
    }

    public ExportQueueAdmission Admit(ExportSubmissionProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        IReadOnlyList<ExportJobSnapshot> accepted;
        IReadOnlyList<ExportJobSnapshot> immediatelyTerminal;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (proposal.Jobs.Count == 0)
                return ExportQueueAdmission.Rejected(new JobIssue("export.queue-empty", "The Export submission contains no jobs.", JobIssueSeverity.Error));
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var proposed in proposal.Jobs)
            {
                if (proposed.PlanItem.OutputPaths.Count != 1)
                    return ExportQueueAdmission.Rejected(new JobIssue("export.queue-output", $"{proposed.PlanItem.Definition.SourceIdentity} does not have exactly one output.", JobIssueSeverity.Error));
                var disk = _inspectOutput(proposed.OutputPath);
                if (proposed.PlanItem.Disposition == JobPlanDisposition.Process
                    && disk.Exists && !proposed.Recipe.OverwriteExistingFiles)
                    return ExportQueueAdmission.Rejected(new JobIssue("export.queue-output-appeared",
                        $"An output file appeared after preflight: {proposed.OutputPath}", JobIssueSeverity.Error));
                if (proposed.PlanItem.Disposition == JobPlanDisposition.Skip && !disk.Exists)
                    return ExportQueueAdmission.Rejected(new JobIssue("export.queue-skip-changed",
                        $"The output selected for preservation changed after preflight: {proposed.OutputPath}", JobIssueSeverity.Error));
                foreach (var path in ReservationPaths(proposed.OutputPath))
                {
                    if (!paths.Add(path))
                        return ExportQueueAdmission.Rejected(new JobIssue("export.queue-collision", $"The submission reserves the same output more than once: {proposed.OutputPath}", JobIssueSeverity.Error));
                    if (_reservations.Contains(path))
                        return ExportQueueAdmission.Rejected(new JobIssue("export.queue-reserved", $"Another non-terminal Job already owns this output: {proposed.OutputPath}", JobIssueSeverity.Error));
                }
            }

            var created = new List<Entry>(proposal.Jobs.Count);
            foreach (var proposed in proposal.Jobs)
            {
                var definition = proposed with { SubmissionId = proposal.SubmissionId, QueueOrder = ++_nextOrder };
                var state = definition.PlanItem.Disposition == JobPlanDisposition.Skip ? JobState.Skipped : JobState.Queued;
                var entry = new Entry(definition, state);
                _jobs.Add(entry);
                created.Add(entry);
                if (!IsTerminal(state)) ReserveLocked(definition);
            }
            PersistLocked();
            accepted = created.Select(SnapshotLocked).ToList();
            immediatelyTerminal = created.Where(entry => IsTerminal(entry.State)).Select(SnapshotLocked).ToList();
        }
        SubmissionAccepted?.Invoke(proposal.SubmissionId);
        foreach (var terminal in immediatelyTerminal) Completed?.Invoke(terminal);
        PublishAndSchedule();
        return new(true, accepted, []);
    }

    public bool Pause(Guid jobId) => ChangeWaiting(jobId, JobState.Paused, JobState.Queued);
    public bool Resume(Guid jobId) => ChangeWaiting(jobId, JobState.Queued, JobState.Paused);
    public bool RetryNeedsAttention(Guid jobId)
    {
        lock (_sync)
        {
            var entry = _jobs.FirstOrDefault(job => job.Definition.JobId == jobId);
            if (entry?.State != JobState.NeedsAttention) return false;
            var issues = _revalidateRecovered?.Invoke(entry.Definition) ?? [];
            if (issues.Any(issue => issue.Severity == JobIssueSeverity.Error)) return false;
            entry.State = JobState.Queued;
            entry.ProgressPercent = null;
            entry.Errors.Clear();
            entry.Warnings.AddRange(issues.Where(issue => issue.Severity == JobIssueSeverity.Warning).Select(issue => issue.Message));
            PersistLocked();
        }
        PublishAndSchedule();
        return true;
    }

    public bool Cancel(Guid jobId)
    {
        ActiveExecution? active = null;
        ExportJobSnapshot? completed = null;
        lock (_sync)
        {
            var entry = _jobs.FirstOrDefault(job => job.Definition.JobId == jobId);
            if (entry is null || IsTerminal(entry.State)) return false;
            if (entry.State == JobState.Running)
            {
                active = _active.GetValueOrDefault(jobId);
                if (active is not null) active.UserCancellationRequested = true;
                active?.Cancellation.Cancel();
                active?.Lease.Terminate();
            }
            else
            {
                entry.State = JobState.Cancelled;
                entry.CompletedAt = DateTimeOffset.Now;
                ReleaseLocked(entry.Definition);
                PersistLocked();
                completed = SnapshotLocked(entry);
            }
        }
        try { if (completed is not null) Completed?.Invoke(completed); }
        finally { PublishAndSchedule(); }
        return true;
    }

    public bool MoveWaiting(Guid jobId, int delta)
    {
        lock (_sync)
        {
            var ordered = _jobs.Where(job => job.State == JobState.Queued).OrderBy(job => job.Definition.QueueOrder).ToList();
            var index = ordered.FindIndex(job => job.Definition.JobId == jobId);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= ordered.Count) return false;
            (ordered[index].Definition, ordered[target].Definition) =
                (ordered[index].Definition with { QueueOrder = ordered[target].Definition.QueueOrder },
                 ordered[target].Definition with { QueueOrder = ordered[index].Definition.QueueOrder });
            PersistLocked();
        }
        PublishAndSchedule();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        ActiveExecution[] active;
        lock (_sync)
        {
            if (_disposed) return;
            _shuttingDown = true;
            _disposed = true;
            active = _active.Values.ToArray();
            PersistLocked();
            foreach (var execution in active) execution.Cancellation.Cancel();
        }
        foreach (var execution in active) execution.Lease.Terminate();
        await Task.WhenAll(active.Select(execution => execution.Task).ToArray()).ConfigureAwait(false);
        foreach (var execution in active) execution.Cancellation.Dispose();
    }

    private bool ChangeWaiting(Guid jobId, JobState next, JobState required)
    {
        lock (_sync)
        {
            var entry = _jobs.FirstOrDefault(job => job.Definition.JobId == jobId);
            if (entry?.State != required) return false;
            entry.State = next;
            PersistLocked();
        }
        PublishAndSchedule();
        return true;
    }

    private void PublishAndSchedule()
    {
        Changed?.Invoke(Jobs);
        List<(Entry Entry, ActiveExecution Active)> starts = [];
        lock (_sync)
        {
            if (_disposed) return;
            while (_active.Count < _maximum)
            {
                var entry = _jobs.Where(job => job.State == JobState.Queued)
                    .OrderBy(job => job.Definition.QueueOrder).FirstOrDefault();
                if (entry is null) break;
                entry.State = JobState.Running;
                entry.StartedAt ??= DateTimeOffset.Now;
                entry.ActiveStartedAt = DateTimeOffset.Now;
                var cancellation = new CancellationTokenSource();
                ExportExecutorLease lease;
                try { lease = _executorFactory(); }
                catch (Exception exception)
                {
                    entry.State = JobState.NeedsAttention;
                    entry.Errors.Add($"The Export executor is unavailable: {exception.Message}");
                    continue;
                }
                var active = new ActiveExecution(lease, cancellation);
                _active.Add(entry.Definition.JobId, active);
                starts.Add((entry, active));
            }
            if (starts.Count > 0) PersistLocked();
        }
        foreach (var (entry, active) in starts)
            active.Task = Task.Run(() => ExecuteAsync(entry, active));
        if (starts.Count > 0) Changed?.Invoke(Jobs);
    }

    private async Task ExecuteAsync(Entry entry, ActiveExecution active)
    {
        JobItemResult<EncodingItemResult> result;
        try
        {
            var progress = new InlineProgress(value =>
            {
                lock (_sync)
                {
                    entry.ProgressPercent = Math.Clamp(value, 0, 100);
                    var bucket = (int)Math.Floor(entry.ProgressPercent.Value / ProgressCheckpointStep);
                    if (bucket != entry.LastProgressCheckpointBucket)
                    {
                        entry.LastProgressCheckpointBucket = bucket;
                        PersistLocked();
                    }
                }
                Changed?.Invoke(Jobs);
            });
            result = await active.Lease.Execute(entry.Definition.PlanItem, entry.Definition.Recipe,
                progress, active.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (active.Cancellation.IsCancellationRequested)
        { result = new(entry.Definition.JobId, JobState.Cancelled, entry.Definition.PlanItem.OutputPaths, [], [], null); }
        catch (Exception exception)
        { result = new(entry.Definition.JobId, JobState.Failed, entry.Definition.PlanItem.OutputPaths, [], [exception.Message], null); }

        ExportJobSnapshot snapshot;
        lock (_sync)
        {
            entry.Elapsed += DateTimeOffset.Now - (entry.ActiveStartedAt ?? DateTimeOffset.Now);
            entry.ActiveStartedAt = null;
            _active.Remove(entry.Definition.JobId);
            if (_shuttingDown && !active.UserCancellationRequested)
            {
                // Application shutdown is an interruption, not the user's Cancel command. Keep Running durable
                // so version-2 recovery reclassifies this Job to NeedsAttention and retains its reservation.
                PersistLocked();
                active.Cancellation.Dispose();
                return;
            }
            entry.State = result.State;
            entry.ProgressPercent = 100;
            entry.CompletedAt = DateTimeOffset.Now;
            entry.Warnings.AddRange(result.Warnings);
            entry.Errors.AddRange(result.Errors);
            entry.Result = result.Data;
            ReleaseLocked(entry.Definition);
            PersistLocked();
            snapshot = SnapshotLocked(entry);
        }
        active.Cancellation.Dispose();
        try { Completed?.Invoke(snapshot); }
        finally { PublishAndSchedule(); }
    }

    private void Restore(IReadOnlyList<ExportJobCheckpoint> checkpoints)
    {
        lock (_sync)
        {
            foreach (var checkpoint in checkpoints.OrderBy(job => job.Definition.QueueOrder))
            {
                if (IsTerminal(checkpoint.State)) continue;
                var state = checkpoint.State == JobState.Running ? JobState.NeedsAttention : checkpoint.State;
                var entry = new Entry(checkpoint.Definition, state)
                {
                    ProgressPercent = checkpoint.ProgressPercent, StartedAt = checkpoint.StartedAt,
                    CompletedAt = checkpoint.CompletedAt, Elapsed = checkpoint.Elapsed, Result = checkpoint.Result
                };
                entry.LastProgressCheckpointBucket = checkpoint.ProgressPercent is { } percent
                    ? (int)Math.Floor(percent / ProgressCheckpointStep) : -1;
                entry.Warnings.AddRange(checkpoint.Warnings);
                entry.Errors.AddRange(checkpoint.Errors);
                if (checkpoint.State == JobState.Running)
                    entry.Errors.Add("Lightflow closed while this Job was exporting. Review its output before retrying.");
                var recoveryIssues = _revalidateRecovered?.Invoke(entry.Definition) ?? [];
                entry.Warnings.AddRange(recoveryIssues.Where(issue => issue.Severity == JobIssueSeverity.Warning).Select(issue => issue.Message));
                entry.Errors.AddRange(recoveryIssues.Where(issue => issue.Severity == JobIssueSeverity.Error).Select(issue => issue.Message));
                if (recoveryIssues.Any(issue => issue.Severity == JobIssueSeverity.Error)) entry.State = JobState.NeedsAttention;
                if (ReservationPaths(entry.Definition.OutputPath).Any(_reservations.Contains))
                {
                    entry.State = JobState.NeedsAttention;
                    entry.Errors.Add("The recovered output reservation conflicts with another unfinished Job.");
                }
                else ReserveLocked(entry.Definition);
                _jobs.Add(entry);
                _nextOrder = Math.Max(_nextOrder, entry.Definition.QueueOrder);
            }
            PersistLocked();
        }
    }

    private ExportJobSnapshot SnapshotLocked(Entry entry)
    {
        var elapsed = entry.Elapsed + (entry.ActiveStartedAt is { } active ? DateTimeOffset.Now - active : TimeSpan.Zero);
        TimeSpan? eta = entry.State == JobState.Running && entry.ProgressPercent is > 0 and < 100
            ? TimeSpan.FromSeconds(elapsed.TotalSeconds * (100 - entry.ProgressPercent.Value) / entry.ProgressPercent.Value) : null;
        return new(entry.Definition, entry.State, entry.ProgressPercent, entry.StartedAt, entry.CompletedAt,
            elapsed, eta, entry.Warnings.ToList(), entry.Errors.ToList(), entry.Result);
    }

    private void PersistLocked() => _store?.Save(_jobs.Select(job => new ExportJobCheckpoint(job.Definition,
        job.State, job.ProgressPercent, job.StartedAt, job.CompletedAt,
        job.Elapsed + (job.ActiveStartedAt is { } active ? DateTimeOffset.Now - active : TimeSpan.Zero),
        job.Warnings.ToList(), job.Errors.ToList(), job.Result)).ToList());

    private void ReserveLocked(ExportJobDefinition definition)
    { foreach (var path in ReservationPaths(definition.OutputPath)) _reservations.Add(path); }
    private void ReleaseLocked(ExportJobDefinition definition)
    { foreach (var path in ReservationPaths(definition.OutputPath)) _reservations.Remove(path); }
    private static IEnumerable<string> ReservationPaths(string finalPath)
    {
        yield return Normalize(finalPath);
        yield return Normalize(EncodingOutputLifecycle.PartialPathFor(finalPath));
    }
    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
    private static bool IsTerminal(JobState state) => state is JobState.Completed or JobState.CompletedWithWarnings
        or JobState.Skipped or JobState.Cancelled or JobState.Failed;
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(GlobalExportScheduler)); }

    private sealed class Entry(ExportJobDefinition definition, JobState state)
    {
        public ExportJobDefinition Definition { get; set; } = definition;
        public JobState State { get; set; } = state;
        public double? ProgressPercent { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? ActiveStartedAt { get; set; }
        public TimeSpan Elapsed { get; set; }
        public List<string> Warnings { get; } = [];
        public List<string> Errors { get; } = [];
        public EncodingItemResult? Result { get; set; }
        public int LastProgressCheckpointBucket { get; set; } = -1;
    }

    private sealed class ActiveExecution(ExportExecutorLease lease, CancellationTokenSource cancellation)
    {
        public ExportExecutorLease Lease { get; } = lease;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task Task { get; set; } = Task.CompletedTask;
        public bool UserCancellationRequested { get; set; }
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
