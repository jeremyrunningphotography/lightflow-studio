namespace LightflowStudio.Tests;

/// <summary>
/// Shared, thread-safe bootstrap for the one <see cref="System.Windows.Application"/> WPF permits per process —
/// needed so MainWindow.xaml's StaticResource lookups (merged from App.xaml) resolve during live-WPF tests,
/// exactly like the real packaged app, without ever going through the generated Main() entry point. Every
/// live-interaction test class shares this one gate rather than each attempting its own construction, guarded
/// by a lock (a naive per-class "if Current is null, construct" check is not safe against a concurrent attempt
/// racing it on the shared StaDispatcher thread) and ShutdownMode=OnExplicitShutdown (without it, closing a
/// test's only Window trips WPF's default OnLastWindowClose shutdown, clearing Application.Current back to
/// null — and WPF's "only one Application per process, ever" restriction is tracked by a flag that never resets
/// alongside the public Current property, so nothing can construct a replacement afterward).
///
/// Investigation found a second, non-obvious source of exactly the same failure: Flyleaf's own player engine
/// (FlyleafPlaybackIntegrationTests) constructs — and, on disposal, shuts down — its own Application if
/// Application.Current is still null when it first needs one. If a Flyleaf test happened to run before any
/// Browser*LiveInteractionTests class in the same process, Flyleaf's teardown would clear Current for good,
/// poisoning every live-WPF test that ran afterward with no recovery possible. FlyleafPlaybackIntegrationTests
/// now calls <see cref="EnsureLoaded"/> itself, first, in both of its own StaDispatcher-driven tests, so this
/// Application always exists before Flyleaf's engine gets a chance to create (and later tear down) its own —
/// a well-behaved WPF library reuses an existing Application rather than owning one.
/// </summary>
internal static class TestWpfApplication
{
    private static readonly object Gate = new();

    public static void EnsureLoaded()
    {
        if (System.Windows.Application.Current is not null) return;
        lock (Gate)
        {
            if (System.Windows.Application.Current is not null) return;
            try
            {
                var app = new App { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
            }
            catch (InvalidOperationException)
            {
                // Belt-and-suspenders: the lock above should already make this unreachable, but WPF's "only one
                // Application per process, ever" restriction is tracked by a static flag this process cannot
                // reset — if something outside this gate's control already constructed one, losing this race
                // is fine as long as Application.Current ends up set, which is all any caller actually needs.
                if (System.Windows.Application.Current is null) throw;
            }
        }
    }
}
