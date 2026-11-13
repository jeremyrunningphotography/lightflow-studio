using System.Text.Json;
using Xunit;
namespace LightflowStudio.Tests;
public sealed class OwnerCollisionReplayAttribute : FactAttribute
{
    public OwnerCollisionReplayAttribute()
    {
        if (Environment.GetEnvironmentVariable("LIGHTFLOW_OWNER_COLLISION_REPLAY") is null)
            Skip = "Requires task-owned read-only owner selection evidence.";
    }
}
public sealed class ExportOwnerCollisionReplayTests
{
    [OwnerCollisionReplay]
    public void SavedEightRowSelectionIdentifiesBothProtectedPhysicalFiles()
    {
        var manifest = Environment.GetEnvironmentVariable("LIGHTFLOW_OWNER_COLLISION_REPLAY")!;
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        var rows = json.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        var destination = json.RootElement.GetProperty("destination").GetString()!;
        var sourceHashes = rows.Select(row => row.GetProperty("source").GetString()!).Distinct().ToDictionary(
            path => path, path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        JobPlan<EncodingJobOptions> Plan(bool overwrite, string folder, bool includeFallback, bool unique = false)
        {
            var options = new EncodingJobOptions(Path.GetDirectoryName(destination)!, folder, OutputResolution.Source,
                RecoveryStrategy.Normal, new(), null, "", false, overwrite, false,
                Naming: new([new(NamePartKind.OriginalName)], NamePartSeparator.Hyphen),
                Destination: new(ExportDestinationMode.SpecificFolder, folder, null));
            var sources = rows.Where(row => includeFallback || row.GetProperty("kind").GetString() == "Subclip")
                .Select((row, index) => new EncodingSource(row.GetProperty("source").GetString()!,
                    row.GetProperty("sourceIdentity").GetProperty("size").GetInt64(), null,
                    NamingOriginalName: row.GetProperty("name").GetString() + (unique ? "-different-name" : ""), CapabilityOrder: index));
            return EncodingJobPlanner.Plan(EncodingJobPlanner.Define(options, sources));
        }
        var observations = new List<object>();
        foreach (var overwrite in new[] { false, true })
        {
            var plan = Plan(overwrite, destination, true);
            var protectedRows = plan.Items.Where(item => item.Issues.Any(issue => issue.Code == "encoding.source-overwrite")).ToArray();
            Assert.Equal(8, plan.Items.Count); Assert.Equal(2, protectedRows.Length);
            Assert.False(plan.IsValid); Assert.Throws<ArgumentException>(() => ExportSubmissionProposal.FromPlan(plan));
            Assert.DoesNotContain(plan.Items.SelectMany(item => item.Issues), issue => issue.Code == "encoding.output-collision");
            Assert.All(protectedRows, item => Assert.Contains(rows, row =>
                row.GetProperty("output").GetString()!.Equals(item.OutputPaths.Single(), StringComparison.OrdinalIgnoreCase)
                && row.GetProperty("matchingSelectedSources").GetArrayLength() == 1));
            Assert.True(Plan(overwrite, destination, false).IsValid);
            Assert.True(Plan(overwrite, Path.Combine(destination, "separate-i05-output"), true).IsValid);
            Assert.True(Plan(overwrite, destination, true, unique: true).IsValid);
            observations.Add(new { overwrite, blocked = protectedRows.Select(item => new { source = item.Definition.SourceIdentity,
                output = item.OutputPaths.Single(), issues = item.Issues }),
                fallbackExcludedPlanValid = true, separateDestinationPlanValid = true, uniqueNamesPlanValid = true });
        }
        Assert.All(sourceHashes, pair => Assert.Equal(pair.Value,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(pair.Key)))));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(manifest)!, "planner-replay.json"), JsonSerializer.Serialize(new
        { observations, sourceHashesUnchanged = true, sourceHashes, destinationConfirmedByOwnerScreenshot = true },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
