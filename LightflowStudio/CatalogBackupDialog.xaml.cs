using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace LightflowStudio;

public partial class CatalogBackupDialog : Window
{
    private readonly LightflowStorageCoordinator _storage;
    private readonly Action<string> _log;
    private readonly bool _exit;
    private CancellationTokenSource? _cancellation;
    internal bool ExitApproved { get; private set; }

    internal CatalogBackupDialog(LightflowStorageCoordinator storage, Action<string> log, bool exit = true, string? destination = null)
    {
        InitializeComponent();
        _storage = storage;
        _log = log;
        _exit = exit;
        if (!exit)
        {
            Explanation.Text = "Save a validated copy of your Lightflow Catalog. Original media and rebuildable Previews are not included.";
            BackupButton.Content = "Back Up Catalog";
            StayButton.Content = "Cancel";
            SkipButton.Visibility = Visibility.Collapsed;
        }
        Destination.Text = destination ?? storage.BackupDirectory;
        StatusText.Text = storage.CatalogAvailable ? "" : "The Catalog is unavailable. Restore it in Settings, or explicitly skip this backup.";
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_cancellation is not null) { e.Cancel = true; RequestCancellation(); }
        base.OnClosing(e);
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose Catalog backup location" };
        if (Directory.Exists(Destination.Text)) picker.InitialDirectory = Destination.Text;
        if (picker.ShowDialog(this) != true) return;
        _cancellation = new CancellationTokenSource();
        SetRunning(true);
        StatusText.Text = "Saving the backup location…";
        try
        {
            var token = _cancellation.Token;
            var destination = await Task.Run(() => CatalogBackupDestination.Validate(_storage.Locations, picker.FolderName), token);
            await _storage.SaveBackupDestinationAsync(destination, token);
            Destination.Text = destination;
            StatusText.Text = "Backup location saved.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Location change cancelled."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { _log($"Backup location could not be saved: {ex}"); StatusText.Text = "That backup location could not be saved. Choose another folder."; }
        finally { _cancellation.Dispose(); _cancellation = null; SetRunning(false); }
    }

    private void Stay_Click(object sender, RoutedEventArgs e) => Close();
    private void Skip_Click(object sender, RoutedEventArgs e) { ExitApproved = true; Close(); }
    private void CancelBackup_Click(object sender, RoutedEventArgs e) => RequestCancellation();
    private void RequestCancellation()
    {
        _cancellation?.Cancel();
        CancelBackupButton.IsEnabled = false;
        StatusText.Text = "Cancelling safely. Waiting for the current SQLite operation to finish…";
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null) return;
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        SetRunning(true);
        try
        {
            var requested = Destination.Text;
            StatusText.Text = "Checking the backup location…";
            var destination = await Task.Run(() => CatalogBackupDestination.Validate(_storage.Locations, requested), token);
            token.ThrowIfCancellationRequested();
            await _storage.SaveBackupDestinationAsync(destination, token);
            var progress = new Progress<string>(message => { if (!token.IsCancellationRequested) StatusText.Text = message; });
            var result = await _storage.BackupForExitAsync(destination, _exit, progress, token);
            if (result.Diagnostic is not null) _log(result.Diagnostic);
            if (result.Succeeded)
            {
                _log($"Catalog backup validated: {result.Backup!.Path}");
                ExitApproved = true;
            }
            else
            {
                StatusText.Text = "The Catalog backup could not be completed. Check the destination, choose another folder, or exit without a backup. Details are in the activity log.";
                SkipButton.Content = "Exit Without Backup";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Backup cancelled. You can retry, change the location, skip this backup, or keep using Lightflow.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _log($"Catalog exit backup failed: {ex}");
            StatusText.Text = "The backup location or Catalog is unavailable. Choose another location or exit without a backup. Details are in the activity log.";
            SkipButton.Content = "Exit Without Backup";
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            SetRunning(false);
        }
        if (ExitApproved) Close();
    }

    private void SetRunning(bool running)
    {
        Destination.IsEnabled = BrowseButton.IsEnabled = !running;
        StayButton.Visibility = BackupButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        SkipButton.Visibility = running || !_exit ? Visibility.Collapsed : Visibility.Visible;
        Progress.Visibility = CancelBackupButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        CancelBackupButton.IsEnabled = true;
    }
}
