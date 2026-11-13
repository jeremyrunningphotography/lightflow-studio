using System.Globalization;
using System.Text.Json;
using System.Windows;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class DjiAcceptanceTheoryAttribute : TheoryAttribute
{
    public DjiAcceptanceTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("LIGHTFLOW_DJI_ACCEPTANCE_MANIFEST") is null)
            Skip = "Requires the owner's two explicitly authorized sources and a task-owned private manifest.";
    }
}

[Collection("STA dispatcher tests")]
public sealed class ExportDjiAcceptanceTests
{
    [DjiAcceptanceTheory]
    [InlineData(true)]
    public async Task OriginalDjiPair_RealDialogPublishesPerFileProgressAndNormalReady(bool measured)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AGENTS.md"))) repo = repo.Parent;
        using var manifest = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("LIGHTFLOW_DJI_ACCEPTANCE_MANIFEST")!));
        var root = Path.Combine(repo!.FullName, "artifacts", "i03-batch-preflight"); Directory.CreateDirectory(root);
        var inputs = manifest.RootElement.GetProperty("sources").EnumerateArray().Select((source, index) =>
        {
            var path = source.GetProperty("path").GetString()!; var identity = TrimSourceIdentity.Read(path)!; var asset = Guid.NewGuid();
            return new EncodingHandoffInput(asset, Guid.NewGuid(), path, $"DJI source {index + 1}", identity.FileSizeBytes,
                new(TimeSpan.FromTicks(source.GetProperty("durationTicks").GetInt64()), TimeSpan.FromTicks(source.GetProperty("inTicks").GetInt64()), TimeSpan.FromTicks(source.GetProperty("outTicks").GetInt64())),
                ExportProvenance: new(ExportItemKind.Subclip, asset, Guid.NewGuid(), $"DJI source {index + 1}", 1),
                NamingOriginalName: $"DJI source {index + 1}", RangeIsFixed: true, SourceIdentity: identity);
        }).ToArray();
        var model = new ExportDialogModel(new(inputs, [], root), new() { PixelFormat = VideoPixelFormat.P010 }, [], [],
            new EncodingLutResourceStore(Path.Combine(root, "luts")), _ => new(false, 0), namingDefault: ExportNamingDefault.Subclip);
        var tools = Environment.GetEnvironmentVariable("LIGHTFLOW_EXPORT_TEST_TOOLS") ?? Path.Combine(repo.FullName, "artifacts", "ffmpeg", "bin");
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var window = new ExportDialog(model, null!, Path.Combine(tools, "ffprobe.exe"))
            { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000 };
            var history = new List<object>();
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var ticks = 0; var lastTick = 0d; var maximumGap = 0d; var sawOneReady = false;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { var now = elapsed.Elapsed.TotalMilliseconds; maximumGap = Math.Max(maximumGap, now - lastTick); lastTick = now; ticks++; };
            void Capture(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (args.PropertyName != string.Empty) return;
                var rows = model.SubmissionItems;
                Assert.DoesNotContain(rows, row => row.HasError);
                Assert.Equal(model.ReadySummary, window.ReadySummaryText.Text);
                Assert.Equal(model.CanExport, window.ExportButton.IsEnabled);
                if (rows.Count(row => row.State == ExportFilePreflightState.Ready) == 1 && rows.Any(row => row.IsChecking))
                { sawOneReady = true; Assert.False(window.ExportButton.IsEnabled); }
                history.Add(new { elapsedMs = elapsed.Elapsed.TotalMilliseconds, footer = model.ReadySummary,
                    states = rows.Select(row => row.State.ToString()).ToArray(), exportEnabled = model.CanExport });
            }
            model.PropertyChanged += Capture;
            try
            {
                timer.Start(); window.Show();
                var deadline = DateTime.UtcNow.AddMinutes(2);
                while (!model.CanExport)
                {
                    Assert.True(DateTime.UtcNow < deadline, string.Join(" | ", model.Errors.Select(issue => issue.Message)));
                    await Task.Delay(20);
                }
                timer.Stop();
                Assert.True(sawOneReady);
                Assert.True(ticks >= 2);
                Assert.True(maximumGap < 1000, $"UI heartbeat stalled for {maximumGap:F0} ms.");
                Assert.Equal("2 videos ready to export", window.ReadySummaryText.Text);
                Assert.Empty(model.Warnings);
                Assert.All(model.SubmissionItems, row => { Assert.Equal(ExportFilePreflightState.Ready, row.State); Assert.False(row.HasIssues); });
                if (measured) File.WriteAllText(Path.Combine(root, "evidence.json"), JsonSerializer.Serialize(new
                { totalMs = elapsed.Elapsed.TotalMilliseconds, maximumHeartbeatGapMs = maximumGap, heartbeatTicks = ticks, history,
                    technicalDetails = model.CurrentPlan!.Items.Select(item => item.Definition.ResolvedRange!.BoundaryDiagnostic) }, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally { model.PropertyChanged -= Capture; timer.Stop(); window.Close(); }
        });
    }

    [DjiAcceptanceTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task OriginalDjiSource_DialogPreflightAndRealExportPreserveVideoAndAudioEof(int sourceIndex)
    {
        var manifest = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("LIGHTFLOW_DJI_ACCEPTANCE_MANIFEST")!));
        var source = manifest.RootElement.GetProperty("sources")[sourceIndex];
        var path = source.GetProperty("path").GetString()!;
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AGENTS.md"))) repo = repo.Parent;
        Assert.NotNull(repo);
        var tools = Environment.GetEnvironmentVariable("LIGHTFLOW_EXPORT_TEST_TOOLS") ?? Path.Combine(repo.FullName, "artifacts", "ffmpeg", "bin");
        var outputRoot = Path.Combine(repo.FullName, "artifacts", "i03-real-export", $"source-{sourceIndex + 1}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        var identity = TrimSourceIdentity.Read(path)!;
        var range = new MediaRange(TimeSpan.FromTicks(source.GetProperty("durationTicks").GetInt64()),
            TimeSpan.FromTicks(source.GetProperty("inTicks").GetInt64()), TimeSpan.FromTicks(source.GetProperty("outTicks").GetInt64()));
        var asset = Guid.NewGuid();
        var input = new EncodingHandoffInput(asset, Guid.NewGuid(), path, $"DJI source {sourceIndex + 1}", identity.FileSizeBytes,
            range, ExportProvenance: new(ExportItemKind.Subclip, asset, Guid.NewGuid(), $"source-{sourceIndex + 1}", 1),
            NamingOriginalName: $"source-{sourceIndex + 1}", RangeIsFixed: true, SourceIdentity: identity);
        var runner = new ProbeProcessRunner();
        var ffprobe = Path.Combine(tools, "ffprobe.exe");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        var resources = new EncodingLutResourceStore(Path.Combine(outputRoot, "luts"));
        var model = new ExportDialogModel(new([input], [], Path.GetDirectoryName(path)),
            new() { PixelFormat = VideoPixelFormat.P010 }, [], [], resources, _ => new(false, 0),
            namingDefault: ExportNamingDefault.Subclip);
        model.Destination = outputRoot;
        model.CreateSubfolder = false;
        Assert.DoesNotContain(model.Issues, issue => issue.Message.Contains("'unknown'"));
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            // Queue is deliberately not invoked by this UI probe; the accepted plan runs below.
            var window = new ExportDialog(model, null!, ffprobe)
            { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000 };
            try
            {
                window.Show();
                var deadline = DateTime.UtcNow.AddMinutes(2);
                while (!model.CanExport && !model.Errors.Any(issue => issue.Code is "export.preflight" or "export.metadata-failed" or "export.encoder-unavailable")
                    && (model.IsAnalyzing || !model.Errors.Any(issue => issue.Code != "export.range-unresolved")))
                {
                    Assert.True(DateTime.UtcNow < deadline, string.Join(" | ", model.Errors.Select(issue => issue.Message)));
                    await Task.Delay(25);
                }
                Assert.True(model.CanExport, string.Join(" | ", model.Errors.Select(issue => issue.Message)));
                Assert.True(window.ExportButton.IsEnabled);
                Assert.DoesNotContain("unknown", window.ValidationText.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Equal("1 video ready to export", window.ReadySummaryText.Text);
                Assert.All(model.SubmissionItems, row => Assert.Equal(ExportFilePreflightState.Ready, row.State));
                Assert.Empty(model.Warnings);
                Assert.DoesNotContain("Saved exclusive source EOF", window.ValidationText.Text);
            }
            finally { window.Close(); }
        });
        var plan = await model.MaterializeAcceptedPlanAsync();
        Assert.True(plan.IsValid);
        var item = Assert.Single(plan.Items);
        var resolved = item.Definition.ResolvedRange!;
        var settings = item.Definition.MaterializedExport!;
        Assert.Equal(VideoCodec.Hevc, settings.Encoding.Codec);
        Assert.Equal(OutputContainer.Mp4, settings.Encoding.Container);
        var executor = new EncodingJobExecutor(ffmpeg, ffprobe, Path.Combine(outputRoot, "identities"), resources);
        var result = await executor.ExecuteAsync(item, plan.Definition.Options, new Progress<double>(), CancellationToken.None);
        Assert.Equal(JobState.Completed, result.State);
        var output = item.OutputPaths.Single();
        async Task<string> Probe(IReadOnlyList<string> arguments)
        {
            var response = await runner.RunAsync(ffprobe, arguments);
            Assert.True(response.ExitCode == 0, response.StandardError);
            return response.StandardOutput;
        }
        var sourceFrames = EncodingRangeResolver.ParseFrameTimestamps(await Probe(FfmpegCommandBuilder.ProbeVideoFrames(path)))
            .Where(pts => pts >= resolved.AbsoluteIn && pts < resolved.ExclusiveOut).Order().ToArray();
        var outputFrames = EncodingRangeResolver.ParseFrameTimestamps(await Probe(FfmpegCommandBuilder.ProbeVideoFrames(output))).Order().ToArray();
        Assert.Equal(sourceFrames.Length, outputFrames.Length);
        for (var index = 0; index < sourceFrames.Length; index++)
            Assert.InRange(Math.Abs((outputFrames[index] - (sourceFrames[index] - resolved.AbsoluteIn)).TotalMilliseconds), 0, .03);
        var streamJson = await Probe(["-v", "error", "-show_entries", "stream=codec_type,codec_name,codec_tag_string,pix_fmt,start_time,duration,time_base:format=format_name,duration", "-of", "json", output]);
        using var outputMetadata = JsonDocument.Parse(streamJson);
        var streams = outputMetadata.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "video");
        Assert.Equal("hevc", video.GetProperty("codec_name").GetString());
        Assert.Equal("hvc1", video.GetProperty("codec_tag_string").GetString());
        var audio = streams.Where(stream => stream.GetProperty("codec_type").GetString() == "audio").ToArray();
        Assert.Equal(2, audio.Length);
        foreach (var track in audio)
        {
            Assert.Equal("aac", track.GetProperty("codec_name").GetString());
            var duration = double.Parse(track.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
            Assert.InRange(Math.Abs(duration - resolved.EffectiveDuration.TotalSeconds), 0, 1d / 48000 + .000002);
        }
        Assert.True(identity.Matches(TrimSourceIdentity.Read(path)!));
        var sourceMetadata = await Probe(FfmpegCommandBuilder.ProbeMetadata(path));
        File.WriteAllText(Path.Combine(outputRoot, "evidence.json"), JsonSerializer.Serialize(new
        {
            sourceNumber = sourceIndex + 1, tools, range, resolved, frameCount = sourceFrames.Length,
            sourcePts = sourceFrames.Select(value => value.TotalSeconds), outputPts = outputFrames.Select(value => value.TotalSeconds),
            sourceMetadata = JsonSerializer.Deserialize<JsonElement>(sourceMetadata),
            outputMetadata = JsonSerializer.Deserialize<JsonElement>(streamJson), unchangedSourceIdentity = true,
            dialogExportEnabled = true, genuineNvencExecution = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
