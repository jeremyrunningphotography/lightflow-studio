using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lightflow.Application;
using Lightflow.Domain;
using Lightflow.Platform.MacOS;

if (args.Length != 3) throw new ArgumentException("existing target, task-local protected root, task-local output");
var baseline = Native.Count();
var results = new List<object>();
var assessor = new MacStorageLocationAssessor([args[1]]);
try
{
    foreach (var role in new[] {StorageRole.ActiveCatalog, StorageRole.MediaSource, StorageRole.ClosedCatalogBackupTransfer})
    {
        // Open only obtains metadata; it never opens SQLite. No mutating operation is requested.
        var request = new StorageAssessmentRequest(Guid.NewGuid(), 0, role,
            role == StorageRole.ActiveCatalog ? StorageOperation.Open : StorageOperation.Read, args[0]);
        var assessment = await assessor.AssessAsync(request);
        results.Add(new { assessment, policy = StorageLocationPolicy.Evaluate(request, assessment, DateTimeOffset.UtcNow) });
    }
}
finally { assessor.Dispose(); }
var after = Native.Count();
var disposed = await assessor.AssessAsync(new(Guid.NewGuid(), 0, StorageRole.MediaSource, StorageOperation.Read, args[0]));
var options = new JsonSerializerOptions {WriteIndented = true};
options.Converters.Add(new JsonStringEnumConverter());
File.WriteAllText(args[2], JsonSerializer.Serialize(new {agent="LF-BOTH-RES-007", results, baseline, after,
    disposedStatus=disposed.Status, cleanupPassed=baseline==after && disposed.Status==StorageAssessmentStatus.Failed}, options));
return baseline == after ? 0 : 1;
static class Native
{
    [DllImport("libLightflowStorage.dylib", EntryPoint="lf_storage_active_descriptors")]
    public static extern int Count();
}
