using LightflowStudio;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class EncodingJobPlannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"Lightflow-JobPlan-{Guid.NewGuid():N}");
    private string OutputRoot => Path.Combine(_root, "output");
    private string IdentityCache => Path.Combine(_root, "identity-cache");

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

    [Fact]
    public void Plan_UsesResolvedTrimDurationForMixedBatchProgress()
    {
        var selected = new MediaRange(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(19));
        var resolved = new ResolvedMediaRange(selected, TimeSpan.Zero, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(10));
        var definition = Define(
            new EncodingSource(Path.Combine(_root, "trimmed.mov"), 100, selected.SourceDuration, selected, resolved),
            Source("full.mov", 30));

        var plan = EncodingJobPlanner.Plan(definition, _ => new(false, 0));

        Assert.Equal(JobWorkUnit.MediaDuration, plan.WorkUnit);
        Assert.Equal(40, plan.Items.Sum(item => item.WorkEstimate.Value));
        Assert.Equal(10, plan.Items.Single(item => item.Definition.ResolvedRange is not null).WorkEstimate.Value);
    }

    [Fact]
    public void Plan_PreservesTrimmedOutputWithoutMatchingIdentityWhenOverwriteIsOff()
    {
        Directory.CreateDirectory(OutputRoot);
        var sourcePath = Path.Combine(_root, "one.mov");
        var trim = new MediaRange(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var resolved = new ResolvedMediaRange(trim, TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.04), TimeSpan.FromSeconds(1.04));
        var definition = Define(new EncodingSource(sourcePath, 100, trim.SourceDuration, trim, resolved, 123));

        var plan = EncodingJobPlanner.Plan(definition, _ => new(true, 100));

        var item = Assert.Single(plan.Items);
        Assert.Equal(JobPlanDisposition.Skip, item.Disposition);
        Assert.Contains(item.Issues, issue => issue.Code == "encoding.existing-output-differs"
            && issue.Severity == JobIssueSeverity.Warning);
    }

    [Fact]
    public void Plan_PreservesExistingOutputRegardlessOfTrimIdentityWhenOverwriteIsOff()
    {
        Directory.CreateDirectory(OutputRoot);
        var trim = new MediaRange(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var resolved = new ResolvedMediaRange(trim, TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.04), TimeSpan.FromSeconds(1.04));
        var definition = Define(new EncodingSource(Path.Combine(_root, "one.mov"), 100, trim.SourceDuration, trim, resolved, 123));
        var initial = EncodingJobPlanner.Plan(definition, _ => new(false, 0));
        var output = Assert.Single(initial.Items).OutputPaths.Single();
        EncodingOutputIdentityStore.Save(output, EncodingOutputIdentity.Create(initial.Items[0].Definition, definition.Options), IdentityCache);

        var matching = EncodingJobPlanner.Plan(definition, _ => new(true, 100), identityCacheDirectory: IdentityCache);
        var changed = definition with
        {
            Items = definition.Items.Select(item => item with
            {
                ResolvedRange = resolved with { RequestedRange = trim with { Out = TimeSpan.FromSeconds(3) } }
            }).ToList()
        };

        Assert.Equal(JobPlanDisposition.Skip, Assert.Single(matching.Items).Disposition);
        var changedItem = Assert.Single(EncodingJobPlanner.Plan(changed, _ => new(true, 100), identityCacheDirectory: IdentityCache).Items);
        Assert.Equal(JobPlanDisposition.Skip, changedItem.Disposition);
        Assert.Contains(changedItem.Issues, issue => issue.Code == "encoding.existing-output-differs");
    }

    [Fact]
    public void Plan_ProcessesChangedTrimWhenOverwriteIsOn()
    {
        var trim = new MediaRange(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        var resolved = new ResolvedMediaRange(trim, TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.04), TimeSpan.FromSeconds(1.04));
        var definition = Define(new EncodingSource(Path.Combine(_root, "one.mov"), 100, trim.SourceDuration, trim, resolved, 123)) with
        {
            Options = Options() with { OverwriteExistingFiles = true }
        };

        var item = Assert.Single(EncodingJobPlanner.Plan(definition, _ => new(true, 100), identityCacheDirectory: IdentityCache).Items);

        Assert.Equal(JobPlanDisposition.Process, item.Disposition);
        Assert.DoesNotContain(item.Issues, issue => issue.Code == "encoding.existing-output-differs");
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
