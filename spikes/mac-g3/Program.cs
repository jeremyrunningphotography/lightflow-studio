using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;

namespace Lightflow.G3;

internal static class Program
{
    internal static string DataRoot = "";
    [STAThread]
    public static int Main(string[] args)
    {
        var index = Array.IndexOf(args, "--data-root");
        if (index < 0 || index + 1 >= args.Length || !Path.IsPathFullyQualified(args[index + 1]))
        {
            Console.Error.WriteLine("An explicit absolute task-owned --data-root is required.");
            return 2;
        }
        DataRoot = Path.GetFullPath(args[index + 1]);
        Directory.CreateDirectory(DataRoot);
        try
        {
            var app = AppBuilder.Configure<ProofApp>();
            if (args.Contains("--headless"))
            {
                app.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
                Evidence.Run();
                return 0;
            }
            return app.UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(DataRoot, "failure.txt"), error.ToString());
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}

public sealed class ProofApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Resources["SystemAccentColor"] = Avalonia.Media.Color.Parse("#FF9A66");
        Resources["SystemControlHighlightListAccentLowBrush"] = Avalonia.Media.Brush.Parse("#282129");
        Resources["SystemControlHighlightListAccentMediumBrush"] = Avalonia.Media.Brush.Parse("#332A32");
        Resources["SystemControlHighlightListAccentHighBrush"] = Avalonia.Media.Brush.Parse("#3D3036");
        Styles.Add(new Style(x => x.OfType<TableView>()) { Setters =
            { new Setter(TableView.BackgroundProperty, Avalonia.Media.Brush.Parse("#0B0D11")) } });
        Styles.Add(new Style(x => x.OfType<Window>())
        {
            Setters = { new Setter(Window.BackgroundProperty, Avalonia.Media.Brush.Parse("#0B0D11")) }
        });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new ProofWindow(100_000);
        base.OnFrameworkInitializationCompleted();
    }
}
