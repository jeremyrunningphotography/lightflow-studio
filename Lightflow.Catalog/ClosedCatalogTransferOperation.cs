using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Lightflow.Application;
using Lightflow.Domain;

namespace LightflowStudio;

/// <summary>Shared closed-byte workflow over native effects. Existing lifecycle owns SQLite, mutation, activation and configuration.</summary>
public class ClosedCatalogTransferOperation : IClosedCatalogTransferOperation
{
    private readonly ILightflowStorageLocations _locations;
    private readonly ICatalogTransferNativeEffects _effects;
    private readonly CatalogTransferFiles _files;
    private readonly string _destination;
    private readonly string _sourceDirectory;
    private StorageRole _sourceRole;
    private readonly Func<CancellationToken, Task> _quiesce;
    private readonly Func<ClosedCatalogTransferRequest, string, CancellationToken, Task<ClosedCatalogManifest>> _snapshot;
    private readonly Func<CancellationToken, Task<ClosedCatalogTransferReceipt>> _activate;
    private readonly Func<CancellationToken, Task<ClosedCatalogTransferReceipt>> _commit;
    private readonly Func<CancellationToken, Task> _complete;
    private readonly Func<CancellationToken, Task<bool>> _rollback;
    private readonly ICatalogStorageAssessor _sourceAssessor;
    private readonly ICatalogStorageAssessor _destinationAssessor;
    private readonly List<FileStream> _closedGuards = [];
    private ClosedCatalogManifest? _manifest;
    private ClosedCatalogTransferRequest _request = null!;
    private StorageLocationAssessment? _sourceEvidence, _destinationEvidence;
    private string _local = "", _stage = "";
    private ICatalogDirectoryLease? _stageLease;
    private string? _publishedIdentity;
    private readonly string _previousConfiguration, _intendedConfiguration;
    public string PublishedPath => _destination;
    public DateTimeOffset SnapshotUtc { get; private set; }

    public ClosedCatalogTransferOperation(ICatalogTransferNativeEffects effects, ILightflowStorageLocations locations, string destination,
        string previousConfiguration, string intendedConfiguration,
        Func<CancellationToken, Task> quiesce,
        Func<ClosedCatalogTransferRequest, string, CancellationToken, Task<ClosedCatalogManifest>> snapshot,
        Func<CancellationToken, Task<ClosedCatalogTransferReceipt>>? activate = null,
        Func<CancellationToken, Task<ClosedCatalogTransferReceipt>>? commit = null,
        Func<CancellationToken, Task>? complete = null, Func<CancellationToken, Task<bool>>? rollback = null,
        string? sourceDirectory = null)
    {
        _effects = effects; _files = new(effects);
        _locations = locations; _destination = Path.GetFullPath(destination);
        _sourceDirectory = sourceDirectory ?? locations.CatalogDirectory;
        _sourceRole = sourceDirectory is null ? StorageRole.ActiveCatalog : StorageRole.ClosedCatalogBackupTransfer;
        _previousConfiguration = previousConfiguration; _intendedConfiguration = intendedConfiguration;
        _quiesce = quiesce; _snapshot = snapshot;
        _activate = activate ?? (_ => throw new InvalidOperationException("Transport artifacts cannot activate a Catalog."));
        _commit = commit ?? (_ => throw new InvalidOperationException("Transport artifacts cannot change Catalog configuration."));
        _complete = complete ?? (_ => Task.CompletedTask); _rollback = rollback ?? (_ => Task.FromResult(true));
        _sourceAssessor = effects.CreateAssessor([locations.PreviewsDirectory, locations.TemporaryDirectory]);
        _destinationAssessor = effects.CreateAssessor([locations.PreviewsDirectory, locations.TemporaryDirectory, locations.CatalogDirectory]);
    }
    public async Task<ClosedCatalogTransferEvidence> PreflightAsync(ClosedCatalogTransferRequest request, CancellationToken token)
    {
        _request = request;
        if (request.Mode == ClosedCatalogTransferMode.MoveAndActivate) _sourceRole = StorageRole.ClosedCatalogBackupTransfer;
        _effects.RequireIsolatedDestination(_locations, _destination);
        _effects.RequireUnlinked(_destination);
        var moving = request.Mode == ClosedCatalogTransferMode.MoveAndActivate;
        if (moving && (Directory.Exists(_destination) || File.Exists(_destination)))
            throw new IOException("The resulting Catalog folder already exists. Choose another name or parent folder.");
        _sourceEvidence = await Assess(_sourceAssessor, _sourceDirectory, _sourceRole, null, token).ConfigureAwait(false);
        _destinationEvidence = await Assess(_destinationAssessor, _destination,
            moving || request.Mode == ClosedCatalogTransferMode.LocalCopyback ? StorageRole.ActiveCatalog : StorageRole.ClosedCatalogBackupTransfer, null, token).ConfigureAwait(false);
        if (moving && _destinationEvidence.Capabilities?.QualifiedCatalogFileSystem != StorageCapability.Supported)
            throw new IOException("Atomic active-Catalog publication is not qualified for this destination.");
        // The selected parent is pinned by the native assessor. Do not create the active destination before publication.
        _local = Path.Combine(CatalogTransferJournal.DirectoryPath(_effects, _locations), request.OperationId.ToString("N") + ".snapshot");
        _stage = moving ? Path.Combine(Path.GetDirectoryName(_destination)!, $".lightflow-{request.OperationId:N}.incomplete") : _destination;
        return new(_sourceEvidence, _destinationEvidence, _previousConfiguration, _intendedConfiguration,
            moving ? ClosedCatalogPublicationMode.AtomicDirectory : ClosedCatalogPublicationMode.VerifiedContainer);
    }
    private async Task<StorageLocationAssessment> Assess(ICatalogStorageAssessor assessor, string path,
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
            _closedGuards.Add(_files.OpenClosedRead(_locations.CatalogDatabasePath));
        }
        _effects.CreateExclusiveDirectory(_local);
        using var stagingAssessor = _effects.CreateAssessor([_locations.CatalogDirectory, _locations.PreviewsDirectory, _locations.TemporaryDirectory]);
        await Assess(stagingAssessor, _local, StorageRole.ActiveCatalog, null, token).ConfigureAwait(false);
        var manifest = await _snapshot(_request, _local, token).ConfigureAwait(false);
        SnapshotUtc = manifest.SnapshotUtc;
        foreach (var artifact in manifest.Artifacts) _closedGuards.Add(_files.OpenClosedRead(_files.Child(_local, artifact.RelativeName)));
        var required = manifest.Artifacts.Sum(a => checked(a.ByteLength));
        // Native volume free-space evidence is advisory; all actual writes remain checked.
        if (_effects.AvailableSpace(Path.GetDirectoryName(_destination)!) is not { } available || available < (ulong)required)
            throw new IOException("The transfer destination has insufficient or unverified free space.");
        return manifest;
    }
    public async Task StageAsync(ClosedCatalogManifest manifest, CancellationToken token)
    {
        _manifest = manifest;
        await RevalidateAsync(token).ConfigureAwait(false);
        _effects.CreateExclusiveDirectory(_stage);
        _stageLease = _effects.AcquireDirectoryLease(_stage, manifest.PublicationMode == ClosedCatalogPublicationMode.AtomicDirectory);
        _files.WriteExclusive(Path.Combine(_stage, "pending.json"), JsonSerializer.SerializeToUtf8Bytes(_request));
        foreach (var artifact in manifest.Artifacts)
        {
            await RevalidateAsync(token).ConfigureAwait(false);
            var destination = _files.Child(_stage, artifact.RelativeName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = _files.OpenClosedRead(_files.Child(_local, artifact.RelativeName));
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
        _effects.RequireUnlinked(_local.Length == 0 ? _locations.ApplicationDataDirectory : _local);
        _effects.RequireUnlinked(_stage.Length == 0 ? _destination : _stage);
        _stageLease?.Revalidate(_stage);
        if (_publishedIdentity is not null) _effects.RequireDirectoryIdentity(_stage, _publishedIdentity);
    }
    public Task<IReadOnlyList<string>> ListStagedArtifactsAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _files.ValidateDirectorySet(_stage, _manifest!);
        IReadOnlyList<string> files = _files.EnumerateRegularFiles(_stage).Select(p => Path.GetRelativePath(_stage, p).Replace('\\', '/'))
            .Where(p => p is not ("pending.json" or "manifest.json" or "ready.json")).ToArray();
        return Task.FromResult(files);
    }
    public Task<Stream> OpenStagedReadAsync(string relativeName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(_files.OpenClosedRead(_files.Child(_stage, relativeName)));
    }
    public async Task<ClosedCatalogTransferReceipt> PublishAsync(ClosedCatalogManifest manifest, ClosedCatalogReadiness readiness, CancellationToken token)
    {
        await RevalidateAsync(token).ConfigureAwait(false);
        ClosedCatalogManifestFormat.ValidateReadiness(manifest, readiness);
        _files.WriteExclusive(Path.Combine(_stage, "manifest.json"), ClosedCatalogManifestFormat.Serialize(manifest));
        _files.WriteExclusive(Path.Combine(_stage, "ready.json"), JsonSerializer.SerializeToUtf8Bytes(readiness));
        _files.VerifyMetadata(_stage, manifest, readiness);
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
        _files.VerifyMetadata(_stage, manifest, readiness);
        return new($"{manifest.PublicationMode}; files flushed/readback verified; namespace={_destination}; hardware power-loss durability unqualified");
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
}
