# Shared Catalog lifecycle completion

LF-WIN-DEV-019 / #432 / Draft PR #435. Jeremy's approval in the updated #432/#425 bodies dated 2026-10-12 supersedes the proposed-decision gate reviewed at `65e917a77392873936c2fbe645df9382186a9503`. This report supersedes the historical implementation checkpoint. Exact-head validation and packaged acceptance evidence are recorded in the PR handoff. Keep the PR Draft: no merge, release or tag. #425 stays blocked, Agent 015 paused and R2 #393 incomplete.

## Final ownership map

| Before: Windows assembly | After: authoritative owner | Windows composition |
| --- | --- | --- |
| Database contracts, migrations, engine, sessions/pools | Catalog: existing implementations | Same namespace, schema, provider and WAL/FULL policy |
| Identity, authored range/Subclip values | Domain | Existing logical IDs/data |
| Layout, authoring declarations, lossless configuration snapshot/effects | Application | Windows layout and complete AppSettings adapter |
| Admission/drain, readiness loss, prepared exit, shutdown | CatalogLifecycle / CatalogMutationLifecycle | Coordinator forwards to shared owner |
| Startup/open/create, explicit selection and configuration rollback | CatalogLifecycle.Operations | Native profile/storage effects and consumer composition |
| Move activation, configuration save/readback, rollback/RecoveryRequired | CatalogLifecycle.Operations | Native transfer/publication effects and existing injected activator/store |
| Restore install/commit/rollback/reactivation and backup drain | CatalogLifecycle.Operations / CatalogRecoveryService | Existing native recovery/read/SQLite-path effects |
| Storage admission and closed-boundary revalidation | CatalogStorageAdmission | Existing Windows binding adapter |
| Ownership evidence and paired/profile completion | StartupStoreEvidence / StartupSessionCompletion / CatalogLifecycle | Original Win32 fingerprint and exclusive bound-file effects |
| Closed transfer bytes, manifests and durable journal | ClosedCatalogTransferOperation / CatalogTransferFiles / CatalogTransferJournal | Windows construction/static compatibility facades |
| Collections/Smart Collection SQL | CatalogCollectionOrganizationStore | Typed facade retains BrowserQueryIntent validation/serialization |
| Media Root SQL/admission sequencing | CatalogMediaRootService | Existing machine identity/filesystem/native path effects |
| MediaAsset SQL and relative identity/scope rules | CatalogMediaAssetRepository / Application contracts | Existing discovery/fingerprint/Browser services |
| LUT resources/color intent and SQL | Existing ColorManagement implementation in Catalog | Same parser/scanner/cache used by Windows |
| Classification, descriptions, asset state, ranges/Subclips, markers, preferred-frame and recursive-root SQL | Catalog; necessary declarations in Domain/Application | Existing Browser/media/Player consumers |
| Authored-state copying and its mutation admission | CatalogAssetCopyStore | Windows retains Preview copy orchestration |
| Browser/media/LUT/Preview/monitoring/scheduler host graph and UI | Windows | Existing implementations and accepted #412 behavior |

The declaration audit follows constructors, record members, return types, interfaces, partials and static helpers. Shared projects have no WPF, Windows coordinator, AppSettings, Preview-service or native P/Invoke implementation reference. Concrete SQLite stays in Catalog. Host verification tools, Preview persistence and presentation stay Windows-owned.

`ICatalogLifecycleEffects` supplies composition/native capabilities; all startup, activation, configuration, rollback, transfer and shutdown decisions remain shared. `ICatalogConfigurationSnapshot` is lossless: Windows wraps the full existing immutable AppSettings object, stages only explicitly selected Catalog fields, serializes the existing representation and saves the complete object. It does not reconstruct preferences through a serialization round trip. Existing save/readback/rollback ordering and comparisons are preserved.

## Exact approved host-consumer port

Source: `Lightflow.Application/ICatalogHostConsumerEffects.cs`, unchanged from the approved checkpoint:

```csharp
void CancelReadUsage();
void DisposeCaches();
Task StopMonitoringAsync();
Task StopDerivedWorkAsync();
Task RestartAsync();
Task<IDisposable> EnterCompletionPeerMaintenanceAsync();
bool HasQualifiedCompletionPeer { get; }
Task CloseCompletionPeerAsync();
void CompleteCompletionPeer();
bool CompletionPeerCompleted { get; }
void ReleaseCompletionPeerEvidence();
void DisposeReadUsage();
```

WindowsCatalogHostConsumers invokes the original effects. WindowsCatalogLifecycleEffects composes that graph after the shared owner establishes session/evidence state. Windows startup/create/select/transfer facades delegate directly. Shared shutdown retains cancellation, cache disposal, monitoring/scheduler stop, drain, transition gate, peer maintenance, closure, completion and release order. No second coordinator is introduced.

## Completion proof

Windows retains the genuine Catalog/Preview pair, Format 1 checksum envelope, native fingerprint representation, Validated provenance, Dirty profile marker and certificate acceptance. Both stores close and complete before profile-token publication. Existing paired optimized-reopen tests exercise the composed owner.

With SupportsCompletionPeer false, startup never invokes peer evidence/store creation. It uses the same ownership/admission/recovery and deep-validation pipeline. Shutdown cannot complete a clean pair/profile. Catalog retains Validated provenance and profile stays Dirty: every reopen validates deeply before session publication/writer admission. Missing/ambiguous evidence fails closed; no blank store substitutes for a peer.

CatalogOnlyLifecycleTests exercises complete shared startup with real Windows native effects, no Preview peer, and three create/reopen/shutdown cycles: stable identity, repeated deep validation, no fast path, dirty/validated evidence, no Preview database, zero peer effects and stale-session refusal. Neutral CatalogUnpairedCompletionTests exercises evidence/disposal with managed fixture effects. Neither qualifies native Mac locking/durability/publication or Windows startup parity.

## Focused validation and retained qualification

Windows tests use Test-Noninteractive.ps1 and its private desktop. Completion runs passed 57 startup/selection/restore/relocation/paired tests; 188 collection/root/asset/LUT tests; 33 paired/shared Catalog-only startup tests; and 34 authored-copy/markers/preferred-frame/recursive-root/unpaired tests before the final direct-copy admission check. Neutral Catalog suite passed 44 tests. Exact-head final suites are reported in the PR handoff.

Recovery/ownership/transfer/closed-backup run: 91 passed, one historical interrupted-child diagnostic assertion failed. The accepted Agent 018 journal-lock collector from main is integrated. Immediate read encounters sharing violation; startup returns CatalogUnavailable/PendingRecovery and refuses a writer; later diagnostic-only reading recovers the retained Activating record. Restart Manager reports no holder at its observation boundary. This failure previously reproduced in an independent untouched accepted-main clone at 38bb1ed. These observations establish neither root cause nor extraction regression. No retry, sleep, assertion/time-limit weakening or unrelated journal investigation was added. Report any final-suite recurrence separately.

## Performance and executable

Use the identical existing fixture with two warmups/eight measured independent profiles in task-owned baseline/current clones, sequentially. It now also emits create/reopen shutdown timings. Catalog-only fixture emits full shared startup/deep-reopen/shutdown costs. These tiny synthetic Catalog observations are not production-size guarantees or native Mac fast-path claims. The PR handoff records samples/results and limitations.

Final executable refresh uses Build-Release PullRequest/SkipInstaller after the final commit, followed by smoke/dependency/timestamp/no-leftover-process checks. Acceptance command:

```powershell
& "C:\Git\Agents\LF-WIN-DEV-019-CatalogExtraction\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-DEV-019-CatalogExtraction\artifacts\acceptance\LF-WIN-DEV-019"
```

## Agent 015 remaining work

Implement and qualify native Mac leases/fingerprints, bound files/directories, recovery SQLite routing, exclusive journal durability/readback and supported no-replace publication. Compose the shared lifecycle/native effects and approved consumer port with lossless configuration and media-path effects. Preserve validated saved-query semantics: no qualified codec means Smart Collection operations fail closed. Qualify independent writers, interruption/recovery, real authored-state round trips, filesystem faults and disposal; measure native Mac deep reopening separately. Do not fabricate Preview completion, copy lifecycle algorithms, expand filesystem policy or claim R2 complete. #425 remains blocked and Agent 015 paused until Jeremy resumes it.
