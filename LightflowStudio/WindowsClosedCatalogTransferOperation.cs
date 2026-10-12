using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Lightflow.Application;
using Lightflow.Domain;

namespace LightflowStudio;

/// <summary>Windows byte effects only. SQLite, mutation, configuration and activation remain delegated owners.</summary>
internal sealed class WindowsClosedCatalogTransferOperation : IClosedCatalogTransferOperation
{
    private readonly LightflowStorageLocations _locations;
    private readonly string _destination;
    private readonly string _sourceDirectory;
    private StorageRole _sourceRole;
    private readonly Func<CancellationToken, Task> _quiesce;
    private readonly Func<ClosedCatalogTransferRequest, string, CancellationToken, Task<ClosedCatalogManifest>> _snapshot;
    private readonly Func<CancellationToken, Task<ClosedCatalogTransferReceipt>> _activate;
    private readonly Func<CancellationToken, Task<ClosedCatalogTransferReceipt>> _commit;
    private readonly Func<CancellationToken, Task> _complete;
    private readonly Func<CancellationToken, Task<bool>> _rollback;
    private readonly WindowsStorageLocationAssessor _sourceAssessor;
    private readonly WindowsStorageLocationAssessor _destinationAssessor;
    private readonly List<FileStream> _closedGuards = [];
    private ClosedCatalogManifest? _manifest;
    private ClosedCatalogTransferRequest _request = null!;
    private StorageLocationAssessment? _sourceEvidence, _destinationEvidence;
    private string _local = "", _stage = "";
    private WindowsStorageLocationAssessor.TransferDirectoryLease? _stageLease;
    private string? _publishedIdentity;
    private readonly string _previousConfiguration, _intendedConfiguration;
    internal string PublishedPath => _destination;
    internal DateTimeOffset SnapshotUtc { get; private set; }

    internal WindowsClosedCatalogTransferOperation(LightflowStorageLocations locations, string destination,
        string previousConfiguration, string intendedConfiguration,
        Func<CancellationToken, Task> quiesce,
        Func<ClosedCatalogTransferRequest, string, CancellationToken, Task<ClosedCatalogManifest>> snapshot,
        Func<CancellationToken, Task<ClosedCatalogTransferReceipt>>? activate = null,
        Func<CancellationToken, Task<ClosedCatalogTransferReceipt>>? commit = null,
        Func<CancellationToken, Task>? complete = null, Func<CancellationToken, Task<bool>>? rollback = null,
        string? sourceDirectory = null)
    {
        _locations = locations; _destination = Path.GetFullPath(destination);
        _sourceDirectory = sourceDirectory ?? locations.CatalogDirectory;
        _sourceRole = sourceDirectory is null ? StorageRole.ActiveCatalog : StorageRole.ClosedCatalogBackupTransfer;
        _previousConfiguration = previousConfiguration; _intendedConfiguration = intendedConfiguration;
        _quiesce = quiesce; _snapshot = snapshot;
        _activate = activate ?? (_ => throw new InvalidOperationException("Transport artifacts cannot activate a Catalog."));
        _commit = commit ?? (_ => throw new InvalidOperationException("Transport artifacts cannot change Catalog configuration."));
        _complete = complete ?? (_ => Task.CompletedTask); _rollback = rollback ?? (_ => Task.FromResult(true));
        _sourceAssessor = new([locations.PreviewsDirectory, locations.TemporaryDirectory]);
        _destinationAssessor = new([locations.PreviewsDirectory, locations.TemporaryDirectory, locations.CatalogDirectory]);
    }
    public async Task<ClosedCatalogTransferEvidence> PreflightAsync(ClosedCatalogTransferRequest request, CancellationToken token)
    {
        _request = request;
        if (request.Mode == ClosedCatalogTransferMode.MoveAndActivate) _sourceRole = StorageRole.ClosedCatalogBackupTransfer;
        if (_locations.IsIsolated) ApplicationDataProfile.RequireContained(_locations.ApplicationDataDirectory, _destination);
        ClosedCatalogTransferJournal.RequireUnlinked(_destination);
        var moving = request.Mode == ClosedCatalogTransferMode.MoveAndActivate;
        if (moving && (Directory.Exists(_destination) || File.Exists(_destination)))
            throw new IOException("The resulting Catalog folder already exists. Choose another name or parent folder.");
        _sourceEvidence = await Assess(_sourceAssessor, _sourceDirectory, _sourceRole, null, token).ConfigureAwait(false);
        _destinationEvidence = await Assess(_destinationAssessor, _destination,
            moving || request.Mode == ClosedCatalogTransferMode.LocalCopyback ? StorageRole.ActiveCatalog : StorageRole.ClosedCatalogBackupTransfer, null, token).ConfigureAwait(false);
        if (moving && _destinationEvidence.Capabilities?.QualifiedCatalogFileSystem != StorageCapability.Supported)
            throw new IOException("Atomic active-Catalog publication is not qualified for this destination.");
        // The selected parent is pinned by the native assessor. Do not create the active destination before publication.
        _local = Path.Combine(ClosedCatalogTransferJournal.DirectoryPath(_locations), request.OperationId.ToString("N") + ".snapshot");
        _stage = moving ? Path.Combine(Path.GetDirectoryName(_destination)!, $".lightflow-{request.OperationId:N}.incomplete") : _destination;
        return new(_sourceEvidence, _destinationEvidence, _previousConfiguration, _intendedConfiguration,
            moving ? ClosedCatalogPublicationMode.AtomicDirectory : ClosedCatalogPublicationMode.VerifiedContainer);
    }
    private async Task<StorageLocationAssessment> Assess(WindowsStorageLocationAssessor assessor, string path,
        StorageRole role, StorageLocationAssessment? prior, CancellationToken token)
    {
        var request = new StorageAssessmentRequest(_request.OperationId, _request.Generation, role,
            role == StorageRole.ActiveCatalog ? StorageOperation.Open : path == _sourceDirectory ? StorageOperation.Read : StorageOperation.Write, path);
        var assessment = await assessor.AssessAsync(request, token).ConfigureAwait(false);
        var result = prior is null ? StorageLocationPolicy.Evaluate(request, assessment, DateTimeOffset.UtcNow, token)
            : StorageLocationPolicy.Revalidate(request, prior, assessment, DateTimeOffset.UtcNow, token);
        if (result.Decision is not (StorageLocationDecision.Eligible or StorageLocationDecision.RequiresVerifiedStaging))
            throw new IOException($"{result.Diagnostic} Transfer boundary {path}; role {role}; {assessment.ProviderDiagnostic}");
        return assessment;
    }
    public Task QuiesceAsync(CancellationToken token) => _quiesce(token);
    public async Task<ClosedCatalogManifest> FinalizeSnapshotAsync(CancellationToken token)
    {
        await RevalidateAsync(token).ConfigureAwait(false);
        if (_request.Mode == ClosedCatalogTransferMode.MoveAndActivate)
        {
            // Acquire after the existing session/read owner has closed; refuses a foreign open writer.
            _closedGuards.Add(OpenClosedRead(_locations.CatalogDatabasePath));
        }
        CreateExclusiveDirectory(_local);
        using var stagingAssessor = new WindowsStorageLocationAssessor([_locations.CatalogDirectory, _locations.PreviewsDirectory, _locations.TemporaryDirectory]);
        await Assess(stagingAssessor, _local, StorageRole.ActiveCatalog, null, token).ConfigureAwait(false);
        var manifest = await _snapshot(_request, _local, token).ConfigureAwait(false);
        SnapshotUtc = manifest.SnapshotUtc;
        foreach (var artifact in manifest.Artifacts) _closedGuards.Add(OpenClosedRead(Child(_local, artifact.RelativeName)));
        var required = manifest.Artifacts.Sum(a => checked(a.ByteLength));
        // Native volume free-space evidence is advisory; all actual writes remain checked.
        if (!GetDiskFreeSpaceExW(WindowsStorageLocationAssessor.NativePath(Path.GetDirectoryName(_destination)!), out var available, out _, out _) || available < (ulong)required)
            throw new IOException("The transfer destination has insufficient or unverified free space.");
        return manifest;
    }
    public async Task StageAsync(ClosedCatalogManifest manifest, CancellationToken token)
    {
        _manifest = manifest;
        await RevalidateAsync(token).ConfigureAwait(false);
        CreateExclusiveDirectory(_stage);
        _stageLease = new(_stage, manifest.PublicationMode == ClosedCatalogPublicationMode.AtomicDirectory);
        WriteExclusive(Path.Combine(_stage, "pending.json"), JsonSerializer.SerializeToUtf8Bytes(_request));
        foreach (var artifact in manifest.Artifacts)
        {
            await RevalidateAsync(token).ConfigureAwait(false);
            var destination = Child(_stage, artifact.RelativeName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = OpenClosedRead(Child(_local, artifact.RelativeName));
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await source.CopyToAsync(output, 128 * 1024, token).ConfigureAwait(false);
            output.Flush(true);
        }
    }
    public async Task RevalidateAsync(CancellationToken token)
    {
        _sourceEvidence = await Assess(_sourceAssessor, _sourceDirectory, _sourceRole, _sourceEvidence, token).ConfigureAwait(false);
        _destinationEvidence = await Assess(_destinationAssessor, _destination,
            _request.Mode is ClosedCatalogTransferMode.MoveAndActivate or ClosedCatalogTransferMode.LocalCopyback ? StorageRole.ActiveCatalog : StorageRole.ClosedCatalogBackupTransfer,
            _destinationEvidence, token).ConfigureAwait(false);
        ClosedCatalogTransferJournal.RequireUnlinked(_local.Length == 0 ? _locations.ApplicationDataDirectory : _local);
        ClosedCatalogTransferJournal.RequireUnlinked(_stage.Length == 0 ? _destination : _stage);
        _stageLease?.Revalidate(_stage);
        if (_publishedIdentity is not null) WindowsStorageLocationAssessor.RequireDirectoryIdentity(_stage, _publishedIdentity);
    }
    public Task<IReadOnlyList<string>> ListStagedArtifactsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateDirectorySet(_stage, _manifest!);
        IReadOnlyList<string> files = EnumerateRegularFiles(_stage).Select(p => Path.GetRelativePath(_stage, p).Replace('\\', '/'))
            .Where(p => p is not ("pending.json" or "manifest.json" or "ready.json")).ToArray();
        return Task.FromResult(files);
    }
    public Task<Stream> OpenStagedReadAsync(string relativeName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(OpenClosedRead(Child(_stage, relativeName)));
    }
    public async Task<ClosedCatalogTransferReceipt> PublishAsync(ClosedCatalogManifest manifest, ClosedCatalogReadiness readiness, CancellationToken token)
    {
        await RevalidateAsync(token).ConfigureAwait(false);
        ClosedCatalogManifestFormat.ValidateReadiness(manifest, readiness);
        WriteExclusive(Path.Combine(_stage, "manifest.json"), ClosedCatalogManifestFormat.Serialize(manifest));
        WriteExclusive(Path.Combine(_stage, "ready.json"), JsonSerializer.SerializeToUtf8Bytes(readiness));
        VerifyMetadata(_stage, manifest, readiness);
        if (manifest.PublicationMode == ClosedCatalogPublicationMode.AtomicDirectory)
        {
            // No replacement flag, same-volume stage beside final target, qualified local NTFS only.
            _stageLease!.PublishNoReplace(_destination);
            _publishedIdentity = _stageLease.Identity;
            _stageLease.Dispose(); _stageLease = null;
            _stage = _destination;
        }
        await RevalidateAsync(token).ConfigureAwait(false);
        await ClosedCatalogManifestFormat.VerifyAsync(manifest, this, token).ConfigureAwait(false);
        VerifyMetadata(_stage, manifest, readiness);
        return new($"{manifest.PublicationMode}; files flushed/readback verified; namespace={_destination}; hardware power-loss durability unqualified");
    }
    internal static void VerifyMetadata(string directory, ClosedCatalogManifest expected, ClosedCatalogReadiness readiness)
    {
        var pendingBytes = ReadBounded(Path.Combine(directory, "pending.json"));
        var pending = JsonSerializer.Deserialize<ClosedCatalogTransferRequest>(pendingBytes) ?? throw new InvalidDataException("Missing operation claim.");
        if (!JsonSerializer.SerializeToUtf8Bytes(pending).AsSpan().SequenceEqual(pendingBytes) ||
            pending.OperationId != expected.OperationId || pending.Generation != expected.Generation || pending.CatalogId != expected.CatalogId ||
            pending.SchemaVersion != expected.SchemaVersion || pending.ApplicationId != expected.ApplicationId || !Enum.IsDefined(pending.Mode))
            throw new InvalidDataException("Operation claim does not match the closed artifact manifest.");
        var actual = ClosedCatalogManifestFormat.Deserialize(ReadBounded(Path.Combine(directory, "manifest.json")));
        if (ClosedCatalogManifestFormat.Digest(actual) != ClosedCatalogManifestFormat.Digest(expected)) throw new InvalidDataException("Published manifest changed.");
        var bytes = ReadBounded(Path.Combine(directory, "ready.json"));
        var actualReady = JsonSerializer.Deserialize<ClosedCatalogReadiness>(bytes) ?? throw new InvalidDataException("Missing readiness receipt.");
        if (!JsonSerializer.SerializeToUtf8Bytes(actualReady).AsSpan().SequenceEqual(bytes) || actualReady != readiness)
            throw new InvalidDataException("Readiness receipt changed or is incomplete.");
        ClosedCatalogManifestFormat.ValidateReadiness(actual, actualReady);
    }
    public Task<ClosedCatalogTransferReceipt> ActivateAsync(CancellationToken token) => _activate(token);
    public Task<ClosedCatalogTransferReceipt> CommitConfigurationAsync(CancellationToken token) => _commit(token);
    public Task CompleteAsync(CancellationToken token) => _complete(token);
    public async Task<bool> RollbackAsync(CancellationToken token)
    {
        foreach (var guard in _closedGuards) guard.Dispose();
        _stageLease?.Dispose(); _stageLease = null;
        _closedGuards.Clear();
        // Artifacts are retained. A revoked path never causes a finally-delete or a retry against a new object.
        return await _rollback(token).ConfigureAwait(false);
    }
    public ValueTask DisposeAsync()
    {
        foreach (var guard in _closedGuards) guard.Dispose();
        _stageLease?.Dispose();
        _sourceAssessor.Dispose(); _destinationAssessor.Dispose();
        return ValueTask.CompletedTask;
    }
    internal static FileStream OpenClosedRead(string path)
    {
        ClosedCatalogTransferJournal.RequireUnlinked(path);
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try { WindowsStorageLocationAssessor.RequireSingleRegularFile(file.SafeFileHandle); return file; }
        catch { file.Dispose(); throw; }
    }
    internal static IEnumerable<string> EnumerateRegularFiles(string directory, string? ownedJournal = null)
    {
        ClosedCatalogTransferJournal.RequireUnlinked(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A transfer artifact is a link or junction.");
            if ((attributes & FileAttributes.Directory) != 0)
                foreach (var file in EnumerateRegularFiles(entry)) yield return file;
            else if (!string.Equals(entry, ownedJournal, StringComparison.OrdinalIgnoreCase))
            { using var guard = OpenClosedRead(entry); yield return entry; }
        }
    }
    internal static string Child(string directory, string relative)
    {
        if (!PortablePathIdentityValidator.ValidateRelativePath(relative).IsSafe) throw new InvalidDataException("Unsafe transfer artifact path.");
        return Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
    }
    internal static void ValidateDirectorySet(string directory, ClosedCatalogManifest manifest)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in manifest.Artifacts)
            for (var parent = Path.GetDirectoryName(artifact.RelativeName.Replace('/', Path.DirectorySeparatorChar)); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                allowed.Add(parent.Replace('\\', '/'));
        ValidateChildren(directory);
        void ValidateChildren(string parent)
        {
            foreach (var child in Directory.EnumerateDirectories(parent))
            {
                ClosedCatalogTransferJournal.RequireUnlinked(child);
                if (!allowed.Contains(Path.GetRelativePath(directory, child).Replace('\\', '/')))
                    throw new InvalidDataException("Unexpected transport directory.");
                ValidateChildren(child);
            }
        }
    }
    internal static byte[] ReadBounded(string path)
    {
        using var file = OpenClosedRead(path);
        if (file.Length > ClosedCatalogManifestFormat.MaximumMetadataBytes) throw new InvalidDataException("Transfer metadata exceeds its format bound.");
        var bytes = new byte[checked((int)file.Length)]; file.ReadExactly(bytes); return bytes;
    }
    internal static void WriteExclusive(string path, byte[] bytes)
    {
        ClosedCatalogTransferJournal.RequireUnlinked(path);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        file.Write(bytes); file.Flush(true);
    }
    internal static void CreateExclusiveDirectory(string path)
    {
        ClosedCatalogTransferJournal.RequireUnlinked(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!CreateDirectoryW(WindowsStorageLocationAssessor.NativePath(path), IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Operation-owned staging directory could not be exclusively created.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateDirectoryW(string path, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceExW(string directory, out ulong available, out ulong total, out ulong free);
}
