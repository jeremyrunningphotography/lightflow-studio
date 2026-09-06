using LightflowStudio;
using System.Text.Json.Nodes;
using Xunit;

namespace LightflowStudio.Tests;

public sealed class ExportDestinationPersistenceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lightflow-export-destination-store-").FullName;

    [Fact]
    public void QueueRoundTripRetainsTypedDestinationAndResolvedOutputPath()
    {
        var source = Path.Combine(_root, "Source", "clip.mp4");
        var destination = new ExportDestination(ExportDestinationMode.SameFolderAsOriginal, null, "1080p");
        var options = new EncodingJobOptions(Path.GetDirectoryName(source)!, "", OutputResolution.FullHd,
            RecoveryStrategy.Normal, new EncodingOptions(), null, "_1080p", false, false, false,
            Destination: destination);
        var definition = EncodingJobPlanner.Define(options,
            [new(source, 1, TimeSpan.FromSeconds(1))]);
        var plan = EncodingJobPlanner.Plan(definition, _ => new(false, 0));
        var proposal = ExportSubmissionProposal.FromPlan(plan);
        var expected = Assert.Single(proposal.Jobs);
        var path = Path.Combine(_root, "queue.json");
        var store = new ExportQueueStore(path);

        store.Save([new(expected, JobState.Queued, null, null, null, TimeSpan.Zero, [], [], null)]);
        var restored = Assert.Single(store.Load()).Definition;

        Assert.Equal(destination, restored.Recipe.Destination);
        Assert.Equal(expected.OutputPath, restored.OutputPath);
        Assert.Equal(Path.Combine(_root, "Source", "1080p", "clip_1080p.mp4"), restored.OutputPath);
    }

    [Fact]
    public void OlderPersistedRecipeWithoutDestinationContinuesUsingLegacyOutputFields()
    {
        var options = new EncodingJobOptions(Path.Combine(_root, "Source"), Path.Combine(_root, "Exports"),
            OutputResolution.FullHd, RecoveryStrategy.Normal, new EncodingOptions(), null, "_1080p",
            PreserveFolderStructure: true, OverwriteExistingFiles: false, DetailedOutput: false);
        var source = Path.Combine(_root, "Source", "Nested", "clip.mp4");

        var plan = EncodingJobPlanner.Plan(EncodingJobPlanner.Define(options,
            [new(source, 1, TimeSpan.FromSeconds(1))]), _ => new(false, 0));
        var proposal = ExportSubmissionProposal.FromPlan(plan);
        var expected = Assert.Single(proposal.Jobs);
        var path = Path.Combine(_root, "legacy-queue.json");
        var store = new ExportQueueStore(path);
        store.Save([new(expected, JobState.Queued, null, null, null, TimeSpan.Zero, [], [], null)]);
        var document = JsonNode.Parse(File.ReadAllText(path))!;
        var recipe = document["jobs"]![0]!["definition"]!["recipe"]!.AsObject();
        Assert.True(recipe.Remove("destination"));
        File.WriteAllText(path, document.ToJsonString());
        var restored = Assert.Single(store.Load()).Definition;

        Assert.Null(restored.Recipe.Destination);
        Assert.Equal(expected.OutputPath, restored.OutputPath);
        Assert.Equal(Path.Combine(_root, "Exports", "Nested", "clip_1080p.mp4"),
            restored.OutputPath);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
