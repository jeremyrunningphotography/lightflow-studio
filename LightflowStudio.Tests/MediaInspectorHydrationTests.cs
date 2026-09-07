using System.Windows;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("STA dispatcher tests")]
public sealed class MediaInspectorHydrationTests
{
    private sealed class DelayedCatalog : IAssetClassificationStore
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        internal int Calls => _calls;
        public async Task<IReadOnlyDictionary<Guid, AssetClassification>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Entered.TrySetResult();
                await Release.Task; // Deliberately ignore cancellation: late services must still never publish stale context.
            }
            return ids.ToDictionary(id => id, AssetClassification.Empty);
        }
        public Task SaveAsync(AssetClassification value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public Task RepeatedDerivedNotifications_DoNotStarveAnUnchangedSelection() => StaDispatcher.RunAsync(async () =>
    {
        TestWpfApplication.EnsureLoaded();
        var catalog = new DelayedCatalog();
        using var view = new MediaInspectorView();
        view.Initialize(() => new MediaInspectorService(null, catalog, Path.GetTempPath()));
        var window = new Window { Content = view, Width = 380, Height = 720, Left = -32000, Top = -32000,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        try
        {
            window.Show();
            InspectorAsset[] selection = [new(Guid.NewGuid(), "photo.jpg", "photo.jpg", MediaPresentationKind.Image)];
            view.SetContext(selection, false);
            await catalog.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i = 0; i < 5; i++)
            {
                view.SetContext(selection, false, force: true);
                await Task.Delay(120);
            }
            Assert.Equal(1, catalog.Calls);
            catalog.Release.TrySetResult();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (view.FieldGroups.ItemsSource is null && DateTime.UtcNow < deadline) await Task.Delay(25);
            Assert.NotNull(view.FieldGroups.ItemsSource);
            Assert.Equal("photo.jpg", view.TitleText.Text);
        }
        finally { catalog.Release.TrySetResult(); window.Close(); }
    });

    [Fact]
    public Task LateHydration_CannotOverwriteNewSelection_PlayerTransition_OrClosedPanel() => StaDispatcher.RunAsync(async () =>
    {
        TestWpfApplication.EnsureLoaded();
        var catalog = new DelayedCatalog();
        using var view = new MediaInspectorView();
        view.Initialize(() => new MediaInspectorService(null, catalog, Path.GetTempPath()));
        var window = new Window { Content = view, Width = 380, Height = 720, Left = -32000, Top = -32000,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false };
        try
        {
            window.Show();
            view.SetContext([new(Guid.NewGuid(), "old.jpg", "old.jpg", MediaPresentationKind.Image)], false);
            await catalog.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            view.SetContext([new(Guid.NewGuid(), "new.mp4", "new.mp4", MediaPresentationKind.Video)], true);
            await view.RefreshAsync();
            Assert.Equal("new.mp4", view.TitleText.Text);
            Assert.Equal("PLAYER", view.ContextText.Text);
            catalog.Release.TrySetResult();
            await Task.Delay(150);
            Assert.Equal("new.mp4", view.TitleText.Text);
            Assert.Equal("Metadata pending", view.StatusText.Text);
            view.SetContext([], false);
            Assert.Contains("Select media", view.TitleText.Text);
            Assert.Null(view.FieldGroups.ItemsSource);
            view.Visibility = Visibility.Collapsed;
            view.SetContext([new(Guid.NewGuid(), "hidden.jpg", "hidden.jpg", MediaPresentationKind.Image)], false);
            Assert.Null(view.FieldGroups.ItemsSource);
            view.Visibility = Visibility.Visible;
            await view.RefreshAsync();
            Assert.Equal("hidden.jpg", view.TitleText.Text);
            Assert.NotNull(view.FieldGroups.ItemsSource);
        }
        finally { catalog.Release.TrySetResult(); window.Close(); }
    });
}
