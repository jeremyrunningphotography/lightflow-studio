using System.Windows;
using System.Windows.Threading;

namespace LightflowStudio;

public partial class App : System.Windows.Application
{
    internal static ActivityLogFile ActivityLog { get; private set; } = null!;
    internal LightflowStorageCoordinator? Storage { get; private set; }
    internal static MediaPlaybackCoordinator Playback { get; } = new(() =>
        new MediaPlaybackService(new FlyleafPlaybackBackend()));

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains(CatalogPackageRuntimeVerifier.CommandLineSwitch, StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            var verified = CatalogPackageRuntimeVerifier.VerifyAsync().GetAwaiter().GetResult();
            Shutdown(verified ? 0 : 1);
            return;
        }

        base.OnStartup(e);
        var storage = LightflowStorageCoordinator.StartAsync().GetAwaiter().GetResult();
        if (storage.Coordinator is null)
        {
            System.Windows.MessageBox.Show(storage.Diagnostic ?? "Lightflow storage configuration could not be loaded.",
                "Storage configuration", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        Storage = storage.Coordinator;
        ActivityLog = new(Storage.Locations.ActivityLogPath);
        ActivityLog.TryAppend($"[App] Lightflow Studio {AppVersion.Display} starting.");
        if (!storage.IsReady)
            ActivityLog.TryAppend($"[Catalog] {storage.Status}: {storage.Diagnostic}");
        if (!Storage.PreviewAvailable)
            ActivityLog.TryAppend($"[Previews] {Storage.PreviewDiagnostic}");
        if (Storage.RecoveryDiagnostic is not null)
            ActivityLog.TryAppend($"[Catalog recovery] {Storage.RecoveryDiagnostic}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Exit += (_, _) =>
        {
            ActivityLog.TryAppend("[App shutdown] Application.Exit entered; disposing playback.");
            Playback.DisposeAsync().AsTask().GetAwaiter().GetResult();
            ActivityLog.TryAppend("[App shutdown] Playback disposal completed; disposing storage.");
            Storage?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            ActivityLog.TryAppend("[App shutdown] Storage disposal completed.");
            ActivityLog.TryAppend("[App] Lightflow Studio exiting.");
        };
        MainWindow = new MainWindow(Storage, storage.Status, storage.Diagnostic);
        MainWindow.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ActivityLog.TryAppend($"[App] Unhandled UI exception: {e.Exception}");
        e.Handled = true;
        System.Windows.MessageBox.Show(
            $"Lightflow encountered an unexpected interface error and must close. Diagnostic details were written to:\n\n{ActivityLog.Path}",
            "Lightflow Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        ActivityLog.TryAppend($"[App] Unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}");

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ActivityLog.TryAppend($"[App] Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }
}
