using System.ComponentModel;
using System.Diagnostics;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExplorerHandoffTests : IDisposable
{
    private readonly string _directory = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "lightflow-explorer-" + Guid.NewGuid().ToString("N"))).FullName;
    private readonly Guid _root = Guid.NewGuid();
    private readonly List<ProcessStartInfo> _launches = [];
    private MediaRootAvailability _availability = MediaRootAvailability.Online;
    private string? _mappedDirectory;

    private ExplorerHandoff Service(Action<ProcessStartInfo>? launch = null) => new(root =>
    {
        Assert.Equal(_root, root);
        return Task.FromResult<MediaRootInfo?>(new(root, "Location", _mappedDirectory ?? _directory, _availability));
    }, launch ?? _launches.Add);

    [Fact]
    public async Task ContextFolderDiffersFromSelection_UsesCurrentMappingWithoutChangingNodes()
    {
        var selected = new BrowserTreeNode("Selected", _directory) { IsSelected = true, IsExpanded = true };
        var context = new BrowserTreeNode("Context", @"C:\stale\context");
        context.SetIdentity(_root, "context");
        var target = ExplorerTarget.Folder(context);
        Directory.CreateDirectory(Path.Combine(_directory, "context"));
        Assert.True(await Service().CanOpenAsync(target));
        _mappedDirectory = Directory.CreateDirectory(Path.Combine(_directory, "remapped")).FullName;
        var expected = Directory.CreateDirectory(Path.Combine(_mappedDirectory, "context")).FullName;
        await Service().OpenAsync(target);
        Assert.Equal(expected, Assert.Single(Assert.Single(_launches).ArgumentList));
        Assert.True(selected.IsSelected);
        Assert.True(selected.IsExpanded);
        Assert.False(context.IsSelected);
        Assert.False(context.IsExpanded);
        Assert.Null(target!.PhysicalFolder);
    }

    [Fact]
    public async Task SelectedFolderAndUnmaterializedManagedRoot_AreSupported()
    {
        var selected = new BrowserTreeNode("Location", @"C:\stale", new("location", "Location", @"C:\stale",
            BrowserStorageKind.ManagedRoot, MediaRootAvailability.Online, _root)) { IsSelected = true };
        await Service().OpenAsync(ExplorerTarget.Folder(selected));
        Assert.Equal(_directory, Assert.Single(Assert.Single(_launches).ArgumentList));
        Assert.True(selected.IsSelected);
    }

    [Fact]
    public async Task BareFilesystemFolder_DoesNotRequireCreatingCatalogIdentity()
    {
        var service = new ExplorerHandoff(_ => throw new InvalidOperationException("No Catalog access expected"), _launches.Add);
        await service.OpenAsync(ExplorerTarget.Folder(new("volume", _directory)));
        Assert.Equal(_directory, Assert.Single(Assert.Single(_launches).ArgumentList));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MediaContextAmongMultipleSelectedItems_RevealsExactlyOneFileAndPreservesSelection(bool contextSelected)
    {
        var other = Tile("other.mp4");
        var context = Tile("clip & (take 1), café's #1.mp4");
        other.IsSelected = true;
        context.IsSelected = contextSelected;
        var id = context.AssetId;
        var path = Path.Combine(_directory, context.RelativePath);
        File.WriteAllText(path, "test");
        await Service().OpenAsync(ExplorerTarget.Media(context));
        var request = Assert.Single(_launches);
        Assert.Equal("explorer.exe", request.FileName);
        Assert.Equal($"/select,\"{path}\"", request.Arguments);
        Assert.Empty(request.ArgumentList);
        Assert.True(request.UseShellExecute);
        Assert.True(other.IsSelected);
        Assert.Equal(contextSelected, context.IsSelected);
        Assert.Equal(id, context.AssetId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrDisappearingTarget_IsUnavailableAndNeverLaunches(bool file)
    {
        var path = Path.Combine(_directory, "target");
        if (file) File.WriteAllText(path, "test"); else Directory.CreateDirectory(path);
        var target = new ExplorerTarget(_root, "target", null, file);
        Assert.True(await Service().CanOpenAsync(target));
        if (file) File.Delete(path); else Directory.Delete(path);
        Assert.False(await Service().CanOpenAsync(target));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Service().OpenAsync(target));
        Assert.Empty(_launches);
    }

    [Theory]
    [InlineData(MediaRootAvailability.Unavailable)]
    [InlineData(MediaRootAvailability.Unmapped)]
    internal async Task OfflineRootCannotLaunchEvenWhenOldPathExists(MediaRootAvailability availability)
    {
        _availability = availability;
        var target = new ExplorerTarget(_root, "", null, false);
        Assert.False(await Service().CanOpenAsync(target));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Service().OpenAsync(target));
        Assert.Empty(_launches);
    }

    [Fact]
    public async Task ShellFailureIsObservableForStyledFeedback_AndIsNotRetried()
    {
        var attempts = 0;
        var service = Service(_ => { attempts++; throw new Win32Exception("shell failed"); });
        await Assert.ThrowsAsync<Win32Exception>(() => service.OpenAsync(new(_root, "", null, false)));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task UnresolvableAndEmptyTargets_DisableCapability()
    {
        var service = new ExplorerHandoff(_ => throw new KeyNotFoundException("removed root"), _launches.Add);
        Assert.False(await service.CanOpenAsync(new(_root, "", null, false)));
        Assert.False(await service.CanOpenAsync(null));
        Assert.Null(ExplorerTarget.Folder(new("placeholder", null, placeholder: true)));
        Assert.Empty(_launches);
    }

    [Theory]
    [InlineData(@"C:\Media space\a & b,(1)'s %.mp4")]
    [InlineData(@"\\server\share name\clip.mp4")]
    [InlineData(@"\\?\C:\Media space\clip.mp4")]
    public void ShellContractTreatsPathAsDataAndMatchesExistingOutputReveal(string path)
    {
        var request = ExplorerShell.Request(path, true);
        Assert.Equal("explorer.exe", request.FileName);
        Assert.Equal($"/select,\"{Path.GetFullPath(path)}\"", request.Arguments);
        Assert.Equal(Path.GetFullPath(path), Assert.Single(ExplorerShell.Request(path, false).ArgumentList));
        Assert.DoesNotContain("cmd.exe", request.FileName);
    }

    [Fact]
    public void ShellContractRejectsQuotesAndPreservesLongPaths()
    {
        Assert.Throws<ArgumentException>(() => ExplorerShell.Request("C:\\clip\" ,/root,C:\\", true));
        var longPath = @"C:\" + string.Join(@"\", Enumerable.Repeat(new string('a', 60), 5)) + @"\clip.mp4";
        Assert.Equal($"/select,\"{longPath}\"", ExplorerShell.Request(longPath, true).Arguments);
    }

    [Fact]
    public async Task LogicalPathCannotEscapeItsMediaRoot()
    {
        var target = new ExplorerTarget(_root, "../outside.mp4", null, true);
        Assert.False(await Service().CanOpenAsync(target));
        await Assert.ThrowsAsync<ArgumentException>(() => Service().OpenAsync(target));
        Assert.Empty(_launches);
    }
    [Fact]
    public void BrowserWiringCapturesContextAndDoesNotNavigateOrChangeSelection()
    {
        var root = FindRepository();
        var source = File.ReadAllText(Path.Combine(root, "LightflowStudio", "MainWindow.xaml.cs"));
        var media = source[source.IndexOf("private void BrowserGridTile_ContextMenuOpening")..source.IndexOf("private void ResetBrowserAssetGesture")];
        Assert.Contains("DataContext as BrowserGridTile", media);
        Assert.Contains("ExplorerTarget.Media(contextTile)", media);
        var folder = source[source.IndexOf("private void BrowserFolderTree_ContextMenuOpening")..source.IndexOf("internal static BrowserTreeNode? LocationNodeFromElement")];
        Assert.Contains("e.CursorLeft < 0 ? _browserTree.SelectedNode : null", folder);
        Assert.Contains("ExplorerTarget.Folder(_locationActionNode)", folder);
        var actions = File.ReadAllText(Path.Combine(root, "LightflowStudio", "MainWindow.Explorer.cs"));
        foreach (var text in new[] { media, folder, actions })
            foreach (var forbidden in new[] { "SelectSingle(", "NavigateTo", "RunBrowserNavigation", "ScrollTo", "SelectedTilesInBrowserOrder" })
                Assert.DoesNotContain(forbidden, text);
        Assert.Contains("NoticeDialog.Show", actions);
        Assert.Contains("AppendLog", actions);
        Assert.Contains("ReferenceEquals(current.Item1, evaluation)", actions);
    }

    private BrowserGridTile Tile(string name)
    {
        var tile = new BrowserGridTile(new(_root, name, name.ToUpperInvariant(), name, false,
            new(MediaTypeCategory.Video), 1, DateTimeOffset.UnixEpoch), 0);
        tile.SetAssetId(Guid.NewGuid());
        return tile;
    }

    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "LightflowStudio"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
