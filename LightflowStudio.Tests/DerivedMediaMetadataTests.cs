using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class DerivedMediaMetadataTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-metadata-").FullName;
    private const string VideoJson = """
        {
          "streams": [
            { "index": 0, "codec_type": "video", "codec_name": "hevc", "profile": "Main 10",
              "width": 3840, "height": 2160, "pix_fmt": "yuv420p10le", "bits_per_raw_sample": "10",
              "color_space": "bt2020nc", "color_transfer": "smpte2084", "color_primaries": "bt2020",
              "avg_frame_rate": "60000/1001", "start_time": "2.5", "duration": "12.25" },
            { "index": 1, "codec_type": "audio", "codec_name": "aac", "sample_rate": "48000",
              "channels": 2, "channel_layout": "stereo", "bit_rate": "192000" }
          ],
          "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "14.75",
            "start_time": "2.5", "size": "123456", "bit_rate": "8000000" }
        }
        """;

    [Fact]
    public void FfprobeNormalizer_ProducesProviderNeutralVideoAndAudioMetadata()
    {
        var result = FfprobeMetadataNormalizer.Normalize(VideoJson, 123456);

        Assert.Equal(DerivedMetadataStatus.Succeeded, result.Status);
        Assert.Equal(DerivedMediaKind.Video, result.Metadata!.Kind);
        Assert.Equal("mov", result.Metadata.Container);
        Assert.Equal(14.75, result.Metadata.DurationSeconds);
        Assert.Equal(2.5, result.Metadata.StartTimestampSeconds);
        Assert.Equal("hevc", result.Metadata.Video!.Codec);
        Assert.Equal(3840, result.Metadata.Video.Width);
        Assert.Equal(2160, result.Metadata.Video.Height);
        Assert.Equal(59.94, result.Metadata.Video.FrameRate!.Value, 2);
        Assert.Equal(10, result.Metadata.Video.BitDepth);
        Assert.Equal("smpte2084", result.Metadata.Video.ColorTransfer);
        Assert.Equal("aac", result.Metadata.Audio!.Codec);
        Assert.Equal(48000, result.Metadata.Audio.SampleRate);
        Assert.Equal(2, result.Metadata.Audio.Channels);
        Assert.Equal(VideoJson, result.RawMetadata);
    }

    [Fact]
    public void FfprobeNormalizer_ReportsMalformedAndUnsupportedOutputWithoutThrowing()
    {
        var malformed = FfprobeMetadataNormalizer.Normalize("not json", 10);
        var unsupported = FfprobeMetadataNormalizer.Normalize("""{"streams":[{"codec_type":"data"}]}""", 10);

        Assert.Equal(DerivedMetadataStatus.Malformed, malformed.Status);
        Assert.Equal("not json", malformed.RawMetadata);
        Assert.Equal(DerivedMetadataStatus.Unsupported, unsupported.Status);
    }

    [Fact]
    public void FfprobeNormalizer_HandlesAudioOnlyMedia()
    {
        var result = FfprobeMetadataNormalizer.Normalize("""
            {"streams":[{"codec_type":"audio","codec_name":"flac","sample_rate":"96000","channels":6,"channel_layout":"5.1"}],
             "format":{"format_name":"flac","duration":"45.5"}}
            """, 2048);

        Assert.Equal(DerivedMetadataStatus.Succeeded, result.Status);
        Assert.Equal(DerivedMediaKind.Audio, result.Metadata!.Kind);
        Assert.Null(result.Metadata.Video);
        Assert.Equal("flac", result.Metadata.Audio!.Codec);
        Assert.Equal(6, result.Metadata.Audio.Channels);
        Assert.Equal(96000, result.Metadata.Audio.SampleRate);
    }

    [Fact]
    public async Task WicReader_NormalizesRepresentativeImageAndExifMetadata()
    {
        var path = Path.Combine(_root, "photo.jpg");
        var pixels = new byte[] { 0, 20, 40, 60, 80, 100 };
        var bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgr24, null, pixels, 6);
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=271}", "JR Photo");
        metadata.SetQuery("/app1/ifd/{ushort=272}", "Lightflow Camera");
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);
        metadata.SetQuery("/app1/ifd/exif/{ushort=36867}", "2026:08:14 12:34:56");
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        await using (var stream = File.Create(path)) encoder.Save(stream);

        var result = await new WicImageMetadataReader().ReadAsync(path, new FileInfo(path).Length);

        Assert.Equal(DerivedMetadataStatus.Succeeded, result.Status);
        Assert.Equal(DerivedMediaKind.Image, result.Metadata!.Kind);
        Assert.Equal(2, result.Metadata.Image!.Width);
        Assert.Equal(1, result.Metadata.Image.Height);
        Assert.Equal("JR Photo", result.Metadata.Image.CameraMake);
        Assert.Equal("Lightflow Camera", result.Metadata.Image.CameraModel);
        Assert.Equal(6, result.Metadata.Image.Orientation);
        Assert.Equal("2026:08:14 12:34:56", result.Metadata.Image.CapturedAt);
        Assert.Contains("cameraMake", result.RawMetadata);
    }

    [Fact]
    public async Task Service_PersistsReopensAndReusesCurrentMetadata()
    {
        await using var fixture = await MetadataFixture.CreateAsync(_root, "clip.mp4", "video");
        var probe = new FakeProbe(SuccessMetadata());
        using (var service = new DerivedMediaMetadataService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, probe))
            Assert.Equal(DerivedMetadataStatus.Succeeded, (await service.ProbeAsync(fixture.AssetId)).Status);
        await fixture.ReopenAsync();
        using var reopened = new DerivedMediaMetadataService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, probe);

        var current = await reopened.ProbeAsync(fixture.AssetId);
        var persisted = await fixture.Coordinator.Previews!.GetAsync(fixture.AssetId);

        Assert.Equal(DerivedMetadataStatus.Current, current.Status);
        Assert.Equal(1, probe.CallCount);
        Assert.Equal(DerivedMediaMetadataService.CurrentProbeVersion, persisted!.MetadataProbeVersion);
        Assert.Equal(PreviewComponentState.Current, persisted.MetadataState);
        Assert.NotNull(persisted.RawMetadataJson);
    }

    [Fact]
    public async Task GeneratorVersionMismatch_ReprobesAndReplacesStaleVersion()
    {
        await using var fixture = await MetadataFixture.CreateAsync(_root, "clip.mp4", "video");
        var probe = new FakeProbe(SuccessMetadata());
        using var service = new DerivedMediaMetadataService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, probe);
        await service.ProbeAsync(fixture.AssetId);
        var previews = fixture.Coordinator.Previews!;
        var record = (await previews.GetAsync(fixture.AssetId))!;
        await previews.SetMetadataAsync(fixture.AssetId,
            new(99, PreviewComponentState.Current, PayloadJson: record.MetadataJson, RawPayloadJson: record.RawMetadataJson));

        var refreshed = await service.ProbeAsync(fixture.AssetId);
        var updated = await previews.GetAsync(fixture.AssetId);

        Assert.Equal(DerivedMetadataStatus.Succeeded, refreshed.Status);
        Assert.Equal(2, probe.CallCount);
        Assert.Equal(DerivedMediaMetadataService.CurrentProbeVersion, updated!.MetadataProbeVersion);
    }

    [Fact]
    public async Task ChangedSourceIdentity_MarksMetadataStaleAndReprobes()
    {
        await using var fixture = await MetadataFixture.CreateAsync(_root, "clip.mp4", "video");
        var probe = new FakeProbe(SuccessMetadata());
        using var service = new DerivedMediaMetadataService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, probe);
        await service.ProbeAsync(fixture.AssetId);
        await File.AppendAllTextAsync(MediaPathSemantics.ResolveContained(fixture.MediaRoot, "clip.mp4"), " changed");

        var refreshed = await service.ProbeAsync(fixture.AssetId);

        Assert.Equal(DerivedMetadataStatus.Succeeded, refreshed.Status);
        Assert.Equal(2, probe.CallCount);
        Assert.Equal(PreviewComponentState.Current,
            (await fixture.Coordinator.Previews!.GetAsync(fixture.AssetId))!.MetadataState);
    }

    [Fact]
    public async Task MalformedRefresh_RetainsExistingMetadataAndRecordsFailure()
    {
        await using var fixture = await MetadataFixture.CreateAsync(_root, "clip.mp4", "video");
        var successful = new FakeProbe(SuccessMetadata());
        using (var service = new DerivedMediaMetadataService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, successful))
            await service.ProbeAsync(fixture.AssetId);
        var before = (await fixture.Coordinator.Previews!.GetAsync(fixture.AssetId))!;
        var malformed = new FakeProbe(new(DerivedMetadataStatus.Malformed, RawMetadata: "bad output", Diagnostic: "Malformed media"));
        using var refresh = new DerivedMediaMetadataService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews, malformed);

        var result = await refresh.ProbeAsync(fixture.AssetId, forceRefresh: true);
        var after = await fixture.Coordinator.Previews.GetAsync(fixture.AssetId);

        Assert.Equal(DerivedMetadataStatus.Malformed, result.Status);
        Assert.Equal(PreviewComponentState.Failed, after!.MetadataState);
        Assert.Equal(before.MetadataJson, after.MetadataJson);
        Assert.Equal(before.RawMetadataJson, after.RawMetadataJson);
    }

    [Fact]
    public async Task Cancellation_StopsProbeAndDoesNotPublishFailedMetadata()
    {
        await using var fixture = await MetadataFixture.CreateAsync(_root, "clip.mp4", "video");
        var probe = new BlockingProbe();
        using var service = new DerivedMediaMetadataService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, probe);
        using var cancellation = new CancellationTokenSource();
        var operation = service.ProbeAsync(fixture.AssetId, cancellationToken: cancellation.Token);
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        var record = await fixture.Coordinator.Previews!.GetAsync(fixture.AssetId);
        Assert.Equal(PreviewComponentState.Missing, record!.MetadataState);
    }

    [Fact]
    public async Task OfflineRoot_RetainsPersistedMetadataAndCatalogIdentity()
    {
        await using var fixture = await MetadataFixture.CreateAsync(_root, "clip.mp4", "video");
        var probe = new FakeProbe(SuccessMetadata());
        using var service = new DerivedMediaMetadataService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, probe);
        await service.ProbeAsync(fixture.AssetId);
        var catalogId = fixture.Coordinator.CatalogSession.Identity.CatalogId;
        Directory.Delete(fixture.MediaRoot, recursive: true);

        var offline = await service.ProbeAsync(fixture.AssetId, forceRefresh: true);
        var retained = await fixture.Coordinator.Previews!.GetAsync(fixture.AssetId);

        Assert.Equal(DerivedMetadataStatus.RootUnavailable, offline.Status);
        Assert.NotNull(offline.Metadata);
        Assert.Equal(PreviewComponentState.Current, retained!.MetadataState);
        Assert.Equal(PreviewSourceAvailability.Unavailable, retained.SourceAvailability);
        Assert.Equal(catalogId, fixture.Coordinator.CatalogSession.Identity.CatalogId);
        Assert.Equal(1, probe.CallCount);
    }

    [Fact]
    public async Task MissingSource_RetainsPersistedMetadataAndDoesNotProbeAgain()
    {
        await using var fixture = await MetadataFixture.CreateAsync(_root, "clip.mp4", "video");
        var probe = new FakeProbe(SuccessMetadata());
        using var service = new DerivedMediaMetadataService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, probe);
        await service.ProbeAsync(fixture.AssetId);
        File.Delete(MediaPathSemantics.ResolveContained(fixture.MediaRoot, "clip.mp4"));

        var missing = await service.ProbeAsync(fixture.AssetId, forceRefresh: true);
        var retained = await fixture.Coordinator.Previews!.GetAsync(fixture.AssetId);

        Assert.Equal(DerivedMetadataStatus.SourceMissing, missing.Status);
        Assert.NotNull(missing.Metadata);
        Assert.Equal(PreviewSourceAvailability.Missing, retained!.SourceAvailability);
        Assert.Equal(PreviewComponentState.Current, retained.MetadataState);
        Assert.Equal(1, probe.CallCount);
    }

    [Fact]
    public async Task Service_BoundsConcurrentProbeWork()
    {
        await using var fixture = await MetadataFixture.CreateAsync(_root, "one.mp4", "video");
        var second = await fixture.AddAssetAsync("two.mp4", "video");
        var third = await fixture.AddAssetAsync("three.mp4", "video");
        var probe = new ConcurrencyProbe();
        using var service = new DerivedMediaMetadataService(fixture.Coordinator.MediaAssets,
            fixture.Coordinator.Previews!, probe, maximumConcurrency: 2);

        await Task.WhenAll(service.ProbeAsync(fixture.AssetId), service.ProbeAsync(second), service.ProbeAsync(third));

        Assert.Equal(2, probe.MaximumObserved);
    }

    [Fact]
    public async Task FfprobeReader_ProbesRepresentativeVideoFixture()
    {
        var dependencies = PlaybackDependencyLocator.FindSharedLibraries()
            ?? throw new InvalidOperationException("Run scripts/Get-PlaybackDependencies.ps1 before integration tests.");
        var source = Path.Combine(_root, "fixture.mkv");
        Run(Path.Combine(dependencies, "ffmpeg.exe"), "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=10:duration=1",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
            "-c:v", "ffv1", "-c:a", "pcm_s16le", "-shortest", source);
        var reader = new FfprobeMediaMetadataReader(Path.Combine(dependencies, "ffprobe.exe"), new ProbeProcessRunner());

        var result = await reader.ProbeAsync(source, "video", new FileInfo(source).Length);

        Assert.Equal(DerivedMetadataStatus.Succeeded, result.Status);
        Assert.Equal(160, result.Metadata!.Video!.Width);
        Assert.Equal(90, result.Metadata.Video.Height);
        Assert.Equal("ffv1", result.Metadata.Video.Codec);
        Assert.Equal("pcm_s16le", result.Metadata.Audio!.Codec);
        Assert.InRange(result.Metadata.DurationSeconds!.Value, 0.9, 1.1);
    }

    [Fact]
    public async Task CatalogSchema_RemainsFreeOfDerivedMetadata()
    {
        await using var fixture = await MetadataFixture.CreateAsync(_root, "clip.mp4", "video");
        using var connection = fixture.Coordinator.CatalogSession.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE name LIKE '%Metadata%' OR name LIKE '%Preview%';";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    private static MediaProbeResult SuccessMetadata() => new(DerivedMetadataStatus.Succeeded,
        new(DerivedMediaKind.Video, "mov", 10, 0, 100, 1_000_000,
            new("h264", "High", 1920, 1080, 30, "yuv420p", 8, "bt709", "bt709", "bt709"),
            new("aac", 2, "stereo", 48000, 192000), null), VideoJson);

    private static void Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
    }

    private sealed class FakeProbe(MediaProbeResult result) : IMediaMetadataProbe
    {
        public int CallCount { get; private set; }
        public Task<MediaProbeResult> ProbeAsync(string path, string mediaType, long fileSizeBytes, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); CallCount++; return Task.FromResult(result); }
    }

    private sealed class BlockingProbe : IMediaMetadataProbe
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MediaProbeResult> ProbeAsync(string path, string mediaType, long fileSizeBytes, CancellationToken cancellationToken = default)
        { Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); throw new UnreachableException(); }
    }

    private sealed class ConcurrencyProbe : IMediaMetadataProbe
    {
        private int _active;
        private int _maximumObserved;
        public int MaximumObserved => _maximumObserved;
        public async Task<MediaProbeResult> ProbeAsync(string path, string mediaType, long fileSizeBytes, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            int observed;
            while ((observed = _maximumObserved) < active &&
                Interlocked.CompareExchange(ref _maximumObserved, active, observed) != observed) { }
            try { await Task.Delay(100, cancellationToken); return SuccessMetadata(); }
            finally { Interlocked.Decrement(ref _active); }
        }
    }

    private sealed class MetadataFixture : IAsyncDisposable
    {
        private readonly string _appData;
        private MediaRootInfo _root = null!;
        public LightflowStorageCoordinator Coordinator { get; private set; } = null!;
        public string MediaRoot { get; private set; } = null!;
        public Guid AssetId { get; private set; }

        private MetadataFixture(string appData) => _appData = appData;
        public static async Task<MetadataFixture> CreateAsync(string testRoot, string relativePath, string mediaType)
        {
            var fixture = new MetadataFixture(Path.Combine(testRoot, "appdata"));
            fixture.Coordinator = (await LightflowStorageCoordinator.StartAsync(fixture._appData)).Coordinator!;
            fixture.MediaRoot = Path.Combine(testRoot, "media");
            Directory.CreateDirectory(fixture.MediaRoot);
            fixture._root = (await fixture.Coordinator.MediaRoots.CreateAsync("Media", fixture.MediaRoot)).Root!;
            fixture.AssetId = await fixture.AddAssetAsync(relativePath, mediaType);
            return fixture;
        }
        public async Task<Guid> AddAssetAsync(string relativePath, string mediaType)
        {
            var path = MediaPathSemantics.ResolveContained(MediaRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "representative source bytes");
            return (await Coordinator.MediaAssets.CreateAsync(_root.RootId, relativePath, mediaType)).Asset!.Asset.AssetId;
        }
        public async Task ReopenAsync()
        {
            await Coordinator.DisposeAsync();
            Coordinator = (await LightflowStorageCoordinator.StartAsync(_appData)).Coordinator!;
        }
        public async ValueTask DisposeAsync() => await Coordinator.DisposeAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() { try { Directory.Delete(_root, true); } catch { } return Task.CompletedTask; }
}
