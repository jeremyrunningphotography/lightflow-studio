using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class StartupIdentityRegressionTests
{
    [Fact]
    public async Task AppCleanup_ClosesSplashIdempotentlyWithoutClosingMainWindow()
    {
        await StaDispatcher.RunAsync(() =>
        {
            TestWpfApplication.EnsureLoaded();
            var app = (App)Application.Current;
            var previous = app.MainWindow;
            var shell = new Window();
            app.MainWindow = shell;
            var splash = new StartupSplash();
            var closed = 0;
            splash.Closed += (_, _) => closed++;
            var field = typeof(App).GetField("_startupSplash", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var cleanup = typeof(App).GetMethod("CloseStartupSplash", BindingFlags.NonPublic | BindingFlags.Instance)!;
            try
            {
                field.SetValue(app, splash);
                cleanup.Invoke(app, null);
                cleanup.Invoke(app, null);
                Assert.Equal(1, closed);
                Assert.Null(field.GetValue(app));
                Assert.Same(shell, app.MainWindow);
                Assert.Equal(ShutdownMode.OnExplicitShutdown, app.ShutdownMode);
            }
            finally { field.SetValue(app, null); shell.Close(); app.MainWindow = previous; }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EveryShellBrandingReference_ResolvesAndDecodesIncludingRetainedAbout()
    {
        await StaDispatcher.RunAsync(() =>
        {
            TestWpfApplication.EnsureLoaded();
            var markup = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "MainWindow.xaml"));
            var paths = markup.Descendants().Attributes()
                .Where(a => a.Name.LocalName is "Icon" or "Source" && a.Value.StartsWith("Assets/Branding/"))
                .Select(a => a.Value).Distinct().ToArray();
            Assert.Equal(3, paths.Length);
            Assert.Equal("Assets/Branding/lightflow-icon-256x256.png", markup.Root!.Attribute("Icon")!.Value);
            foreach (var path in paths)
            {
                var bitmap = BitmapFrame.Create(new Uri($"pack://application:,,,/LightflowStudio;component/{path}"));
                Assert.True(bitmap.PixelWidth > 0);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void StartupContract_HasNoDurationGateAndCleansUpBeforeFailureDialogs()
    {
        var app = Read("LightflowStudio/App.xaml.cs");
        var splash = Read("LightflowStudio/StartupSplash.cs");
        var ready = Read("LightflowStudio/MainWindow.Startup.cs");
        foreach (var source in new[] { app, splash, ready })
        {
            Assert.DoesNotContain("Task.Delay", source);
            Assert.DoesNotContain("Thread.Sleep", source);
            Assert.DoesNotContain("DispatcherTimer", source);
        }
        Assert.True(app.IndexOf("_startupSplash.Show()") < app.IndexOf("await LightflowStorageCoordinator.StartAsync()"));
        Assert.True(app.IndexOf("await mainWindow.PresentationReady") < app.IndexOf("SetCloaked(mainWindow, false)"));
        var failure = app[app.IndexOf("catch (Exception exception)")..];
        Assert.True(failure.IndexOf("CloseStartupSplash()") < failure.IndexOf("MessageBox.Show"));
        Assert.Contains("ShutdownMode = ShutdownMode.OnExplicitShutdown", app);
        Assert.Contains("ShutdownMode = ShutdownMode.OnMainWindowClose", app);
        Assert.Contains("finally { _presentingStartup = false; CloseStartupSplash(); }", app);
        Assert.Contains("await RestoreWorkspaceContinuationAsync()", ready);
    }

    [Fact]
    public void BuildAndInstaller_UseOneApprovedIcoWithoutExtraIconCanvas()
    {
        var project = XDocument.Load(Path.Combine(Root(), "LightflowStudio", "LightflowStudio.csproj"));
        Assert.Equal(@"Assets\Branding\LightflowStudio.ico", project.Descendants("ApplicationIcon").Single().Value);
        Assert.Contains(@"LightflowStudio\Assets\Branding\LightflowStudio.ico", Read("scripts/Build-Release.ps1"));
        var installer = Read("installer/LightflowStudio.iss");
        Assert.Contains(@"SetupIconFile={#SourceDir}\LightflowStudio.ico", installer);
        Assert.Contains(@"UninstallDisplayIcon={app}\{#MyAppExeName}", installer);
        var shortcuts = installer.Split('\n').Where(line => line.StartsWith("Name: ") && line.Contains("Filename:"));
        Assert.All(shortcuts, line => Assert.Contains(@"Filename: ""{app}\{#MyAppExeName}""", line));
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(Root(), path));
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "LightflowStudio", "LightflowStudio.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
