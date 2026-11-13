using System.Globalization;
using System.Text.Json;
using System.Windows;
using Xunit;

namespace LightflowStudio.Tests;

[Collection("Flyleaf playback integration")]
public sealed class ActiveRangeExportIntegrationTests
{
    [ExportNvencTheory]
    [InlineData("both", false, 0)]
    [InlineData("in", false, 0)]
    [InlineData("out", false, 0)]
    [InlineData("none", false, 0)]
    [InlineData("saved", false, 0)]
    [InlineData("eof", false, 0)]
    [InlineData("eof", false, 5)]
    [InlineData("both", true, 0)]
    public async Task Packaged_tools_export_active_ranges_with_expected_frames_and_audio(string mode, bool vfr, int offset)
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AGENTS.md"))) repo = repo.Parent;
        Assert.NotNull(repo);
        var tools = Environment.GetEnvironmentVariable("LIGHTFLOW_EXPORT_TEST_TOOLS")
            ?? Path.Combine(repo.FullName, "artifacts", "ffmpeg", "bin");
        var root = Path.Combine(repo.FullName, "artifacts", "export-048", $"{mode}-{vfr}-{offset}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe"); var ffprobe = Path.Combine(tools, "ffprobe.exe");
        var runner = new ProbeProcessRunner();
        async Task<string> Run(string executable, IReadOnlyList<string> arguments)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var result = await runner.RunAsync(executable, arguments, timeout.Token);
            Assert.True(result.ExitCode == 0, result.StandardError); return result.StandardOutput;
        }
        var source = Path.Combine(root, "source.mp4");
        await Run(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=256x256:rate=20:duration=2",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2",
            "-vf", vfr ? "select='not(eq(mod(n,3),1))'" : "null", "-fps_mode", "vfr",
            "-c:v", "h264_nvenc", "-c:a", "aac", "-output_ts_offset", offset.ToString(CultureInfo.InvariantCulture), source]);
        var sourceIdentity = TrimSourceIdentity.Read(source)!;
        Assert.True(MediaMetadataParser.TryParse(await Run(ffprobe, FfmpegCommandBuilder.ProbeMetadata(source)), sourceIdentity.FileSizeBytes, out var metadata));
        var pts = EncodingRangeResolver.ParseFrameTimestamps(await Run(ffprobe, FfmpegCommandBuilder.ProbeVideoFrames(source))).Order().ToArray();
        var duration = TimeSpan.FromSeconds(metadata.DurationSeconds);
        var selectedIn = pts[2] - metadata.StartTimestamp;
        var selectedOut = pts[8] - metadata.StartTimestamp;
        MediaRange? range = mode == "none" ? null : new(duration,
            mode == "out" ? null : selectedIn,
            mode is "in" ? null : mode == "eof" ? duration : selectedOut);
        var kind = mode == "saved" ? ExportItemKind.Subclip : mode == "none"
            ? ExportItemKind.NoSubclipFullSourceFallback : ExportItemKind.NoSubclipActiveRange;
        var asset = Guid.NewGuid();
        var input = new EncodingHandoffInput(asset, Guid.NewGuid(), source, "source.mp4", sourceIdentity.FileSizeBytes, range,
            ExportProvenance: new(kind, asset, mode == "saved" ? Guid.NewGuid() : null, mode == "saved" ? "Saved" : null, mode == "saved" ? 1 : null),
            NamingOriginalName: "export", NamingIndexNumberBasis: "source", RangeIsFixed: true, SourceIdentity: sourceIdentity);
        var resources = new EncodingLutResourceStore(Path.Combine(root, "luts"));
        var handoff = new EncodingHandoffResult([input], [], root);
        var model = new ExportDialogModel(handoff, new(), [], [], resources,
            revalidate: (_, _) => Task.FromResult(handoff), namingDefault: ExportNamingDefault.Subclip);
        model.Destination = Path.Combine(root, "output"); model.CreateSubfolder = false;
        await StaDispatcher.RunAsync(async () =>
        {
            TestWpfApplication.EnsureLoaded();
            var window = new ExportDialog(model, null!, ffprobe)
            { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000 };
            try
            {
                window.Show();
                var deadline = DateTime.UtcNow.AddSeconds(90);
                while (model.IsAnalyzing)
                { Assert.True(DateTime.UtcNow < deadline, "Preflight timed out"); await Task.Delay(20); }
                if (mode == "eof" && offset == 5)
                {
                    // AAC priming starts before the offset video, so format EOF evidence
                    // differs from the captured video duration. Preserve #496's strict refusal.
                    Assert.False(model.CanExport);
                    Assert.False(window.ExportButton.IsEnabled);
                    Assert.Contains(model.Errors, issue => issue.Code == "export.range-unresolved"
                        && issue.Message.Contains("Source EOF could not be verified"));
                    return;
                }
                Assert.True(model.CanExport, string.Join(" | ", model.Errors.Select(issue => issue.Message)));
                Assert.True(window.ExportButton.IsEnabled);
                var row = Assert.Single(model.SubmissionItems);
                Assert.Equal(range is not null, row.UseRange);
                Assert.StartsWith(mode == "saved" ? "Saved Subclip" : range is null ? "Full video" : "Active In/Out", row.RangeToolTip);
            }
            finally { window.Close(); }
        });
        if (mode == "eof" && offset == 5)
        {
            Assert.True(sourceIdentity.Matches(TrimSourceIdentity.Read(source)!));
            Assert.False(Directory.Exists(model.Destination));
            File.WriteAllText(Path.Combine(root, "evidence.json"), JsonSerializer.Serialize(new
            { mode, offset, rejected = true, reasons = model.Errors.Select(issue => issue.Message), unchangedSourceIdentity = true }));
            return;
        }
        var plan = await model.MaterializeAcceptedPlanAsync(); var item = Assert.Single(plan.Items);
        Assert.Equal(kind, item.Definition.ExportProvenance!.Kind);
        var resolved = item.Definition.ResolvedRange;
        var executor = new EncodingJobExecutor(ffmpeg, ffprobe, Path.Combine(root, "identities"), resources);
        var execution = await executor.ExecuteAsync(item, plan.Definition.Options, new Progress<double>(), CancellationToken.None);
        Assert.Equal(JobState.Completed, execution.State);
        var output = item.OutputPaths.Single();
        var actual = EncodingRangeResolver.ParseFrameTimestamps(await Run(ffprobe, FfmpegCommandBuilder.ProbeVideoFrames(output))).Order().ToArray();
        var first = mode is "none" or "out" ? pts[0] : pts[2];
        var last = mode is "none" or "in" or "eof" ? pts[^1] : pts[8];
        var expected = pts.Where(value => value >= first && value <= last).ToArray();
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < actual.Length; index++)
            Assert.InRange(Math.Abs((actual[index] - (expected[index] - first)).TotalMilliseconds), 0, .05);
        using var outputProbe = JsonDocument.Parse(await Run(ffprobe, FfmpegCommandBuilder.ProbeMetadata(output)));
        var streams = outputProbe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var audio = Assert.Single(streams, stream => stream.GetProperty("codec_type").GetString() == "audio");
        var audioDuration = double.Parse(audio.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        var expectedAudioDuration = resolved?.EffectiveDuration.TotalSeconds ?? metadata.DurationSeconds;
        Assert.InRange(Math.Abs(audioDuration - expectedAudioDuration), 0, .025);
        Assert.True(sourceIdentity.Matches(TrimSourceIdentity.Read(source)!));
        File.WriteAllText(Path.Combine(root, "evidence.json"), JsonSerializer.Serialize(new
        { mode, vfr, offset, tools, range, resolved, kind = kind.ToString(), expectedFrames = expected.Length, actualFrames = actual.Length,
            sourcePts = expected.Select(value => value.TotalSeconds), outputPts = actual.Select(value => value.TotalSeconds),
            audioDuration, expectedAudioDuration, unchangedSourceIdentity = true, dialogExportEnabled = true, realNvenc = true },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
