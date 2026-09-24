using System.Text.Json;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class BrowserAspectRatioTests
{
    [Fact]
    public async Task RealProbeAndDecodedPresentationAgreeForLandscapePortraitAndRotatedVideo()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Directory.Build.props"))) repository = repository.Parent;
        var bin = Path.Combine(repository!.FullName, "artifacts", "ffmpeg", "bin");
        var root = Path.Combine(repository.FullName, ".task-notes", "aspect-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var runner = new ProbeProcessRunner();
        async Task<string> Run(string executable, params string[] args)
        {
            var result = await runner.RunAsync(Path.Combine(bin, executable), args);
            Assert.True(result.ExitCode == 0, result.StandardError);
            return result.StandardOutput;
        }
        foreach (var (width, height) in new[] { (320, 180), (180, 320) })
        {
            var original = Path.Combine(root, $"{width}x{height}.mp4");
            await Run("ffmpeg.exe", "-v", "error", "-y", "-f", "lavfi", "-i", $"color=size={width}x{height}:rate=25:duration=0.2",
                "-c:v", "libopenh264", "-pix_fmt", "yuv420p", original);
            foreach (var rotation in new[] { 0, 90, 180, 270 })
            {
                var source = Path.Combine(root, $"{width}x{height}-{rotation}.mp4");
                await Run("ffmpeg.exe", "-v", "error", "-y", "-display_rotation", rotation.ToString(), "-i", original, "-c", "copy", source);
                var raw = await Run("ffprobe.exe", "-v", "error", "-show_streams", "-show_format", "-of", "json", source);
                var metadata = FfprobeMetadataNormalizer.Normalize(raw, new FileInfo(source).Length).Metadata!;
                var png = source + ".png";
                await Run("ffmpeg.exe", "-v", "error", "-y", "-i", source, "-frames:v", "1", png);
                using var stream = File.OpenRead(png);
                var decoded = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
                Assert.Equal(new MediaAspectRatio(decoded.PixelWidth, decoded.PixelHeight), metadata.Video!.SourceDisplayAspectRatio);
                var tile = new BrowserGridTile(Entry(Guid.NewGuid()), 0);
                tile.ApplyMetadata(BrowserQueryEngine.ExtractMetadata(JsonSerializer.Serialize(metadata, DerivedMetadataJson.Options)));
                foreach (var adjustment in new[] { 0, 90, 180, 270 })
                {
                    tile.ApplyVideoRotation(new(tile.AssetId!.Value, new(adjustment), adjustment));
                    var display = new VideoRotation(adjustment).Dimensions(decoded.PixelWidth, decoded.PixelHeight);
                    Assert.True(BrowserFilterPredicate.ForAspectRatio(display.Width, display.Height).Matches(tile));
                }
            }
        }
    }

    [Theory]
    [InlineData(1920, 1080, 0, 0, 16, 9)]
    [InlineData(1080, 1920, 0, 0, 9, 16)]
    [InlineData(1920, 1080, 90, 0, 9, 16)]
    [InlineData(1920, 1080, -90, 0, 9, 16)]
    [InlineData(1920, 1080, 270, 0, 9, 16)]
    [InlineData(1920, 1080, 180, 0, 16, 9)]
    [InlineData(1920, 1080, 90, 90, 16, 9)]
    [InlineData(1920, 1080, 0, 270, 9, 16)]
    [InlineData(2520, 1080, 0, 0, 21, 9)]
    public void SourceAndAuthoredOrientationDetermineMembership(int width, int height, int source, int authored, int n, int d)
    {
        var tile = Video(width, height, source);
        var filter = BrowserFilterPredicate.ForAspectRatio(n, d);
        Assert.False(filter.Matches(tile)); // Authored state must arrive before a video can match.
        tile.ApplyVideoRotation(new(tile.AssetId!.Value, new(authored), 1));
        Assert.True(filter.Matches(tile));
        Assert.True(BrowserFilterPredicate.ForResolution(width, height).Matches(tile));
        var saved = BrowserQueryIntent.Deserialize(new BrowserQueryIntent { Filters = [filter] }.Serialize());
        Assert.Equal(filter, Assert.Single(saved.Filters));
        Assert.Single(BrowserQueryEngine.Filter([tile], saved.ToQuery()));
    }

    [Fact]
    public void RotationChangesReevaluateMembershipAndIgnoreStaleReads()
    {
        var tile = Video(1920, 1080, 0);
        var id = tile.AssetId!.Value;
        var grid = new BrowserGridModel();
        grid.Populate([Entry(id)]);
        grid.ApplyMetadata(id, new(null, null, null, null, null, 1920, 1080, null, new(16, 9)));
        grid.SetDefiningQuery(new() { Filters = [BrowserFilterPredicate.ForAspectRatio(9, 16)] });
        Assert.Empty(grid.Tiles);
        grid.ApplyVideoRotations([new(id, new(90), 2)]); grid.ReapplyQuery(); Assert.Single(grid.Tiles);
        grid.ApplyVideoRotations([new(id, new(0), 1)]); grid.ReapplyQuery(); Assert.Single(grid.Tiles);
        grid.ApplyVideoRotations([new(id, new(180), 3)]); grid.ReapplyQuery(); Assert.Empty(grid.Tiles);
        grid.InvalidateVideoRotations();
        grid.ApplyVideoRotations([new(id, new(90), 1)]); grid.ReapplyQuery(); Assert.Single(grid.Tiles);
    }

    [Theory]
    [InlineData(1, 3, 2)] [InlineData(2, 3, 2)] [InlineData(3, 3, 2)] [InlineData(4, 3, 2)]
    [InlineData(5, 2, 3)] [InlineData(6, 2, 3)] [InlineData(7, 2, 3)] [InlineData(8, 2, 3)]
    public void StillOrientationUsesTheRenderingTransform(int orientation, int n, int d)
    {
        var metadata = new DerivedMediaMetadata(DerivedMediaKind.Image, "jpg", null, null, 1, null, null, null,
            new("jpg", 600, 400, 8, orientation, null, null, null, null));
        var projection = BrowserQueryEngine.ExtractMetadata(JsonSerializer.Serialize(metadata, DerivedMetadataJson.Options));
        Assert.Equal(new MediaAspectRatio(n, d), projection.SourceDisplayAspectRatio);
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(600, 400, 96, 96,
            System.Windows.Media.PixelFormats.Gray8, null, new byte[600 * 400], 600);
        var rendered = WicImageThumbnailRenderer.ApplyOrientation(bitmap, orientation);
        Assert.Equal(new MediaAspectRatio(rendered.PixelWidth, rendered.PixelHeight), projection.SourceDisplayAspectRatio);
    }

    [Theory]
    [InlineData("\"sample_aspect_ratio\":\"4:3\"")]
    [InlineData("\"display_aspect_ratio\":\"4:3\"")]
    [InlineData("\"side_data_list\":[{\"rotation\":45}]")]
    public void AmbiguousGeometryIsUnknown(string additional)
    {
        var result = FfprobeMetadataNormalizer.Normalize("{\"streams\":[{\"codec_type\":\"video\",\"width\":1920,\"height\":1080," + additional + "}]}", 1);
        Assert.True(result.Metadata is not null);
        Assert.Null(result.Metadata.Video!.SourceDisplayAspectRatio);
    }

    [Fact]
    public void LegacyRawProbeProjectionAndPresetsAreStable()
    {
        var raw = Probe(1920, 1080, 90);
        var current = FfprobeMetadataNormalizer.Normalize(raw, 1).Metadata!;
        var legacy = current with { Video = current.Video! with { SourceDisplayAspectRatio = null } };
        var json = JsonSerializer.Serialize(legacy, DerivedMetadataJson.Options);
        Assert.Null(BrowserQueryEngine.ExtractMetadata(json).SourceDisplayAspectRatio);
        Assert.Equal(new MediaAspectRatio(9, 16), BrowserQueryEngine.ExtractMetadata(json, raw).SourceDisplayAspectRatio);
        var presets = BrowserFilterDescriptors.Values(BrowserFilterField.AspectRatio, []).ToArray();
        Assert.Equal(new[] { "16:9", "9:16", "4:3", "3:2", "1:1", "21:9" }, presets.Select(BrowserFilterDescriptors.ValueLabel));
        Assert.Equal(BrowserFilterPredicate.ForAspectRatio(21, 9), BrowserFilterPredicate.ForAspectRatio(7, 3));
        Assert.NotEqual(new MediaAspectRatio(21, 9), new MediaAspectRatio(2560, 1080));
        Assert.Throws<ArgumentException>(() => new BrowserQueryIntent { Filters = [new() { Field = BrowserFilterField.AspectRatio }] }.Validate());
    }

    private static BrowserGridTile Video(int width, int height, int rotation)
    {
        var tile = new BrowserGridTile(Entry(Guid.NewGuid()), 0);
        var metadata = FfprobeMetadataNormalizer.Normalize(Probe(width, height, rotation), 1).Metadata;
        tile.ApplyMetadata(BrowserQueryEngine.ExtractMetadata(JsonSerializer.Serialize(metadata, DerivedMetadataJson.Options)));
        return tile;
    }
    internal static string Probe(int width, int height, int rotation) => $$"""
        {"streams":[{"codec_type":"video","width":{{width}},"height":{{height}},"sample_aspect_ratio":"1:1",
        "side_data_list":[{"side_data_type":"Display Matrix","rotation":{{rotation}}}]}]}
        """;
    private static MediaFolderEntry Entry(Guid id) => new(Guid.NewGuid(), "test.mp4", "TEST.MP4", "test.mp4", false,
        new(MediaTypeCategory.Video), 1, DateTimeOffset.UtcNow, AssetId: id);
}
