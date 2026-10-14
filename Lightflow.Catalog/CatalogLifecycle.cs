using Lightflow.Application;
using Microsoft.Data.Sqlite;
namespace LightflowStudio;

/// <summary>The one session/readiness/mutation and shutdown owner, independent of host presentation.</summary>
public sealed partial class CatalogLifecycle : IAsyncDisposable
{
    private readonly ICatalogHostConsumerEffects _host;
    public CatalogLifecycle(ICatalogHostConsumerEffects host) => _host = host ?? throw new ArgumentNullException(nameof(host));
    public StartupSessionCompletion? ProfileCompletion { get; set; }
    public StartupStoreEvidence? CatalogEvidence { get; set; }
    private StartupSessionCompletion? _startupCompletion { get => ProfileCompletion; set => ProfileCompletion = value; }
    private StartupStoreEvidence? _catalogStartup { get => CatalogEvidence; set => CatalogEvidence = value; }
    public bool AdmissionClosed { get; set; }
    private bool _shutdownAdmissionClosed { get => AdmissionClosed; set => AdmissionClosed = value; }
    private bool _cleanShutdownAllowed = true;
    public void PreventCleanShutdown() => _cleanShutdownAllowed = false;
    public SemaphoreSlim TransitionGate { get; } = new(1, 1);
    private SemaphoreSlim _mutationGate => TransitionGate;
    public CatalogMutationLifecycle Mutations { get; } = new();
    public bool CatalogAvailable => Session is not null && !_workspaceReadinessLost;
    public string? RecoveryDiagnostic { get; private set; }
    internal void SetRecoveryDiagnostic(string? diagnostic) => RecoveryDiagnostic = diagnostic;
    public CatalogMutationLifecycle.Quiescence? ExitQuiescence { get; set; }
    private CatalogMutationLifecycle.Quiescence? _exitQuiescence { get => ExitQuiescence; set => ExitQuiescence = value; }
    public void CancelPreparedExit() { _exitQuiescence?.Dispose(); _exitQuiescence = null; }
    public void CompletePreparedExit()
    {
        if (_exitQuiescence is not null) { _exitQuiescence.CompleteShutdown(); _shutdownAdmissionClosed = true; }
        _exitQuiescence = null;
    }
    public async Task StopConsumersAsync()
    {
        await _host.StopMonitoringAsync().ConfigureAwait(false);
        await _host.StopDerivedWorkAsync().ConfigureAwait(false);
    }
    public Task RestartConsumersAsync() => _host.RestartAsync();
    public static void RequireCompletePeers(StartupStoreEvidence? catalog, StartupStoreEvidence? preview)
    {
        var cleanPair = catalog?.KnownClean == true && preview?.KnownClean == true;
        catalog?.RequireCompletePeer(cleanPair);
        preview?.RequireCompletePeer(cleanPair);
    }
    private CatalogDatabaseSession? _session;
    public CatalogDatabaseSession? Session
    {
        get => _session;
        set
        {
            if (value is not null)
            {
                RequireCatalogOwnership(value.ResolvedDatabasePath);
                value.Mutations = Mutations;
                value.RequireOwnership = RequireCatalogOwnership;
                value.ReadinessFailed = LoseWorkspaceReadiness;
            }
            _session = value;
        }
    }
    public void RequireCatalogOwnership(string database)
    {
        if (_workspaceReadinessLost) throw new IOException("Catalog readiness is unavailable. Authoring is blocked.");
        try
        {
            if (_catalogStartup is null) throw new IOException("Catalog ownership is unavailable. Authoring is blocked.");
            _catalogStartup.RequireOwnership(database);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        { LoseWorkspaceReadiness(error.Message); throw; }
    }

    private volatile bool _workspaceReadinessLost;
    public event Action? WorkspaceReadinessLost;
    public bool CatalogTransitionInProgress => _mutationGate.CurrentCount == 0;
    public bool VerifyWorkspaceReadiness()
    {
        if (!CatalogAvailable) return false;
        try
        {
            RequireCatalogOwnership(Session!.ResolvedDatabasePath);
            if (!File.Exists(Session.ResolvedDatabasePath))
                throw new IOException("The active Catalog is unavailable. Its configured location has been retained.");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        { LoseWorkspaceReadiness(error.Message); return false; }
    }
    public void LoseWorkspaceReadiness(string diagnostic)
    {
        if (_workspaceReadinessLost) return;
        _workspaceReadinessLost = true;
        PreventCleanShutdown(); RecoveryDiagnostic = diagnostic;
        Mutations.Dispose();
        _shutdownAdmissionClosed = true;
        WorkspaceReadinessLost?.Invoke();
    }

    public ValueTask DisposeAsync() => DisposeAsync(null);

    public async ValueTask DisposeAsync(Action<string>? diagnostic)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        void Report(string stage) => diagnostic?.Invoke($"[Storage shutdown pid={Environment.ProcessId} elapsed={timer.Elapsed.TotalMilliseconds:F1}ms] {stage}");
        try
        {
            Report("Canceling read-only usage measurement");
            _host.CancelReadUsage();
            Report("Disposing LUT cache");
            _host.DisposeCaches();
            Report("Stopping media monitoring");
            await _host.StopMonitoringAsync().ConfigureAwait(false);
            Report("Stopping derived work");
            await _host.StopDerivedWorkAsync().ConfigureAwait(false);
            Report("Draining Catalog mutations");
            if (!_shutdownAdmissionClosed)
            {
                if (_exitQuiescence is not null) CompletePreparedExit();
                else
                {
                    var quiet = await Mutations.QuiesceAsync().ConfigureAwait(false);
                    quiet.CompleteShutdown(); _shutdownAdmissionClosed = true;
                }
            }
            Report("Waiting for storage operations");
            await _mutationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                Report("Waiting for Preview operations");
                using var previewLease = await _host.EnterCompletionPeerMaintenanceAsync().ConfigureAwait(false);
                var healthy = _cleanShutdownAllowed && Session is not null && _host.HasQualifiedCompletionPeer;
                var resolvedCatalogPath = Session?.ResolvedDatabasePath;
                Report("Closing Catalog");
                if (Session is not null) await Session.DisposeAsync().ConfigureAwait(false);
                Session = null;
                Report("Closing Previews");
                await _host.CloseCompletionPeerAsync().ConfigureAwait(false);
                if (healthy)
                {
                    // Failure to attest is conservative: shutdown may finish, next launch validates.
                    try
                    {
                        _catalogStartup?.Complete(resolvedCatalogPath!);
                        _host.CompleteCompletionPeer();
                        if (_catalogStartup?.Completed == true && _host.CompletionPeerCompleted)
                        {
                            _startupCompletion?.Complete();
                            Report("Durable clean storage completion recorded");
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
                    { Report($"Clean completion unavailable; next startup validates: {error.GetType().Name}: {error.Message}"); }
                }
                Report("Completed");
            }
            finally
            {
                _mutationGate.Release();
                _mutationGate.Dispose();
                _host.DisposeReadUsage();
                Mutations.Dispose();
            }
        }
        finally
        {
            _catalogStartup?.Dispose(); _catalogStartup = null;
            _host.ReleaseCompletionPeerEvidence();
            _startupCompletion?.Dispose(); _startupCompletion = null;
        }
    }

}
