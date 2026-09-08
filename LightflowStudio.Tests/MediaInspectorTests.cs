using System.Text.Json;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class MediaInspectorTests
{
    [Fact]
    public void FolderAction_LaunchesExplorerWithOnlyTheContainingDirectory()
    {
        var resolution = new MediaPathResolution(Guid.NewGuid(), "shoot/clip.mp4", "shoot/clip.mp4",
            @"C:\Media library\shoot\clip.mp4", MediaRootAvailability.Online, true);
        var start = MainWindow.InspectorFolderStartInfo(resolution);
        Assert.Equal("explorer.exe", start.FileName);
        Assert.Equal(@"C:\Media library\shoot", Assert.Single(start.ArgumentList));
        Assert.True(start.UseShellExecute);
        Assert.Throws<DirectoryNotFoundException>(() => MainWindow.InspectorFolderStartInfo(
            resolution with { PhysicalPath = null, RootAvailability = MediaRootAvailability.Unavailable }));
    }

    private sealed class Classifications : IAssetClassificationStore
    {
        internal int LargestBatch;
        public Task<IReadOnlyDictionary<Guid, AssetClassification>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        {
            LargestBatch = Math.Max(LargestBatch, ids.Count);
            return Task.FromResult<IReadOnlyDictionary<Guid, AssetClassification>>(ids.ToDictionary(id => id, AssetClassification.Empty));
        }
        public Task SaveAsync(AssetClassification value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task MixedSelection_UsesPreviewMetadata_TruthfulAggregates_AndSeparateCatalogState()
    {
        var root = Path.Combine(Path.GetTempPath(), "inspector-" + Guid.NewGuid());
        try
        {
            await using var store = new PreviewStoreService(LightflowStorageLocations.Create(root));
            var image = Guid.NewGuid(); var video = Guid.NewGuid(); var pending = Guid.NewGuid();
            await store.ObserveSourceAsync(image, new(1024, 1, 1, "abc"));
            await store.ObserveSourceAsync(video, new(2048, 1, 1, "abc"));
            var photo = new DerivedMediaMetadata(DerivedMediaKind.Image, "JPEG", null, null, 1024, null, null, null,
                new("JPEG", 6000, 4000, 24, 1, "Canon", "R5", "RF 50mm", "2026:09:07 10:00:00"));
            var movie = new DerivedMediaMetadata(DerivedMediaKind.Video, "mov", 61.25, 0, 2048, null,
                new("h264", "High", 1920, 1080, 29.97, "yuv420p", 8, "bt709", "bt709", "bt709"), null, null);
            await store.SetMetadataAsync(image, new(1, PreviewComponentState.Current,
                PayloadJson: JsonSerializer.Serialize(photo, DerivedMetadataJson.Options), RawPayloadJson: "{\"cameraMake\":\"Canon\"}"));
            await store.SetMetadataAsync(video, new(1, PreviewComponentState.Current,
                PayloadJson: JsonSerializer.Serialize(movie, DerivedMetadataJson.Options), RawPayloadJson: "{\"streams\":[{\"codec_name\":\"h264\"}]}"));
            var reader = new MediaInspectorService(store, new Classifications(), root);
            var assets = new InspectorAsset[] { new(image, "photo.jpg", "photo.jpg", MediaPresentationKind.Image, 1024),
                new(video, "movie.mov", "movie.mov", MediaPresentationKind.Video, 2048),
                new(pending, "pending.mov", "pending.mov", MediaPresentationKind.Video) };
            var result = await reader.ReadAsync(assets, default);
            Assert.DoesNotContain(result.Fields, f => f.CanOpenFolder);
            Assert.Contains(result.Fields, f => f.Name == "Total size" && f.Value.Contains("2 of 3"));
            Assert.Contains(result.Fields, f => f.Name == "Video duration" && f.Value.Contains("00:01:01.25") && f.Value.Contains("1 of 2"));
            Assert.Contains(result.Fields, f => f.Name == "Media type" && f.Value == "Mixed values");
            Assert.Contains(result.Fields, f => f.Group == "Camera" && f.Value.Contains("2 missing"));
            Assert.Contains(result.Fields, f => f.Group == "Lightflow Catalog" && f.Name == "Rating" && f.Value.Contains("common"));
            var single = await reader.ReadAsync([assets[0]], default);
            Assert.Contains(single.Fields, f => f.Group == "Lens" && f.Value == "RF 50mm");
            Assert.Empty(single.Status);
            Assert.True(Assert.Single(single.Fields, f => f.Name == "Relative path").CanOpenFolder);
            Assert.Contains("Canon", (await store.GetAsync(image))!.RawMetadataJson);
            await store.SetSourceAvailabilityAsync(image, PreviewSourceAvailability.Missing);
            Assert.Contains("Source missing", (await reader.ReadAsync([assets[0]], default)).Status);
            await store.SetMetadataAsync(video, new(1, PreviewComponentState.Failed));
            Assert.Contains("failed", (await reader.ReadAsync([assets[1]], default)).Status);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LargeSelection_IsBatchedAndBounded_AndCancellationIsObserved()
    {
        var catalog = new Classifications();
        var service = new MediaInspectorService(null, catalog, Path.GetTempPath());
        var assets = Enumerable.Range(0, 10000).Select(i => new InspectorAsset(Guid.NewGuid(), $"{i}.jpg", $"{i}.jpg", MediaPresentationKind.Image, 100)).ToArray();
        var result = await service.ReadAsync(assets, default);
        Assert.InRange(catalog.LargestBatch, 1, MediaInspectorService.BatchSize);
        Assert.InRange(result.Fields.Count, 1, 40);

        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync(assets, canceled.Token));
    }

    [Fact]
    public void RoundedPresentation_DoesNotMakeDifferentSourceValuesCommon()
    {
        var summary = new MediaInspectorService.FieldSummary();
        summary.Add("29.97 fps", "29.97001");
        summary.Add("29.97 fps", "29.97002");
        Assert.Equal("Mixed values", summary.Describe(2));
        Assert.Equal("00:01:00", MediaInspectorService.Seconds(59.99995));
    }

    [Fact]
    public void Layout_ToleratesOldState_ClampsWidth_AndMergesWithoutLosingOtherPanels()
    {
        var service = new WorkspaceStateService("unused", new() { Layout = new() { FullJobsListPaneWidth = 450, BrowserLocationsPaneWidth = 300 } });
        service.SetRightPanel(420, true, "inspector");
        var normalized = WorkspaceState.Normalize(service.Current);
        Assert.Equal(420, normalized.Layout!.RightPanelWidth);
        Assert.True(normalized.Layout.RightPanelOpen);
        Assert.Equal("inspector", normalized.Layout.RightPanelActiveSurface);
        Assert.Equal(450, normalized.Layout.FullJobsListPaneWidth);
        Assert.Equal(300, normalized.Layout.BrowserLocationsPaneWidth);
        Assert.Equal(600, WorkspaceState.Normalize(new() { Layout = new() { RightPanelWidth = 900 } }).Layout!.RightPanelWidth);
        Assert.Null(WorkspaceState.Normalize(new() { Layout = new() { RightPanelWidth = double.NaN } }).Layout!.RightPanelWidth);
        Assert.False(new WorkspaceLayoutState().RightPanelOpen);
        Assert.Null(MediaInspectorService.ResolvePreviewPath(Path.GetTempPath(), "../outside.jpg"));
    }
}
