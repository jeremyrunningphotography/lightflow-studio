using System.Security.Cryptography;
using Lightflow.Domain;
using Xunit;

namespace Lightflow.Application.Tests;

public sealed class ClosedCatalogTransferTests
{
    private static readonly byte[] Payload = "closed immutable Catalog bytes"u8.ToArray();
    private static ClosedCatalogTransferRequest Request(ClosedCatalogTransferMode mode = ClosedCatalogTransferMode.MoveAndActivate) => new(Guid.NewGuid(), 7, mode, Guid.NewGuid(), 12, 123);
    private static ClosedCatalogManifest Manifest(ClosedCatalogTransferRequest request) => new(1, request.OperationId, request.Generation,
        request.CatalogId, request.SchemaVersion, request.ApplicationId, DateTimeOffset.UtcNow, "Existing snapshot owner validated identity/full integrity",
        request.Mode == ClosedCatalogTransferMode.MoveAndActivate ? ClosedCatalogPublicationMode.AtomicDirectory : ClosedCatalogPublicationMode.VerifiedContainer,
        1, [new(request.Mode == ClosedCatalogTransferMode.MoveAndActivate ? "LightflowCatalog.db" : "catalog.snapshot",
            ClosedCatalogArtifactRole.CatalogDatabase, Payload.Length, Convert.ToHexString(SHA256.HashData(Payload)))], []);

    [Theory]
    [InlineData(ClosedCatalogTransferMode.MoveAndActivate)]
    [InlineData(ClosedCatalogTransferMode.BackupTransport)]
    [InlineData(ClosedCatalogTransferMode.ClosedTransport)]
    [InlineData(ClosedCatalogTransferMode.LocalCopyback)]
    public async Task CompletionRequiresActualModeReceipts(ClosedCatalogTransferMode mode)
    {
        var request = Request(mode); var operation = new Operation(request); var journal = new Journal();
        var result = await new ClosedCatalogTransferOrchestrator().ExecuteAsync(request, operation, journal);
        Assert.True(result.Succeeded, result.Diagnostic);
        Assert.False(ClosedCatalogJournalFormat.RequiresRecovery(journal.Records));
        Assert.True(operation.Disposed);
        if (mode == ClosedCatalogTransferMode.MoveAndActivate)
            Assert.True(operation.Effects.IndexOf(ClosedCatalogTransferState.Activating) < operation.Effects.IndexOf(ClosedCatalogTransferState.ConfigurationCommitted));
        else Assert.DoesNotContain(ClosedCatalogTransferState.Activating, operation.Effects);
        foreach (var prefix in Enumerable.Range(1, journal.Records.Count - 1))
            Assert.True(ClosedCatalogJournalFormat.RequiresRecovery(journal.Records.Take(prefix).ToArray()));
    }

    [Theory]
    [InlineData(ClosedCatalogTransferState.Preflight)]
    [InlineData(ClosedCatalogTransferState.Quiescing)]
    [InlineData(ClosedCatalogTransferState.SnapshotFinalized)]
    [InlineData(ClosedCatalogTransferState.Staging)]
    [InlineData(ClosedCatalogTransferState.Verified)]
    [InlineData(ClosedCatalogTransferState.Published)]
    [InlineData(ClosedCatalogTransferState.Activating)]
    [InlineData(ClosedCatalogTransferState.ConfigurationCommitted)]
    public async Task EffectFailureNeverClaimsSuccessAndRetainsSource(ClosedCatalogTransferState fault)
    {
        var request = Request(); var operation = new Operation(request) { Fault = fault }; var journal = new Journal();
        var result = await new ClosedCatalogTransferOrchestrator().ExecuteAsync(request, operation, journal);
        Assert.False(result.Succeeded); Assert.True(operation.RolledBack); Assert.True(operation.Disposed);
        Assert.Equal(ClosedCatalogTransferState.Failed, result.State);
        Assert.DoesNotContain(ClosedCatalogTransferState.Completed, operation.Effects);
        Assert.False(ClosedCatalogJournalFormat.RequiresRecovery(journal.Records));
    }

    [Fact]
    public async Task UnprovedRollbackRequiresRecovery()
    {
        var request = Request(); var operation = new Operation(request) { Fault = ClosedCatalogTransferState.Activating, SafeRollback = false }; var journal = new Journal();
        var result = await new ClosedCatalogTransferOrchestrator().ExecuteAsync(request, operation, journal);
        Assert.Equal(ClosedCatalogTransferState.RecoveryRequired, result.State);
        Assert.True(ClosedCatalogJournalFormat.RequiresRecovery(journal.Records));
    }
    [Fact]
    public async Task ReadbackDetectsSameLengthChangedBytesBeforePublication()
    {
        var request = Request(); var operation = new Operation(request) { Corrupt = true }; var journal = new Journal();
        var result = await new ClosedCatalogTransferOrchestrator().ExecuteAsync(request, operation, journal);
        Assert.False(result.Succeeded); Assert.DoesNotContain(ClosedCatalogTransferState.Published, operation.Effects);
    }
    [Theory]
    [InlineData(ClosedCatalogTransferState.Quiescing)]
    [InlineData(ClosedCatalogTransferState.Staging)]
    [InlineData(ClosedCatalogTransferState.Verified)]
    public async Task CancellationStopsBeforePublicationAndReleasesOwner(ClosedCatalogTransferState at)
    {
        var request = Request(); using var cancelled = new CancellationTokenSource();
        var operation = new Operation(request) { CancelAt = at, Cancellation = cancelled }; var journal = new Journal();
        var result = await new ClosedCatalogTransferOrchestrator().ExecuteAsync(request, operation, journal, token: cancelled.Token);
        Assert.Equal(ClosedCatalogTransferState.Cancelled, result.State);
        Assert.True(operation.RolledBack); Assert.DoesNotContain(ClosedCatalogTransferState.Published, operation.Effects);
    }
    [Fact]
    public async Task CancellationInsideCommitReportsTruthfulCompletion()
    {
        var request = Request(); using var cancelled = new CancellationTokenSource();
        var operation = new Operation(request) { CancelAt = ClosedCatalogTransferState.Published, Cancellation = cancelled }; var journal = new Journal();
        var result = await new ClosedCatalogTransferOrchestrator().ExecuteAsync(request, operation, journal, token: cancelled.Token);
        Assert.True(result.Succeeded, result.Diagnostic);
    }
    [Fact]
    public async Task EveryDurableIntentReceiptInterruptionFailsClosed()
    {
        var completed = new Journal(); var referenceRequest = Request();
        Assert.True((await new ClosedCatalogTransferOrchestrator().ExecuteAsync(referenceRequest, new Operation(referenceRequest), completed)).Succeeded);
        for (var record = 0; record < completed.Records.Count; record++)
        {
            var request = Request(); var journal = new Journal { FailAt = record }; var operation = new Operation(request);
            var result = await new ClosedCatalogTransferOrchestrator().ExecuteAsync(request, operation, journal);
            Assert.False(result.Succeeded); Assert.True(operation.Disposed);
            if (journal.Records.Count > 0) Assert.True(ClosedCatalogJournalFormat.RequiresRecovery(journal.Records));
        }
    }
    [Theory]
    [InlineData("../catalog.snapshot")]
    [InlineData("/catalog.snapshot")]
    [InlineData("Backups/../catalog.snapshot")]
    [InlineData("Backups/file ")]
    [InlineData("Backups/CON.db")]
    public void UnsafeArtifactsAreRefused(string name)
    {
        var manifest = Manifest(Request());
        Assert.Throws<InvalidDataException>(() => ClosedCatalogManifestFormat.Validate(manifest with
            { Artifacts = [manifest.Artifacts[0], manifest.Artifacts[0] with { RelativeName = name, Role = ClosedCatalogArtifactRole.ManagedBackup }] }));
    }
    [Fact]
    public void CompleteManifestRejectsCollisionsUnknownRolesAndReadiness()
    {
        var manifest = Manifest(Request());
        Assert.Throws<InvalidDataException>(() => ClosedCatalogManifestFormat.Validate(manifest with
            { Artifacts = [manifest.Artifacts[0], manifest.Artifacts[0] with { RelativeName = "LIGHTFLOWCATALOG.DB", Role = ClosedCatalogArtifactRole.ManagedBackup }] }));
        Assert.Throws<InvalidDataException>(() => ClosedCatalogManifestFormat.Validate(manifest with
            { Artifacts = [manifest.Artifacts[0] with { Role = (ClosedCatalogArtifactRole)99 }] }));
        var readiness = new ClosedCatalogReadiness(1, manifest.OperationId, manifest.Generation, manifest.CatalogId,
            ClosedCatalogManifestFormat.Digest(manifest), manifest.Artifacts.Count);
        ClosedCatalogManifestFormat.ValidateReadiness(manifest, readiness);
        Assert.Throws<InvalidDataException>(() => ClosedCatalogManifestFormat.ValidateReadiness(manifest, readiness with { ManifestDigest = new string('0', 64) }));
        var bytes = ClosedCatalogManifestFormat.Serialize(manifest);
        Assert.ThrowsAny<Exception>(() => ClosedCatalogManifestFormat.Deserialize(bytes.AsSpan(0, bytes.Length - 1)));
    }
    [Fact]
    public void ExternalReferencesAreNonPayloadAndDoNotRequireAvailability()
    {
        var manifest = Manifest(Request()) with { ExternalDependencies = [new(Guid.NewGuid(), new string('A', 64), "external.cube", "Missing external resource; reference retained")] };
        var roundtrip = ClosedCatalogManifestFormat.Deserialize(ClosedCatalogManifestFormat.Serialize(manifest));
        Assert.Single(roundtrip.Artifacts); Assert.Single(roundtrip.ExternalDependencies);
        Assert.Equal(ClosedCatalogManifestFormat.Digest(manifest), ClosedCatalogManifestFormat.Digest(roundtrip));
    }
    [Fact]
    public void NullPayloadDeclarationsAreInvalidEvidence()
    {
        var manifest = Manifest(Request());
        Assert.Throws<InvalidDataException>(() => ClosedCatalogManifestFormat.Validate(manifest with { Artifacts = [null!] }));
        Assert.Throws<InvalidDataException>(() => ClosedCatalogManifestFormat.Validate(manifest with { ExternalDependencies = [null!] }));
    }
    [Fact]
    public async Task ReorderedJournalCannotAuthorizeStartup()
    {
        var request = Request(); var journal = new Journal();
        await new ClosedCatalogTransferOrchestrator().ExecuteAsync(request, new Operation(request), journal);
        var records = journal.Records.ToArray(); (records[2], records[3]) = (records[3], records[2]);
        Assert.Throws<InvalidDataException>(() => ClosedCatalogJournalFormat.RequiresRecovery(records));
    }

    private sealed class Journal : IClosedCatalogTransferJournal
    {
        public readonly List<ClosedCatalogJournalRecord> Records = [];
        public int FailAt = -1;
        public Task AppendAsync(ClosedCatalogJournalRecord record, CancellationToken token)
        {
            if (Records.Count == FailAt) throw new IOException("Injected interrupted journal append");
            ClosedCatalogJournalFormat.Validate(Records.Append(record).ToArray()); Records.Add(record); return Task.CompletedTask;
        }
    }
    [Theory]
    [InlineData("source-unavailable")]
    [InlineData("destination-unavailable")]
    [InlineData("read-only")]
    [InlineData("unsupported-filesystem")]
    [InlineData("unknown-filesystem")]
    [InlineData("network-active")]
    [InlineData("ambiguous-alias")]
    public async Task RefusedNativeFactsCannotQuiesceOrPublish(string facts)
    {
        var request = Request(); var operation = new Operation(request) { RefusedFacts = facts };
        var result = await new ClosedCatalogTransferOrchestrator().ExecuteAsync(request, operation, new Journal());
        Assert.False(result.Succeeded); Assert.True(operation.RolledBack); Assert.True(operation.Disposed);
        Assert.DoesNotContain(ClosedCatalogTransferState.Quiescing, operation.Effects);
        Assert.DoesNotContain(ClosedCatalogTransferState.Published, operation.Effects);
    }
    private sealed class Operation(ClosedCatalogTransferRequest request) : IClosedCatalogTransferOperation
    {
        public string? RefusedFacts;
        public ClosedCatalogTransferState? Fault, CancelAt;
        public CancellationTokenSource? Cancellation;
        public bool Corrupt, RolledBack, Disposed, SafeRollback = true;
        public readonly List<ClosedCatalogTransferState> Effects = [];
        private void Effect(ClosedCatalogTransferState state, CancellationToken token)
        {
            Effects.Add(state);
            if (state == Fault) throw new IOException("Injected " + state);
            if (state == CancelAt) Cancellation!.Cancel();
            token.ThrowIfCancellationRequested();
        }
        private StorageLocationAssessment Assessment(StorageRole role)
        {
            var now = DateTimeOffset.UtcNow;
            return new(new(request.OperationId, request.Generation, role, role == StorageRole.ActiveCatalog ? StorageOperation.Open : StorageOperation.Write, role.ToString()), Guid.NewGuid(), now, now.AddMinutes(1),
                StorageLocationPolicy.Version, StorageAssessmentStatus.Complete, new(role.ToString(), "object", "volume", "qualified"), StorageLocality.Local,
                StorageAvailability.Available, StorageAvailability.Available, StorageResolutionConfidence.Resolved, StorageResolutionConfidence.Resolved,
                new(StorageCapability.Supported, StorageCapability.Supported, StorageCapability.Supported, StorageCapability.Supported, StorageCapability.Supported, StorageCapability.Supported));
        }
        public Task<ClosedCatalogTransferEvidence> PreflightAsync(ClosedCatalogTransferRequest r, CancellationToken t)
        {
            Effect(ClosedCatalogTransferState.Preflight, t);
            var source = Assessment(StorageRole.ActiveCatalog);
            var destination = Assessment(r.Mode is ClosedCatalogTransferMode.MoveAndActivate or ClosedCatalogTransferMode.LocalCopyback ? StorageRole.ActiveCatalog : StorageRole.ClosedCatalogBackupTransfer);
            if (RefusedFacts == "source-unavailable") source = source with { PathAvailability = StorageAvailability.Unavailable };
            destination = RefusedFacts switch
            {
                "destination-unavailable" => destination with { VolumeAvailability = StorageAvailability.Unavailable },
                "read-only" => destination with { Capabilities = destination.Capabilities! with { Write = StorageCapability.Unsupported } },
                "unsupported-filesystem" => destination with { Capabilities = destination.Capabilities! with { QualifiedCatalogFileSystem = StorageCapability.Unsupported } },
                "unknown-filesystem" => destination with { Capabilities = destination.Capabilities! with { QualifiedCatalogFileSystem = StorageCapability.Unknown } },
                "network-active" => destination with { Locality = StorageLocality.Network },
                "ambiguous-alias" => destination with { AliasResolution = StorageResolutionConfidence.Ambiguous },
                _ => destination
            };
            return Task.FromResult(new ClosedCatalogTransferEvidence(source, destination,
                "prior", "intended", Manifest(r).PublicationMode));
        }
        public Task QuiesceAsync(CancellationToken t) { Effect(ClosedCatalogTransferState.Quiescing, t); return Task.CompletedTask; }
        public Task<ClosedCatalogManifest> FinalizeSnapshotAsync(CancellationToken t) { Effect(ClosedCatalogTransferState.SnapshotFinalized, t); return Task.FromResult(Manifest(request)); }
        public Task StageAsync(ClosedCatalogManifest m, CancellationToken t) { Effect(ClosedCatalogTransferState.Staging, t); return Task.CompletedTask; }
        public Task RevalidateAsync(CancellationToken t) { Effect(ClosedCatalogTransferState.Verified, t); return Task.CompletedTask; }
        public Task<IReadOnlyList<string>> ListStagedArtifactsAsync(CancellationToken t) => Task.FromResult<IReadOnlyList<string>>([Manifest(request).Artifacts[0].RelativeName]);
        public Task<Stream> OpenStagedReadAsync(string n, CancellationToken t)
        {
            var bytes = Payload.ToArray(); if (Corrupt) bytes[0] ^= 1; return Task.FromResult<Stream>(new MemoryStream(bytes));
        }
        public Task<ClosedCatalogTransferReceipt> PublishAsync(ClosedCatalogManifest m, ClosedCatalogReadiness ready, CancellationToken t)
        { Effect(ClosedCatalogTransferState.Published, t); return Task.FromResult(new ClosedCatalogTransferReceipt("publication verified")); }
        public Task<ClosedCatalogTransferReceipt> ActivateAsync(CancellationToken t)
        { Effect(ClosedCatalogTransferState.Activating, t); return Task.FromResult(new ClosedCatalogTransferReceipt("actual lifecycle activation")); }
        public Task<ClosedCatalogTransferReceipt> CommitConfigurationAsync(CancellationToken t)
        { Effect(ClosedCatalogTransferState.ConfigurationCommitted, t); return Task.FromResult(new ClosedCatalogTransferReceipt("config readback")); }
        public Task CompleteAsync(CancellationToken t) { Effect(ClosedCatalogTransferState.Completed, t); return Task.CompletedTask; }
        public Task<bool> RollbackAsync(CancellationToken t) { RolledBack = true; return Task.FromResult(SafeRollback); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
