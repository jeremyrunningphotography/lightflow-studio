# Closed Catalog transfer implementation — assignment 011

LF-WIN-DEV-011, 2026-10-10. Bounded production implementation of #392; Draft PR and owner acceptance remain required. No merge, release, schema/provider change, storage-policy expansion or #412 UI is authorized by this report.

Workspace: C:\Git\Agents\LF-WIN-DEV-011-ClosedTransfer. Branch: codex/392-verified-closed-transfer. Fetched main: 011c97d2533d3e6eb441425592d6a0ba966f388c, rechecked during implementation. The canonical checkout is unchanged. Prerequisite #388/#389/#390/#391 implementations and architecture PR #413 are merged. The accepted architecture remains [closed-catalog-transfer-392.md](closed-catalog-transfer-392.md).

## Current ownership and changed seams

| Responsibility | Existing authoritative owner | Implemented adaptation |
| --- | --- | --- |
| Paths / identity | LightflowStorageLocations; AppSettings.CatalogId | Preserve CatalogId and centralized routing; unused destination required. Profile/preferences/Preview/Jobs/machine identity stay local. |
| Open/create/migrate/validate | CatalogDatabaseService | Existing open/identity/schema/integrity authority activates the published destination. Incomplete pending artifacts without readiness refuse opening. No new SQL lifecycle. |
| Snapshot and recovery | SqliteCatalogRecoveryService.BackupDatabase / Inspect / BeginRestoreValidatedAsync / RestoreTransaction | New CatalogRecovery.Transfer.cs exposes finalized local snapshots and the managed-Backups inventory. Existing SQLite online backup and standalone DELETE finalization remain the only snapshot implementation. |
| SQL readers | CatalogDatabaseSession.DisposeAsync / CatalogSqliteConnectionFactory.CloseAndDrainAsync | Close admission, drain checked-out connections, clear only this factory pool. Reject retained stale-session and reopened stale-connection use. No process-wide pool clearing. |
| Logical mutations | CatalogMutationLifecycle.QuiesceAsync | Move drains outside admitted mutation execution; exit/manual backup retains existing quiescence and cancellation ownership. |
| Background reads | Coordinator DisposeMediaMonitoringAsync / DisposeDerivedWorkSchedulerAsync / RecreateMediaMonitoringAsync | Stop existing consumers before source close; recreate against committed or proven prior session. |
| Relocation / activation / rollback | LightflowStorageCoordinator; existing CatalogSessionActivator and TryReactivateCatalogAsync | StorageManagement.Transfer.cs replaces RelocateCatalogCoreAsync with a shared-orchestrator consumer. Provisional destination remains unpublished while authoring is blocked. Activate and verify before configuration save/readback. Proven rollback resumes source; uncertainty blocks authoring. |
| Configuration | AppSettingsStore.Save / AppSettingsStorageConfigurationStore | Flush temporary settings before existing replace/move; reload expected CatalogId/path after activation. |
| User and exit backup | CreateUserBackupAsync; BackupForExitAsync; CatalogBackupDialog | Thin compatibility delegation to the same orchestrator. Snapshot locally, then transport closed bytes. Active source stays open. #272 UI, schedules, retention, retry/cancel/exit semantics remain. |
| Restore input | CatalogRecovery.Transport.cs PrepareRecoveryInputAsync / CopyTransportBackLocallyAsync | Verify container and copy back to qualified local storage before existing recovery opens SQLite. Legacy discovery stays intact; unsuitable legacy source bytes use a guarded, size/hash-verified local copyback bridge, then existing full validation. |
| Native suitability | WindowsStorageLocationAssessor; StorageLocationPolicy | Policy unchanged. Windows operation retains native source/destination and staging identity leases. Single-link regular files only; links/junctions refuse transfer. |
| Path safety | PortablePathIdentityValidator | Validate safe manifest names/collisions and copied Catalog RootId/AssetId/relative-path candidates without rewriting authored values. |
| Cleanup | Existing recovery and operation ownership | Transfer never deletes the retained source, prior backups or uncertain staging through remembered paths. Native leases are scoped and released on completion/failure. |
| UI / packaged verifier | Existing ChangeCatalogLocation_Click and CatalogBackupPathVerifier | Existing folder picker chooses the parent of a new Lightflow Catalog child; existing styled confirmation explains source retention. Packaged path verifier now measures transport payload length and confirms SQLite stays in local transfer staging. No #412 Move/Open controls. |

## Exact LUT ownership and storage boundary

Jeremy resolved the dependency gate in [the #392 decision](https://github.com/jeremyrunningphotography/lightflow-studio/issues/392#issuecomment-6101842488). Assignment is not ownership. External LUT files stay external; unavailable references do not invalidate an otherwise healthy Catalog.

Current durable Catalog paths are CatalogDirectory\LightflowCatalog.db and CatalogDirectory\Backups. Catalog migrations store LutResources.LutId, DisplayName, OriginalFileName, ContentSha256, kind/size/timestamps and MediaAssetColor authored references. These rows are preserved by the SQLite snapshot. They contain no managed LUT byte directory and no portable managed-file path.

Actual LUT file resolution belongs to CatalogFolderLutLibrary.RefreshAsync and ApplicationLutLibraryCache in ColorManagement.cs. They scan the profile preferences CameraLutFolder and CreativeLutFolder, retain the resolved FilePath in runtime candidates/cache, and associate content hashes with durable IDs. AppSettingsStore.cs migrates legacy LutFolder into the two configured folders. The legacy non-isolated fallback in LutCatalog.DefaultFolder is J:\Photography\LUTs; isolated profiles start empty. These are externally configured paths, not Catalog-owned managed dependency locations. ApplicationDataDirectory\settings.json remains profile-owned and is not transferred.

No current authoritative repository-owned LUT byte store exists within the durable Catalog unit. Therefore no .cube bytes are included. The manifest records every LutResources ID/content hash/original filename as a nonpayload external dependency with an unresolved-on-receiving-host diagnostic. This does not claim a file is absent on the source host, nor reinterpret, clear, relink or replace any reference. Current profile paths remain unchanged during relocation; receiving-machine resolution retains existing behavior. An unknown .cube placed inside CatalogDirectory triggers the ordinary unknown-entry refusal; its presence or assignment cannot invent ownership. Player, Color and Batch Encode processing code is unchanged. No embedding, dependency packager, LUT management UI or media export was added.

## Shared contract, manifest and journal

Lightflow.Application/ClosedCatalogTransfer.cs owns the neutral state machine and narrow lifecycle/snapshot/native/configuration ports. Modes are MoveAndActivate, ClosedTransport, BackupTransport and LocalCopyback. Every operation carries a fresh OperationId/generation and expected CatalogId/schema/application identity. MediaSource facts cannot authorize Catalog transfer. Neutral code references neither WPF nor SQLite/native APIs.

Manifest version 1 declares exact safe relative names, roles, lengths and uppercase SHA-256 hashes, identity, snapshot provenance, publication/readiness versions and external dependencies. Sorted canonical JSON binds the manifest digest. The required database name is LightflowCatalog.db for atomic whole-unit publication and catalog.snapshot for conservative transport. Missing/extra files or directories, unsupported roles, unsafe names, collisions, changed hashes, malformed metadata and inconsistent pending/readiness claims refuse use. Metadata bounds are 32 MiB and 100,000 artifacts/dependencies. Move includes every recognized Backups database/metadata entry without invoking retention; invalid/nonmatching historical backups are retained and labelled as historical evidence. Unknown Catalog entries refuse the move. Source startup evidence stays at source and is not copied as fresh startup authority.

ClosedCatalogJournalFormat.cs verifies strict state ordering, intent/completion pairs, operation consistency and the previous-record digest chain. Profile-owned ClosedCatalogTransferJournal persists Catalog Transfers\<OperationId>.journal outside Catalog/Preview/Temporary ownership. Frames contain little-endian length, canonical JSON and SHA-256, with maximum 256 records / 128 KiB per record. Each append flushes and validates readback through its exclusively held single-file handle and native directory lease. Preflight includes old/intended configuration and native assessments; later records bind manifest/publication/activation/configuration receipts. Failure receipts explicitly identify proven previous-owner recovery versus blocked authoring/evidence retention.

Restart checks journals before any ordinary Catalog writer opening or creation. Missing/truncated/inconsistent/pending evidence blocks authoring, keeps existing data and reports the operation/journal. For recorded identities, restart performs fresh native assessments and compares the recorded identities; remembered paths and timestamps grant no authority. Recovery is intentionally conservative: it does not silently select the retained stale source, delete artifacts, infer completion after a torn commit, or automatically resume chunks. Reconnect/review and a newly authorized preflight are required when state cannot be proven. #412 owns the subsequent user-facing recovery selection. Completed/verified rollback journals do not block ordinary existing-lifecycle startup.

## Publication, cancellation and existing consumers

WindowsClosedCatalogTransferOperation owns only byte/native effects. It uses an exclusively created qualified local snapshot directory under Catalog Transfers and holds immutable regular-file read leases. Move obtains a closed source-main-file lease after lifecycle/read drain; a foreign writer refuses the move. Stage is a fresh sibling .lightflow-<OperationId>.incomplete directory; its native object is retained. Payload writes are exclusive and flushed. Size/hash verification precedes publication.

Qualified local atomic publication uses SetFileInformationByHandle(FileRenameInfo) with ReplaceIfExists=false, the bound staging handle, a terminated wide-character target name, and same-volume sibling staging. It verifies directory object identity and all bytes/metadata after rename. No permissive fallback replaces or overwrites an existing destination. File flush/readback and native namespace behavior are tested; hardware power-loss and directory-entry persistence certification are not claimed.

Backup transport uses a unique LightflowCatalog-User-v<schema>-<UTC>-<OperationId>.incomplete container, with catalog.snapshot payload, pending.json, manifest.json and ready.json. Readiness is logical verified publication, not atomic replacement. Every completed-container discovery and restore read revalidates native binding, canonical metadata and all sizes/hashes. Incomplete/corrupt containers stay unlisted; prior verified backups remain intact. No SQLite connection is opened on a selected transport destination. Legacy raw backups retain their existing filename discovery and identity/integrity validation, with local copyback before SQLite when active-local qualification is absent.

Move ordering is preflight → quiesce/read drain → existing-owner finalized snapshot → complete unit stage/verify → publish → existing lifecycle open/validate/activate → save/reload configuration → durable completion → release authoring. Source is retained as a pre-move recovery copy, stale after destination edits. Failed activation/save never claims success; proven source/config rollback resumes its owner, otherwise RecoveryRequired disables authoring and prevents clean-start attestation. Transfer evidence and local immutable spools are deliberately retained; automatic garbage collection is outside this slice.

Cancellation is honored before publication and during asynchronous copies/hash reads. Synchronous SQLite/native calls and admitted readers must finish safely. The publication/activation/configuration interval finishes without cancellation and reports the actual committed or rolled-back outcome. Ordinary shutdown does not invoke transfer when none is requested. The owning storage gate always releases, including consumer-recreation errors.

## Qualification and remaining gates

| Evidence class | Coverage / limit |
| --- | --- |
| Neutral contract | Deterministic receipt/state, journal append faults, cancellation, byte corruption, metadata/path safety and refused synthetic native facts. Windows/macOS CI runs the same neutral project. Synthetic facts do not qualify devices. |
| Native Windows | Real task-owned NTFS directory no-replace/object identity/collision, closed SQLite snapshots/restores, checked-out reader drain, foreign writer refusal, metadata corruption, source/backup retention and restart writer refusal. |
| Native Mac | Existing MacStorageLocationAssessor suite and standalone native host remain required CI jobs. No Mac transfer-effects adapter, production Catalog application composition or Mac UI exists in this bounded Windows consumer integration. |
| Network / physical | Byte transport protocol is exercised locally and negative policy facts are tested. Actual SMB disconnect/remount, a real network destination, cross-platform production round trip, removable-media/power-loss/surprise-removal qualification remain explicit host gates. No JRPhoto4T or user data is modified. |
| Product acceptance | Owner packaged hands-on and architecture acceptance remain required; Draft PR stays unmerged. #392/#412/#406 and R2 #393 remain incomplete. ExFAT active use and active network Catalogs remain refused. |

Future #412 consumes the shared modes/results through the existing coordinator. It must distinguish retained pre-move source from the current owner, verify a selected closed unit before first opening, preserve selected IDs, use explicit recovery selection and retain the accepted lifecycle/identity/mutation/configuration owners. It must not activate catalog.snapshot containers or infer creation when expected storage is missing.

## Validation and reproduction

Validation evidence is retained under artifacts\validation and project TestResults. The neutral project passed 136/136 and companion checks passed 90/90. Full Windows Release validation at commit 24325393c15718738bf5556a09c83e35160e500b passed 2,808 tests with 2 existing environment-dependent skips (lf011-full-release-verified.trx). After the final journal-profile qualification and held recovery-input binding changes, the affected native/recovery/backup suites passed 89/89 (lf011-final-bindings.trx). Final-head CI and freshly committed packaging are tracked on Draft PR #417; acceptance remains pending. Earlier failures and diagnostics are retained.

The journal profile is admitted through the existing local durable-storage policy, with native ancestor bindings held through journal disposal. Recovery holds qualified local parent and regular-file bindings throughout existing SQLite inspection and snapshot copy, then revalidates them. This closes the interval between local preparation and SQLite use without introducing a new SQLite owner.

Representative DARKMATTER synthetic local fixture: 17,260,544 bytes; snapshot 139.1752 ms, copy 39.9047 ms (412.506 MiB/s), SHA verification 23.3471 ms, three flushed/readback journal records 23.7046 ms, journal scan 6.1752 ms, cancellation before copy 12.3072 ms, ordinary shutdown 27.6389 ms. Native journal lease release was verified. These observations are not release budgets, network performance or hardware certification.
From the task clone:

    dotnet test .\Lightflow.Application.Tests\Lightflow.Application.Tests.csproj -c Release --disable-build-servers
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -Filter 'FullyQualifiedName~ClosedCatalogTransferIntegrationTests|FullyQualifiedName~CatalogExitBackupTests|FullyQualifiedName~StorageManagementTests|FullyQualifiedName~CatalogBackupPathTests'
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller

Native WPF tests and packaging run through the required private-desktop boundary. Every application smoke/acceptance launch supplies an absolute task-owned --data-root. The final handoff will name exact PR commit, refreshed packaged executable, isolated acceptance root, startup command, dependency/smoke outcomes and remaining gates.

