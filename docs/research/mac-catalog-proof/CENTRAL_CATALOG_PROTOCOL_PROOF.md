# M2 central-authority protocol proof — 2026-10-06

**Disposition B2: pure SMB fencing is insufficiently established for the supported-client design; retain architecture B and require a minimal central authority/publication service for the next owner-reviewed proof.** This is not a theorem that no pure-SMB protocol could ever work. No service is implemented here. G2 remains OPEN / UNPASSED; #369 Open / In Progress, Epic #366 open.

## Provenance and merge boundary

Owner accepted architecture B FOR PROOF and explicitly authorized evidence merge. PR #373 accepted head `c2181d57d2828a7e70ec5434f17eab5f340f6af1` was unchanged, appropriately sanitized, and contained no production/schema diff. Unit tests and Windows installer/portable package checks succeeded; tagged release was correctly skipped. Main had not advanced. Normal merge commit: `6c61451cf4f22d773b38a67eea0787b2110113e6`. The merged documentation branch was retired remotely and locally; private drafting history and fixtures were preserved. Fresh main at that merge is the baseline of `codex/x2-central-protocol-proof-20261006`.

[Accepted architecture](CENTRAL_CATALOG_ARCHITECTURE.md) and historical [Outcome D](LIVE_NAS_QUALIFICATION.md) remain unchanged. Current mandatory-WAL production cannot directly open/create its Catalog on the tested SMB share. B means one central NAS Catalog, exclusive local WAL working copy, local Preview, explicit dirty state and verified central publication. This proof does not change any production source or Catalog schema.

## What was executed versus modeled

`tools/X2CentralProtocolProof/proof.py` creates uniquely owned disposable local/NAS fixtures. It uses macOS filesystem APIs through Python, separate subprocesses and a deterministic atomic-transition **oracle**. The oracle specifies the required boundary; it is not a server, not distributed coordination code and not evidence that SMB provides that boundary. Actual primitive observations are in [OWNERSHIP_RESULTS.json](protocol/OWNERSHIP_RESULTS.json); [FAULT_MATRIX.json](protocol/FAULT_MATRIX.json) distinguishes proven, simulated, reasoned only and unresolved.

Native actual work used the existing mounted SMB 3.1.1 test share without unmount, network settings, sleep, NAS restart or power interruption. Eight separate processes competed for create-new and mkdir: exactly one winner and seven existing-object rejections each. This proves only observed same-Mac-client contention, not all cross-host/server failure behavior. No packet trace was taken.

A one-byte `fcntl` lock request returned errno 45 (ENOTSUP) on the mounted share. Whole-file `flock` was available, rejected a second process, and became acquirable after the owner process was killed. Ownership metadata remained. The previous lock holder's death was real; an old client's delayed operation was represented by a separate process schedule, not a real network reconnect.

**Critical counterexample:** while the successor held the separate whole-file owner lock, an independent stale candidate successfully replaced the current manifest. The stale contents described generation 1 after generation 2. This demonstrates that this lock is not a fence for that namespace operation. It is an intentionally unsafe candidate design on disposable data, not a production corruption event. Client token checks before that replace would leave the same pause/loss/resume window.

macOS `renamex_np(RENAME_EXCL)` returned EEXIST for an existing target and preserved its bytes. That establishes an observed no-overwrite condition, not a compare-and-publish check of owner plus expected generation, cross-host correctness or crash durability.

## Authoritative primitive analysis

SMB byte-range locks are associated with an Open/FileId, and the client also resolves same-client process conflicts. They are not a general atomic condition on a separate file replacement. [SMB LOCK](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-smb2/6178b960-48b6-4999-b589-669f88e9017d)

SMB rename exposes ReplaceIfExists: false requires failure when the target already exists; true permits replacement. It does not carry an application CatalogId/epoch/expected-generation comparison. [SMB rename information](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fscc/52aa0b70-8094-4971-862d-79793f41e6a8)

Some opens are preserved across connection loss for reconnect. Disconnection or missed heartbeat cannot establish the previous session has lost all authority. [SMB connection-loss handling](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-smb2/eb5bfe99-47fe-4e87-8e87-08a084dcefb6)

Apple documents rename namespace behavior, not application token validation or the tested NAS's power-failure durability. Atomic visibility, durable persistence and conditional authority are different requirements. [Apple rename](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/rename.2.html)

| Candidate primitive | Useful evidence | Missing requirement / disposition |
|---|---|---|
| Create-new / mkdir | One winner observed among eight local processes over SMB | Persistent stale object has no safe timeout revocation; removing/recreating it can let old clients resume. No server atomic token comparison. |
| Atomic replacement | Replacement observed while separate lock held | Replaces destination regardless of old expected contents; not CAS. |
| No-replace rename | Existing destination rejection observed | Could compete for a unique slot, but does not alone make admission, takeover and payload promotion one conditional transition. |
| Byte-range / whole-file locks | Byte-range unavailable via tested Mac adapter; flock excludes tested processes | Server enforcement on mixed hosts and reconnect unqualified; separate publication is not fenced. |
| Exclusive handle to the control object itself | Plausible narrower adapter design | Windows sharing modes vs Mac adapter parity, stale-handle invalidation, atomic control updates and durable reconnect are unproved. Do not infer impossibility from separate-file counterexample. |
| Immutable ownership UUID + epoch | Makes stale requests recognizable | Recognition in a client is not server enforcement at mutation. |
| Append-only, no-replace transition chain | A serious possible pure-filesystem alternative: admission, takeover and publication all compete for next immutable slot | Needs complete-record atomic installation, validated predecessor/owner transitions, recovery from incomplete/invalid winning slots, nonrollback durable chain discovery and cross-client qualification. Not implemented or proved here; a frozen/poisoned slot and stale cache must not become silent takeover. |

No tested primitive meets the complete required contract. Per owner instruction, uncertainty at this gate selects B2 and stops further pure-SMB qualification. The positive oracle tests cannot be substituted for this missing primitive. Server-side serialized validation would need to reject stale epochs/parents before changing committed state, retain immutable verified payloads and make acknowledgement/retry crash-safe. A small authority/publication service should expose acquire, status, conditional publish, release and explicit takeover; it need not serve media or SQL queries. Hosting, authentication, storage durability and service crash recovery would require a new bounded owner-reviewed proof. No service code was written.

## Shared protocol and ownership

[STATE_MODEL.json](protocol/STATE_MODEL.json) defines acquire, validate, open local, publish, acknowledge, heartbeat, release, recover and takeover. Central records carry CatalogId, version, monotonic sequence, unique generation/publication identity, expected parent, epoch/session, length/hash and previous generation. Local state retains DB/WAL, authority identity, base, pending publication and last acknowledgement. Unknown dirty state is recovered conservatively; an independently written boolean cannot prove clean shutdown.

Acquire rejects a second active owner. A stale heartbeat only prompts investigation. Explicit takeover must warn that abandoned unpublished work may exist, establish exclusive authority, invalidate the old token at the same serialization boundary used by publication and preserve current/previous. A user confirmation is not a fencing primitive. On the oracle, takeover requires an explicit `fenced=True` precondition; this deliberately marks the missing adapter capability, not an achieved test result.

After a takeover, returning old local work is quarantined for explicit recovery, never last-writer-wins publication. Wrong base or unexpected central identity/generation blocks automatic writes. Duplicate publication requires matching identity and request; lost acknowledgement is reconciled against central committed identity. Rollback to an older sequence is detectable against a client's remembered acknowledgement; a brand-new client cannot detect a coherently rolled-back entire authority without independently retained durable history. That history is part of service/storage qualification, not solved by hashes.

## Publication and failure boundary

Actual payload controls: create local WAL workload, full checkpoint, SQLite-aware local backup, finalize standalone DELETE snapshot, local integrity/hash verification, upload NEW candidate, fsync file, read back into a new local file and verify integrity/hash. No active NAS SQLite and no Preview transfer. The tested writer never promotes these candidates as real authoritative generations because no fenced primitive is qualified.

Partial and corrupt candidates fail hash comparison without changing the two retained verified payloads. Missing committed manifest blocks automatic selection. A previous verified payload can be read back and validated; publishing it as a new authority generation remains unimplemented. Selecting the highest-looking filename or silently reverting an acknowledged generation is forbidden.

Promotion failure before the oracle's indivisible transition preserves current/previous; lost acknowledgement after it resolves through publication identity. A failure *inside a real SMB or service promotion*, including server power loss, remains unresolved. No finite hash readback demonstrates persistence through server failure. The required service must durably retain current/previous control state before acknowledging and preserve an idempotency record across restart.

Local SIGKILL control proves committed-but-unpublished data survives in local WAL while an uncommitted edit rolls back, for the disclosed Python SQLite runtime. It is supplementary, not a rerun of the pinned production provider proof. The prior actual production-provider local controls remain evidence in the accepted research. No OS crash or machine power loss occurred.

## Availability and durability policy

Require central reachability AND valid admission for initial authoring. On loss/uncertainty, pause all authored mutations, imports/identity creation, background Catalog writes, migrations, restore and publication. Local read-only review can continue with explicit last-central-save status and no hidden writes. Detection is not instantaneous; edits accepted before failure detection remain dirty. Reconnection must revalidate authority identity, version, epoch, parent and pending publication; any mismatch enters recovery.

Recommend a hybrid trigger: bounded periodic dirty-age target plus coalesced transaction batches, idle opportunities and explicit milestone/close publication. Idle-only can starve under sustained editing; per-edit full snapshots amplify transfer cost. Ten seconds is exploratory, never an SLA. Stop accepting edits when the owner-approved maximum unpublished age is exceeded. Do not silently increase that bound for a large Catalog; apply backpressure or revise the design.

Machine loss can lose every edit newer than the last acknowledged generation. For interval T and snapshot/verification/transfer/commit time P, normal exposure is approximately T+P plus scheduling/queue delay; failures make this unbounded unless authoring is paused at a specified dirty-age limit. With no approved limit and no measured fenced promotion, **there is no defensible finite maximum loss window yet**. Previous generation and independent backups must survive separately; storage destruction requires backups outside that NAS.

## Exploratory publication cost

Three samples per size, sequential warm/cold-cache mixture; median phase times below. The payload uses deterministic UUIDs and approximately 4 KiB notes per row. These are byte-scaling controls, not realistic complete Catalog benchmarks.

| Rows | Snapshot bytes | WAL checkpoint | Snapshot | Local integrity | Local hash | Upload + file fsync | Readback + local fsync |
|---|---:|---:|---:|---:|---:|---:|---:|
| 32 | 159,744 | 1.8 ms | 4.1 ms | 0.8 ms | 0.3 ms | 68.8 ms | 27.0 ms |
| 4,096 | 19,075,072 | 2.5 ms | 46.2 ms | 8.8 ms | 11.1 ms | 1,488.7 ms | 43.3 ms |
| 16,384 | 76,341,248 | 3.8 ms | 142.2 ms | 25.6 ms | 38.7 ms | 6,518.3 ms | 3,090.8 ms |

Measured phase subtotals ranged 94–107 ms, 1.57–1.76 s and 9.54–10.90 s respectively. **Not total publication latency:** returned-file hash/integrity checks were executed but not separately timed, and fenced promotion/durable acknowledgement were not implemented or measured. Full phase records and hashes: [PERFORMANCE.json](protocol/PERFORMANCE.json). Readback may be cache-served; fsync success is not server power-loss qualification. No GUI responsiveness or concurrent production workload was measured.

The largest control already approaches/exceeds the exploratory 10-second interval before all verification/promotion overhead. A 10-second full-snapshot cadence is therefore not justified generally. A nominal 10-second trigger plus this sample's measured subtotal would expose roughly 19.5–20.9 seconds before omitted phases, even without failures; that is an illustration, not a maximum or SLA. Size-sensitive backpressure and an agreed dirty-age limit are required before promising durability. There is no defensible finite machine-loss bound today.

Fault matrix: 25 rows — 2 proven within stated control scope, 20 simulated, 1 reasoned only, 2 unresolved. Six additional primitive observations are in OWNERSHIP_RESULTS.json. A simulated success is not a production or SMB pass. The unsafe stale replacement is a demonstrated counterexample, not an accepted protocol success.

## Preview, paths and Windows gate

Preview/cache stays machine-local, rebuildable, and excluded from payloads. Durable authored resources must be part of the Catalog generation contract. CatalogId/RootId/AssetId are portable; authority location and machine-specific media mappings are distinct. No path normalization or mapping migration is introduced. Existing case/Unicode/symlink hazards remain independent G2 blockers.

[Updated Windows handoff](protocol/WINDOWS_HANDOFF.md) targets the selected B2 contract: Mac acquire/local-WAL/publish, Windows acquire/edit/publish next generation, Mac reacquire/verify, reverse direction and stale-owner rejection. It is blocked pending a separately authorized service/adapter proof; no Windows execution or remote control occurs here. The earlier fixture remains preserved, but it cannot qualify a nonexistent protocol.

## Reproduction and limits

Run from repository root with a disposable authorized mounted SMB test parent and a task-local local parent:

```sh
python3 tools/X2CentralProtocolProof/proof.py --local-parent <task-local-data> --nas-parent <authorized-mounted-test-parent> --out <task-local-results>
```

Supply only an owner-authorized disposable test parent. The public safety guard verifies it is below an existing mounted SMB share; the executed private guard additionally hardcoded this task’s authorized parent. Only that guard was generalized for publication; provenance records both source hashes. No experiment logic changed and no additional native run occurred after the B2 gate. Results normalize operational paths; the private local resumption map is not published. The deterministic larger workload is a **four-column synthetic authoring payload, not the full Lightflow schema**. It models snapshot byte cost, not Browser/Smart Collections or production authoring semantics. It uses Python's SQLite runtime (see PROVENANCE.json), not Microsoft.Data.Sqlite. Three samples per size are exploratory and cache-sensitive.

Setup failures were retained, not counted as passing evidence: Python compile-cache location required task-local override; initial byte-range-lock success assumption failed with ENOTSUP and was replaced by recording the result; first backup verification failed opening a standalone WAL-mode copy read-only (relative and absolute URI attempts). Finalizing the copy to DELETE before transfer fixed that harness path. This reinforces the requirement for standalone verified publication payloads; it is not a production journal-policy change.

Only new proof tooling and evidence change. No production/schema/service implementation, X1/X3 contact, Windows round-trip, M4+ or disruptive NAS test. Raw fixtures remain retained; mount stays unchanged. Proof stops at B2 pending owner review.
