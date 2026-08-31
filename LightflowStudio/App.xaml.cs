using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace LightflowStudio;

public partial class App : System.Windows.Application
{
    private readonly UnexpectedInterfaceErrorGate _unexpectedInterfaceErrorGate = new();
    private IApplicationInstanceCoordinator? _applicationInstance;
    internal static ActivityLogFile ActivityLog { get; private set; } = null!;
    internal LightflowStorageCoordinator? Storage { get; private set; }
    internal static MediaPlaybackCoordinator Playback { get; } = new(() =>
        new MediaPlaybackService(new FlyleafPlaybackBackend()));

    protected override void OnStartup(StartupEventArgs e)
    {
        var migrationCopySwitch = Array.IndexOf(e.Args, CatalogPackageRuntimeVerifier.MigrationCopyCommandLineSwitch);
        if (migrationCopySwitch >= 0)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            var databasePath = migrationCopySwitch + 1 < e.Args.Length ? e.Args[migrationCopySwitch + 1] : "";
            var verified = CatalogPackageRuntimeVerifier.VerifyMigrationCopyAsync(databasePath).GetAwaiter().GetResult();
            Shutdown(verified ? 0 : 1);
            return;
        }
        if (e.Args.Contains(CatalogPackageRuntimeVerifier.CommandLineSwitch, StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            var verified = CatalogPackageRuntimeVerifier.VerifyAsync().GetAwaiter().GetResult();
            Shutdown(verified ? 0 : 1);
            return;
        }

        _applicationInstance = new WindowsApplicationInstanceCoordinator();
        _applicationInstance.LaunchRequested += request => Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is MainWindow mainWindow) mainWindow.ActivateFromLaunch(request);
        });
        var instance = _applicationInstance.StartOrSignal(ApplicationLaunchRequest.Current(e.Args));
        if (instance.Status != ApplicationInstanceStatus.Primary)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            if (instance.Status == ApplicationInstanceStatus.ExistingInstanceActivationFailed)
            {
                Trace.WriteLine(instance.Diagnostic);
                var bootstrapLog = BootstrapDiagnostics.TryWrite(instance.Diagnostic!);
                var diagnostic = bootstrapLog is null
                    ? instance.Diagnostic
                    : $"{instance.Diagnostic}\n\nDiagnostic details were written to:\n{bootstrapLog}";
                System.Windows.MessageBox.Show(diagnostic, "Lightflow Studio",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _applicationInstance.Dispose();
                _applicationInstance = null;
                Shutdown(1);
                return;
            }
            _applicationInstance.Dispose();
            _applicationInstance = null;
            Shutdown(0);
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

    protected override void OnExit(ExitEventArgs e)
    {
        try { base.OnExit(e); }
        finally
        {
            // Keep ownership until every normal Exit handler has finished disposing shared application state.
            _applicationInstance?.Dispose();
            _applicationInstance = null;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ActivityLog.TryAppend($"[App] Unhandled UI exception: {e.Exception}");
        e.Handled = true;
        if (!_unexpectedInterfaceErrorGate.TryEnter()) return;
        try
        {
            System.Windows.MessageBox.Show(
                $"Lightflow encountered an unexpected interface error and must close. Diagnostic details were written to:\n\n{ActivityLog.Path}",
                "Lightflow Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
        finally { _unexpectedInterfaceErrorGate.Exit(); }
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        ActivityLog.TryAppend($"[App] Unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}");

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ActivityLog.TryAppend($"[App] Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }
}

internal static class BootstrapDiagnostics
{
    public static string? TryWrite(string diagnostic)
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"LightflowStudio-startup-{Environment.ProcessId}.log");
            System.IO.File.WriteAllText(path, $"[{DateTimeOffset.Now:O}] {diagnostic}{Environment.NewLine}");
            return path;
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Could not write the Lightflow bootstrap diagnostic: {exception.Message}");
            return null;
        }
    }
}

internal sealed class UnexpectedInterfaceErrorGate
{
    private int _active;
    public bool TryEnter() => Interlocked.CompareExchange(ref _active, 1, 0) == 0;
    public void Exit() => Interlocked.Exchange(ref _active, 0);
}
