# Shared storage contracts — R2-A / #388

LF-WIN-DEV-004 — Shared Storage Contracts. Jeremy authorized this bounded production-source slice on 2026-10-09. Baseline: fetched `origin/main` `b8a4aecd068f7be01728e4b8eab1ce095a3ebb22`, including Agent 003's completed registry merge #400. Architecture and packaged owner acceptance remain pending. This document specifies the implementation for review; it does not authorize #389–#392.

## Ownership and dependencies

`Lightflow.Domain/StorageLocationAssessment.cs` owns immutable role, operation, identity and capability facts. `Lightflow.Application/StorageLocationPolicy.cs` is the sole new neutral role-suitability policy; `IStorageLocationAssessor` is the OS facts port. Existing net8.0 assemblies suffice. Application references only Domain and Actions; Domain has no project/package dependencies. No references, packages, native types, SQLite connections or UI dependencies are added. The Windows shell already references Application through R1.

The policy is production source awaiting adapter consumption, not production enforcement. No `LightflowStudio` source changes occur here. Current Windows path selection, startup, backup, restore, instance ownership and writer lifecycle remain authoritative and unchanged. No compatibility adapter is added prematurely; #389 supplies facts and consumption in the existing Windows coordinator. Policy eligibility neither opens a database nor grants a writer lease, and does not establish Catalog existence, identity, schema, integrity, explicit first creation, destination emptiness or transfer completion.

## Role and operation matrix

All roles require complete, fresh operation-bound assessments, resolved identities, available volume/path, known locality, resolved aliases and protected containment. Path availability for Create means a resolved accessible creation parent and intended leaf; an absent database is never evidence of first run. Platform facts must distinguish inaccessible/unavailable from absent.

| Role | Operations | Locality and capabilities | Result when suitable |
| --- | --- | --- | --- |
| Active Catalog | Open, Create, Relocate, RestoreActivation | Qualified local internal or external; read/write, qualified filesystem, durable writes, file locking and companion files | Eligible location; existing ownership and Catalog lifecycle still required |
| Closed Catalog backup/transfer | Read, Write | Local or network; operation-specific read/write | RequiresVerifiedStaging, never unconditional transport authorization |
| Media source | Read | Local or network; readable, may be read-only | Eligible |
| Rebuildable Preview | Read, Write, Relocate | Machine-local per accepted ADR 0005/G2; operation-specific read/write | Eligible, without Catalog durability requirements |
| Temporary/profile | Read, Write | Known local or network; operation-specific read/write | Eligible location only; feature-specific profile isolation, cleanup and operational persistence remain separate |

Temporary/profile locality is not assigned the active-Catalog restriction. This narrow suitability result does not make profile state portable or authorize destructive cleanup. It leaves any stricter feature-specific constraints with their existing owner. Catalog capability facts are irrelevant for ordinary media, transport, Preview and temporary/profile roles. Preview does not inherit WAL/FULL/durability semantics. Network backup Read is conditional too: verified local copyback precedes restore activation under a separate ActiveCatalog request.

`QualifiedCatalogFileSystem` is explicit adapter evidence of accepted support, not a string allowlist or an independent policy implementation. It must be grounded in the accepted platform qualification matrix; generic OS write access cannot establish it. The shared policy requires it alongside explicit durability, locking and companion-file facts. Physical USB/Thunderbolt transport alone does not qualify a filesystem or power-loss behavior. This tranche sets no new filesystem support matrix.

## Snapshot and revalidation contract

The caller supplies a fresh nonempty OperationId, a nonnegative invalidation Generation, exact opaque RequestedLocation, Role and Operation. The provider echoes that request exactly. Neutral code does not trim, case-fold, normalize Unicode, parse mount paths or infer locality from UNC/drive spelling. Mapped network drives and local-looking links to shares must produce Network facts; local external volumes produce Local facts when established.

Each snapshot has a nonempty AssessmentId, AssessedAtUtc, exclusive ExpiresAtUtc and `storage-roles/1` PolicyVersion. The provider owns a bounded validity interval justified by its observations. The caller supplies current UTC to the pure evaluator. Future-dated, expired or invalid intervals fail closed. Expiry is an upper bound, never permission to skip a boundary probe. The policy does not impose an arbitrary OS timeout or assume mounts stay stable for that interval.

ResolvedStorageIdentity contains canonical display/use location, opaque resolved target identity, volume/mount identity and filesystem identity. Creation identity includes the intended leaf and resolved parent. A mount identity must distinguish replacement/remount epochs, including a new mount at a reused drive letter/path. The provider must report Unknown/Failed when it cannot establish that binding. Persistent CatalogId/RootId/AssetId are distinct from these machine-local observations and are never reassigned by the policy.

At open/create, relocation preflight and destination activation, restore activation, and other identity-sensitive transitions, obtain new current facts. Within the same operation, `Revalidate` compares the prior snapshot to a newly assessed snapshot; reuse of AssessmentId or backward observation time fails. It checks request, policy, resolved target/canonical location, filesystem/volume and all relevant facts. Any change rejects continuation and requires explicit restarted preflight; it does not substitute a path. An expired prior snapshot may be refreshed by a genuinely new snapshot with the same binding; the expired approval itself is never reused. A new operation/generation requires a new request and Evaluate, not borrowing an old result.

Revalidate evaluates current safety first, so current unavailability/network/read-only rejection takes precedence over comparison diagnostics. Prior snapshots must have been eligible/conditional at their original observation time. A fresh result is still a snapshot, not an atomic check-and-use guarantee: #389/#390 must keep resolved identity checks coupled to the actual OS open/use, detect mount/alias changes at use, and preserve rollback through the existing lifecycle. No handles, locks or second mutation lifecycle are introduced in this tranche.

## Failure, cancellation and diagnostics

`Evaluate` and `Revalidate` have no filesystem access or internal clock. Their result includes decision, stable reason, actionable diagnostic, current policy version, request and exact assessment. Provider diagnostics remain separate; the policy does not turn exception strings into a success or overwrite paths.

Deterministic first-reason order: cancellation; invalid request; incomplete snapshot; request/version mismatch; provider failure/incomplete status; lifetime; unavailable/incomplete availability; unknown locality; role-locality restriction; missing identity; unresolved alias/containment; missing/read/write facts; Catalog qualification/safety facts; suitable role result. Revalidation then checks prior binding/validity and changed identities/facts. Enum values outside known allowed values cannot satisfy required safety.

| Failure | Behavior |
| --- | --- |
| Unavailable path/volume | LocationUnavailable; preserve configuration/files and reconnect or explicitly select storage |
| Unknown locality/filesystem identity | UnknownLocality or MissingIdentity; assess again |
| Unknown qualification/capability | MissingCapabilities; unknown is never supported |
| Unsupported Catalog filesystem/capability | UnsupportedCatalogFileSystem / UnsupportedCatalogCapabilities |
| Network/mapped-network active Catalog | NetworkActiveCatalog, regardless of path spelling |
| Read-only destination | ReadOnly; readable media/closed sources remain eligible/conditional |
| Alias/containment ambiguity | UnresolvedAliases / AmbiguousContainment; no identity normalization/merge |
| Stale/reused snapshot | StaleAssessment; obtain fresh boundary facts |
| Changed target, mount or filesystem | LocationChanged / VolumeChanged / FileSystemChanged; restart preflight |
| Changed other facts | Current safety rejection or CapabilityFactsChanged |
| Policy mismatch | PolicyVersionMismatch; reassess under current version |
| Provider failure | Failed snapshot produces AssessmentFailed; no fallback initialization |
| Cancellation | Cancelled result if token already cancelled or snapshot Cancelled; no eligibility |

Assessor implementations must return explicit failure snapshots for expected assessment errors. They may propagate OperationCanceledException; consumers stop before admission. Unexpected provider exceptions also stop admission and must be surfaced by the existing operation owner, never converted to missing storage or first creation. Pure policy execution performs no async work; callers recheck cancellation at actual admission after awaits. Cancellation cannot revoke already admitted writes through a new lifecycle.

Closed artifacts require local SQLite-aware finalization, closed byte transfer, hash verification, retention of prior verified backups and verified local copyback before restore. The conditional result cannot be treated as Eligible. No staging implementation or claims of network interruption qualification are added here.

## Current owners and future replacement gates

| Current owner/methods | Retained behavior | Future responsibility and integration gate |
| --- | --- | --- |
| `LightflowStorageLocations.CreateAtRoot`, `WithOverrides`, `ResolveOverride`, `ValidateOwnershipBoundaries`, `PathsOverlap` | Central configured/derived paths and lexical overlap checks | #389 Windows adapter resolves real target/volume/containment facts, shared policy evaluates roles; retire spelling-only safety as authority only after native guard and packaged acceptance. Central path routing remains. #390 provides native macOS facts without copying admission rules. |
| `OwnedStoragePaths.Contains` / `Within` | Current dynamic owned-directory exclusions | #391 audits conservative identity/containment; resolved adapter checks eventually replace lexical containment authority without changing authored IDs or independently admitting writers. |
| `LightflowStorageCoordinator.StartAsync` | Explicit first creation versus expected/missing/unavailable Catalog, configured identity check, independent Previews | #389 inserts shared-policy consumption at open/create within this coordinator; preserve unavailable diagnostics/configuration and no empty replacement. #390's native facts feed the same shared policy for a later host. No enforcement added here. |
| `LightflowStorageCoordinator.ValidateDestination`, `IsUnc`, `SamePath`, `ProbeWritable`, `RelocateCatalogAsync`, `RestoreCatalogAsync`, `RelocatePreviewsCoreAsync` | Current destination checks, protected relocation/restore, staging and rollback | #389 replaces UNC-only locality decision with adapter facts/shared policy; #391 owns later identity-sensitive integration. Concrete write probes/staging are operation effects, not neutral assessment. Retire duplicated locality rules only after accepted native/integration tests and owner package review. |
| `CatalogDatabaseService.CreateNewAsync` / `OpenExistingAsync` | Concrete SQLite creation/open, schema/migration, identity/integrity and durability | Existing owner remains. Future composition revalidates before actual open/create; no concrete session/SQLite extraction or second Catalog admission engine. |
| `SqliteCatalogRecoveryService.CreateBackupAsync`, `BeginRestoreAsync` and current user-backup paths in `CatalogExitBackup.cs` | Protected SQLite backup/restore and current explicit backup UX | #392 consumes conditional closed-transport policy, finalized staging/hash/copyback; restore activation separately requires local ActiveCatalog assessment. Existing #272 behavior is retained until accepted integration. |
| `WindowsApplicationInstanceCoordinator.StartOrSignal` | Single normal application ownership before storage | #389 preserves it; later macOS ownership/IPC adapter is separately scoped. Location eligibility never establishes single writer. |
| `CatalogMutationLifecycle.RunAsync`, `QuiesceAsync` | Complete-operation admission/drain, nested accounting, shutdown | Unchanged sole mutation lifecycle. Every future adapter/integration preserves it; no replacement planned by this issue. |

#389 and #390 require Jeremy's acceptance of these exact neutral contracts, then separate implementation authorization, native mapped/link/mount/capability fixtures, operation revalidation and platform package evidence. #391/#392 remain unstarted. This issue does not introduce new dialogs, startup enforcement, locking/schema/provider changes, transfer or migration.

## Validation

`StorageLocationPolicyTests` covers each supported role/operation, internal/external equivalence, adapter-supplied Network including mapped/local-looking paths, unknown/unavailable facts, readonly and unsupported capability combinations, ambiguity, request binding, stale/reused snapshots, changed identities/facts/version, failure/cancellation, stable reason precedence and no filesystem creation even on Eligible results. Assembly-reference/PInvoke checks exercise neutral dependencies. Runtime `.deps.json` closes to Application, Domain and Actions only. These facts fixtures prove policy portability, not OS filesystem qualification.

Existing neutral CI runs the same Application test project on windows-latest and macos-latest with locked net8.0 dependencies. Its existing job/check names remain stable. Required Windows tests continue through Test-Noninteractive.ps1; package validation continues through the private desktop in Build-Release.ps1. Exact executed results and package/head provenance will be linked from the Draft PR handoff.
