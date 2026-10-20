using LightflowStudio;
using System.ComponentModel;
using Lightflow.Application;

namespace Lightflow.UI;

/// <summary>Transient presentation only. Readiness and durable state come from the application service.</summary>
public sealed class ShellViewModel(IShellCatalogSession catalog) : INotifyPropertyChanged
{
    public BrowserViewModel? Browser { get; } = catalog is IBrowserFolderSession browser ? new(browser) : null;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string DataRoot => catalog.DataRoot;
    public ShellCatalogSnapshot Snapshot { get; private set; } = new(StorageStartupStatus.FirstRunChoiceRequired);
    private long _generation;
    public bool IsBusy { get; private set; }
    public bool IsClosing { get; private set; }
    public bool WorkspaceAvailable => Snapshot.IsReady && !IsBusy && !IsClosing;
    public bool CanChoose => !IsBusy && !IsClosing && Snapshot.Reason != CatalogRecoveryReason.PendingRecovery && Snapshot.Status != StorageStartupStatus.InvalidConfiguration;
    public string Title => IsClosing ? "Closing Catalog…" : IsBusy ? "Opening Catalog…" : Snapshot.IsReady ? "Catalog ready" : Snapshot.Status == StorageStartupStatus.FirstRunChoiceRequired ? "Welcome to Lightflow Studio" : UnavailableTitle;
    private string UnavailableTitle => Snapshot.Reason == CatalogRecoveryReason.PendingRecovery ? "Catalog needs attention" : Snapshot.Status switch
    {
        StorageStartupStatus.CatalogMissing => "Catalog unavailable",
        StorageStartupStatus.InvalidConfiguration => "Catalog settings unavailable",
        _ => Snapshot.Reason switch
        {
            CatalogRecoveryReason.CompetingOwner => "Catalog already in use",
            CatalogRecoveryReason.PendingRecovery => "Catalog needs attention",
            CatalogRecoveryReason.UnsupportedStorage => "Catalog location not supported",
            CatalogRecoveryReason.LocationUnavailable => "Catalog unavailable",
            _ => "Couldn't open Catalog"
        }
    };
    public string Message => IsClosing ? "Finishing Catalog work before quitting." : IsBusy ? "Checking your Catalog before opening the workspace." : Snapshot.IsReady ? "Your Catalog is open and ready." : Snapshot.Status == StorageStartupStatus.FirstRunChoiceRequired ? "Create a new Catalog or open an existing one to get started." : UnavailableMessage;
    private string UnavailableMessage => Snapshot.Reason == CatalogRecoveryReason.PendingRecovery ? "A previous Catalog operation needs attention. Your Catalog files have been preserved. Quit and retain this data folder for recovery." : Snapshot.Status switch
    {
        StorageStartupStatus.CatalogMissing => "Lightflow Studio couldn't find your last Catalog. Reconnect its drive, then retry, or choose another Catalog.",
        StorageStartupStatus.CatalogCorrupt => "This Catalog couldn't be validated. Your files have been preserved. Quit and retain the Catalog for recovery.",
        StorageStartupStatus.CatalogIdentityMismatch => "This Catalog doesn't match the saved Catalog identity. Choose the intended Catalog; existing files have been preserved.",
        StorageStartupStatus.InvalidConfiguration => "The Catalog settings couldn't be read. Quit and retain this data folder for recovery, or retry after the settings have been recovered.",
        _ => Snapshot.Reason switch
        {
            CatalogRecoveryReason.CompetingOwner => "Close this Catalog in the other Lightflow Studio session, then retry.",
            CatalogRecoveryReason.PendingRecovery => "A previous Catalog operation needs attention. Your Catalog files have been preserved. Quit and retain this data folder for recovery.",
            CatalogRecoveryReason.UnsupportedStorage => "Choose a Catalog on supported local APFS storage.",
            CatalogRecoveryReason.LocationUnavailable => "Lightflow Studio couldn't find your last Catalog. Reconnect its drive, then retry, or choose another Catalog.",
            CatalogRecoveryReason.OutsideWorkspace => "Choose a Catalog inside the isolated data folder shown below.",
            CatalogRecoveryReason.CatalogDirectoryRequired => "Choose the Catalog folder itself, rather than its parent folder.",
            _ => "Lightflow Studio couldn't open this Catalog. Retry or choose a supported Catalog. Existing files have been preserved."
        }
    };
    public Task StartAsync() => RunAsync(catalog.StartAsync);
    public Task CreateAsync(string directory) => RunAsync(() => catalog.CreateAsync(directory));
    public Task OpenAsync(string directory) => RunAsync(() => catalog.OpenAsync(directory));
    public async Task VerifyAsync()
    {
        if (IsBusy || IsClosing || !Snapshot.IsReady) return;
        var generation = _generation;
        var observed = await catalog.VerifyAsync();
        if (generation == _generation && !IsClosing) { Snapshot = observed; if (!observed.IsReady) Browser?.Suspend(); Notify(); }
    }
    private async Task RunAsync(Func<Task<ShellCatalogSnapshot>> request)
    {
        if (IsBusy || IsClosing) return;
        _generation++; Browser?.Suspend(); IsBusy = true; Notify();
        try { Snapshot = await request(); if (Snapshot.IsReady && Snapshot.CatalogId is { } id && Browser is not null) await Browser.OpenAsync(id); }
        finally { IsBusy = false; Notify(); }
    }
    public async Task CloseAsync()
    {
        if (IsClosing) { await catalog.DisposeAsync(); return; }
        _generation++; Browser?.Suspend(); IsClosing = true; Notify();
        await catalog.DisposeAsync();
    }
    private void Notify() => PropertyChanged?.Invoke(this, new(null));
}
