using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lightflow.Application;
using Lightflow.Domain;
using Lightflow.Platform.MacOS;

if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Native host requires macOS.");
if (args.Length < 2 || args[0] != "--data-root" || !Path.IsPathFullyQualified(args[1]))
    throw new ArgumentException("Use --data-root <absolute new task-owned fixture directory> [--probe <existing location>].");
var root = args[1];
if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Fixture root must be new; existing data is never reused.");
Directory.CreateDirectory(root);
var catalog = Directory.CreateDirectory(Path.Combine(root, "catalog")).FullName;
var cache = Directory.CreateDirectory(Path.Combine(root, "cache")).FullName;
var temporary = Directory.CreateDirectory(Path.Combine(root, "temporary")).FullName;
var existing = Path.Combine(catalog, "catalog-like.bin");
File.WriteAllBytes(existing, "LF006 immutable synthetic catalog-like fixture; no database schema\n"u8.ToArray());
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
var originalHash = Hash(existing);
var results = new List<object>();
var failures = new List<string>();
void Check(string name, bool passed) { results.Add(new { name, passed }); if (!passed) failures.Add(name); }
using var assessor = new MacStorageLocationAssessor([cache, temporary]);
async Task<StorageLocationAssessment> Observe(string name, string path, StorageRole role = StorageRole.ActiveCatalog,
    StorageOperation operation = StorageOperation.Open, StorageLocationReason? reason = null)
{
    var request = new StorageAssessmentRequest(Guid.NewGuid(), 1, role, operation, path);
    var assessment = await assessor.AssessAsync(request);
    var evaluated = StorageLocationPolicy.Evaluate(request, assessment, DateTimeOffset.UtcNow);
    results.Add(new { name, evidence = "native", assessment, evaluated });
    Console.WriteLine(JsonSerializer.Serialize(new { name, evaluated.Reason, assessment.ProviderDiagnostic }));
    Check(name + ": request preserved", ReferenceEquals(request, assessment.Request));
    if (reason is not null) Check(name + ": expected reason", evaluated.Reason == reason);
    return assessment;
}
var local = await Observe("APFS directory", catalog, reason: StorageLocationReason.Eligible);
await Observe("APFS catalog-like file", existing, reason: StorageLocationReason.Eligible);
var missing = Path.Combine(catalog, "new-catalog.db");
await Observe("missing creation leaf", missing, operation: StorageOperation.Create, reason: StorageLocationReason.Eligible);
Check("assessment never creates intended leaf", !File.Exists(missing) && !Directory.Exists(missing));
await Observe("missing parent", Path.Combine(root, "absent-parent", "db"), operation: StorageOperation.Create, reason: StorageLocationReason.AssessmentFailed);
await Observe("missing open is not first run", missing, reason: StorageLocationReason.AssessmentFailed);
var fresh = await assessor.AssessAsync(local.Request);
var revalidated = StorageLocationPolicy.Revalidate(local.Request, local, fresh, DateTimeOffset.UtcNow);
results.Add(new { name = "fresh native revalidation", local, fresh, revalidated });
Check("unchanged native identity revalidates", revalidated.Reason == StorageLocationReason.Eligible);
Check("new assessment identity", fresh.AssessmentId != local.AssessmentId);
Check("same mount anchor", fresh.Identity?.VolumeIdentity == local.Identity?.VolumeIdentity);
Check("reused snapshot refused", StorageLocationPolicy.Revalidate(local.Request, local, local, DateTimeOffset.UtcNow).Reason == StorageLocationReason.StaleAssessment);
Check("new operation refused borrowed snapshot", StorageLocationPolicy.Evaluate(local.Request with { OperationId = Guid.NewGuid() }, local, DateTimeOffset.UtcNow).Reason == StorageLocationReason.RequestMismatch);
Check("expiry refused", StorageLocationPolicy.Evaluate(local.Request, local, local.ExpiresAtUtc).Reason == StorageLocationReason.StaleAssessment);
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    var cancelled = await assessor.AssessAsync(local.Request, cancellation.Token);
    Check("native cancellation prevents admission", StorageLocationPolicy.Evaluate(local.Request, cancelled, DateTimeOffset.UtcNow).Decision == StorageLocationDecision.Cancelled);
}
var readonlyDirectory = Directory.CreateDirectory(Path.Combine(root, "read-only")).FullName;
File.SetUnixFileMode(readonlyDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
try { await Observe("read-only permissions", readonlyDirectory, reason: StorageLocationReason.ReadOnly); }
finally { File.SetUnixFileMode(readonlyDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
var denied = Directory.CreateDirectory(Path.Combine(root, "denied")).FullName;
var deniedFile = Path.Combine(denied, "existing.bin");
File.WriteAllText(deniedFile, "LF006 inaccessible fixture");
File.SetUnixFileMode(denied, UnixFileMode.None);
try { await Observe("permission denied does not mean first run", deniedFile, reason: StorageLocationReason.AssessmentFailed); }
finally { File.SetUnixFileMode(denied, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
var unrelated = Directory.CreateDirectory(Path.Combine(root, "unrelated")).FullName;
var link = Path.Combine(root, "link");
Directory.CreateSymbolicLink(link, catalog);
var linked = await Observe("local symlink", link, reason: StorageLocationReason.UnresolvedAliases);
Directory.Delete(link);
Directory.CreateSymbolicLink(link, unrelated);
var replaced = await assessor.AssessAsync(linked.Request);
Check("replaced symlink continuation rejected", StorageLocationPolicy.Revalidate(linked.Request, linked, replaced, DateTimeOffset.UtcNow).Decision == StorageLocationDecision.Rejected);
Check("replaced symlink recorded new target", linked.Identity?.LocationIdentity != replaced.Identity?.LocationIdentity);
var chain = Path.Combine(root, "chain");
Directory.CreateSymbolicLink(chain, link);
await Observe("symlink chain", chain, reason: StorageLocationReason.UnresolvedAliases);
var broken = Path.Combine(root, "broken");
File.CreateSymbolicLink(broken, Path.Combine(root, "never-created"));
await Observe("broken symlink", broken, operation: StorageOperation.Create, reason: StorageLocationReason.AssessmentFailed);
await Observe("protected cache containment", cache, reason: StorageLocationReason.AmbiguousContainment);
var nfc = Path.Combine(catalog, "caf\u00e9.bin");
var nfd = Path.Combine(catalog, "cafe\u0301.bin");
File.WriteAllText(nfc, "NFC synthetic bytes");
var a = await Observe("NFC exact request", nfc);
var b = await Observe("NFD exact request", nfd);
Check("Unicode request spelling retained", a.Request.RequestedLocation == nfc && b.Request.RequestedLocation == nfd);
var caseA = Path.Combine(catalog, "Case.bin");
File.WriteAllText(caseA, "case fixture");
await Observe("case original request", caseA);
await Observe("case alternate request", Path.Combine(catalog, "case.bin"));
for (var i = 0; i < 100; i++)
{
    var repeated = await assessor.AssessAsync(local.Request);
    if (repeated.Status != StorageAssessmentStatus.Complete) failures.Add("repeated native assessment " + i);
}
Check("catalog-like bytes unchanged", Hash(existing) == originalHash);
// Controlled immutable fact changes exercise the accepted policy, not native hardware evidence.
void Controlled(string name, Func<StorageLocationAssessment, StorageLocationAssessment> change, StorageLocationReason expected)
{
    var current = change(fresh with { AssessmentId = Guid.NewGuid(), AssessedAtUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(5) });
    var result = StorageLocationPolicy.Revalidate(local.Request, local, current, DateTimeOffset.UtcNow);
    results.Add(new { name, evidence = "controlled shared facts; native scenario UNQUALIFIED", current, result });
    Check(name, result.Reason == expected);
}
Controlled("changed mount", s => s with { Identity = s.Identity! with { VolumeIdentity = "different mount epoch" } }, StorageLocationReason.VolumeChanged);
Controlled("changed capability", s => s with { Capabilities = s.Capabilities! with { Write = StorageCapability.Unsupported } }, StorageLocationReason.ReadOnly);
Controlled("unknown filesystem qualification", s => s with { Capabilities = s.Capabilities! with { QualifiedCatalogFileSystem = StorageCapability.Unknown } }, StorageLocationReason.MissingCapabilities);
Controlled("unknown locality", s => s with { Locality = StorageLocality.Unknown }, StorageLocationReason.UnknownLocality);
Controlled("unavailable mount", s => s with { VolumeAvailability = StorageAvailability.Unavailable }, StorageLocationReason.LocationUnavailable);
Controlled("provider failure", s => s with { Status = StorageAssessmentStatus.Failed }, StorageLocationReason.AssessmentFailed);
Controlled("network active Catalog", s => s with { Locality = StorageLocality.Network }, StorageLocationReason.NetworkActiveCatalog);
foreach (var role in new[] { StorageRole.MediaSource, StorageRole.ClosedCatalogBackupTransfer })
{
    var request = local.Request with { OperationId = Guid.NewGuid(), Role = role, Operation = StorageOperation.Read };
    var assessment = fresh with { Request = request, Locality = StorageLocality.Network };
    var result = StorageLocationPolicy.Evaluate(request, assessment, DateTimeOffset.UtcNow);
    results.Add(new { name = role.ToString(), evidence = "controlled facts; native network UNQUALIFIED", result });
    Check(role.ToString(), result.Decision == (role == StorageRole.MediaSource ? StorageLocationDecision.Eligible : StorageLocationDecision.RequiresVerifiedStaging));
}
if (args.Length == 4 && args[2] == "--probe")
{
    foreach (var role in new[] { StorageRole.ActiveCatalog, StorageRole.MediaSource, StorageRole.ClosedCatalogBackupTransfer })
    {
        var observed = await Observe("optional read-only native location probe", args[3], role, role == StorageRole.ActiveCatalog ? StorageOperation.Open : StorageOperation.Read);
        var evaluated = StorageLocationPolicy.Evaluate(observed.Request, observed, DateTimeOffset.UtcNow);
        if (observed.Locality == StorageLocality.Network)
            Check("native network " + role, evaluated.Decision == (role == StorageRole.ActiveCatalog
                ? StorageLocationDecision.Rejected : role == StorageRole.MediaSource ? StorageLocationDecision.Eligible : StorageLocationDecision.RequiresVerifiedStaging));
    }
    var networkLink = Path.Combine(root, "local-looking-mounted-target");
    Directory.CreateSymbolicLink(networkLink, args[3]);
    await Observe("local-looking symlink to supplied mount", networkLink);
}
assessor.Dispose();
var disposed = await assessor.AssessAsync(local.Request);
Check("disposed provider fails closed", disposed.Status == StorageAssessmentStatus.Failed);
var options = new JsonSerializerOptions { WriteIndented = true };
options.Converters.Add(new JsonStringEnumConverter());
var manifest = new { agent = "LF-MAC-DEV-006", utc = DateTimeOffset.UtcNow, root, originalHash,
    finalHash = Hash(existing), results, failures,
    limitations = new[] { "Physical external volume, SMB, force-unmount and power-loss are UNQUALIFIED unless separately recorded.",
        "Mount anchors must be disposed before ordinary eject. Native calls are not interruptible.", "Standalone host does not grant Catalog creation or writer ownership." } };
var output = Path.Combine(root, "results.json");
File.WriteAllText(output, JsonSerializer.Serialize(manifest, options));
Console.WriteLine(JsonSerializer.Serialize(new { output, passed = failures.Count == 0, failures }, options));
return failures.Count == 0 ? 0 : 1;
