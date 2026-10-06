# Central NAS Catalog architecture decision — M2 / #369

## Decision and scope

**Recommend B: central NAS authority plus an exclusive local WAL working Catalog. Require NAS availability during authoring for the first release.** This is the preferred direction to prove, not a release qualification or owner acceptance. G2 remains **OPEN / UNPASSED**: ownership/publication semantics, cross-platform handoff and path identity remain unresolved. No implementation follows from this document.

Fetched authoritative main: `aed6c2906637c8a5a9d71c1c18ac24bed48a8724` (unchanged). Research branch: `codex/x2-central-catalog-architecture-20261005`, based on published evidence commit `824e142a946910239bc01188f5d90ac92713f23d`. Historical [Outcome D](LIVE_NAS_QUALIFICATION.md) is preserved verbatim. Its local-active recommendation was not an owner decision to abandon central NAS Catalogs. This document supersedes that recommendation, not the measurements.

Requirements: one logical CatalogId, Catalog-owned authored metadata, interchangeable supported Windows and Apple Silicon Mac use, one active owner, no media-folder metadata sidecars, local or central media, local derived Preview/cache. A local working copy is not an independently editable Catalog. No concurrent collaboration or offline merge system is required. Architecture/control manifests reside inside the central Catalog container, not alongside media.

## Evidence we actually have

See [native report](LIVE_NAS_QUALIFICATION.md), [runtime](SQLITE_ARM64_RUNTIME_EVIDENCE.json), [path findings](PATH_IDENTITY_MATRIX.md), [root proposal](PROPOSED_ROOT_IDENTITY_ARCHITECTURE.md) and [Windows handoff](WINDOWS_NAS_FIXTURE_HANDOFF.md).

- Apple M2 Pro, macOS 26.6.2, pinned Microsoft.Data.Sqlite 8.0.29 / SQLitePCLRaw 2.1.13; selected arm64 SQLite 3.53.3. Local Catalog schema 19 uses WAL/FULL; local Preview schema 4 uses WAL/NORMAL. Local controls and owned process-kill recovery passed their bounded checks.
- The tested SMB 3.1.1 NAS/share: WAL request returned `delete`; current production create/open rejected the mandatory-WAL mismatch. Current production cannot directly use this NAS Catalog. This is not proof that all SMB implementations behave identically.
- Proof-only DELETE/FULL control completed 41 checks. Thirty-two synthetic assets and authored state across 23 tables survived tested backup/replacement and process-kill recovery. A hot rollback journal was recovered; committed work survived and uncommitted bulk edits rolled back. Same-host connection contention worked. These are neither cross-host ownership nor power-loss guarantees.
- Real network interruption, server restart, machine/power loss, Windows SMB locking parity and sustained large-Catalog performance were not qualified. A closed directory rename is not a disconnect experiment. NAS backing filesystem, controller/cache/UPS guarantees are unknown. Observed VFS name `unix` does not establish its exact locking implementation.
- Case-fold, Unicode, trimming/backslash and symlink hazards remain. Architecture selection fixes none of them.

## Upstream and filesystem boundary

SQLite says rollback mode can mitigate remote-file risks, but does not test SQLite across all network configurations; successful early tests are not assurance. Direct remote SQLite is conditional on the installation's correct locking and synchronization, not generally endorsed SMB support. Its usual recommendation is an engine colocated with its storage, accessed through a service. [SQLite network guidance](https://sqlite.org/useovernet.html)

Normal WAL requires same-host shared-memory coordination. The exclusive-locking, no-shared-memory exception is a different connection discipline; it neither describes current pooled Lightflow access nor certifies remote flush behavior. B keeps WAL local. [WAL documentation](https://sqlite.org/wal.html)

Rollback safety depends on ordered durable journal/database writes and reliable journal invalidation; FULL cannot repair a lying flush or broken storage. DELETE-mode EXTRA adds directory synchronization after journal unlink where implemented; evaluate it if A is revisited, rather than assuming FULL alone covers sudden power loss. [Atomic commit](https://sqlite.org/atomiccommit.html), [synchronous settings](https://sqlite.org/pragma.html#pragma_synchronous)

Rollback locking must coordinate all relevant processes through compatible OS/VFS semantics, including hot-journal recovery. Do not delete a journal to clear a lock. [SQLite locking](https://sqlite.org/lockingv3.html) Missing journals, broken locks/sync and inappropriate copying or renaming of active files can cause corruption. [Corruption guidance](https://sqlite.org/howtocorrupt.html)

SMB FLUSH requests propagation to the backing persistent store; protocol wording is not evidence that this NAS, both OS adapters and the complete hardware stack honor the required failure boundary. Durable opens can survive connection loss and reconnect; loss of a connection does not establish that the previous owner is gone. SMB caching leases are not application ownership leases. [SMB FLUSH](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-smb2/e494678b-b1fc-44a0-b86e-8195acf74ad7), [server FLUSH processing](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-smb2/026984f6-38af-4408-8200-50557eb0a286), [connection loss](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-smb2/eb5bfe99-47fe-4e87-8e87-08a084dcefb6), [durable reconnect](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-smb2/3309c3d1-3daf-4448-9faa-81d2d6aa3315)

The architecture below is an engineering proposal inferred from these constraints and our evidence, not an upstream-provided protocol.

## A — direct active NAS SQLite: ten answers

1. **Upstream support:** conditional feasibility, not a blanket SMB durability endorsement; see network guidance above.
2. **Required guarantees:** mutually coherent locks across Windows/Mac and aliases; coherent reads after lock acquisition; correct byte-range I/O, truncation and error reporting; journal creation/content durability before database overwrite; database flush before journal invalidation; durable namespace changes; no lost hot journal; stable file identity while open. The complete client/server/filesystem/device stack must honor them.
3. **Runtime verification:** check mode, VFS, integrity, permissions, basic contention and capabilities. These detect some bad configurations. They cannot establish power-loss flush truth, every reconnect interleaving or absence of stale caches. Integrity checks cannot detect every logically lost authored update.
4. **Support scope:** necessarily depends on OS/SMB client, NAS firmware, filesystem and configuration, plus storage durability. A narrow qualified matrix and change/requalification policy would be necessary.
5. **Reconnect:** freeze authoring on uncertain I/O/ownership, preserve DB/journal, abandon uncertain connections safely, reacquire proven ownership and recover before reopening. Never blindly retry an ambiguous commit as a new edit. This requires operation-level reconciliation.
6. **Stale state:** yes, stale locks/cache or incompatible locking could cause undetected correctness failures. A successful ping, heartbeat or integrity check cannot exclude them.
7. **DELETE/FULL:** materially removes normal WAL's shared-memory dependency and supplies rollback recovery. Forty-one checks demonstrate useful feasibility, not sufficient risk reduction for production. EXTRA/fullfsync choices require actual platform/VFS evidence, not generic preference.
8. **Further qualification:** authorized disposable crash harness, interruption at journal/write/flush/unlink boundaries, delayed/reordered/error I/O, real NAS restart and power-loss trials, reconnect/stale handles, disk-full, replacement/restore, aliases and cross-host contention. Vendor durability documentation and fault testing must agree; tests alone cannot prove every installation.
9. **OS parity:** separately qualify Windows and Mac and mixed-host sequential handoff/contended open. Same SMB dialect does not imply identical OS API behavior.
10. **Trust decision:** not preferred for years of Jeremy's metadata on the present evidence. A might be supportable on a tightly controlled stack, but puts the sole mutable truth and its recovery journal across the uncertain boundary for every transaction. Explicit ownership reduces concurrency risk without eliminating remote I/O risk.

## B — authority, ownership and durable publication

### One Catalog, two persistence boundaries

The NAS container holds the authoritative committed generation, previous verified generation, ownership/control record and retained backups. Each immutable generation describes CatalogId, format/schema, generation identity, parent identity, owner epoch/session, byte length and cryptographic hash; any required authored resources must be covered by the same manifest. Use unique identities as well as sequence numbers; timestamps do not order commits.

The owner machine holds a task-independent durable recovery location for the same CatalogId: active local SQLite DB/WAL, base central generation, last acknowledged generation, owner/session epoch, local dirty state and any pending publication identity/hash. Preserve WAL with its DB after a crash; SHM can be rebuilt through SQLite. Never infer cleanliness from a separate dirty flag alone: a crash between DB commit and flag update must conservatively cause recovery/verification. Preview/cache is separate and disposable.

A local SQLite commit makes the edit recoverable on that computer, subject to local storage durability. Only successful central publication makes it available to another computer and protected against loss of the owner machine. UI must not say “Saved to NAS” for a merely local commit. This remains one Catalog because only the admitted owner authors and only a fenced successor generation becomes central truth; orphaned working copies are recovery evidence, not alternate writable Catalogs.

### Minimum ownership contract — a proof prerequisite

Use one canonical central authority per CatalogId and an OS-local instance guard plus **server-coordinated exclusive admission**. Human-readable central metadata identifies machine/session, epoch, base generation and last contact. Heartbeats are diagnostic. Clock expiry only means “possibly unavailable,” never permission to steal.

All admission, generation promotion and release must be serialized by a qualified primitive that rejects a stale session at the instant of mutation. A fencing number checked in client code before a rename is insufficient: the client can pause, lose ownership, then complete its stale write. Likewise check-then-replace is not compare-and-swap. Plain lock-file existence, delete/recreate takeover, directory listing freshness and TTL are not the protocol.

The next proof must establish whether supported SMB APIs can couple exclusive authority to publication across disconnect/reconnect with no stale mutation window. A separate lock handle does not automatically protect a pointer rename through another handle. If this cannot be demonstrated, **use the fallback authority service below**, rather than weakening exclusion. No automatic forced takeover in initial scope. Explicit takeover requires evidence that old access is fenced/revoked (including outstanding server operations), acquisition of new exclusive authority and verified central state. If fencing cannot be established, refuse takeover even after user confirmation. Availability loses to integrity.

An older client that bypasses the protocol must not open the managed central container as a normal mutable Catalog. Protocol/version admission and layout/access controls must be designed; a friendly lock file alone does not constrain legacy software or manual file copying. Detect replicated/divergent authority containers and stop rather than choose by timestamp.

### Publication sequence and uncertain outcomes

1. Under valid admission, verify the last committed manifest, Catalog identity/schema and candidate database integrity. Stage locally and verify before enabling local WAL authoring. Reject incomplete, ambiguous or incompatible authority state.
2. From the live local database use a SQLite-aware backup into a **local** standalone snapshot. Do not copy an active DB without WAL. Finalize, validate identity/schema/integrity and hash the exact immutable snapshot. Snapshotting concurrent local writes is allowed only with a defined consistent boundary; later edits remain dirty.
3. Upload to a unique central candidate, never overwrite current or previous. Flush contents and required namespace metadata through a qualified adapter, then verify length/hash by reopening. Readback catches transfer damage but may read cache; it is not proof against power loss.
4. Publish a manifest referencing the complete candidate with expected parent generation and current owner epoch **at the serialized/fenced commit point**. Qualify atomic visibility and durable persistence separately. A rename alone does not prove both; multiple renames are not one transaction. Preserve a recoverable prior control record and previous generation before retiring anything.
5. Acknowledge only the published durable generation; record that acknowledgement locally. Edits newer than the snapshot boundary remain unpublished. On lost acknowledgement, freeze and reconcile by publication identity: central equals candidate means already published; central equals expected parent permits an idempotent retry only under valid authority; any other state requires recovery, never blind overwrite.
6. Clean close drains edits, publishes and verifies the last dirty boundary, closes working DB safely, durably records clean release and relinquishes admission. If publication fails, offer an explicit “Close with work saved only on this computer” recovery path; do not report successful handoff or discard recovery state.

SQLite's backup API supplies a consistent snapshot, not distributed publication/ownership. Handle BUSY/LOCKED with bounded retry and surface I/O failure; do not treat an incomplete backup as publishable. [Backup API](https://sqlite.org/backup.html), [API completion/error contract](https://sqlite.org/c3ref/backup_finish.html)

### Twenty explicit B answers

| Question | Proposed initial behavior |
|---|---|
| 1. Exclusive lease? | Server-coordinated admission plus fenced publication, local instance guard and fail-closed uncertainty; contract above must be proved. |
| 2. Metadata location? | Central Catalog container; durable local recovery record mirrors session/base/acknowledgement. No media sidecars. |
| 3. Prevent dual owners? | Atomic admission and publication exclusion, independent of heartbeat timestamps; stale sessions cannot promote. Same-machine and mixed Windows/Mac opens use the same authority. |
| 4. Clean close? | Final verified publication then clean release; another machine acquires and verifies that generation. |
| 5. Process crash? | Preserve local DB/WAL and publication intent; restart recovers locally, reconciles central acknowledgement and reacquires authority before writing/publishing. |
| 6. Computer crash? | Same recovery if disk survives. If lost, recover last durable central generation; unpublished local edits can be lost. No zero-loss claim. |
| 7. NAS disappears? | Pause new authoring/import/background Catalog mutations on detected uncertainty; retain local committed/queued recovery state. In-flight operation may have unknown central outcome. |
| 8. NAS returns? | Verify authority identity, ownership/epoch and generation afresh; reconcile pending publication. Never resume because the pathname merely exists again. |
| 9. Stale lease? | Missed heartbeat suggests suspicion; actual release or proven fencing plus serialized acquisition establishes safe succession. |
| 10. Automatic takeover? | No forced timeout takeover. Normal acquisition after verified clean release can be automatic. Unclean takeover requires explicit recovery and proven exclusion. |
| 11. Safe for second machine? | Admission first, verified committed manifest/database and clean/recovered control state; show last central save and unresolved previous-session risk. No opening an upload-in-progress. |
| 12. Old copy overwrite? | Expected-parent and owner-epoch validation at commit point; reject stale copies. Preserve/export orphaned recovery evidence for deliberate recovery, with no automatic merge. |
| 13. Frequency? | Publish dirty state frequently, plus explicit Save to NAS and final close. Start next proof with a proposed 10-second coalescing target, not an approved SLA. Measure size/load and agree maximum dirty age before acceptance. |
| 14. Transactional generations? | Yes: immutable candidates and one qualified serialized promotion; no overwrite of active central SQLite pages. |
| 15. Previous generation? | Yes, at least current and previous fully verified generations, plus independent retained backups; never garbage-collect uncertain or recovery-needed state. |
| 16. Reuse backup architecture? | Reuse SQLite-aware copy, identity/schema/integrity validation and protected recovery concepts. Existing methods are not a lease or crash-atomic distributed commit. |
| 17. Local crash state? | DB/WAL, CatalogId, canonical authority identity, base generation, epoch/session, pending publication/hash and last acknowledgement; protect against partial bookkeeping updates. |
| 18. Extended offline? | Remain protected; allow clearly stale read-only inspection if useful. Preserve unpublished work across close/restart. Never silently fork the Catalog. |
| 19. Continue editing offline? | No for first release. NAS required while authoring. Reachability probes alone do not prove ownership; uncertainty also pauses writes. |
| 20. Understandable? | Open the NAS Catalog; show “In use on Mac,” “Saving to NAS,” “Saved to NAS at …,” or “NAS unavailable—editing paused; recent work saved here.” Recovery explains location and last central save, not epochs. |

A timer is not a hard recovery-point guarantee. Dirty exposure includes scheduling delay, snapshot duration, transfer/verification and acknowledgement. Stop accepting edits once the agreed maximum unpublished age is exceeded; an outage can still strand already accepted edits locally. A crash can occur before outage detection. If Jeremy requires every acknowledged edit to survive immediate machine loss, each edit/batch needs central acknowledgement before UI claims durable success. That stricter latency tradeoff needs owner selection. Local WAL checkpointing alone never advances NAS truth.

Online-required B avoids divergent offline editing and merge semantics. Offline editing would permit two recoverable histories after takeover, require a conflict/import policy and increase both conceptual and engineering scope to Very High. It is not part of this recommendation.

## Side-by-side decision matrix

| Criterion | A: direct NAS DELETE/FULL | B: central authority + local WAL |
|---|---|---|
| Transaction integrity / partial write | Remote pager/journal correctness for every edit; faulty remote ordering can affect live truth | Local pager transactions; interrupted upload stays an uncommitted candidate if promotion protocol is correct |
| Corruption / stale filesystem state | Live DB and hot journal depend on coherent remote I/O | Current/previous immutable generations reduce mutation exposure; stale control state or faulty promotion is still dangerous |
| Network interruption | Freeze, preserve hot journal, resolve uncertain transaction and locks | Freeze authoring, keep local state, reconcile candidate acknowledgement before publishing |
| Process crash | SQLite rollback recovery plus application ownership recovery | Local WAL recovery plus pending publication reconciliation |
| OS/machine/power loss | Remote durability may be uncertain; client caches can matter | Local disk surviving supports recovery; lost disk loses unpublished edits; acknowledged central state depends on qualified flush |
| NAS/server loss | Active DB/journal may require rollback or backup restoration | Prior verified generation remains usable if storage survives; total NAS loss needs independent backup |
| Ownership/stale owner | App admission plus SQLite locking; remote stale writer can mutate live DB | Same hard admission problem, but fenced promotion can confine stale copies to uncommitted candidates |
| SQLite assumptions | Qualified rollback locks, sync and journal lifecycle over SMB; normal WAL unavailable here | Normal WAL stays local; NAS stores closed snapshots/control state, not active WAL |
| Windows/Mac parity | Separate pager/locking/reconnect qualification, then mixed-host contention | Shared protocol/state machine; separate OS adapters for locks, flush/namespace durability, local WAL and recovery |
| Backup/recovery | Consistent API backup under ownership; retain journals; never active-file copy/replace | Local consistent snapshot can feed publication and backups; restore by publishing a new successor of selected older content |
| Open/handoff | Avoid full download, but remote schema/integrity/query I/O | Acquire/verify/download costs scale with Catalog; handoff waits for final publish |
| Writes / Browser / Smart Collections | Remote latency in queries/commits; proof predicate is not full Smart evaluation | Foreground local I/O expected to benefit; snapshot/hash/upload contention must be measured |
| Publish / close | Each commit remotely synchronized; close still verifies ownership/recovery | Periodic transfer cost; close only drains remaining dirty work, potentially slow/unavailable |
| UX | Simple location model until uncertain I/O/lock recovery | Same NAS-open concept; visible central-save freshness and protected mode are essential |
| Implementation | Medium policy/storage work; High ownership/recovery/UI; Very High qualification across storage stacks | High storage staging, ownership, generation/recovery/UI and parity work; Very High if offline editing added |
| Testability | Inject pager faults plus physical OS/NAS matrix; finite tests cannot certify arbitrary remote filesystems | State-machine fault injection and exhaustive commit-stage interruption are tractable; physical fencing/flush proof remains mandatory |
| Maintainability | Ongoing vendor/OS-dependent pager support burden | Explicit versioned protocol with shared tests; more owned code but bounded failure states |

No credible engineer-week range follows from the available 32-asset proof. Relative complexity is more honest than inventing a budget. B does not eliminate NAS support constraints; it narrows where they must be trusted.

## Failure and recovery rules

| Event | A disposition | B disposition |
|---|---|---|
| Second open (same or other OS) | Deny via application owner guard, even if SQLite could allow readers | Deny active authoring before staging/open; display owner and last central save |
| Kill during edit | Hot-journal recovery under exclusive ownership | Local WAL transaction recovery; committed local work remains dirty if not published |
| Kill during snapshot/upload | Valid backup completion must be checked | Ignore incomplete candidate; current remains unchanged; reconcile exact pending identity |
| Kill after promotion before acknowledgement | Ambiguous commit needs application-level reconciliation | Recognize matching committed candidate as success; no duplicate generation/edit |
| Sleep, network partition, stale session | Stop until lock/transaction certainty restored | Stop authoring on uncertainty/wake; stale epoch cannot promote; heartbeat timeout cannot transfer ownership |
| NAS restart/power loss | Recover DB/journal only after stack consistency established | Validate control record and referenced candidate; if inconsistent, enter recovery with previous state, never silently roll back acknowledged edits |
| Corrupt current / incomplete manifest | Preserve evidence; protected backup restore | Block open; offer explicit recovery to previous verified generation, as a new generation under exclusive authority |
| Disk full/permission failure | Transaction/backup fails; preserve recovery files | Local full: stop edits; central full: stop publication then edits at dirty-age bound, retain local work/current/previous |
| Old machine returns after takeover | Must be prevented from resuming live writes | Quarantine its dirty working copy; cannot overwrite newer central state; no automatic merge |
| NAS destroyed | Independent backup required | Independent backup required; local working copy may aid explicit recovery but is not silently made a new authority |

Initial automatic backups in `LightflowStudio/CatalogRecovery.cs` can reuse today's Automatic backup (`onlyIfNeededToday`) and retain 10 daily / 3 monthly entries. These are retention/backup policies, not frequent central durability. Current staging validation, SQLite `BackupDatabase`, protected replacement and rollback are useful components. Current `File.Move` and separate metadata writes do not establish cross-file crash atomicity or fenced NAS publication. `MainWindow.CatalogBackup.cs` also has optional close-backup behavior; B's central publication must not depend on that option. Restores must run under exclusive ownership, retain displaced content, and preserve monotonically advancing authority history. Retention on the same NAS is not protection against loss of that NAS.

## Performance: bounded evidence only

One warm 32-asset NAS control measured approximately: production restore 2.36 s; failed production open 1.00 s; raw DELETE open/read/write/commit combined 0.98 s; simple rating predicate 32 ms; NAS backup 3.21 s; local backup 1.25 s; protected closed replacement 6.37 s. These combine operations and are not representative application benchmarks or acceptance thresholds. No B publisher, full Browser or full Smart Collection benchmark exists. Full-snapshot B has size-dependent transfer/hash/integrity cost; a 10-second target may be infeasible for large Catalogs. First measure bounded dirty age and contention before considering incremental publication; do not prematurely design a replication log.

## Preview and root mapping

Use machine-local Preview/cache for both A and B. Current Preview persistence stores derived records/fingerprint/retry/cache state; no authored Preview state has been identified that needs transfer. Authored preferred frames, rotations, color/LUT choices and related metadata belong to Catalog and must travel with its generation. If referenced authored resources such as LUT bytes live outside the DB, the portability audit must include them in the Catalog-owned generation contract; a valid DB/hash alone would not prove a complete Catalog. Transfer of optional derived cache is an optimization, never a correctness requirement. Regeneration on the other machine is expected.

Both architectures preserve logical RootId/AssetId and relative media identity while resolving RootId through machine-specific paths. Windows UNC/drive and Mac mount spellings are not Catalog identity. B must separate local operational mappings from portable authoring without dropping other machines' mappings during snapshots. Current case-folding, NFC/NFD, path trimming/backslash and symlink-escape findings require their own fail-closed policy/possible versioned migration. Do not normalize globally or infer same media from matching display paths. A and B both need root preflight and operation guards; neither authorizes schema changes here.

## Weighted recommendation, fallback and next proof

**B wins on integrity and recoverability**, because ordinary transactions stay on local storage and failed transport can leave prior central truth intact. It also offers explicit failure states and one shared Windows/Mac protocol. It costs more application engineering and introduces an honest unpublished-work window, but those risks can be exposed, bounded and tested. A's smaller apparent implementation does not outweigh ongoing dependence on remote pager correctness. UX remains opening one NAS Catalog; no independent-catalog synchronization is proposed.

Fallback: retain B's user model but place a small authority/publication service beside durable central storage, enforcing epochs and conditional promotion server-side. This is not a claim that such a service exists, nor permission to implement it. If pure SMB cannot supply the minimum primitive, select that service-backed variant for a new owner review. Do not fall back silently to direct NAS SQLite or local-only Catalogs. Service hosting/security/support would add scope and needs a separate decision.

Unresolved decisions: pure-SMB admission/fencing feasibility; server flush/namespace guarantees and supported NAS matrix; maximum acceptable unpublished age and zero-loss expectations; large-Catalog snapshot cost; independent backup destination/retention; full authored resource closure; legacy-client exclusion; explicit takeover/recovery UX; path policy and machine-local mapping persistence. Recommendation B is clear, but these prevent declaring the architecture fully qualified or G2 passed.

Proposed **next bounded proof, only after authorization**: build a disposable protocol model/harness outside production with two simulated owners, 32 existing synthetic assets and full authored-row invariants. First prove admission/promotion or reject the SMB primitive; do not begin with more SQLite throughput tests. Inject pause/crash/failure at every acquisition, snapshot, upload, flush, promotion, acknowledgement and release transition. Require zero stale promotions, no dual admitted authoring, no acceptance of incomplete generations, exact acknowledged-state preservation, previous-generation recovery and explicit orphaned-local-state handling. Exercise corrupt manifests, full disk, stale caches/epochs, clock jumps and lost acknowledgements. Then qualify the chosen primitive on real Windows/Mac in both handoff directions and same-machine/mixed-host contention. Real network/server/power interruption requires a separately authorized disposable environment. Stop if fencing or durability assumptions cannot be defended; propose the service fallback. Agree dirty-age/performance targets and proof budget before pass/fail acceptance. No execution of that proof occurs in this slice.

## Preservation and handoff

Prepared Windows round-trip remains **preserved, intentionally deferred, not executed**. Existing supplemental fixture tests metadata portability, not B's unimplemented authority protocol; retain it as input and extend only after architecture selection. Do not represent it as qualifying B.

Reproducible evidence is preserved in the repository-relative reports linked above, `evidence/nas-live/`, `CROSS_OS_ROUNDTRIP_MANIFEST.json`, `REPRODUCIBLE_COMMANDS.md` and `tools/X2CatalogProof/`. `EVIDENCE_HASHES.json` records the historical artifact hashes; `CENTRAL_CATALOG_ARCHITECTURE.sha256` verifies this document. The retained disposable fixtures comprise a closed-snapshot transport fixture, an initial mandatory-WAL failure fixture, and the completed 32-asset DELETE/FULL control with authored-state and crash-recovery evidence. Transfer archives and exact local/NAS locations are retained privately for resumption; they are not needed to reproduce the architectural conclusion. No NAS write, cleanup, remount or disruptive probe occurred in this slice.

#369 and Epic #366 remain open; G2 and the combined owner gate remain incomplete. The research is submitted through a Draft PR for owner review, without merge authorization. X1/X3 are not messaged or resumed. Only this research document and its dedicated checksum file change in the architecture publication; no production, schema, proof-tool, backup or runtime-policy changes. No native/build process is launched. The next bounded proof and Windows round-trip remain deferred.

Publication review generalized the NAS identity and removed local transfer filenames, fixture directory names, local handoff details and publication mechanics. Versions, measurements, limitations, repository-relative evidence links and all architecture reasoning remain unchanged.

Unexpected architectural finding: B does not make ownership trivial. A separate SMB lock plus an epoch written into a file is not sufficient fencing for a later namespace replacement. Also, a successful transfer readback does not establish power-loss durability. These are the first proof gates, ahead of the deferred Windows round-trip.
