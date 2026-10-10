using System.Security.Cryptography;
using System.Text.Json;

namespace Lightflow.Application;

/// <summary>Strict intent/effect receipt ordering. Malformed evidence never authorizes activation or cleanup.</summary>
public static class ClosedCatalogJournalFormat
{
    public const int MaximumRecords = 256;
    public const int MaximumRecordBytes = 128 * 1024;
    public static string Digest(ClosedCatalogJournalRecord record) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(record)));

    public static bool RequiresRecovery(IReadOnlyList<ClosedCatalogJournalRecord> records)
    {
        Validate(records);
        var last = records[^1];
        return last.IsIntent || last.State is not (ClosedCatalogTransferState.Completed or ClosedCatalogTransferState.Cancelled or ClosedCatalogTransferState.Failed);
    }

    public static void Validate(IReadOnlyList<ClosedCatalogJournalRecord> records)
    {
        if (records.Count is < 1 or > MaximumRecords) throw new InvalidDataException("Missing or oversized transfer journal.");
        ClosedCatalogTransferState? pending = null;
        var lastCompleted = -1;
        var terminal = false;
        var previous = "";
        string? manifest = null;
        for (var i = 0; i < records.Count; i++)
        {
            var r = records[i];
            if (r is null || r.Request is null || r.Version != 1 || r.Sequence != i || r.Request != records[0].Request ||
                r.Request.OperationId == Guid.Empty || r.Request.Generation < 0 || r.Request.CatalogId == Guid.Empty ||
                r.Request.SchemaVersion < 1 || r.Request.ApplicationId == 0 || !Enum.IsDefined(r.Request.Mode) ||
                !Enum.IsDefined(r.State) || r.PreviousRecordDigest != previous ||
                (terminal && !(r.IsIntent && r.State == ClosedCatalogTransferState.RecoveryRequired &&
                    lastCompleted == (int)ClosedCatalogTransferState.Completed)))
                throw new InvalidDataException("Inconsistent or out-of-order transfer journal.");
            if (r.ManifestDigest is not null)
            {
                if (!ClosedCatalogManifestFormat.ValidHash(r.ManifestDigest) || manifest is not null && manifest != r.ManifestDigest)
                    throw new InvalidDataException("Transfer manifest identity changed in the journal.");
                manifest = r.ManifestDigest;
            }
            if (i == 0 && (r.State != ClosedCatalogTransferState.Preflight || !r.IsIntent))
                throw new InvalidDataException("Transfer journal must begin with preflight intent.");
            var failure = r.State is ClosedCatalogTransferState.Cancelled or ClosedCatalogTransferState.Failed or ClosedCatalogTransferState.RecoveryRequired;
            if (r.IsIntent)
            {
                terminal = false;
                if (!failure)
                {
                    var expected = lastCompleted + 1;
                    if (r.Request.Mode != ClosedCatalogTransferMode.MoveAndActivate && lastCompleted == (int)ClosedCatalogTransferState.Published)
                        expected = (int)ClosedCatalogTransferState.Completed;
                    if (pending is not null || (int)r.State != expected)
                        throw new InvalidDataException("Transfer effect intent skipped or repeated a state.");
                }
                pending = r.State;
            }
            else
            {
                if (pending != r.State || string.IsNullOrWhiteSpace(r.Receipt))
                    throw new InvalidDataException("Transfer completion has no matching intent or receipt.");
                if (r.State == ClosedCatalogTransferState.Preflight && r.PreflightEvidence is null)
                    throw new InvalidDataException("Transfer journal has no preflight identities/configuration.");
                if (r.State == ClosedCatalogTransferState.Preflight && r.PreflightEvidence is { } evidence &&
                    (evidence.Source is null || evidence.Destination is null || evidence.Source.Request is null || evidence.Destination.Request is null ||
                     evidence.PreviousConfiguration is null || evidence.IntendedConfiguration is null || !Enum.IsDefined(evidence.PublicationMode)))
                    throw new InvalidDataException("Transfer journal has incomplete preflight evidence.");
                if (r.State >= ClosedCatalogTransferState.SnapshotFinalized && !failure && manifest is null)
                    throw new InvalidDataException("Transfer completion has no manifest receipt.");
                pending = null;
                lastCompleted = (int)r.State;
                terminal = failure || r.State == ClosedCatalogTransferState.Completed;
            }
            previous = Digest(r);
        }
    }
}
