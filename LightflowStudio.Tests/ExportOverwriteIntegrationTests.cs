using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExportOverwriteIntegrationTests
{
    [ExportNvencTheory]
    [InlineData(true)]
    public async Task RealQueuePreservesSkippedFileThenReplacesItAndProtectsSources(bool enabled)
    {
        Assert.True(enabled);
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AGENTS.md"))) repo = repo.Parent;
        var tools = Environment.GetEnvironmentVariable("LIGHTFLOW_EXPORT_TEST_TOOLS") ?? Path.Combine(repo!.FullName, "artifacts", "ffmpeg", "bin");
        var root = Path.Combine(repo!.FullName, "artifacts", "i04-overwrite", Guid.NewGuid().ToString("N"));
        var inputs = Path.Combine(root, "input"); var outputs = Path.Combine(root, "output");
        Directory.CreateDirectory(inputs); Directory.CreateDirectory(outputs);
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe"); var ffprobe = Path.Combine(tools, "ffprobe.exe");
        var runner = new ProbeProcessRunner();
        async Task<string> Run(string exe, IReadOnlyList<string> args)
        {
            var result = await runner.RunAsync(exe, args);
            Assert.True(result.ExitCode == 0, result.StandardError); return result.StandardOutput;
        }
        var source1 = Path.Combine(inputs, "existing.mp4"); var source2 = Path.Combine(inputs, "new.mp4");
        await Run(ffmpeg, ["-v", "error", "-f", "lavfi", "-i", "testsrc2=size=256x256:rate=25:duration=0.4", "-c:v", "mpeg4", source1]);
        File.Copy(source1, source2);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var sourceHashes = new[] { Hash(source1), Hash(source2) };
        var existing = Path.Combine(outputs, "existing.mp4"); File.WriteAllText(existing, "unrelated existing output - preserve unless overwrite");
        var existingHash = Hash(existing);
        var options = new EncodingJobOptions(inputs, outputs, OutputResolution.Source, RecoveryStrategy.Normal,
            new() { AudioMode = AudioEncodingMode.None, FrameRate = 0 }, null, "", false, false, false,
            Naming: new([new(NamePartKind.OriginalName)], NamePartSeparator.Hyphen));
        var sources = new[] { source1, source2 }.Select(path => new EncodingSource(path, new FileInfo(path).Length,
            TimeSpan.FromSeconds(.4), HasAudio: false)).ToArray();
        JobPlan<EncodingJobOptions> Plan(EncodingJobOptions recipe) => EncodingJobPlanner.Plan(EncodingJobPlanner.Define(recipe, sources));
        async Task<ExportJobSnapshot[]> Execute(JobPlan<EncodingJobOptions> plan)
        {
            Assert.True(plan.IsValid);
            await using var scheduler = new GlobalExportScheduler(2, () =>
            {
                var executor = new EncodingJobExecutor(ffmpeg, ffprobe, Path.Combine(root, "identities"));
                return new(executor.ExecuteAsync, () => { });
            });
            var admission = scheduler.Admit(ExportSubmissionProposal.FromPlan(plan));
            Assert.True(admission.Accepted, string.Join("; ", admission.Issues.Select(issue => issue.Message)));
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (scheduler.Jobs.Any(job => job.State is JobState.Queued or JobState.Running) && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            return scheduler.Jobs.ToArray();
        }
        var skipPlan = Plan(options);
        Assert.Equal(JobPlanDisposition.Skip, skipPlan.Items[0].Disposition);
        var skipJobs = await Execute(skipPlan);
        Assert.Single(skipJobs, job => job.State == JobState.Skipped);
        Assert.Single(skipJobs, job => job.State == JobState.Completed);
        Assert.Equal(existingHash, Hash(existing));
        Assert.True(File.Exists(Path.Combine(outputs, "new.mp4")));
        var replacePlan = Plan(options with { OverwriteExistingFiles = true });
        Assert.All(replacePlan.Items, item => Assert.Contains(item.Issues, issue => issue.Code == "encoding.existing-output-replaced"));
        var replaceJobs = await Execute(replacePlan);
        Assert.All(replaceJobs, job => Assert.Equal(JobState.Completed, job.State));
        Assert.NotEqual(existingHash, Hash(existing));
        var probe = await Run(ffprobe, FfmpegCommandBuilder.ProbeMetadata(existing));
        Assert.True(MediaMetadataParser.TryParse(probe, new FileInfo(existing).Length, out var outputMetadata));
        Assert.Equal("h264", outputMetadata.VideoCodec); Assert.Contains("mp4", outputMetadata.Container);
        foreach (var overwrite in new[] { false, true })
        {
            var sourceCollision = Plan(options with { OutputRoot = inputs, OverwriteExistingFiles = overwrite });
            Assert.False(sourceCollision.IsValid);
            Assert.All(sourceCollision.Items, item => Assert.Contains(item.Issues, issue => issue.Code == "encoding.source-overwrite"));
            Assert.Throws<ArgumentException>(() => ExportSubmissionProposal.FromPlan(sourceCollision));
            var duplicate = Plan(options with { OverwriteExistingFiles = overwrite,
                Naming = new([new(NamePartKind.CustomText, "duplicate")], NamePartSeparator.Hyphen) });
            Assert.False(duplicate.IsValid);
            Assert.Throws<ArgumentException>(() => ExportSubmissionProposal.FromPlan(duplicate));
        }
        Assert.Equal(sourceHashes, new[] { Hash(source1), Hash(source2) });
        Assert.Empty(Directory.GetFiles(outputs, "*.lightflow"));
        File.WriteAllText(Path.Combine(root, "evidence.json"), JsonSerializer.Serialize(new
        {
            tools, root, sourceHashes, sourceHashesAfter = new[] { Hash(source1), Hash(source2) },
            preservedHashBefore = existingHash, preservedHashAfterSkip = existingHash, replacedHash = Hash(existing),
            skipJobs = skipJobs.Select(job => new { job.DisplayName, state = job.State.ToString() }),
            replaceJobs = replaceJobs.Select(job => new { job.DisplayName, state = job.State.ToString() }),
            sourceCollisionsBlockedWithOverwriteOffAndOn = true, duplicateOutputsBlockedWithOverwriteOffAndOn = true,
            outputMetadata, probe = JsonSerializer.Deserialize<JsonElement>(probe)
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
