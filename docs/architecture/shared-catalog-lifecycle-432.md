# Shared Catalog lifecycle extraction — architecture decision gate

**Current continuation:** Jeremy approved the narrow host-consumer effects port and conservative unpaired completion on 2026-10-12 in #432/#425. The original decision gate below is historical. Implementation is in progress; see [the implementation checkpoint](catalog-lifecycle-432-implementation.md). This approval is not implementation acceptance.

Assignment: LF-WIN-DEV-019. Issue: #432; downstream #425; migration parent #366; Catalog coordination #289; R2 #393.

Status: dependency inventory and proposed boundary, not an implementation or acceptance report. Owner review is required for the host-consumer boundary below before production changes. No code has moved. No schema, provider, durability, storage policy or accepted ADR has changed.

## Source and administration

Independent clone: `C:\Git\Agents\LF-WIN-DEV-019-CatalogExtraction`.
Branch: `codex/432-shared-catalog-lifecycle`.
Independently fetched main: `38bb1ed8fe65a4bb6232a542e79a5d2288fea90f`, accepted #412 / PR #422 merge.

On 2026-10-12, verified #432 was initially absent from Project #4 and had no native parent or dependency. Added native parent #366, native #432 blocks #425, and Project #4 membership with existing Catalog Area and Backlog Status. Priority remains unset. #425 retains native parent #366, Catalog Area, Next Status and unset Priority; its issue explicitly pauses implementation until #432 acceptance and merge. #393 remains Backlog/incomplete and already depends on #425. #289 and #366 remain In Progress/incomplete. There is no new dependency from #432 to #425/#393 and no introduced cycle.

019 is unused in fetched main's registry. The kickoff's owner allocations 015, 017 and 018 are preserved even though their registry records are not merged on this main. Local reservation records orientation, not implementation. Remote branch inventory contains no competing #432 implementation branch at inspection time; this does not establish the state of unpushed agent work.

## Current ownership and exact coupling

| Responsibility | Current authoritative source | Coupling relevant to extraction |
| --- | --- | --- |
| Open/create and schema/integrity validation | `CatalogDatabaseService.cs`, `CatalogMigrations.cs` | Storage-location interface, startup diagnostics/reasons, admission exception; service callbacks revalidate storage at explicit SQLite boundaries. Creation uses an exclusive new file; missing open never creates a replacement. |
| Catalog identity and SQLite session | `CatalogDatabaseContracts.cs` | Session publication, ownership callback and readiness-failure callback; session authoring uses the owning mutation lifecycle. |
| SQLite provider, WAL/FULL and pool retirement | `CatalogSqliteConnectionFactory.cs` | Published/checked-out guards; drain prevents stale reopen after disposal; Windows assessor is referenced by diagnostic target formatting. Provider/runtime policy remain unchanged. |
| Repositories and authored state | `CatalogClassifications.cs`, `CatalogDescriptions.cs`, `CatalogAssetState.cs`, `CatalogCollections.cs`, `CatalogSmartCollections.cs`, `CatalogMediaRanges.cs`, `CatalogMarkers.cs`, `CatalogSubclips.cs`, `CatalogPreviewFrames.cs`, `MediaFileCatalog.cs`, `MediaRoots.cs`, `LutCatalog.cs` | Repository session delegates and mutation participant; several files co-locate value models, interfaces, policy and persistence. Media/LUT owners need a separate declaration-level closure audit before moving files wholesale. |
| Logical mutation admission and drain | `CatalogMutationLifecycle.cs` | Neutral implementation; counted nested operations, captured continuations, one quiescence generation, cancellation and shutdown wakeups must remain one owner. |
| Catalog writer lease and startup evidence | `StartupStoreEvidence.cs` | Windows ownership-journal acquisition/binding, Win32 handle identity fingerprint, durable journal writes; clean Catalog certificate is paired with Preview and profile-session evidence. |
| Profile/process ownership | `ApplicationInstance.cs`, `ApplicationDataProfile.cs`, `StartupSessionCompletion` in `StartupStoreEvidence.cs` | Windows mutex/pipe application activation is separate from per-Catalog lease. Profile initialization and containment precede storage use. Profile session state spans both stores. |
| Native storage admission | `CatalogLocationAdmission.cs`, `WindowsStorageLocationAssessor.cs` | Shared `StorageLocationPolicy` evaluates existing assessments; Windows adapter binds canonical paths and guards main-file identity. Injectable assessment alone does not substitute for native binding. |
| Path and volume identity | Domain storage facts, Application `PortablePathIdentityValidator`, `WindowsStorageLocationAssessor.cs`, `MediaRoots.cs` | Native canonical identity and logical IDs remain separate. Windows path comparison/native guards must not silently become Mac authority. |
| Recovery and restore | `CatalogRecovery.cs`, `.Transfer.cs`, `.Transport.cs`; `StorageManagement.cs` restore/reactivation | SQLite-aware backup/integrity and managed retention coexist with Windows closed-read guards, storage assessment, transfer helpers, local spool and rollback. |
| Transfer journals | `ClosedCatalogTransferJournal.cs`; Application `ClosedCatalogJournalFormat` | Shared sequence/digest validation already exists; file owner currently requires bound Windows profile/directory/file guards and durable append/readback. Journal paths never grant use authority. |
| Closed transfer and publication | Application `ClosedCatalogTransfer`; `WindowsClosedCatalogTransferOperation.cs`; `StorageManagement.Transfer.cs` | Shared transfer state machine exists. Windows operation also owns portable manifest/staging work alongside native effects. Coordinator closures own session drain, consumer stop, destination ownership, activation, commit/readback and rollback. |
| Configuration | `IStorageConfigurationStore` in `StorageManagement.cs`, `AppSettingsStore.cs`, `LightflowStorageLocations.cs` | Full application settings include unrelated export, LUT and screengrab preferences. Catalog transitions preserve the entire previous/intended configuration, not just CatalogDirectory/CatalogId. Default settings depend on Windows application composition. |
| Activation/readiness and rollback | `StorageManagement.cs`, `.Selection.cs`, `.Creation.cs`, `.Transfer.cs` | The same coordinator owns source/destination leases, provisional sessions, commit ordering and authoring reopening. No second coordinator should recreate these workflows. |
| Background consumer stop/restart | `StorageManagement.cs` constructor and scheduler/monitoring helpers; `.Transfer.cs` | Coordinator directly constructs Browser/media/LUT/Preview services, monitoring and derived-work scheduling. Move stops consumers before closing SQLite, and failure/success paths restore consumer availability. |
| Application shutdown | `StorageManagement.cs` `DisposeAsync`; `CatalogExitBackup.cs`; WPF application/MainWindow exit composition | LUT cache disposal, monitoring/scheduler stop, Catalog mutation drain, transition semaphore, Preview maintenance, both session closes and paired clean attestation are ordered in one owner. Presentation and application exit remain WPF effects. |

## Material boundary decision

The shared lifecycle cannot be extracted as a database service plus native file/lease adapters alone. That leaves the authoritative activation/rollback/shutdown workflow in Windows and makes #425 recreate it.

The alternative of moving the existing coordinator wholesale pulls Browser, Preview, media scheduling, settings defaults and Windows composition into the shared Catalog boundary. That is wider than a narrowly native-effects extraction.

Exact evidence at the accepted source:

- `StorageManagement.cs:135` onwards constructs the repository/service/consumer graph, including Windows Browser volume composition.
- `StorageManagement.cs:348` onwards computes a clean Catalog/Preview pair and downgrades both peers when the pair is incomplete, before database use.
- `StorageManagement.Transfer.cs:49` onwards drains admitted mutations and stops monitoring/derived work before closing the source session. Its activation/commit/rollback closures remain the authoritative transaction owner.
- `StorageManagement.cs:1021` onwards stops consumers and drains Catalog authoring, then enters Preview maintenance. `healthy` requires both a Catalog session and a Preview store. Profile clean completion requires both store certificates.
- `StartupStoreEvidence.cs:51` onwards binds a clean certificate to the prior profile-session token, native file fingerprint and absence of recovery files. `StartupSessionCompletion` durably marks the profile Dirty before use.

This is an ownership/scope decision, not evidence of a current production defect. Native handle ports can preserve the writer protection. The unresolved decision is which host participants the shared workflow may require and how the Catalog-only Mac host participates in the existing paired profile completion.

## Proposed resolution for owner review

Extract one shared Catalog lifecycle owner with an explicitly bounded **host-consumer participant port** in Application, alongside the requested native effects ports. Windows supplies the existing consumer graph through a compatibility adapter. Shared code owns when stop, drain, close, restart, activation and rollback occur; the host adapter only implements the effects. Do not give the host a second activation/rollback algorithm.

Keep paired Catalog/Preview/profile clean attestation unchanged for Windows. For #425, explicitly choose one of these boundaries before implementation:

1. Recommended bounded Mac host: no Preview peer means no paired clean-profile attestation; always use the existing conservative deep-validation path on reopen. Never fabricate a completed peer. Writer ownership, dirty evidence, mutation drain and safe disposal still apply. This carries a startup/performance qualification obligation and does not claim Windows fast-path parity.
2. Require a real qualified Preview/profile peer in #425. This expands the minimal Mac host beyond Catalog-only composition and needs explicit issue scope/qualification changes.

The recommended option does not alter Windows certificates or the database format. It must be made explicit in #432/#425 so Agent 015 does not invent peer-completion semantics later. This report requests the decision; it does not declare it accepted.

## Proposed assembly and port ownership

| Boundary | Proposed ownership after accepted extraction |
| --- | --- |
| Domain | Existing logical IDs/authored values; no native paths as durable IDs. Move declarations only where needed; no schema/key migration. |
| Application | Lifecycle contracts, storage policy, existing transfer/journal protocol, host-consumer effect contract and configuration transaction effects. No concrete SQLite/native/UI dependencies. |
| Catalog | Existing migrations, SQLite sessions/pools/repositories, mutation lifecycle, SQLite backup/restore and the one authoritative activation/recovery/transition implementation. No shell reference. |
| Windows compatibility | Existing native assessor/guards, writer lease/fingerprint, durable journal file/directory effects, atomic publication and full-settings persistence; existing WPF/consumer composition. |
| Mac adapter / #425 | Existing Mac assessor plus separately implemented and qualified native writer, durable journal, closed-file/identity and publication effects; host participant choice above. No copied lifecycle. |

Required effects must return owned capabilities, not successful booleans that substitute for native identity:

- Admission/binding: initial assessment, revalidation and canonical use routing, bound main-file lifetime and closed-boundary release.
- Writer evidence: exclusive journal acquisition before SQLite, bound ownership revalidation, database fingerprint, durable certificate write and safe release.
- Durable journal: exclusive creation, guarded append/flush/readback, framed reading, profile/directory revalidation and retained recovery evidence.
- Closed artifacts/publication: guarded regular-file enumeration/read, containment, exclusive staging, durable writes and qualified atomic directory no-replace publication. Unsupported capability fails closed.
- Configuration: capture full previous/intended configuration, commit, readback verification and restore; preserve unrelated settings and current ordering.
- Host consumers: stop/restart owned background consumers and provide real peer completion evidence where a peer exists; no dispatcher/control/native handle types in the shared contract.
- Process/profile composition: acquire profile/process ownership before use, keep Catalog lease independent, dispose only task-owned resources.

Exact signatures and exact moved declarations remain pending. This is not a new framework/Common assembly proposal.

## Validation status and remaining work

No production edits, tests, performance measurements, package refresh or CI acceptance have occurred. No Draft implementation PR is ready. The before map above is source inspection; the after map is proposed, not achieved.

After the decision: finish declaration-level closure and accepted-contract audit; extract owners and Windows adapters; add neutral production-schema/session/mutation regressions and native ownership/journal/publication/rollback tests; run required focused and full private-desktop Release checks, neutral Windows/macOS and Mac adapter CI, companion checks and exact-head installer/package validation. Refresh the task-owned `artifacts\release\LightflowStudio\LightflowStudio.exe`, verify smoke/dependencies/timestamp/no owned processes, then supply an isolated acceptance root and complete startup command. Do not supply an old or unbuilt executable as a hands-on handoff.

Remaining Mac gaps include native effects implementation/qualification, production host composition, independent-process locking, safe disposal, real logical-state round trip, physical fault qualification and any unqualified publication capability. Neither this inventory nor neutral compilation constitutes Mac product acceptance. R2 remains incomplete, #425 blocked, and no merge/release/tag is authorized.
