> Historical checkpoint, superseded by [completion report](catalog-lifecycle-432-completion.md).

# Catalog lifecycle extraction implementation checkpoint

LF-WIN-DEV-019 / #432 / Draft PR #435. Owner architecture approval is recorded in the updated #432/#425 bodies dated 2026-10-12 and supersedes the historical gate at `65e917a77392873936c2fbe645df9382186a9503`.

**Incomplete implementation; not an acceptance handoff.** Shared infrastructure is extracted and Windows consumes it. The remaining activation/configuration transition and repository closure described below must be finished before #425 can consume one complete lifecycle. No merge, release or tag. #432 remains In Progress, #425 blocked, #393 incomplete.

## Implemented ownership map

| Before, in Windows assembly | Current owner | Windows compatibility |
| --- | --- | --- |
| Catalog database service, contracts, migrations, connection factory | `Lightflow.Catalog`: existing source moved; same schema/provider/WAL/FULL policy | Existing composition references Catalog; original namespace retained for compatibility |
| Catalog identity | `Lightflow.Domain/CatalogIdentity.cs` | Same values and field names; no key/data migration |
| Storage locations and boundary contracts | Application | Windows layout implementation retained; standard filenames refer to shared `CatalogLayout` constants |
| Mutation lifecycle and participant | Catalog | One existing implementation, including nested accounting, captured continuations, quiescence and shutdown |
| Classification, descriptions, asset-state, range and Subclip repositories | Catalog | Existing Windows consumers use the moved implementations |
| Authored range/Subclip values and eligibility | Domain; existing range-boundary policy in Application | Necessary declaration closure only; no Player/UI replacement |
| Startup evidence/profile-session completion | Catalog | `WindowsCatalogEvidenceEffects` supplies the original bound journal and Win32 fingerprint operations |
| Session/readiness state, admission closure, prepared exit and shutdown order | `CatalogLifecycle` | Windows coordinator forwards state/readiness/disposal to this owner |
| Admission/revalidation/closed boundaries | `CatalogStorageAdmission` | `CatalogLocationAdmission` composes the existing Windows assessor and native guards |
| Initial admitted database opening | `CatalogLifecycle.OpenAdmittedAsync` | Windows startup delegates the existing SQLite open/create/closed-revalidation block |
| Transfer byte workflow and manifest verification | `ClosedCatalogTransferOperation`, `CatalogTransferFiles` | `WindowsClosedCatalogTransferOperation` is a construction/helper facade over Windows native effects |
| Durable journal framing, append/flush/readback and pending recovery | `CatalogTransferJournal` | Windows journal facade supplies native guarded files/directories/assessors |
| SQLite recovery, managed retention, restore, backup transport/copyback and external dependency inspection | `CatalogRecoveryService` partials | `SqliteCatalogRecoveryService` is a construction/static compatibility facade; native provider path spelling and read guards stay Windows |
| Backup destination policy | Catalog | Windows facade supplies existing containment/path-overlap effects |
| Consumer stop/restart effects | `ICatalogHostConsumerEffects` in Application; ordering entry points in Catalog | Existing monitoring, scheduler, LUT cache, Preview maintenance/store and read-cancellation implementations remain Windows |

## Exact host-consumer contract

Authoritative source: `Lightflow.Application/ICatalogHostConsumerEffects.cs`. Its typed effects are `CancelReadUsage`, `DisposeCaches`, `StopMonitoringAsync`, `StopDerivedWorkAsync`, `RestartAsync`, `EnterCompletionPeerMaintenanceAsync`, `HasQualifiedCompletionPeer`, `CloseCompletionPeerAsync`, `CompleteCompletionPeer`, `CompletionPeerCompleted`, `ReleaseCompletionPeerEvidence`, and `DisposeReadUsage`.

`WindowsCatalogHostConsumers` implements these effects using the pre-existing consumer methods. It contains no activation/configuration/rollback algorithm. Shared shutdown orders cancellation, cache disposal, monitoring/scheduler stop, mutation drain, transition-gate acquisition, peer maintenance, Catalog/peer closure and evidence completion/release. Restore and Move now invoke the shared consumer stop/restart entry points; their transaction decisions still need the remaining extraction.

## Native ports

All neutral contracts are in Application:

- `ICatalogEvidenceEffects`: exclusive writer/profile journals, bound ownership checks, regular-file checks, native path equality and fingerprint. Windows fingerprint format and acquisition primitives are unchanged.
- `ICatalogAdmissionNativeEffects`: assessor, native binding flag, access guard, canonical routing, main-file guard/release and native path equality. Shared policy retains eligibility and revalidation decisions.
- `ICatalogTransferNativeEffects`, `ICatalogStorageAssessor`, `ICatalogDirectoryLease`: native assessor lifetime, containment/overlap, guarded reads/journals, regular files, exclusive directories, volume free space, directory binding and no-replace publication.
- `ICatalogRecoveryNativeEffects`, `ICatalogRecoveryReadGuard`: existing recovery access guard, owned-file enumeration, native SQLite path spelling and guarded read lifetime.

Managed `FileStream` and `IDisposable` capabilities cross these infrastructure ports; native handles/Win32 structures do not. Capability implementations must throw on unsupported or ambiguous effects. No Mac publication/locking qualification is claimed, and no permissive fallback is supplied.

## Completion compatibility

Windows startup still requires the real Catalog/Preview pair before fast eligibility. `CatalogLifecycle.RequireCompletePeers` is the moved original pair predicate. Shared shutdown checks the actual Windows Preview availability, closes both stores, completes the original Catalog/Preview certificates, and publishes the original profile-session token last only when both genuinely completed. Format/version, checksum envelope, native Windows fingerprint, `Validated` meaning, profile Dirty marker and clean acceptance are unchanged.

Without a qualified peer, shutdown never invokes peer completion or publishes a clean profile token. The profile remains `Dirty`; Catalog `Validated` evidence is retained because it is the existing mutable-Catalog provenance marker, not paired clean attestation. Every subsequent unpaired reopen is ineligible for the clean fast path. No Catalog or Preview is silently created to produce a peer. `OpenAdmittedAsync` enforces pair eligibility before the existing deep SQLite validation and leaves its returned session unpublished for activation/configuration.

## Evidence retained

Local Windows tests use `scripts/Test-Noninteractive.ps1` and task-owned fixtures. Successful runs reported zero owned processes:

| Run | Result |
| --- | --- |
| `lf019-core-baseline.trx` | 65 passed |
| `lf019-shared-shutdown-corrected.trx` | 99 passed; paired startup/optimized reopen, shutdown, ownership transitions and UI readiness fixtures |
| `lf019-shared-recovery.trx` | 75 passed; recovery/restore, path boundaries, backup, transfer and lease transitions |
| `lf019-consumer-transition-port.trx` | 89 passed; transfer, recovery, selection, paired completion and backup |
| `lf019-neutral-recovery-head.trx` | 44 passed; neutral production-schema/mutation tests plus unpaired completion/reopen/disposal |

The neutral suite links the existing production-schema and mutation tests to the moved owners with a synthetic locations adapter. Its unpaired effect adapter is test-only managed IO; it does not qualify native Mac locking/durability. CI now runs this suite on Windows and macOS without Windows targeting packs. Exact-head CI results remain pending publication/qualification.

Retained failures: the initial shutdown run had 72 passes and one fixture failure because reflection targeted the old coordinator field; fixtures now target the actual shared owner without weakening assertions. A new unpaired test initially expected peer evidence release before read-usage disposal; corrected to the unchanged original order. A missing `System.IO` import caused one adapter compilation failure and was corrected.

Interrupted independent Move qualification remains unresolved. `lf019-shared-open-admission-corrected.trx` had 114 passes, one existing native symlink skip and one interruption diagnostic failure. `lf019-shared-transfer-journal.trx` had 62 passes and one such failure. Both returned CatalogUnavailable/PendingRecovery, retained the journal, and refused a writer because an immediate closed journal read encountered sharing violation. A subsequent diagnostic read succeeded. The same test failure reproduced on untouched accepted main `38bb1ed` in independent clone `C:\Git\Agents\LF-WIN-DEV-019-CatalogExtraction-Baseline` (`lf019-accepted-baseline-interruption.trx`: one passed/one failed). No root cause, test relaxation, writer-conflict retry or unrelated Agent 018 correction is claimed.

## Performance observations

Unchanged Windows synthetic fixture, two warmups/eight measured samples, using independent clones and separate private desktops: current create median 253.852 ms / reopen 35.537 ms; accepted baseline create 233.579 ms / reopen 34.412 ms. Runs overlapped on the same PC; treat the roughly 20 ms create difference as an observation requiring sequential confirmation, not a proven regression or accepted budget.

The small neutral unpaired fixture recorded create 197.449 ms, two deep reopens 7.502/7.911 ms and shutdown 1.423/0.136/0.075 ms in its corrected run. These use test-only managed effects and exclude the Windows host consumer graph, so they are not a Windows fast-path comparison or Mac performance claim. Native ownership/recovery preflight, representative host shutdown, sequential baseline comparison and final-head performance qualification remain outstanding. Raw TRX and `artifacts/lf019-orientation/performance.json` are retained in the owning workspaces.

## Required remaining implementation and acceptance

1. Move the remaining authoritative startup selection/create, destination activation, full-settings commit/readback, rollback/RecoveryRequired and restore/reactivation decisions from `StorageManagement*.cs` into the shared lifecycle. Preserve the full opaque application-settings transaction through a narrow configuration port; do not build another coordinator or duplicate the Windows workflow in #425.
2. Finish necessary repository/declaration closure for Collections/Smart Collections, Media Roots/Assets and LUT intent/reference owners. Do not pull WPF Browser/Player types into shared assemblies to resolve co-located declarations.
3. Route neutral unpaired composition through admitted opening with qualified/native-effect fixtures; add final transaction/rollback/cancellation and preservation regressions. Finish native interruption qualification without weakening its refusal assertions or taking over Agent 018's unrelated work.
4. Complete final-head neutral/macOS/native checks, full local Windows Release/companion suite, exact-head CI, installer/portable and fresh local package validation. None is claimed complete by this checkpoint.
5. Build the task-owned `artifacts\release\LightflowStudio\LightflowStudio.exe`, verify startup/shutdown/dependencies, newer-than-commit timestamp and zero owned processes, then supply the complete isolated startup command. No acceptance executable has been refreshed or supplied yet.

Mac #425 remains blocked until the complete extraction is accepted and merged. Agent 015 must still implement/qualify the native effects, compose the unpaired host, preserve logical state/identities through controlled edits and reopen, qualify process locking/disposal/publication and measure real deep reopen costs. Neutral compilation alone is not Mac product acceptance. No user Catalog, external physical fixture or other agent workspace has been modified.
