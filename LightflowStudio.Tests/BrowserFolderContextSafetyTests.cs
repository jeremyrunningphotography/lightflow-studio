using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class BrowserFolderContextSafetyTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, Hidden)!.Invoke(owner, args);
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Hidden)!.GetValue(owner)!;

    [Theory]
    [InlineData("Copy")]
    [InlineData("Keyboard Copy")]
    [InlineData("Delete Confirm")]
    [InlineData("Stale During Rename")]
    [InlineData("ReadOnly New Folder")]
    [InlineData("Cut")]
    [InlineData("Rename…")]
    [InlineData("New Folder…")]
    [InlineData("Paste into folder")]
    [InlineData("Delete")]
    [InlineData("Cancel Rename")]
    [InlineData("Unavailable")]
    public Task MenuCommandsKeepSelectionAndUseInvocationFolder(string command) => StaDispatcher.RunAsync(async () =>
    {
        TestWpfApplication.EnsureLoaded();
        Clipboard.Clear();
        var directory = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "folder-context-tests", Guid.NewGuid().ToString("N"))).FullName;
        var media = Directory.CreateDirectory(Path.Combine(directory, "media")).FullName;
        var a = Directory.CreateDirectory(Path.Combine(media, "A")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(media, "B")).FullName;
        var startup = await LightflowStorageCoordinator.StartAsync(Path.Combine(directory, "data"));
        var storage = startup.Coordinator!;
        await storage.MediaMonitoring!.DisposeAsync();
        storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
        var root = (await storage.MediaRoots.CreateAsync("Fixture", media)).Root!;
        File.WriteAllText(Path.Combine(b, "clip.mov"), "disposable fixture");
        var asset = (await storage.MediaAssets.CreateAsync(root.RootId, "B/clip.mov", "video")).Asset!.Asset;
        await storage.AssetClassifications.SaveAsync(new(asset.AssetId, 4, AssetFlag.Picked, AssetColorLabel.Blue, ["fixture"], 0));
        var window = new MainWindow(storage, startup.Status, startup.Diagnostic)
        { Left = -32000, Top = -32000, ShowInTaskbar = false, Width = 1440, Height = 900 };
        DirectorySecurity? originalAcl = null;
        try
        {
            // OleFlushClipboard cannot persist delayed data on the private desktop.
            // Keep the actual WPF clipboard/formats, with process-lifetime ownership.
            DataObject? writtenClipboard = null;
            window.FolderClipboardWriter = data => { writtenClipboard = data; Clipboard.SetDataObject(data, false); };
            window.Show();
            Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)));
            window.BrowserCurrentPath.Text = a;
            await (Task)Call(window, "NavigateToEnteredBrowserPathAsync")!;
            var tree = Field<BrowserTreeModel>(window, "_browserTree");
            var selected = tree.SelectedNode!;
            var node = tree.FindByPath(b)!;
            Assert.NotNull(node);
            Assert.Equal(a, selected.AbsolutePath);
            var location = Field<BrowserNavigationSession>(window, "_browserNavigation").ActiveLocation;
            var row = new TreeViewItem { DataContext = node, Header = "B" };
            // Use real routed preview input: WPF's item right-button handler must not select B.
            window.BrowserFolderTree.ItemsSource = null;
            window.BrowserFolderTree.Items.Clear();
            window.BrowserFolderTree.Items.Add(row);
            var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
            { RoutedEvent = Mouse.PreviewMouseDownEvent };
            row.RaiseEvent(press);
            Assert.True(press.Handled);
            Assert.Same(selected, tree.SelectedNode);
            Assert.Same(location, Field<BrowserNavigationSession>(window, "_browserNavigation").ActiveLocation);
            if (command == "Keyboard Copy")
            {
                var focused = new TreeViewItem { DataContext = selected, Header = "A" };
                window.BrowserFolderTree.Items.Add(focused);
                window.UpdateLayout();
                window.Activate();
                Keyboard.Focus(focused);
                Assert.True(focused.IsKeyboardFocusWithin);
            }
            var args = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs), Hidden, null,
                [row, true, command == "Keyboard Copy" ? -1d : 10d, command == "Keyboard Copy" ? -1d : 10d], null)!;
            Call(window, "BrowserFolderTree_ContextMenuOpening", window.BrowserFolderTree, args);
            Assert.Same(command == "Keyboard Copy" ? selected : node, Field<BrowserFolderContextTarget>(window, "_folderContextTarget").Node);
            Assert.Equal(a, ((FileOperationSource)Call(window, "SelectedFolderOperationSource")!).Path);
            Assert.Equal(a, Call(window, "CurrentBrowserFolder"));
            // Later selection notifications cannot redirect the invocation to A.
            tree.RequestSelection(node);
            tree.RequestSelection(selected);
            Assert.Same(selected, tree.SelectedNode);

            var menuCommand = command switch { "Cancel Rename" or "Stale During Rename" => "Rename…", "Unavailable" or "Keyboard Copy" => "Copy", "Delete Confirm" => "Delete", "ReadOnly New Folder" => "New Folder…", _ => command };
            var item = window.BrowserFolderTree.ContextMenu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, menuCommand));
            Assert.True(item.IsEnabled);
            if (command == "Paste into folder")
            {
                var copy = Path.Combine(directory, "incoming.txt");
                File.WriteAllText(copy, "fixture");
                var data = new DataObject(DataFormats.FileDrop, new[] { copy });
                Clipboard.SetDataObject(data);
            }
            if (command == "Unavailable") Directory.Move(b, b + "-gone");
            if (command == "ReadOnly New Folder")
            {
                var info = new DirectoryInfo(b);
                originalAcl = info.GetAccessControl();
                var denied = info.GetAccessControl();
                denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateDirectories, AccessControlType.Deny));
                info.SetAccessControl(denied);
            }
            var seenDialog = false;
            Exception? dialogFailure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += (_, _) =>
            {
                foreach (var dialog in Application.Current.Windows.Cast<Window>().Where(x => x != window && x.IsVisible).ToArray())
                {
                    seenDialog = true;
                    try
                    {
                        if (dialog is TextEntryDialog entry)
                        {
                            if (command is not ("New Folder…" or "ReadOnly New Folder")) Assert.Equal("B", entry.Value);
                            entry.ValueText.Text = "Changed";
                            if (command == "Stale During Rename") Directory.Move(b, b + "-gone");
                            entry.DialogResult = command != "Cancel Rename";
                        }
                        else if (dialog is ConfirmationDialog confirmation)
                        {
                            Assert.Contains(b, confirmation.MessageText.Text);
                            Assert.DoesNotContain(a, confirmation.MessageText.Text);
                            // Cancelling must leave both filesystem and Catalog unchanged.
                            confirmation.DialogResult = command == "Delete Confirm";
                        }
                        else { Assert.Contains(command, new[] { "Unavailable", "Stale During Rename", "ReadOnly New Folder" }); dialog.Close(); }
                    }
                    catch (Exception ex) { dialogFailure = ex; dialog.Close(); }
                }
            };
            timer.Start();
            try
            {
                window.BrowserFolderTree.ContextMenu.IsOpen = true;
                ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(25);
                    if (command is "Copy" or "Cut" or "Keyboard Copy" && Clipboard.ContainsFileDropList()) break;
                    if (command == "Delete Confirm" && !Directory.Exists(b)) break;
                    if (command == "ReadOnly New Folder" && seenDialog && !Application.Current.Windows.Cast<Window>().Any(x => x != window && x.IsVisible)) break;
                    if (command == "Stale During Rename" && seenDialog && !Application.Current.Windows.Cast<Window>().Any(x => x != window && x.IsVisible)) break;
                    if (command == "Rename…" && Directory.Exists(Path.Combine(media, "Changed"))) break;
                    if (command == "New Folder…" && Directory.Exists(Path.Combine(b, "Changed"))) break;
                    if (command == "Paste into folder" && File.Exists(Path.Combine(b, "incoming.txt"))) break;
                    if (command is "Cancel Rename" or "Delete" or "Unavailable" && seenDialog) break;
                }
                Assert.Null(dialogFailure);
                Assert.True(Directory.Exists(a));
                if (command is "Copy" or "Cut" or "Keyboard Copy")
                {
                    Assert.Equal(new[] { command == "Keyboard Copy" ? a : b }, Clipboard.GetFileDropList().Cast<string>());
                    Assert.Equal(command == "Cut" ? "Move" : "Copy", writtenClipboard!.GetData("LightflowStudio.FileOperation"));
                }
                if (command == "ReadOnly New Folder")
                {
                    Assert.True(seenDialog);
                    Assert.False(Directory.Exists(Path.Combine(b, "Changed")));
                    Assert.False(Directory.Exists(Path.Combine(a, "Changed")));
                }
                if (command == "Delete Confirm") Assert.False(Directory.Exists(b));
                if (command == "Stale During Rename") { Assert.False(Directory.Exists(Path.Combine(media, "Changed"))); Assert.True(Directory.Exists(b + "-gone")); }
                if (command == "Rename…") { Assert.False(Directory.Exists(b)); Assert.True(Directory.Exists(Path.Combine(media, "Changed"))); }
                if (command == "New Folder…") { Assert.True(Directory.Exists(Path.Combine(b, "Changed"))); Assert.False(Directory.Exists(Path.Combine(a, "Changed"))); }
                if (command == "Paste into folder") { Assert.True(File.Exists(Path.Combine(b, "incoming.txt"))); Assert.False(File.Exists(Path.Combine(a, "incoming.txt"))); }
                if (command is "Delete" or "Cancel Rename") { Assert.True(seenDialog); Assert.True(Directory.Exists(b)); }
                if (command == "Unavailable") { Assert.True(seenDialog); Assert.False(Clipboard.ContainsFileDropList()); }
                var persisted = (await storage.MediaAssets.GetAsync(asset.AssetId))!.Asset;
                Assert.Equal(asset.AssetId, persisted.AssetId);
                Assert.Equal(command == "Rename…" ? "Changed/clip.mov" : "B/clip.mov", persisted.RelativePath);
                var state = (await storage.BrowserAssetStates.GetQueryStatesAsync([asset.AssetId]))[asset.AssetId];
                Assert.Equal(4, state.Classification!.Rating);
                Assert.Contains("fixture", state.Classification.Keywords);
                Assert.Equal(a, tree.SelectedNode!.AbsolutePath);
                Assert.Equal(a, Field<BrowserNavigationSession>(window, "_browserNavigation").ActiveLocation!.AbsolutePath);
                Call(window, "BrowserFolderContextMenu_Closed", window.BrowserFolderTree.ContextMenu, new RoutedEventArgs());
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Null(typeof(MainWindow).GetField("_folderContextTarget", Hidden)!.GetValue(window));
            }
            finally { timer.Stop(); Clipboard.Clear(); }
        }
        finally { if (originalAcl is not null) new DirectoryInfo(b).SetAccessControl(originalAcl); window.Close(); await storage.DisposeAsync(); }
    });

    [Fact]
    public void SnapshotRejectsReplacementRemovedNodeVirtualAndOfflineTargets()
    {
        var path = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "folder-snapshot-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var node = new BrowserTreeNode("B", path);
            var target = BrowserFolderContextTarget.Capture(node)!;
            Assert.True(target.IsCurrent([node]));
            Assert.False(target.IsCurrent([new BrowserTreeNode("A", path)]));
            Assert.False(target.IsCurrent([]));
            Directory.Move(path, path + "-old");
            Directory.CreateDirectory(path);
            Assert.False(target.IsCurrent([node]));
            Directory.Delete(path + "-old");
            Assert.Null(BrowserFolderContextTarget.Capture(new("Virtual", null)));
            Assert.Null(BrowserFolderContextTarget.Capture(new("Placeholder", path, placeholder: true)));
            Assert.Null(BrowserFolderContextTarget.Capture(new("Offline", path,
                new("offline", "Offline", path, BrowserStorageKind.ManagedRoot, MediaRootAvailability.Unavailable, Guid.NewGuid()))));
            var root = BrowserFolderContextTarget.Capture(new("Drive", Path.GetPathRoot(path)))!;
            Assert.False(root.CanChangeEntry);
        }
        finally { Directory.Delete(path, true); }
    }
}

