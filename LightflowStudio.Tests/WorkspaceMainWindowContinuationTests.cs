using System.Reflection;
using System.Windows;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class WorkspaceMainWindowContinuationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualStartupBoundary_HydratesQueryAndIndependentBranchesOrYieldsToInput(bool cancel)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lightflow-workspace-247-{Guid.NewGuid():N}");
        try
        {
            await StaDispatcher.RunAsync(async () =>
            {
                TestWpfApplication.EnsureLoaded();
                var media = Path.Combine(directory, "media");
                Directory.CreateDirectory(Path.Combine(media, "active"));
                Directory.CreateDirectory(Path.Combine(media, "archive", "old"));
                var startup = await LightflowStorageCoordinator.StartAsync(Path.Combine(directory, "app"));
                Assert.True(startup.IsReady, startup.Diagnostic);
                await using var storage = startup.Coordinator!;
                var root = (await storage.MediaRoots.CreateAsync("Library", media)).Root!;
                var workspace = new WorkspaceStateService(storage.Locations.WorkspaceStatePath);
                workspace.SetBrowserLocation(root.RootId, "active", Path.Combine(media, "active"));
                workspace.SetContinuation(new()
                {
                    Query = new() { SearchText = "remember me", SortMode = BrowserSortMode.ModifiedDate, SortDescending = true },
                    QueryLocked = true,
                    ExpandedFolders = [new() { RootId = root.RootId }, new() { RootId = root.RootId, RelativeFolder = "archive" }]
                });
                workspace.Save();
                var window = new MainWindow(storage, startup.Status, startup.Diagnostic);
                try
                {
                    // Call the same startup boundary without opening a desktop window or replaying mouse actions.
                    Assert.Equal("remember me", window.BrowserSearchBox.Text);
                    Assert.True(window.BrowserQueryLockButton.IsChecked);
                    await InvokeTask(window, "RefreshBrowserStorageAsync");
                    if (cancel)
                    {
                        Invoke(window, "WorkspaceUserInteraction");
                        window.BrowserSearchBox.Text = "new work";
                    }
                    await InvokeTask(window, "RestoreWorkspaceContinuationAsync");
                    if (cancel)
                    {
                        Assert.Equal("new work", window.BrowserSearchBox.Text);
                        Assert.Null(Field<BrowserTreeModel>(window, "_browserTree").SelectedNode);
                    }
                    else
                    {
                        var tree = Field<BrowserTreeModel>(window, "_browserTree");
                        Assert.Equal(Path.Combine(media, "active"), tree.SelectedNode!.AbsolutePath);
                        Assert.True(tree.FindByPath(Path.Combine(media, "archive"))!.IsExpanded);
                        Assert.NotNull(tree.FindByPath(Path.Combine(media, "archive", "old")));
                        Assert.False(tree.SelectedNode.IsExpanded);
                        Assert.Equal("remember me", Field<BrowserGridModel>(window, "_browserGrid").Query.SearchText);
                    }
                }
                finally { window.Close(); }
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static object? Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null);
    private static Task InvokeTask(object target, string method) => (Task)Invoke(target, method)!;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
}
