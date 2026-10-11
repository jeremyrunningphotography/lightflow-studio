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
    internal bool CompletedSuccessfully { get; private set; }
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
                StatusText.Text = await _operation(_cancellation.Token, progress);
                CompletedSuccessfully = true;
            }
            catch (OperationCanceledException) { StatusText.Text = "Cancelled safely. Previous Catalog configuration was retained."; }
            catch (Exception exception) { StatusText.Text = exception.Message; }
            finally
            {
                _running = false; Progress.Visibility = CancelButton.Visibility = Visibility.Collapsed;
                CloseButton.Visibility = Visibility.Visible; _cancellation.Dispose();
            }
        };
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
