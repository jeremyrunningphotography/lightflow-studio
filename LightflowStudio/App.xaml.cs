using System.Windows;
using System.Windows.Threading;

namespace LightflowStudio;

public partial class App : System.Windows.Application
{
    internal static ActivityLogFile ActivityLog { get; } = new(LightflowStorageLocations.Current.ActivityLogPath);
    internal static MediaPlaybackCoordinator Playback { get; } = new(() =>
        new MediaPlaybackService(new FlyleafPlaybackBackend()));

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ActivityLog.TryAppend($"[App] Lightflow Studio {AppVersion.Display} starting.");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Exit += (_, _) =>
        {
            Playback.DisposeAsync().AsTask().GetAwaiter().GetResult();
            ActivityLog.TryAppend("[App] Lightflow Studio exiting.");
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        ActivityLog.TryAppend($"[App] Unhandled UI exception: {e.Exception}");

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        ActivityLog.TryAppend($"[App] Unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}");

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ActivityLog.TryAppend($"[App] Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }
}
