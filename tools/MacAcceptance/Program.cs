using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lightflow.Application;
using Lightflow.Domain;
using Lightflow.Platform.MacOS;
using LightflowStudio;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;

// Development workflow only. Never imported by the application.
try
{
    if (!OperatingSystem.IsMacOS() || args.Length != 3 || args[0] is not ("initialize" or "check"))
        throw new ArgumentException("initialize|check <absolute task data root> <absolute packaged executable>");
    var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    var root = Path.GetFullPath(args[1]);
    var persistent = Path.Combine(repo, "acceptance/persistent");
    var disposable = Path.Combine(repo, "artifacts/lf036-acceptance-workflow/disposable") + "/";
    if (!Path.IsPathFullyQualified(args[1]) || (root != persistent && !root.StartsWith(disposable, StringComparison.Ordinal)))
        throw new IOException("Only this assignment's retained acceptance root or explicit disposable workflow roots are allowed.");
    var executable = Path.GetFullPath(args[2]);
    if (!Path.IsPathFullyQualified(args[2]) || !executable.StartsWith(Path.Combine(repo, "artifacts") + "/", StringComparison.Ordinal) || !File.Exists(executable))
        throw new IOException("Supply the freshly packaged task executable.");
    // Bind inspection code to the actual package. Unknown/new composition requires review.
    foreach (var name in new[] { "Lightflow.Platform.MacOS.dll", "Lightflow.Catalog.dll", "Lightflow.Application.dll", "Lightflow.Domain.dll", "libLightflowStorage.dylib" })
        if (!SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name))).SequenceEqual(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(executable)!, name)))))
            throw new IOException("Preflight/package mismatch: " + name + ". Build the helper from the package's exact reviewed source.");
    var native = new MacCatalogNativeEffects(root);
    native.RequireUnlinked(root);
    var catalog = Path.Combine(root, "Lightflow Test");
    if (args[0] == "initialize" && !Directory.Exists(root) && !File.Exists(root))
    {
        Directory.CreateDirectory(catalog);
        // Supported production creation owns identity, schema, admission and selection.
        await using var shell = new MacShellCatalogSession(root);
        var created = await shell.CreateAsync(catalog);
        if (!created.IsReady) throw new IOException("Initialization refused; preserve partial state: " + created.Diagnostic);
        Console.Error.WriteLine("Created Lightflow Test through MacShellCatalogSession.CreateAsync. Retain this data root.");
    }
    // Existing and partial roots are never initialized, reset, repaired or migrated here.
    var settings = JsonSerializer.Deserialize<MacCatalogLifecycleEffects.Snapshot>(File.ReadAllText(Path.Combine(root, "settings.json")))
        ?? throw new IOException("Missing configuration.");
    if (settings.CatalogDirectory != catalog || settings.CatalogId is null || settings.PreviewsDirectory is not null)
        throw new IOException("Unexpected data-root configuration; preserve it for investigation.");
    var locations = new MacCatalogLocations(root, catalog);
    native.RequireUnlinked(catalog);
    using var profileLease = ReadLease(StartupSessionCompletion.PathFor(root));
    using var directoryLease = ReadLease(catalog, true);
    using var catalogLease = ReadLease(StartupStoreEvidence.JournalPath(locations.CatalogDatabasePath));
    using var journalInput = new FileStream(catalogLease, FileAccess.Read);
    var envelope = JsonDocument.Parse(journalInput).RootElement;
    var payload = envelope.GetProperty("Payload").GetString()!;
    if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))) != envelope.GetProperty("Checksum").GetString())
        throw new IOException("Uncertain ownership evidence checksum.");
    var certificate = JsonDocument.Parse(payload).RootElement;
    if (!Guid.TryParseExact(certificate.GetProperty("Session").GetString(), "N", out _) ||
        certificate.GetProperty("Version").GetInt32() != 1 || certificate.GetProperty("Store").GetString() != "Catalog" ||
        certificate.GetProperty("Database").GetString() != locations.CatalogDatabasePath ||
        certificate.GetProperty("State").GetString() is not ("Validated" or "Clean"))
        throw new IOException("Uncertain ownership evidence; investigate a disposable clone.");
    // Catalog-only Mac completion deliberately remains Validated/Dirty. Never forge Clean.
    var pending = CatalogTransferJournal.InspectPending(native, locations);
    if (pending is not null) throw new IOException(pending);
    using var assessor = new MacStorageLocationAssessor(new[] { locations.PreviewsDirectory, locations.TemporaryDirectory });
    var request = new StorageAssessmentRequest(Guid.NewGuid(), 1, StorageRole.ActiveCatalog, StorageOperation.Open, catalog);
    var admission = StorageLocationPolicy.Evaluate(request, await assessor.AssessAsync(request), DateTimeOffset.UtcNow);
    if (admission.Decision != StorageLocationDecision.Eligible) throw new IOException(admission.Diagnostic);
    var expected = new CatalogDatabaseService(locations).CurrentSchemaVersion;
    var identity = Inspect(locations.CatalogDatabasePath, expected, 0x4C465343, native, true);
    if (identity != settings.CatalogId.Value.ToString()) throw new IOException("Configured Catalog identity mismatch.");
    var preview = File.Exists(locations.PreviewsDatabasePath);
    if (preview) Inspect(locations.PreviewsDatabasePath, CatalogPreviewStoreService.SchemaVersion, CatalogPreviewStoreService.SqliteApplicationId, native, false);
    Console.WriteLine(JsonSerializer.Serialize(new { passed = true, root, catalogName = "Lightflow Test", catalog, catalogId = identity,
        catalogSchema = expected, previewSchema = preview ? CatalogPreviewStoreService.SchemaVersion : (int?)null,
        previewAbsent = !preview, storage = admission.Reason.ToString(), evidence = certificate.GetProperty("State").GetString(),
        recovery = "No pending transfer or nonempty SQLite recovery files; full application admission/deep startup remains authoritative", executable,
        executableSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))), checkedUtc = DateTimeOffset.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception error) { Console.Error.WriteLine("REFUSED; no reset, repair or migration attempted: " + error.Message); return 1; }

static SafeFileHandle ReadLease(string path, bool directory = false)
{
    var fd = Native.Open(path, 0, 0, 2, directory ? 1 : 0);
    if (fd < 0) throw new IOException("Read-only exclusive lease unavailable: " + path);
    return new SafeFileHandle((IntPtr)fd, true);
}
static string? Inspect(string path, int schema, int app, MacCatalogNativeEffects native, bool catalog)
{
    native.RequireUnlinked(path);
    foreach (var suffix in new[] { "-wal", "-shm", "-journal" }) native.RequireUnlinked(path + suffix);
    foreach (var suffix in new[] { "-wal", "-journal" })
        if (File.Exists(path + suffix) && new FileInfo(path + suffix).Length != 0) throw new IOException("SQLite recovery companion is nonempty: " + path + suffix);
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = new Uri(path).AbsoluteUri + "?immutable=1", Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    connection.Open();
    object Scalar(string sql) { using var cmd = connection.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar()!; }
    if (Convert.ToInt32(Scalar("PRAGMA user_version")) != schema || Convert.ToInt32(Scalar("PRAGMA application_id")) != app)
        throw new IOException("Schema/application identity mismatch; migration is not authorized: " + path);
    if ((string)Scalar("PRAGMA integrity_check") != "ok") throw new IOException("Integrity uncertain; preserve data.");
    if (!catalog) return null;
    if ((string)Scalar("SELECT ApplicationIdentity FROM CatalogInfo WHERE SingletonId=1") != "LightflowStudio.Catalog") throw new IOException("Wrong Catalog identity.");
    return Guid.Parse((string)Scalar("SELECT CatalogId FROM CatalogInfo WHERE SingletonId=1")).ToString();
}
static class Native
{
    [DllImport("libLightflowStorage.dylib", EntryPoint = "lf_cat_open", SetLastError = true)]
    internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int create, int writable, int mode, int directory);
}
