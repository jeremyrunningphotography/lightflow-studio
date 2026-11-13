using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class PreflightTimingFactAttribute : FactAttribute
{
    public PreflightTimingFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LIGHTFLOW_PREFLIGHT_TIMING") is null
            || Environment.GetEnvironmentVariable("LIGHTFLOW_DJI_ACCEPTANCE_MANIFEST") is null)
            Skip = "Opt-in original-source preflight timing measurement.";
    }
}

public sealed class ExportPreflightTimingTests
{
    [PreflightTimingFact]
    public async Task OriginalSources_PreflightStageTimings()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AGENTS.md"))) repo = repo.Parent;
        Assert.NotNull(repo);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("LIGHTFLOW_DJI_ACCEPTANCE_MANIFEST")!));
        var inputs = manifest.RootElement.GetProperty("sources").EnumerateArray().Select((source, index) =>
        {
            var path = source.GetProperty("path").GetString()!;
            var identity = TrimSourceIdentity.Read(path)!;
            return new EncodingHandoffInput(Guid.NewGuid(), Guid.NewGuid(), path, $"source-{index + 1}", identity.FileSizeBytes,
                new(TimeSpan.FromTicks(source.GetProperty("durationTicks").GetInt64()), TimeSpan.FromTicks(source.GetProperty("inTicks").GetInt64()),
                    TimeSpan.FromTicks(source.GetProperty("outTicks").GetInt64())), SourceIdentity: identity);
        }).ToArray();
        var tools = Path.Combine(repo.FullName, "artifacts", "ffmpeg", "bin");
        var runs = new List<object>();
        for (var sample = 0; sample < 3; sample++)
        {
            var runner = new TimedRunner();
            var service = new ExportSourcePreflight(Path.Combine(tools, "ffprobe.exe"), runner);
            var elapsed = Stopwatch.StartNew();
            var hardwareMs = 0d;
            async Task<EncoderCapability> Hardware(CancellationToken token)
            {
                var hardwareTimer = Stopwatch.StartNew();
                var hardware = await new EncoderCapabilityService(Path.Combine(tools, "ffmpeg.exe"), new FfmpegEncoderCapabilityProbe()).GetAsync(token);
                hardwareMs = hardwareTimer.Elapsed.TotalMilliseconds;
                var capability = hardware.Single(value => value.Backend == EncoderBackend.NvidiaNvenc);
                Assert.True(capability.IsUsable);
                return capability;
            }
            var ranges = new List<ResolvedMediaRange?>();
            var mode = Environment.GetEnvironmentVariable("LIGHTFLOW_PREFLIGHT_TIMING");
            if (mode == "after")
            {
                var model = new ExportDialogModel(new(inputs, [], repo.FullName), new(), [], [],
                    new EncodingLutResourceStore(Path.Combine(repo.FullName, "artifacts", "i03-timings", "luts")), _ => new(false, 0));
                await new ExportPreflightCoordinator(service, Hardware).RunAsync(model);
                Assert.True(model.CanExport, string.Join(" | ", model.Errors.Select(issue => issue.Message)));
                Assert.Empty(model.Warnings);
                ranges.AddRange(model.CurrentPlan!.Items.Select(item => item.Definition.ResolvedRange));
            }
            else
            {
                await Hardware(CancellationToken.None);
                var metadata = new List<ExportSourceProbe>();
                foreach (var input in inputs) metadata.Add(await service.ProbeAsync(input));
                for (var index = 0; index < inputs.Length; index++)
                {
                    var input = inputs[index];
                    var info = metadata[index].Metadata!;
                    string? packets = null;
                    var args = FfmpegCommandBuilder.ProbeVideoFrames(input.SourcePath, input.InitialTrim!, info.StartTimestamp);
                    if (mode == "terminal-experiment")
                    {
                        var response = await runner.RunAsync(Path.Combine(tools, "ffprobe.exe"),
                            FfmpegCommandBuilder.ProbeVideoPackets(input.SourcePath, input.InitialTrim! with { Out = null }, info.StartTimestamp));
                        packets = response.StandardOutput;
                        args[args.IndexOf("-read_intervals") + 1] = (info.StartTimestamp + input.InitialTrim!.SourceDuration - TimeSpan.FromMilliseconds(50)).TotalSeconds
                            .ToString("F7", System.Globalization.CultureInfo.InvariantCulture) + "%";
                    }
                    // This preserves I02's original sequential wide-frame algorithm for reproducibility.
                    var identity = TrimSourceIdentity.Read(input.SourcePath)!;
                    Assert.True(metadata[index].Identity!.Matches(identity));
                    var frames = await runner.RunAsync(Path.Combine(tools, "ffprobe.exe"), args);
                    if (packets is not null)
                        Assert.Contains(EncodingRangeResolver.ParsePresentationTimestamps(packets),
                            timestamp => Math.Abs((timestamp - info.StartTimestamp - input.InitialTrim!.EffectiveIn).TotalMilliseconds) <= 2);
                    ranges.Add(EncodingRangeResolver.Resolve(packets is null ? input.InitialTrim! : input.InitialTrim! with { In = null },
                        info.StartTimestamp, frames.StandardOutput, info.EofEvidence));
                    Assert.True(identity.Matches(TrimSourceIdentity.Read(input.SourcePath)!));
                }
            }
            elapsed.Stop();
            Assert.All(ranges, range => Assert.True(range!.RetainsSourceEof));
            var stat = Stopwatch.StartNew();
            foreach (var input in inputs) Assert.NotNull(TrimSourceIdentity.Read(input.SourcePath));
            stat.Stop();
            runs.Add(new { sample, totalMs = elapsed.Elapsed.TotalMilliseconds, hardwareMs,
                twoIdentityReadsMs = stat.Elapsed.TotalMilliseconds, steps = runner.Steps.ToArray() });
        }
        var label = Environment.GetEnvironmentVariable("LIGHTFLOW_PREFLIGHT_TIMING")!;
        var directory = Path.Combine(repo.FullName, "artifacts", "i03-timings");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, label + ".json"), JsonSerializer.Serialize(new { label, runs }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class TimedRunner : IProbeProcessRunner
    {
        public List<object> Steps { get; } = [];
        public async Task<ProbeProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            var timer = Stopwatch.StartNew();
            var result = await new ProbeProcessRunner().RunAsync(executable, arguments, cancellationToken);
            timer.Stop();
            lock (Steps) Steps.Add(new { kind = arguments.Contains("-show_packets") ? "packets" : arguments.Any(argument => argument.StartsWith("frame=")) ? "decodedFrames" : "metadata",
                elapsedMs = timer.Elapsed.TotalMilliseconds, result.ExitCode });
            return result;
        }
    }
}
