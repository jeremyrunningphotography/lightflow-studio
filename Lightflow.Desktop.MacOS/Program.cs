using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Lightflow.Platform.MacOS;
using Lightflow.UI;
using Lightflow.Application;
using System.ComponentModel;

namespace Lightflow.Desktop.MacOS;
internal static class Program
{
    public static string DataRoot = "";
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsMacOS() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.Arm64 ||
            args.Length != 2 || args[0] != "--data-root" || !Path.IsPathFullyQualified(args[1]))
        { Console.Error.WriteLine("Usage: LightflowStudio.Mac --data-root <absolute isolated task-owned folder> (Apple Silicon macOS)"); return 64; }
        DataRoot = Path.GetFullPath(args[1]);
        return AppBuilder.Configure<MacApplication>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}
public sealed class MacApplication : Avalonia.Application
{
    public override void Initialize()
    {
        Name = "Lightflow Studio";
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var model = new ShellViewModel(new MacShellCatalogSession(Program.DataRoot, new ThumbnailImageCodec()), new MacBrowserLocationProvider());
            BrowserLocationMonitor? volumes=null;
            try
            {
                volumes=new(new MacVolumeChangeSource(),async token=>
                {
                    await Dispatcher.UIThread.InvokeAsync(()=>model.Browser?.ReconcileLocationsAsync(token) ?? Task.CompletedTask);
                });
            }
            catch(Exception error) when(error is IOException or DllNotFoundException or EntryPointNotFoundException)
            { Console.Error.WriteLine($"Automatic volume updates unavailable; manual Refresh remains available. {error}"); }
            void LocationsReady(object? sender,PropertyChangedEventArgs args)
            {
                if(!model.WorkspaceAvailable)return;
                if(volumes is not null && model.Browser is { } browser)volumes.RequestAfterOpen(browser.Epoch);
                else model.Browser?.ReportLocationUpdatesUnavailable();
            }
            // Also reconcile after Catalog open/switch, closing the registration/initial-snapshot race.
            model.PropertyChanged+=LocationsReady;
            var window = new ShellWindow(model, BrowserLayoutStore.Load(Program.DataRoot));
            if(Environment.GetEnvironmentVariable("LIGHTFLOW_WINDOW_TRACE")=="1")
                window.WindowGeometryDiagnostic+=line=>
                {
                    try { File.AppendAllText(Path.Combine(Program.DataRoot,"window-geometry.log"),$"{DateTimeOffset.UtcNow:O} {line}{Environment.NewLine}"); }
                    catch(Exception error) when(error is IOException or UnauthorizedAccessException) { Console.Error.WriteLine("Window diagnostic could not be written."); }
                };
            window.LayoutChanged += layout => BrowserLayoutStore.Save(Program.DataRoot, layout);
            bool drained = false;
            Task? shutdown = null;
            async Task CloseAsync()
            {
                try
                {
                    model.PropertyChanged-=LocationsReady;
                    if(volumes is not null)await volumes.DisposeAsync();
                    await model.CloseAsync();
                    drained = true;
                    window.Close();
                    desktop.Shutdown();
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"Catalog shutdown failed; preserve the data folder. {error}");
                    // Keep the process alive and do not claim successful disposal.
                }
            }
            void RequestClose() => shutdown ??= CloseAsync();
            window.Closing += (_, e) => { if (!drained) { e.Cancel = true; Dispatcher.UIThread.Post(RequestClose); } };
            desktop.ShutdownRequested += (_, e) => { if (!drained) { e.Cancel = true; RequestClose(); } };
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
