using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class EncodingJobPlannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"Lightflow-JobPlan-{Guid.NewGuid():N}");
    private string OutputRoot => Path.Combine(_root, "output");

    public EncodingJobPlannerTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Plan_IsDeterministicValidAndDoesNotCreateOutputs()
    {
        var definition = Define(
            Source("z.mov", 120),
            Source("A.mov", 60));

        var plan = EncodingJobPlanner.Plan(definition, _ => new(false, 0));

        Assert.True(plan.IsValid);
        Assert.Equal(["A.mov", "z.mov"], plan.Items.Select(item => Path.GetFileName(item.Definition.SourceIdentity)));
        Assert.Equal(JobWorkUnit.MediaDuration, plan.WorkUnit);
        Assert.Equal(180, plan.Items.Sum(item => item.WorkEstimate.Value));
        Assert.False(Directory.Exists(OutputRoot));
    }

    [Fact]
    public void Plan_ClassifiesExistingOutputsBeforeExecution()
    {
        var definition = Define(Source("one.mov", 60));

        var plan = EncodingJobPlanner.Plan(definition, _ => new(true, 100));

        Assert.Equal(JobPlanDisposition.Skip, Assert.Single(plan.Items).Disposition);
        Assert.Empty(plan.ExecutableItems);
    }

    [Fact]
    public void Plan_ReportsOutputCollisionsBeforeExecution()
    {
        var nestedA = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
        var nestedB = Directory.CreateDirectory(Path.Combine(_root, "b")).FullName;
        var definition = Define(
            new EncodingSource(Path.Combine(nestedA, "clip.mov"), 1, TimeSpan.FromSeconds(1)),
            new EncodingSource(Path.Combine(nestedB, "clip.mov"), 1, TimeSpan.FromSeconds(1))) with
        {
            Options = Options() with { PreserveFolderStructure = false }
        };

        var plan = EncodingJobPlanner.Plan(definition, _ => new(false, 0));

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Issues, issue => issue.Code == "encoding.output-collision");
        Assert.All(plan.Items, item => Assert.Contains(item.Issues, issue => issue.Code == "encoding.output-collision"));
    }

    [Fact]
    public void Plan_AllowsNoLutWithoutChangingEncodingOptions()
    {
        var definition = Define(Source("one.mov", 60)) with { Options = Options() with { LutPath = null } };

        var plan = EncodingJobPlanner.Plan(definition, _ => new(false, 0));
        var args = FfmpegCommandBuilder.Encode(
            plan.Items[0].Definition.SourceIdentity,
            plan.Items[0].OutputPaths[0],
            plan.Definition.Options.LutPath,
            plan.Definition.Options.Recovery,
            plan.Definition.Options.Resolution,
            encoding: plan.Definition.Options.Encoding);

        Assert.True(plan.IsValid);
        Assert.DoesNotContain(args, argument => argument.Contains("lut3d"));
        Assert.Contains("scale=-2:1080", args);
    }

    [Fact]
    public void Plan_FallsBackToItemWorkWhenDurationIsUnknown()
    {
        var definition = Define(
            new EncodingSource(Path.Combine(_root, "unknown.mov"), 100, null),
            Source("known.mov", 60));

        var plan = EncodingJobPlanner.Plan(definition, _ => new(false, 0));

        Assert.Equal(JobWorkUnit.Items, plan.WorkUnit);
        Assert.All(plan.Items, item => Assert.Equal(1, item.WorkEstimate.Value));
    }

    private JobDefinition<EncodingJobOptions> Define(params EncodingSource[] sources) =>
        EncodingJobPlanner.Define(Options(), sources, Guid.Parse("11111111-1111-1111-1111-111111111111"), DateTimeOffset.UnixEpoch);

    private EncodingJobOptions Options() => new(
        _root,
        OutputRoot,
        OutputResolution.FullHd,
        RecoveryStrategy.Normal,
        EncodingPresetCatalog.Recommended,
        null,
        "_1080p",
        PreserveFolderStructure: true,
        OverwriteExistingFiles: false,
        DetailedOutput: false);

    private EncodingSource Source(string name, double durationSeconds) =>
        new(Path.Combine(_root, name), 100, TimeSpan.FromSeconds(durationSeconds));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
