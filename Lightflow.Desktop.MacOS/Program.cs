using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Lightflow.Platform.MacOS;
using Lightflow.UI;

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
        var theme = new FluentTheme();
        theme.Palettes[Avalonia.Styling.ThemeVariant.Dark] = new ColorPaletteResources { Accent = Avalonia.Media.Color.Parse("#D86B37") };
        Styles.Add(theme);
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var model = new ShellViewModel(new MacShellCatalogSession(Program.DataRoot), new MacBrowserLocationProvider());
            var window = new ShellWindow(model, BrowserLayoutStore.Load(Program.DataRoot));
            if (window.Browser is { } browser) browser.LayoutChanged += layout => BrowserLayoutStore.Save(Program.DataRoot, layout);
            bool drained = false;
            Task? shutdown = null;
            async Task CloseAsync()
            {
                try
                {
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
