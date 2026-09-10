using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace LightflowStudio;

public partial class App : System.Windows.Application
{
    private readonly bool _runStartup = true;
    public App() { }
    // Resource-only bootstrap for live WPF tests; avoids opening real user storage or the instance mutex.
    internal App(bool runStartup, ActivityLogFile activityLog)
    {
        _runStartup = runStartup;
        ActivityLog = activityLog;
    }
    private readonly UnexpectedInterfaceErrorGate _unexpectedInterfaceErrorGate = new();
    private IApplicationInstanceCoordinator? _applicationInstance;
    private StartupSplash? _startupSplash;
    private bool _presentingStartup;
    internal static ActivityLogFile ActivityLog { get; private set; } = null!;
    internal LightflowStorageCoordinator? Storage { get; private set; }
    internal static MediaPlaybackCoordinator Playback { get; } = new(() =>
        new MediaPlaybackService(new FlyleafPlaybackBackend()));

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (!_runStartup) return;
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
        _applicationInstance.LaunchRequested += request => Dispatcher.Invoke(() =>
        {
            if (MainWindow is MainWindow mainWindow)
            {
                if (_presentingStartup) return; // The eventual reveal activates the coherent workspace.
                ActivityLog.TryAppend("[App] Received a secondary launch request; activating the existing window.");
                mainWindow.ActivateFromLaunch(request);
            }
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

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _presentingStartup = true;
        try
        {
            _startupSplash = new StartupSplash();
            _startupSplash.Show();
            // Let WPF render the lightweight artwork before storage or shell construction begins.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            base.OnStartup(e);
            var storage = await LightflowStorageCoordinator.StartAsync();
            if (storage.Coordinator is null)
            {
                CloseStartupSplash();
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
            var mainWindow = new MainWindow(Storage, storage.Status, storage.Diagnostic)
            {
                ShowActivated = false,
                ShowInTaskbar = false
            };
            MainWindow = mainWindow;
            mainWindow.SourceInitialized += (_, _) => StartupWindowPresentation.SetCloaked(mainWindow, true);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
            await mainWindow.PresentationReady;
            mainWindow.ShowInTaskbar = true;
            StartupWindowPresentation.SetCloaked(mainWindow, false);
            mainWindow.Activate();
            CloseStartupSplash();
            ActivityLog.TryAppend("[App startup] Workspace presentation ready; main window revealed and splash closed.");
            var reportSwitch = Array.IndexOf(e.Args, "--startup-presentation-report");
            if (e.Args.Contains("--startup-smoke-test") && reportSwitch >= 0 && reportSwitch + 1 < e.Args.Length)
            {
                if (!await mainWindow.StartupCompletion) throw new InvalidOperationException("Packaged shell initialization failed.");
                System.IO.File.WriteAllText(e.Args[reportSwitch + 1], "presentation-ready; splash-closed; shell-initialized");
            }
        }
        catch (OperationCanceledException) when (Dispatcher.HasShutdownStarted || MainWindow is not { IsLoaded: true })
        {
            CloseStartupSplash();
        }
        catch (Exception exception)
        {
            CloseStartupSplash();
            var diagnostic = BootstrapDiagnostics.TryWrite(exception.ToString());
            System.Windows.MessageBox.Show($"Lightflow could not finish starting.\n\n{exception.Message}\n\n{diagnostic}",
                "Lightflow Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
        finally { _presentingStartup = false; CloseStartupSplash(); }
    }

    private void CloseStartupSplash()
    {
        _startupSplash?.Close();
        _startupSplash = null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        CloseStartupSplash();
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
        CloseStartupSplash();
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
