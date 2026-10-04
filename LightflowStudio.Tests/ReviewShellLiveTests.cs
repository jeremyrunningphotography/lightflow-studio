using Lightflow.Actions;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class ReviewShellLiveTests
{
    [Fact]
    public async Task RealShellThumbnailAndPanelActionsPreserveScopeAndRejectUnavailableSurfaces()
    {
        await StaDispatcher.RunAsync(async () => {
            TestWpfApplication.EnsureLoaded();
            var data = Path.Combine(Path.GetTempPath(), "lightflow-352-shell-" + Guid.NewGuid().ToString("N"));
            var startup = await LightflowStorageCoordinator.StartAsync(data); var storage = startup.Coordinator!;
            storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
            var window = new MainWindow(storage, startup.Status, startup.Diagnostic) { ShowActivated = false, Left = -32000, Opacity = 0 };
            try {
                window.Show(); Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)));
                var target = window.ShellActionTarget;
                var grid = (BrowserGridModel)typeof(MainWindow).GetField("_browserGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var selection = grid.SelectedAssetIdsInBrowserOrder.ToArray(); var query = grid.Query;
                var source = new ActionInputSource("direct-shell-controller", ActionInputKind.Controller);
                async Task<ActionResult> Call(string id, ActionArguments arguments) => await window.ShellActions.InvokeAsync(new(id, arguments, source, Guid.NewGuid(), window.ShellActionTarget));
                for (var i = 0; i < 10; i++) await Call(ReviewShellActions.ThumbnailSize, new LevelArguments(1));
                Assert.Equal(BrowserGridLayout.ThumbnailSizes.Count - 1, window.BrowserThumbnailSizeSlider.Value);
                Assert.Equal(ActionOutcome.NoChange, (await Call(ReviewShellActions.ThumbnailSize, new LevelArguments(1))).Outcome);
                for (var i = 0; i < 10; i++) await Call(ReviewShellActions.ThumbnailSize, new LevelArguments(-1));
                Assert.Equal(0, window.BrowserThumbnailSizeSlider.Value); Assert.Equal(selection, grid.SelectedAssetIdsInBrowserOrder); Assert.Equal(query, grid.Query);
                Assert.Equal(target, window.ShellActionTarget);
                var panelBeforeRepeat = window.HomeRightPanel.Visibility;
                Assert.Equal(ActionOutcome.NoChange, (await window.DispatchShellActionAsync(ReviewShellActions.TogglePanel, NoActionArguments.Instance,
                    new ActionInputSource("keyboard-repeat", ActionInputKind.Keyboard), repeat: true)).Outcome);
                Assert.Equal(panelBeforeRepeat, window.HomeRightPanel.Visibility);
                await Call(ReviewShellActions.ShowPanel, new PanelSurfaceArguments(ReviewPanelSurface.Inspector)); Assert.Equal(Visibility.Visible, window.HomeRightPanel.Visibility);
                Assert.Equal(ActionOutcome.NoChange, (await Call(ReviewShellActions.ShowPanel, new PanelSurfaceArguments(ReviewPanelSurface.Inspector))).Outcome);
                Assert.Equal(ActionUnavailableReason.SurfaceUnavailable, (await Call(ReviewShellActions.ShowPanel, new PanelSurfaceArguments(ReviewPanelSurface.Subclips))).Reason);
                await Call(ReviewShellActions.TogglePanel, NoActionArguments.Instance); Assert.Equal(Visibility.Collapsed, window.HomeRightPanel.Visibility);
                Assert.Equal(ActionUnavailableReason.ExportUnavailable, (await Call(ReviewShellActions.Export, new ExportEntryArguments(ExportEntry.BrowserVideos))).Reason);
                var stale = new ReviewShellInvocation(ReviewShellActions.TogglePanel, NoActionArguments.Instance, source, Guid.NewGuid(), target);
                window.ApplyBrowserLayout(BrowserLayoutMode.Details, false);
                Assert.Equal(ActionOutcome.Superseded, (await window.ShellActions.InvokeAsync(stale)).Outcome);
                Assert.Equal(ActionUnavailableReason.InactivePresentation, (await Call(ReviewShellActions.ThumbnailSize, new LevelArguments(1))).Reason);
                Assert.Null(window.CheckExportPresentationAdmission(() => true, CancellationToken.None));
                Assert.Equal(ActionOutcome.Superseded, window.CheckExportPresentationAdmission(() => false, CancellationToken.None)!.Outcome);
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                Assert.Equal(ActionOutcome.Cancelled, window.CheckExportPresentationAdmission(() => true, cancelled.Token)!.Outcome);
                System.Windows.Interop.ComponentDispatcher.PushModal();
                try {
                    Assert.Equal(ActionUnavailableReason.ModalInteraction, (await Call(ReviewShellActions.TogglePanel, NoActionArguments.Instance)).Reason);
                    Assert.Equal(ActionUnavailableReason.ModalInteraction, window.CheckExportPresentationAdmission(() => true, CancellationToken.None)!.Reason);
                }
                finally { System.Windows.Interop.ComponentDispatcher.PopModal(); }
            } finally {
                window.Close();
                Assert.Equal(ActionOutcome.Superseded, window.CheckExportPresentationAdmission(() => true, CancellationToken.None)!.Outcome);
                Assert.False(window.ShellActions.Eligibility(ReviewShellActions.TogglePanel, window.ShellActionTarget, NoActionArguments.Instance).Available);
                await storage.DisposeAsync();
                try { Directory.Delete(data, true); } catch (IOException) { }
            }
        });
    }
}
