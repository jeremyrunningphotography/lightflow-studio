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
    private string[]? _catalogRestartArguments;
    internal void PrepareCatalogRestart(string? directory = null, Guid? catalogId = null)
    {
        var arguments = new List<string>();
        if (LightflowStorageLocations.Current.IsIsolated)
        {
            arguments.Add("--data-root"); arguments.Add(LightflowStorageLocations.Current.ApplicationDataDirectory);
        }
        if (directory is not null)
        {
            arguments.Add("--open-catalog"); arguments.Add(directory);
            arguments.Add("--selected-catalog-id"); arguments.Add(catalogId!.Value.ToString("D"));
        }
        _catalogRestartArguments = arguments.ToArray();
    }
    internal void CancelCatalogRestart() => _catalogRestartArguments = null;
    internal static ActivityLogFile ActivityLog { get; private set; } = null!;
    internal LightflowStorageCoordinator? Storage { get; private set; }
    internal static MediaPlaybackCoordinator Playback { get; } = new(() =>
        new MediaPlaybackService(new FlyleafPlaybackBackend()));

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (!_runStartup) return;
        InitializedDataProfile? verificationProfile = null;
        try
        {
            _ = LightflowStorageLocations.Current;
            ValidationPresentation.ValidateStartup(e.Args, LightflowStorageLocations.Current.IsIsolated);
            // Explicit headless diagnostics have no splash; share their validation with storage too.
            if (e.Args.Any(argument => argument is "--verify-preview-regeneration" or "--verify-preview-quiescence" or
                "--verify-catalog-backup-paths" || argument == CatalogPackageRuntimeVerifier.CommandLineSwitch ||
                argument == CatalogPackageRuntimeVerifier.MigrationCopyCommandLineSwitch))
                verificationProfile = ApplicationDataProfile.Initialize(LightflowStorageLocations.Current);
        }
        catch (Exception exception)
        {
            // Configuration failures must never enter logging, activation, or normal storage startup.
            Console.Error.WriteLine("Lightflow data-root startup failed: " + exception.Message);
            Trace.WriteLine(exception);
            BootstrapDiagnostics.TryWrite("Lightflow data-root startup failed: " + exception);
            Shutdown(2);
            return;
        }
        if (e.Args.Contains(CatalogOwnershipVerifier.Switch, StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            try { Shutdown(await CatalogOwnershipVerifier.RunAsync(e.Args) ? 0 : 1); }
            catch (Exception exception) { Trace.WriteLine(exception); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--verify-preview-regeneration", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            var verified = await PreviewRegenerationVerifier.RunAsync(LightflowStorageLocations.Current, e.Args, verificationProfile);
            Shutdown(verified ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--verify-preview-quiescence", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            var verified = await PreviewQuiescenceVerifier.RunAsync(LightflowStorageLocations.Current, e.Args, verificationProfile);
            Shutdown(verified ? 0 : 1);
            return;
        }
        if (e.Args.Contains("--verify-catalog-backup-paths", StringComparer.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            var verified = await CatalogBackupPathVerifier.VerifyAsync(LightflowStorageLocations.Current, verificationProfile);
            Shutdown(verified ? 0 : 1);
            return;
        }
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
            var reportSwitch = Array.IndexOf(e.Args, "--sqlite-runtime-report");
            var reportPath = reportSwitch >= 0 && reportSwitch + 1 < e.Args.Length ? e.Args[reportSwitch + 1] : null;
            var verified = CatalogPackageRuntimeVerifier.VerifyAsync(reportPath).GetAwaiter().GetResult();
            Shutdown(verified ? 0 : 1);
            return;
        }

        _applicationInstance = new WindowsApplicationInstanceCoordinator(
            ApplicationDataProfile.InstanceIdentity(LightflowStorageLocations.Current));
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
            var splashProcessAge = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            InitializedDataProfile initialized;
            try
            {
                initialized = LightflowStorageLocations.Current.IsIsolated
                    ? await Task.Run(() => ApplicationDataProfile.Initialize(LightflowStorageLocations.Current))
                    : ApplicationDataProfile.Initialize(LightflowStorageLocations.Current);
            }
            catch (Exception exception)
            {
                BootstrapDiagnostics.TryWrite("Lightflow data-root startup failed: " + exception);
                CloseStartupSplash(); Shutdown(2); return;
            }
            ActivityLog = new(LightflowStorageLocations.Current.ActivityLogPath);
            using var startupDiagnostics = new StartupDiagnostics(message => ActivityLog.TryAppend(message),
                progress => Dispatcher.BeginInvoke(() => _startupSplash?.SetProgress(progress)),
                validation => Dispatcher.BeginInvoke(() =>
                {
                    _startupSplash?.SetValidation(validation);
                    if (LightflowStorageLocations.Current.IsIsolated &&
                        Environment.GetEnvironmentVariable("LIGHTFLOW_STARTUP_CAPTURE") is { Length: > 0 } capture)
                        _startupSplash?.CaptureValidation(capture);
                }));
            StartupDiagnostics.Note($"Process entered startup; process age={(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds:F1}ms");
            StartupDiagnostics.Note($"Splash rendered: processAge={splashProcessAge:F1}ms");
            if (initialized.Locations.IsIsolated)
                StartupDiagnostics.Note($"Isolated profile validation: completed invocations={initialized.ValidationInvocations} inspectedPaths={initialized.InspectedPaths} descendantEnumerations=0 duration={initialized.ElapsedMilliseconds:F1}ms");
            base.OnStartup(e);
            var selectionIndex = Array.IndexOf(e.Args, "--open-catalog");
            var identityIndex = Array.IndexOf(e.Args, "--selected-catalog-id");
            string? selectedDirectory = selectionIndex >= 0 && selectionIndex + 1 < e.Args.Length ? e.Args[selectionIndex + 1] : null;
            Guid? selectedId = identityIndex >= 0 && identityIndex + 1 < e.Args.Length && Guid.TryParse(e.Args[identityIndex + 1], out var parsedId) ? parsedId : null;
            var malformedSelection = (selectionIndex >= 0 || identityIndex >= 0) &&
                (string.IsNullOrWhiteSpace(selectedDirectory) || selectedDirectory.StartsWith("--", StringComparison.Ordinal) || selectedId is null || selectedId == Guid.Empty);
            var storage = malformedSelection
                ? new StorageStartupResult(StorageStartupStatus.CatalogUnreadable,
                    Diagnostic: "Catalog selection requires a verified folder and identity. The previous configuration and Catalog have been retained.",
                    RecoveryReason: CatalogRecoveryReason.ValidationFailed)
                : await LightflowStorageCoordinator.StartAsync(initializedProfile: initialized,
                allowFirstCreation: e.Args.Contains("--startup-smoke-test"), existingCatalogDirectory: selectedDirectory, selectedCatalogId: selectedId,
                resumePreviousCatalogOnFailure: false);
            while (!storage.IsReady || storage.Coordinator?.VerifyWorkspaceReadiness() != true)
            {
                if (storage.IsReady)
                    storage = storage with { Status = StorageStartupStatus.CatalogUnavailable,
                        Diagnostic = storage.Coordinator?.RecoveryDiagnostic,
                        RecoveryReason = CatalogRecoveryReason.OwnershipUnavailable };
                CloseStartupSplash();
                await using var gate = new CatalogStartupGate(storage, initialized.Locations, initialized);
                var setup = new CatalogSetupDialog(gate, initialized.Locations);
                var accepted = setup.ShowDialog() == true;
                if (!accepted) { await gate.DisposeAsync(); Shutdown(0); return; }
                storage = gate.TakeReady();
            }
            Storage = storage.Coordinator;

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
            Exit += (_, exit) =>
            {
                if (exit.ApplicationExitCode != 0) Storage?.PreventCleanShutdown();
                ActivityLog.TryAppend("[App shutdown] Application.Exit entered; disposing playback.");
                Playback.DisposeAsync().AsTask().GetAwaiter().GetResult();
                ActivityLog.TryAppend("[App shutdown] Playback disposal completed; disposing storage.");
                Storage?.DisposeAsync(message => ActivityLog.TryAppend(message)).AsTask().GetAwaiter().GetResult();
                ActivityLog.TryAppend("[App shutdown] Storage disposal completed.");
                ActivityLog.TryAppend("[App] Lightflow Studio exiting.");
            };
            using var presentationTiming = StartupDiagnostics.Stage("Workspace construction and presentation", "Restoring workspace…");
            var mainWindow = new MainWindow(Storage, storage.Status, storage.Diagnostic, storage.RecoveryReason)
            {
                ShowActivated = false,
                ShowInTaskbar = false
            };
            MainWindow = mainWindow;
            mainWindow.SourceInitialized += (_, _) => StartupWindowPresentation.SetCloaked(mainWindow, true);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
            await mainWindow.PresentationReady;
            if (!Storage.VerifyWorkspaceReadiness()) { mainWindow.EnsureCatalogWorkspace(); return; }
            mainWindow.ShowInTaskbar = true;
            await mainWindow.RevealStartupPresentationAsync();
            mainWindow.Activate();
            StartupDiagnostics.Note($"Browser usable: processAge={(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds:F1}ms");
            CloseStartupSplash();
            ActivityLog.TryAppend("[App startup] Workspace presentation ready; main window revealed and splash closed.");
            if (storage.IsReady && Storage.RecoveryDiagnostic is not null)
                NoticeDialog.Show(mainWindow, "Catalog selection", "The selected Catalog was not opened", Storage.RecoveryDiagnostic);
            var reportSwitch = Array.IndexOf(e.Args, "--startup-presentation-report");
            if (e.Args.Contains("--startup-smoke-test") && reportSwitch >= 0 && reportSwitch + 1 < e.Args.Length)
            {
                System.IO.File.WriteAllText(e.Args[reportSwitch + 1], "presentation-ready; splash-closed");
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
            if (e.ApplicationExitCode == 0 && _catalogRestartArguments is { } arguments)
            {
                var launch = new ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory, "LightflowStudio.exe"))
                    { UseShellExecute = false };
                foreach (var argument in arguments) launch.ArgumentList.Add(argument);
                try { Process.Start(launch); }
                catch (Exception error) { BootstrapDiagnostics.TryWrite("Catalog restart failed; configuration remains recoverable: " + error); }
            }
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CloseStartupSplash();
        Storage?.PreventCleanShutdown();
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
        Storage?.PreventCleanShutdown();
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
