using System.Windows.Threading;

namespace LightflowStudio;

public partial class MainWindow
{
    private readonly TaskCompletionSource _presentationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task PresentationReady => _presentationReady.Task;

    internal async Task RestoreStartupPresentationAsync()
    {
        try
        {
            await RestoreWorkspaceContinuationAsync();
            // Process restored layout/scroll and Player presentation before App reveals the HWND.
            await Dispatcher.InvokeAsync(() =>
            {
                UpdateLayout();
                ApplyPendingWorkspaceGrid();
            }, DispatcherPriority.Loaded);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            if (!_workspaceClosed) _presentationReady.TrySetResult();
        }
        catch (Exception exception) { _presentationReady.TrySetException(exception); }
    }
}
