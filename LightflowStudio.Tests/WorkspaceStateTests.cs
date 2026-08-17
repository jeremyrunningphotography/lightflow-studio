using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class WorkspaceStateStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"LightflowStudio-workspace-{Guid.NewGuid():N}");
    private string StatePath => Path.Combine(_folder, "workspace-state.json");

    [Fact]
    public void Load_ReturnsEmptyOnFirstLaunchWithNoPersistedState()
    {
        var state = WorkspaceStateStore.Load(StatePath);

        Assert.Equal(WorkspaceState.CurrentVersion, state.Version);
        Assert.Null(state.Browser);
        Assert.Null(state.Window);
        Assert.Null(state.Layout);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsEveryPersistedSection()
    {
        var rootId = Guid.NewGuid();
        var state = new WorkspaceState
        {
            Browser = new() { RootId = rootId, RelativeFolder = "Trips/Iceland", LastResolvedAbsolutePath = @"D:\Trips\Iceland" },
            Window = new() { Width = 1500, Height = 950, Left = 40, Top = 20, IsMaximized = true },
            Layout = new() { BrowserLocationsPaneWidth = 300 }
        };

        WorkspaceStateStore.Save(StatePath, state);
        var loaded = WorkspaceStateStore.Load(StatePath);

        Assert.Equal(rootId, loaded.Browser!.RootId);
        Assert.Equal("Trips/Iceland", loaded.Browser.RelativeFolder);
        Assert.Equal(@"D:\Trips\Iceland", loaded.Browser.LastResolvedAbsolutePath);
        Assert.Equal(1500, loaded.Window!.Width);
        Assert.Equal(950, loaded.Window.Height);
        Assert.Equal(40, loaded.Window.Left);
        Assert.Equal(20, loaded.Window.Top);
        Assert.True(loaded.Window.IsMaximized);
        Assert.Equal(300, loaded.Layout!.BrowserLocationsPaneWidth);
    }

    [Fact]
    public void Load_FallsBackToEmptyWhenPersistedDocumentIsCorrupt()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(StatePath, "{ not json ");

        var state = WorkspaceStateStore.Load(StatePath);

        Assert.Equal(WorkspaceState.Empty, state);
    }

    [Fact]
    public void Load_ToleratesMissingAndUnknownFields()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(StatePath, """{"Version":1,"Layout":{"BrowserLocationsPaneWidth":260},"SomeFutureField":{"Nested":true}}""");

        var state = WorkspaceStateStore.Load(StatePath);

        Assert.Null(state.Browser);
        Assert.Null(state.Window);
        Assert.Equal(260, state.Layout!.BrowserLocationsPaneWidth);
    }

    [Fact]
    public void Save_AtomicallyReplacesExistingStateWithoutLeavingTemporaryFiles()
    {
        WorkspaceStateStore.Save(StatePath, new WorkspaceState { Layout = new() { BrowserLocationsPaneWidth = 250 } });

        WorkspaceStateStore.Save(StatePath, new WorkspaceState { Layout = new() { BrowserLocationsPaneWidth = 400 } });

        Assert.Equal(400, WorkspaceStateStore.Load(StatePath).Layout!.BrowserLocationsPaneWidth);
        Assert.Empty(Directory.EnumerateFiles(_folder, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }
}

public sealed class WorkspaceStateNormalizationTests
{
    [Fact]
    public void Normalize_DropsOnlyTheBrowserSectionWhenItsRelativeFolderIsMalformed()
    {
        var state = new WorkspaceState
        {
            Browser = new() { RootId = Guid.NewGuid(), RelativeFolder = @"..\escaping" },
            Layout = new() { BrowserLocationsPaneWidth = 300 }
        };

        var normalized = WorkspaceState.Normalize(state);

        Assert.Null(normalized.Browser);
        Assert.Equal(300, normalized.Layout!.BrowserLocationsPaneWidth);
    }

    [Fact]
    public void Normalize_TreatsAnEmptyRootIdAsNoSavedBrowserLocation()
    {
        var state = new WorkspaceState { Browser = new() { RootId = Guid.Empty, RelativeFolder = "" } };

        Assert.Null(WorkspaceState.Normalize(state).Browser);
    }

    [Theory]
    [InlineData(0, 720)]
    [InlineData(1120, 0)]
    [InlineData(double.NaN, 720)]
    public void Normalize_DropsWindowBoundsThatAreNotUsablyPositive(double width, double height)
    {
        var state = new WorkspaceState { Window = new() { Width = width, Height = height, Left = 0, Top = 0 } };

        Assert.Null(WorkspaceState.Normalize(state).Window);
    }

    [Theory]
    [InlineData(50, WorkspaceState.MinLocationsPaneWidth)]
    [InlineData(9000, WorkspaceState.MaxLocationsPaneWidth)]
    public void Normalize_ClampsLocationsPaneWidthToItsSupportedRange(double saved, double expected)
    {
        var state = new WorkspaceState { Layout = new() { BrowserLocationsPaneWidth = saved } };

        Assert.Equal(expected, WorkspaceState.Normalize(state).Layout!.BrowserLocationsPaneWidth);
    }
}

public sealed class WorkspaceStateServiceTests
{
    [Fact]
    public void SetBrowserLocation_DoesNotPersistTransientMediaSelection()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"LightflowStudio-workspace-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "workspace-state.json");
        try
        {
            var service = new WorkspaceStateService(path, WorkspaceState.Empty);

            service.SetBrowserLocation(Guid.NewGuid(), "Trips/Iceland", @"D:\Trips\Iceland");
            service.Save();

            // WorkspaceBrowserLocationState has no selection-related member, so restoring the last folder
            // can never resurrect an arbitrary prior multi-selection: there is nowhere to have stored it.
            Assert.DoesNotContain("Select", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void MutatingSections_UpdatesInMemoryStateWithoutTouchingDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"LightflowStudio-workspace-{Guid.NewGuid():N}", "workspace-state.json");
        var service = new WorkspaceStateService(path, WorkspaceState.Empty);
        var rootId = Guid.NewGuid();

        service.SetBrowserLocation(rootId, "Trips/Iceland", @"D:\Trips\Iceland");
        service.SetWindow(new() { Width = 1300, Height = 800, Left = 10, Top = 10 });
        service.SetBrowserLocationsPaneWidth(310);

        Assert.Equal(rootId, service.Current.Browser!.RootId);
        Assert.Equal(1300, service.Current.Window!.Width);
        Assert.Equal(310, service.Current.Layout!.BrowserLocationsPaneWidth);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Save_PersistsTheCurrentInMemoryDocumentAndNeverThrowsWhenTheDirectoryIsUnwritable()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"LightflowStudio-workspace-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "workspace-state.json");
        try
        {
            var service = new WorkspaceStateService(path, WorkspaceState.Empty);
            service.SetBrowserLocationsPaneWidth(275);

            service.Save();

            Assert.Equal(275, WorkspaceStateStore.Load(path).Layout!.BrowserLocationsPaneWidth);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
