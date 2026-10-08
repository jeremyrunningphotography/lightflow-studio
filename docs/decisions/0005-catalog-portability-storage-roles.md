# ADR 0005: Catalog portability and storage roles

- **Status:** Accepted selected initial P2 direction; production guards/workflows require implementation
- **Date:** 2026-10-08
- **Authority:** Epic #366, accepted M0/G1/G2/G3 records and owner architecture-publication instruction; [evidence and status](../architecture/convergence-evidence.md).
- **Publication gate:** This documentation Draft PR requires explicit owner acceptance before merge. Accepted principles do not authorize implementation.

## Context

[ADR 0001](0001-lightflow-catalog-persistence.md) remains the persistence authority. G2 accepted local SQLite and closed Windows/Mac transfer. Earlier central/NAS experiments and failed direct backup are preserved; no authority service is selected.

## Decision

One logical Catalog owns durable authored media metadata and stable Catalog/Root/Asset IDs. No scattered media-folder metadata-sidecar architecture. Logical roots resolve through per-machine mappings; absolute path spellings are not asset identity. Machine-local Preview/cache is rebuildable and excluded from Catalog transfer.

Initial active Catalogs use supported local storage. P2 verifies CLOSED transfer; NAS/network media and closed backup transport are separate roles. Active network Catalogs and simultaneous/concurrent multi-writer access are unsupported. P1 physical portable SSD remains deferred. No schema change is required merely for the tested RootId/per-MachineId remapping.

Accepted follow-through is fail-closed unsafe path/identity policy: case/Unicode ambiguity, lossy trim/backslash interpretation, unsupported portable names and ambiguous link containment cannot silently merge or repair identities. Resolve volume/filesystem locality and containment through OS adapters, revalidating operations. Unknown active-authoring classification fails closed.

NAS backup follows local SQLite-aware snapshot/finalization → closed staged byte transfer/hash verification → verified local copyback/restore. Retain prior verified backups. Preserve direct local WAL-to-SMB CANTOPEN14 failure; no live backup-to-SMB claim. Current production has not implemented all G2 guards/transfer UX.

Retain ADR 0001 migration/durability/recovery policy and [current backup contract](../catalog-backup-272.md). No provider/schema/locking-model change is approved.

## Alternatives considered

Direct shared NAS SQLite, sidecars, database replacement and central authority service are outside the selected initial direction. A future central product requirement requires a separate architecture gate.

## Consequences

Portability, network media access and concurrency are distinct. G2 synthetic identity fixtures are not a media playback corpus or power-loss/physical SSD certification. Missing configured storage must never cause empty-Catalog replacement.

## Follow-up work

Implement and qualify conservative guards, staged transfer/backup and remapping in separately authorized slices. Broader permissive key migration must audit collisions, preserve IDs and protect rollback; do not silently rewrite historical rows.
