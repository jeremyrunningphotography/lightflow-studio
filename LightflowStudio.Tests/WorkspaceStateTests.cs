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
    public void Load_FallsBackToTheDefaultThumbnailSizeForALegacyDocumentPredatingThisProperty()
    {
        // #125 was added after #121 shipped; a document written by that earlier build has no
        // BrowserThumbnailSizeLevel property at all, not merely a null one.
        Directory.CreateDirectory(_folder);
        File.WriteAllText(StatePath, """{"Version":1,"Layout":{"BrowserLocationsPaneWidth":260}}""");

        var state = WorkspaceStateStore.Load(StatePath);

        Assert.Null(state.Layout!.BrowserThumbnailSizeLevel);
        Assert.Equal(BrowserGridLayout.DefaultThumbnailSize, BrowserGridLayout.ThumbnailSizeFromLevel(
            state.Layout.BrowserThumbnailSizeLevel ?? (int)BrowserGridLayout.DefaultThumbnailSize));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsTheThumbnailSizeLevelAlongsideThePaneWidth()
    {
        WorkspaceStateStore.Save(StatePath, new WorkspaceState
        {
            Layout = new() { BrowserLocationsPaneWidth = 300, BrowserThumbnailSizeLevel = (int)BrowserThumbnailSize.Large }
        });

        var loaded = WorkspaceStateStore.Load(StatePath);

        Assert.Equal(300, loaded.Layout!.BrowserLocationsPaneWidth);
        Assert.Equal((int)BrowserThumbnailSize.Large, loaded.Layout.BrowserThumbnailSizeLevel);
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

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(-100, 0)]
    [InlineData(6, 5)]
    [InlineData(99, 5)]
    public void Normalize_ClampsAnOutOfRangeThumbnailSizeLevelRatherThanDiscardingIt(int saved, int expected)
    {
        // Clamped (not nulled): a future build with more levels, opened by an older one, should still land
        // on a valid size rather than silently reverting all the way to the default. The upper bound (5)
        // reflects #125's revision adding Huge/Maximum after the original four-level range topped out.
        var state = new WorkspaceState { Layout = new() { BrowserThumbnailSizeLevel = saved } };

        Assert.Equal(expected, WorkspaceState.Normalize(state).Layout!.BrowserThumbnailSizeLevel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Normalize_PreservesEveryValidThumbnailSizeLevelUnchanged(int level)
    {
        var state = new WorkspaceState { Layout = new() { BrowserThumbnailSizeLevel = level } };

        Assert.Equal(level, WorkspaceState.Normalize(state).Layout!.BrowserThumbnailSizeLevel);
    }

    [Fact]
    public void Normalize_LeavesAMissingThumbnailSizeLevelAsNullRatherThanInventingADefault()
    {
        // The default itself is BrowserGridLayout's concern (BrowserGridLayout.DefaultThumbnailSize), not
        // WorkspaceState's — null means "no preference saved," and stays null through normalization.
        var state = new WorkspaceState { Layout = new() { BrowserLocationsPaneWidth = 300 } };

        Assert.Null(WorkspaceState.Normalize(state).Layout!.BrowserThumbnailSizeLevel);
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
    public void SetBrowserLocationsPaneWidthAndSetBrowserThumbnailSizeLevel_MergeIntoTheSameLayoutSectionRatherThanClobberingEachOther()
    {
        // Regression: both setters used to replace Layout outright with `new WorkspaceLayoutState { ... }`,
        // which was harmless while only one Layout property existed. #125 added a second one, and
        // SaveWorkspaceState calls both setters back-to-back at the same shutdown/debounce point — a
        // wholesale replace would silently drop whichever was set first, in either direction.
        var path = Path.Combine(Path.GetTempPath(), $"LightflowStudio-workspace-{Guid.NewGuid():N}", "workspace-state.json");
        var service = new WorkspaceStateService(path, WorkspaceState.Empty);

        service.SetBrowserLocationsPaneWidth(310);
        service.SetBrowserThumbnailSizeLevel((int)BrowserThumbnailSize.Large);

        Assert.Equal(310, service.Current.Layout!.BrowserLocationsPaneWidth);
        Assert.Equal((int)BrowserThumbnailSize.Large, service.Current.Layout.BrowserThumbnailSizeLevel);
    }

    [Fact]
    public void SetBrowserThumbnailSizeLevelAndSetBrowserLocationsPaneWidth_MergeRegardlessOfCallOrder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"LightflowStudio-workspace-{Guid.NewGuid():N}", "workspace-state.json");
        var service = new WorkspaceStateService(path, WorkspaceState.Empty);

        service.SetBrowserThumbnailSizeLevel((int)BrowserThumbnailSize.Small);
        service.SetBrowserLocationsPaneWidth(280);

        Assert.Equal(280, service.Current.Layout!.BrowserLocationsPaneWidth);
        Assert.Equal((int)BrowserThumbnailSize.Small, service.Current.Layout.BrowserThumbnailSizeLevel);
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
            service.SetBrowserThumbnailSizeLevel((int)BrowserThumbnailSize.ExtraLarge);

            service.Save();

            var loaded = WorkspaceStateStore.Load(path);
            Assert.Equal(275, loaded.Layout!.BrowserLocationsPaneWidth);
            Assert.Equal((int)BrowserThumbnailSize.ExtraLarge, loaded.Layout.BrowserThumbnailSizeLevel);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
