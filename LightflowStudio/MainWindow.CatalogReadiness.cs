using System.Windows.Threading;

namespace LightflowStudio;

public partial class MainWindow
{
    private DispatcherTimer? _catalogReadinessTimer;
    private bool _catalogRecoveryRestarting;
    internal bool EnsureCatalogWorkspace()
    {
        if (_catalogRecoveryRestarting || _workspaceClosed) return false;
        if (_storage.VerifyWorkspaceReadiness()) return true;
        QueueCatalogRecovery(); return false;
    }
    private void StartCatalogReadinessObservation()
    {
        _storage.WorkspaceReadinessLost += QueueCatalogRecovery;
        _catalogReadinessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _catalogReadinessTimer.Tick += (_, _) =>
        {
            if (!_storage.CatalogTransitionInProgress) EnsureCatalogWorkspace();
        };
        _catalogReadinessTimer.Start();
        Closed += (_, _) => { _catalogReadinessTimer.Stop(); _storage.WorkspaceReadinessLost -= QueueCatalogRecovery; };
    }
    private void QueueCatalogRecovery() => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_workspaceClosed || _catalogRecoveryRestarting) return;
        _catalogRecoveryRestarting = true;
        IsEnabled = false; Hide();
        if (System.Windows.Application.Current is App app) app.PrepareCatalogRestart();
        Close();
    }));
}
