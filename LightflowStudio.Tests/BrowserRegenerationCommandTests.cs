using System.Collections.Concurrent;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class BrowserRegenerationCommandTests
{
    [Fact]
    public Task InvocationImmediatelyRegeneratesScopeOrSelectionWithoutConfirmation() => StaDispatcher.RunAsync(async () =>
    {
        TestWpfApplication.EnsureLoaded();
        var directory = Directory.CreateTempSubdirectory("lightflow-regenerate-command-").FullName;
        var startup = await LightflowStorageCoordinator.StartAsync(Path.Combine(directory, "profile"));
        var storage = startup.Coordinator!;
        storage.SaveSettings(storage.Settings with { BackupCatalogOnClose = false });
        var media = Directory.CreateDirectory(Path.Combine(directory, "media")).FullName;
        var root = (await storage.MediaRoots.CreateAsync("Regeneration command", media)).Root!;
        var entries = new List<MediaFolderEntry>();
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(8, 6, 96, 96,
            PixelFormats.Bgr24, null, new byte[8 * 6 * 3], 24)));
        using var encoded = new MemoryStream();
        encoder.Save(encoded);
        var jpeg = encoded.ToArray();
        for (var index = 0; index < 77; index++)
        {
            var name = $"image-{index:D2}.jpg";
            await File.WriteAllBytesAsync(Path.Combine(media, name), jpeg);
            var asset = (await storage.MediaAssets.CreateAsync(root.RootId, name, "image")).Asset!.Asset;
            entries.Add(new(root.RootId, name, name.ToUpperInvariant(), name, false,
                new(MediaTypeCategory.StillImage), asset.FileSizeBytes, DateTimeOffset.UtcNow, AssetId: asset.AssetId));
        }
        var window = new MainWindow(storage, startup.Status, startup.Diagnostic)
        { Left = -32000, Top = -32000, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            window.Show();
            Assert.True(await window.StartupCompletion.WaitAsync(TimeSpan.FromSeconds(30)));
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var grid = (BrowserGridModel)typeof(MainWindow).GetField("_browserGrid", flags)!.GetValue(window)!;
            grid.Populate(entries);
            var command = typeof(MainWindow).GetMethod("RegenerateBrowserThumbnailsAsync", flags)!;
            foreach (var selectedCount in new[] { 0, 1, 2 })
            {
                grid.ClearSelection();
                if (selectedCount > 0) grid.SelectSingle(0);
                if (selectedCount > 1) grid.ToggleCtrl(1);
                var expected = selectedCount == 0 ? grid.ThumbnailApplicableAssetIdsInScope : grid.SelectedAssetIdsInBrowserOrder;
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var generated = new ConcurrentBag<Guid>();
                void Activity(object? sender, ThumbnailGenerationActivityChanged change)
                {
                    if (!change.IsGenerating) return;
                    generated.Add(change.AssetId);
                    started.TrySetResult();
                }
                storage.ThumbnailActivity.Changed += Activity;
                try
                {
                    // Invoke the shared toolbar/context-menu command itself, with no dialog service or response.
                    var operation = (Task)command.Invoke(window, null)!;
                    await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    await operation.WaitAsync(TimeSpan.FromSeconds(30));
                    Assert.Equal(expected.Order(), generated.Order());
                    Assert.Equal($"Regenerated {expected.Count} Preview{(expected.Count == 1 ? "" : "s")}", window.BrowserStatusText.Text);
                    Assert.True(window.BrowserRegenerateThumbnailsButton.IsEnabled);
                    foreach (var id in expected)
                        Assert.Equal(PreviewComponentState.Current, (await storage.Previews!.GetAsync(id))!.ThumbnailState);
                }
                finally { storage.ThumbnailActivity.Changed -= Activity; }
            }
        }
        finally
        {
            window.Close();
            await storage.DisposeAsync();
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    });
}
