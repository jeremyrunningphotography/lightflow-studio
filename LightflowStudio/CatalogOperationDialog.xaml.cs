using System.ComponentModel;
using System.Windows;
using Lightflow.Application;

namespace LightflowStudio;

public partial class CatalogOperationDialog : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Func<CancellationToken, IProgress<ClosedCatalogTransferState>, Task<string>> _operation;
    private bool _running = true;
    private bool _cancellable = true;
    private string? _moveDirectory;
    private Func<bool>? _verifyMoveOwnership;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Completion => _completion.Task;
    internal bool CompletedSuccessfully { get; private set; }
    internal bool CloseOnSuccess { get; init; }
    internal CatalogOperationDialog(string title, string detail,
        Func<CancellationToken, IProgress<ClosedCatalogTransferState>, Task<string>> operation)
    {
        InitializeComponent(); Title = Heading.Text = title; Detail.Text = detail;
        _operation = operation;
        SourceInitialized += (_, _) => WindowAppearance.EnableDarkTitleBar(this);
        Loaded += async (_, _) =>
        {
            try
            {
                StatusText.Text = "Checking Catalog and storage…";
                var progress = new Progress<ClosedCatalogTransferState>(state =>
                {
                    if (!_running) return;
                    _cancellable = state < ClosedCatalogTransferState.Published;
                    CancelButton.IsEnabled = _cancellable && !_cancellation.IsCancellationRequested;
                    StatusText.Text = state switch
                    {
                        ClosedCatalogTransferState.Preflight => "Checking source, destination and storage…",
                        ClosedCatalogTransferState.Quiescing => "Finishing Catalog operations safely…",
                        ClosedCatalogTransferState.SnapshotFinalized => "Creating the protected local snapshot…",
                        ClosedCatalogTransferState.Staging => "Copying the closed Catalog…",
                        ClosedCatalogTransferState.Verified => "Verifying the copied Catalog…",
                        ClosedCatalogTransferState.Published => "Publishing the verified Catalog. Cancellation is no longer available…",
                        ClosedCatalogTransferState.Activating => "Acquiring ownership and activating the destination…",
                        ClosedCatalogTransferState.ActivatedPendingConfiguration => "Checking destination activation…",
                        ClosedCatalogTransferState.ConfigurationCommitted => "Saving and verifying Catalog configuration…",
                        _ => "Finishing the Catalog operation safely…"
                    };
                });
                var diagnostic = await _operation(_cancellation.Token, progress);
                if (_moveDirectory is not null)
                {
                    if (_verifyMoveOwnership?.Invoke() != true)
                        throw new System.IO.IOException("The moved Catalog could not be confirmed ready. Close this dialog to review recovery.");
                    Title = "Catalog moved successfully";
                    Heading.Visibility = Detail.Visibility = StatusText.Visibility = Visibility.Collapsed;
                    Heading.Text = Detail.Text = StatusText.Text = "";
                    SuccessLocation.Text = _moveDirectory;
                    MoveSuccessPanel.Visibility = Visibility.Visible;
                    CloseButton.Content = "Continue";
                    CloseButton.Style = (Style)FindResource("PrimaryButton");
                }
                else StatusText.Text = diagnostic;
                CompletedSuccessfully = true;
            }
            catch (OperationCanceledException) { StatusText.Text = "Cancelled safely. Previous Catalog configuration was retained."; }
            catch (Exception exception)
            {
                if (_moveDirectory is not null || exception.Message.Contains("--data-root", StringComparison.Ordinal))
                {
                    var collision = exception is MoveFailureException { Status: StorageChangeStatus.ConflictingCatalog };
                    StatusText.Text = collision ? "The resulting Catalog folder already exists. Choose another name or parent folder."
                        : "Lightflow Studio couldn't complete this Catalog action. Review the troubleshooting details before trying again.";
                    FailureDiagnostic.Text = exception.Message;
                    FailureDetails.Visibility = collision ? Visibility.Collapsed : Visibility.Visible;
                }
                else StatusText.Text = exception.Message;
                StatusText.Foreground = (System.Windows.Media.Brush)FindResource("RedBrush");
                if (_moveDirectory is not null) Heading.Text = "Catalog move needs attention";
            }
            finally
            {
                _running = false; Progress.Visibility = CancelButton.Visibility = Visibility.Collapsed;
                CloseButton.Visibility = Visibility.Visible; _cancellation.Dispose();
                _completion.TrySetResult();
                if (CompletedSuccessfully && CloseOnSuccess)
                    _ = Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) Close(); }));
            }
        };
    }
    internal static CatalogOperationDialog ForMove(LightflowStorageCoordinator storage, string destination) =>
        new("Move Catalog", destination, async (token, progress) =>
        {
            var result = await storage.RelocateCatalogAsync(destination, token, progress);
            if (!result.Succeeded) throw new MoveFailureException(result.Status, result.Diagnostic);
            return result.Diagnostic ?? "";
        })
        {
            _moveDirectory = destination,
            _verifyMoveOwnership = () => storage.VerifyWorkspaceReadiness() &&
                string.Equals(storage.Locations.CatalogDirectory, destination, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(storage.Settings.CatalogDirectory, destination, StringComparison.OrdinalIgnoreCase)
        };
    private sealed class MoveFailureException(StorageChangeStatus status, string? diagnostic) : System.IO.IOException(diagnostic)
    {
        internal StorageChangeStatus Status { get; } = status;
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_running) { e.Cancel = true; if (_cancellable) Cancel_Click(this, new RoutedEventArgs()); }
        base.OnClosing(e);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_running || !_cancellable) return;
        _cancellation.Cancel(); CancelButton.IsEnabled = false;
        StatusText.Text = "Cancelling safely; waiting for the admitted operation to finish…";
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
