using Lightflow.Application;
using LightflowStudio;

namespace Lightflow.Platform.MacOS;

/// <summary>Thin shell composition adapter; CatalogLifecycle retains admission, configuration and resource ownership.</summary>
public sealed class MacShellCatalogSession(string dataRoot) : IShellCatalogSession
{
    public string DataRoot { get; } = Path.GetFullPath(dataRoot);
    private readonly SemaphoreSlim _requests = new(1, 1);
    private CatalogLifecycle? _owner;
    private ShellCatalogSnapshot _snapshot = new(StorageStartupStatus.FirstRunChoiceRequired);
    private bool _closed;
    private Task? _closeTask;
    private readonly object _closeLock = new();

    public Task<ShellCatalogSnapshot> StartAsync() => RequestAsync(async () =>
    {
        await RetireAsync().ConfigureAwait(false);
        return Accept(await NewLifecycle().StartAsync(allowFirstCreation: false).ConfigureAwait(false));
    });

    public Task<ShellCatalogSnapshot> CreateAsync(string preparedDirectory) => RequestAsync(async () =>
    {
        if (_snapshot.Reason == CatalogRecoveryReason.PendingRecovery) return _snapshot;
        // Native #425 deliberately refuses missing-to-created folder binding transitions.
        // Preparation is the user's explicit operation, not a new publication protocol in the shell.
        if (!Directory.Exists(preparedDirectory))
        {
            await RetireAsync().ConfigureAwait(false);
            return Failure(CatalogRecoveryReason.LocationRefused, "Select a prepared empty folder inside the isolated data folder.");
        }
        await RetireAsync().ConfigureAwait(false);
        var lifecycle = NewLifecycle();
        return Accept(await lifecycle.CreateCatalogAsync(preparedDirectory).ConfigureAwait(false));
    });

    public Task<ShellCatalogSnapshot> OpenAsync(string directory) => RequestAsync(async () =>
    {
        if (_snapshot.Reason == CatalogRecoveryReason.PendingRecovery) return _snapshot;
        // Preflight uses the shared owner and exact Catalog identity; never reads SQLite from presentation.
        if (_owner is null)
        {
            var initial = await NewLifecycle().StartAsync(allowFirstCreation: false).ConfigureAwait(false);
            Accept(initial);
        }
        if (_owner is null || _snapshot.Reason == CatalogRecoveryReason.PendingRecovery) return _snapshot;
        var selection = await _owner.InspectCatalogSelectionAsync(directory).ConfigureAwait(false);
        if (!selection.Succeeded)
        {
            // Failed selection must not leave an active but hidden writer. Retire through its owner.
            await RetireAsync().ConfigureAwait(false);
            return Failure(selection.RecoveryReason, selection.Diagnostic);
        }
        await RetireAsync().ConfigureAwait(false);
        return Accept(await NewLifecycle().StartAsync(allowFirstCreation: false,
            existingCatalogDirectory: selection.Directory, selectedCatalogId: selection.CatalogId,
            resumePreviousCatalogOnFailure: false).ConfigureAwait(false));
    });

    public Task<ShellCatalogSnapshot> VerifyAsync() => RequestAsync(() =>
    {
        if (_snapshot.IsReady && _owner?.VerifyWorkspaceReadiness() != true)
            _snapshot = new(StorageStartupStatus.CatalogUnavailable, CatalogRecoveryReason.LocationUnavailable,
                _owner?.RecoveryDiagnostic, _snapshot.Directory);
        return Task.FromResult(_snapshot);
    });

    private CatalogLifecycle NewLifecycle() => new(new MacCatalogLifecycleEffects(DataRoot));
    private ShellCatalogSnapshot Accept(CatalogStartupResult result)
    {
        _owner = result.Coordinator;
        return _snapshot = new(result.Status, result.RecoveryReason, result.Diagnostic,
            _owner?.Locations.CatalogDirectory,
            result.IsReady ? _owner!.CatalogSession.Identity.CatalogId : null,
            result.IsReady ? _owner!.CatalogSession.SchemaVersion : null);
    }
    private ShellCatalogSnapshot Failure(CatalogRecoveryReason reason, string? diagnostic) =>
        _snapshot = new(StorageStartupStatus.CatalogUnavailable, reason, diagnostic, _snapshot.Directory);

    private async Task<ShellCatalogSnapshot> RequestAsync(Func<Task<ShellCatalogSnapshot>> request)
    {
        await _requests.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            try { return await request().ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            {
                await RetireAsync().ConfigureAwait(false);
                return Failure(CatalogRecoveryReason.OperationFailed, error.Message);
            }
        }
        finally { _requests.Release(); }
    }
    private async Task RetireAsync()
    {
        var owner = _owner;
        _owner = null;
        if (owner is not null) await owner.DisposeAsync(Console.Error.WriteLine).ConfigureAwait(false);
    }
    public ValueTask DisposeAsync()
    {
        lock (_closeLock) return new(_closeTask ??= CloseAsync());
    }
    private async Task CloseAsync()
    {
        await _requests.WaitAsync().ConfigureAwait(false);
        try { _closed = true; await RetireAsync().ConfigureAwait(false); }
        finally { _requests.Release(); }
    }
}
