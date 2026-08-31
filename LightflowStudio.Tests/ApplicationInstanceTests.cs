using System.Diagnostics;
using System.Threading;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ApplicationInstanceTests
{
    [Fact]
    public async Task SecondLaunch_SignalsOwnerWithVersionedArguments_AndNeverBecomesPrimary()
    {
        var identity = UniqueIdentity();
        using var received = new ManualResetEventSlim();
        ApplicationLaunchRequest? forwarded = null;
        using var owner = new WindowsApplicationInstanceCoordinator(identity);
        owner.LaunchRequested += request => { forwarded = request; received.Set(); };

        Assert.Equal(ApplicationInstanceStatus.Primary,
            owner.StartOrSignal(ApplicationLaunchRequest.Current([])).Status);
        var result = await Task.Run(() =>
        {
            using var second = new WindowsApplicationInstanceCoordinator(identity);
            return second.StartOrSignal(ApplicationLaunchRequest.Current(["future.lightflow"]));
        });

        Assert.Equal(ApplicationInstanceStatus.ExistingInstanceActivated, result.Status);
        Assert.True(received.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotNull(forwarded);
        Assert.Equal(ApplicationLaunchRequest.CurrentVersion, forwarded.Version);
        Assert.Equal(["future.lightflow"], forwarded.Arguments);
    }

    [Fact]
    public void OwnerExit_ReleasesStableIdentityForAnotherExecutableLocation()
    {
        var identity = UniqueIdentity();
        using (var first = new WindowsApplicationInstanceCoordinator(identity))
            Assert.Equal(ApplicationInstanceStatus.Primary,
                first.StartOrSignal(ApplicationLaunchRequest.Current([@"C:\one\LightflowStudio.exe"])).Status);

        using var replacement = new WindowsApplicationInstanceCoordinator(identity);
        Assert.Equal(ApplicationInstanceStatus.Primary,
            replacement.StartOrSignal(ApplicationLaunchRequest.Current([@"D:\portable\LightflowStudio.exe"])).Status);
    }

    [Fact]
    public void AbandonedOwner_DoesNotPermanentlyBlockRelaunch()
    {
        var identity = UniqueIdentity();
        Mutex? abandoned = null;
        using var acquired = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            abandoned = new Mutex(true, $"Local\\{identity}", out _);
            acquired.Set();
        });
        thread.Start();
        Assert.True(acquired.Wait(TimeSpan.FromSeconds(5)));
        thread.Join();

        using var replacement = new WindowsApplicationInstanceCoordinator(identity);
        Assert.Equal(ApplicationInstanceStatus.Primary,
            replacement.StartOrSignal(ApplicationLaunchRequest.Current([])).Status);
        abandoned?.Dispose();
    }

    [Fact]
    public void SignalingFailure_FailsSafelyInsteadOfStartingCompetingOwner()
    {
        var identity = UniqueIdentity();
        using var release = new ManualResetEventSlim();
        using var acquired = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            using var mutex = new Mutex(true, $"Local\\{identity}", out _);
            acquired.Set();
            release.Wait();
            mutex.ReleaseMutex();
        });
        thread.Start();
        Assert.True(acquired.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            using var contender = new WindowsApplicationInstanceCoordinator(identity, TimeSpan.FromMilliseconds(100));
            var result = contender.StartOrSignal(ApplicationLaunchRequest.Current([]));
            Assert.Equal(ApplicationInstanceStatus.ExistingInstanceActivationFailed, result.Status);
            Assert.Contains("activation signaling failed", result.Diagnostic);
        }
        finally
        {
            release.Set();
            thread.Join();
        }
    }

    [Fact]
    public void Bootstrap_DecidesOwnershipBeforeStorageCatalogOrMainWindowInitialization()
    {
        var source = File.ReadAllText(PathAtRoot("LightflowStudio", "App.xaml.cs"));
        var decision = source.IndexOf("StartOrSignal", StringComparison.Ordinal);
        Assert.True(decision >= 0);
        Assert.True(decision < source.IndexOf("LightflowStorageCoordinator.StartAsync", StringComparison.Ordinal));
        Assert.True(decision < source.IndexOf("new MainWindow", StringComparison.Ordinal));
    }

    [Fact]
    public void Activation_RestoresAndReusesExistingWindowWithoutResettingApplicationState()
    {
        var window = new TestApplicationWindow { IsMinimized = true, IsVisible = true, CurrentWorkspace = "Jobs" };

        ApplicationWindowActivation.RestoreAndActivate(window);

        Assert.False(window.IsMinimized);
        Assert.Equal(1, window.RestoreCount);
        Assert.Equal(0, window.ShowCount);
        Assert.Equal(1, window.ActivateCount);
        Assert.Equal(1, window.FocusCount);
        Assert.Equal("Jobs", window.CurrentWorkspace);
    }

    private static string UniqueIdentity() =>
        $"JeremyRunningPhotography.LightflowStudio.Tests.{Process.GetCurrentProcess().Id}.{Guid.NewGuid():N}";

    private static string PathAtRoot(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(current, "Directory.Build.props")))
            current = Directory.GetParent(current)?.FullName
                ?? throw new DirectoryNotFoundException("Repository root not found.");
        return Path.Combine([current, .. parts]);
    }

    private sealed class TestApplicationWindow : IApplicationWindow
    {
        public bool IsMinimized { get; set; }
        public bool IsVisible { get; set; }
        public string CurrentWorkspace { get; set; } = "Home";
        public int RestoreCount { get; private set; }
        public int ShowCount { get; private set; }
        public int ActivateCount { get; private set; }
        public int FocusCount { get; private set; }
        public void Restore() { IsMinimized = false; RestoreCount++; }
        public void Show() { IsVisible = true; ShowCount++; }
        public bool Activate() { ActivateCount++; return true; }
        public bool Focus() { FocusCount++; return true; }
    }
}
