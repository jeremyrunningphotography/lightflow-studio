using System.IO;

namespace LightflowStudio;

// UI flow only. Every native/SQLite/lease/configuration effect belongs to the existing coordinator.
internal sealed class CatalogStartupGate : IAsyncDisposable
{
    private readonly LightflowStorageLocations _profile;
    private readonly InitializedDataProfile? _initialized;
    private bool _closed;
    private bool _busy;
    private readonly bool _retryInitialCreation;
    internal StorageStartupResult Current { get; private set; }
    internal string LastCatalogDirectory { get; }
    internal string DisplayCatalogDirectory { get; private set; }
    internal bool CanEnterWorkspace => !_closed && !_busy && Current.IsReady && Current.Coordinator?.VerifyWorkspaceReadiness() == true;
    internal bool CanRetry { get; }
    internal string ProposedNewDirectory { get; }

    internal CatalogStartupGate(StorageStartupResult current, LightflowStorageLocations profile, InitializedDataProfile? initialized = null)
    {
        Current = current; _profile = profile; _initialized = initialized;
        var configuration = new AppSettingsStorageConfigurationStore(profile.SettingsPath, profile.IsIsolated);
        if (configuration.TryLoad(out var settings, out _))
        {
            LastCatalogDirectory = settings.CatalogDirectory ?? profile.CatalogDirectory;
            CanRetry = current.Status != StorageStartupStatus.FirstRunChoiceRequired;
            _retryInitialCreation = settings.CatalogId is null && settings.CatalogDirectory is null &&
                !File.Exists(profile.CatalogDatabasePath) && current.RecoveryReason is CatalogRecoveryReason.InitialLocationUnavailable or CatalogRecoveryReason.LocationRefused or CatalogRecoveryReason.UnsupportedStorage;
            try { ProposedNewDirectory = LightflowStorageCoordinator.ProposeNewCatalogDirectory(profile, settings); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { ProposedNewDirectory = ""; }
        }
        else { LastCatalogDirectory = profile.CatalogDirectory; ProposedNewDirectory = ""; }
        DisplayCatalogDirectory = current.Status == StorageStartupStatus.FirstRunChoiceRequired ? ProposedNewDirectory : LastCatalogDirectory;
    }

    internal Task RetryAsync(CancellationToken token = default)
    {
        DisplayCatalogDirectory = LastCatalogDirectory;
        return AttemptAsync(() => LightflowStorageCoordinator.StartAsync(profile: _profile, initializedProfile: _initialized, allowFirstCreation: _retryInitialCreation, cancellationToken: token));
    }

    internal Task CreateAsync(string directory, CancellationToken token = default)
    {
        if (Current.RecoveryReason == CatalogRecoveryReason.PendingRecovery) return Task.CompletedTask;
        DisplayCatalogDirectory = directory;
        return AttemptAsync(() => LightflowStorageCoordinator.CreateCatalogAsync(directory, _profile, _initialized, token: token));
    }

    internal async Task OpenAsync(string directory, CancellationToken token = default)
    {
        if (_closed || _busy) return;
        if (Current.RecoveryReason == CatalogRecoveryReason.PendingRecovery) return;
        DisplayCatalogDirectory = directory;
        var pending = ClosedCatalogTransferJournal.InspectPending(_profile);
        if (pending is not null) { SetFailure(StorageStartupStatus.CatalogUnavailable, pending, CatalogRecoveryReason.PendingRecovery); return; }
        // Obtain a nonauthoring preflight coordinator if the previous failure had none.
        if (Current.Coordinator is null)
        {
            var preflight = await LightflowStorageCoordinator.StartAsync(profile: _profile, initializedProfile: _initialized, allowFirstCreation: false, cancellationToken: token);
            if (_closed) { if (preflight.Coordinator is not null) await preflight.Coordinator.DisposeAsync(); return; }
            // Reconnected prior storage supplies preflight services, not implicit selection success.
            Current = preflight.IsReady ? preflight with { Status = StorageStartupStatus.CatalogUnavailable } : preflight;
        }
        if (Current.Coordinator is null) return;
        var selection = await Current.Coordinator.InspectCatalogSelectionAsync(directory, token);
        if (_closed) return;
        if (!selection.Succeeded)
        { SetFailure(StorageStartupStatus.CatalogUnreadable, selection.Diagnostic, selection.RecoveryReason); return; }
        await AttemptAsync(() => LightflowStorageCoordinator.StartAsync(profile: _profile, initializedProfile: _initialized,
            allowFirstCreation: false, existingCatalogDirectory: selection.Directory, selectedCatalogId: selection.CatalogId,
            resumePreviousCatalogOnFailure: false, cancellationToken: token));
    }

    private void SetFailure(StorageStartupStatus status, string? diagnostic, CatalogRecoveryReason reason) =>
        Current = new(status, Current.Coordinator, diagnostic, reason);
    internal void ReportFailure(string diagnostic) => SetFailure(StorageStartupStatus.CatalogUnreadable, diagnostic, CatalogRecoveryReason.OwnershipUnavailable);

    private async Task AttemptAsync(Func<Task<StorageStartupResult>> attempt)
    {
        if (_closed || _busy) return;
        _busy = true;
        try
        {
            var previous = Current.Coordinator;
            Current = Current with { Coordinator = null };
            if (previous is not null) await previous.DisposeAsync();
            var result = await attempt();
            if (_closed) { if (result.Coordinator is not null) await result.Coordinator.DisposeAsync(); return; }
            Current = result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException or Microsoft.Data.Sqlite.SqliteException)
        { Current = new(StorageStartupStatus.CatalogUnreadable, Diagnostic: error.Message, RecoveryReason: CatalogRecoveryReason.OwnershipUnavailable); }
        finally { _busy = false; }
    }

    internal StorageStartupResult TakeReady()
    {
        if (!CanEnterWorkspace) throw new InvalidOperationException("A validated Catalog is required before entering the workspace.");
        var result = Current; Current = Current with { Coordinator = null }; _closed = true; return result;
    }
    internal void CloseAdmission() => _closed = true;
    public async ValueTask DisposeAsync()
    {
        _closed = true;
        if (Current.Coordinator is { } coordinator) { Current = Current with { Coordinator = null }; await coordinator.DisposeAsync(); }
    }
}
