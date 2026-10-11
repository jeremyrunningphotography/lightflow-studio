# External Catalog filesystem qualification — LF-MAC-RES-013

2026-10-10 · Issue #406 · Apple Silicon Mac · documentation/evidence only.

## Executive recommendation

**CONDITIONAL acceptance of bounded research; REVISE Mac-produced closed transport; BLOCK active ExFAT support.** JRPhoto4T demonstrates ordinary synthetic SQLite recovery and file operations, but does not qualify Lightflow's intended direct active cross-platform Catalog workflow. Keep ExFAT active admission refused and defer direct external active use. No common active filesystem is currently qualified by both accepted adapters.

Two concrete publication gates remain. Both Darwin exclusive rename APIs reject file and directory publication on this physical ExFAT volume with **errno 45 / Operation not supported**; `RENAME_SWAP` also rejects. In addition, Mac-created files acquire AppleDouble `._` companions. The accepted #392 exact-artifact-set rule rejects the otherwise complete physical transport container. This is a safely detected interoperability failure, not a reason to ignore files or change the accepted format. Physical writes stopped after discovering this refusal; all evidence remains retained.

Closed transport remains the preferred candidate strategy: finalize locally, transfer closed bytes, revalidate the complete unit, and copy back to qualified local storage before SQLite use. This phase does **not** qualify the tested Mac producer for successful #392 publication. An owner-reviewed metadata strategy, native effects adapter and Windows consumption qualification are required first. Verified copying and readiness are not crash-atomic publication.

## Authority and inspected baseline

Jeremy's kickoff authorizes read-only identification, reuse of 007 evidence, exactly one new synthetic physical root after safety gates, maximum 256 MiB allocated, controlled failures, native research, documentation/Draft PR and directly affected issue summaries. It excludes owner-data changes, writing to 007's root, formatting/repair/drivers, surprise removal/power interruption/forced eject, production source/support-policy changes and merge.

Independent full clone: `/Users/jeremyrunning/Git/agents/LF-MAC-RES-013-ExternalCatalog`; branch `codex/406-mac-filesystem-013`. `origin/main` was fetched and rechecked as `9e9f3509f63f821fcaa91367eaa7cb8797935613`. No worktrees, canonical-checkout writes or other-workspace writes. SDK/package inputs were copied read-only from retained 007 dependency storage into this task; all builds, caches, outputs and fixtures are task-local.

Inspected authority includes root AGENTS.md and assignment registry; #406/#77/#289/#393/#366; accepted #388/#389/#390/#391/#392 contracts, code and tests; ADRs 0001/0005/0007 and shared-product boundaries; both #392 architecture and implementation reports; accepted #407 research/manifests/private inventories and #417 merged implementation. #407 merge is `dfab688757939a58c3e884c6938e97e17a0104de`; #417 merge is `a0a04da07dde9c8c7609f61ab3e21656c8c8b4dd`, accepted implementation `3c5ac9e33f614f980034b0b7fdbb06dbba04abea`. Earlier pending wording in historical reports does not override later owner acceptance.

013 is unused on baseline and reserved by this PR. Owner-designated 012 owns #412 and 014 owns R2 planning; their branches/workspaces are untouched. #406 remains Open/In Progress/Catalog/Priority unset, native parent #77 and blocker of #393. The native parent/dependency endpoints were rechecked. #77 and #393 remain open; R2 and broader Catalog/migration Epics remain incomplete. No native dependency or Project field was changed by this qualification.

## Prior accepted evidence, without repeating its matrix

007 proved physical Mac/Windows ExFAT identification, synthetic WAL/FULL and cross-process writer exclusion, checkpoint/normal close/reopen, SQLite backup/restore, closed-copy hashes, stable CatalogId/RootId/32 AssetIds and representative authored state. Windows changed exactly one intended note. Ordinary Windows safe removal returned CR_SUCCESS/no veto. Earlier Mac eject is owner-reported, not agent-observed. Immutable Mac return verified the original and Windows derivative without writing.

The unmodified published 007 return verifier passed **343/343 before and after this phase**. All **24 original Mac and 14 Windows** files still match lengths and SHA-256; 007 remains 37 MiB allocated. Original closed Mac hash is `d9a9010a5733d8342c57cece8a75896ade106265b3c205d250e7f35f88352008`; Windows derivative is `a1e7a33dba9dde85b0d60c4f6baf3b5d90ee4f615a586d421c5c4adb2e634e30`.

Those findings do not prove production schema/lifecycle, Settings activation, direct active admission, crash-durable namespace publication, hardware flush honesty or removal/power-loss recovery. This phase adds reader-held checkpoint behavior, controlled process recovery, alternate native publication APIs and actual accepted-format discovery failures. It does not rerun 007's full writer round trip for another passing result.

## Physical verification and safety baseline

Native `diskutil list external physical`, disk/volume plist queries, USBHost inventory, `statfs/getattrlist`, Foundation resource values and retained private inventory identify **JRPhoto4T / SanDisk Extreme Pro 55AF / USB / GPT / ExFAT**, writable/local/external. Persistent volume and partition identifiers, physical model and layer-specific capacities match private 007 evidence. Private identifiers and raw USB inventory remain internal. Current enumeration is not treated as a permanent BSD disk or mount assignment. Current USBHost inventory reports a 10 Gb/s link, matching accepted prior evidence; link speed is not a durability guarantee.

| Layer | Bytes |
| --- | ---: |
| Physical disk | 4,000,753,467,904 |
| Partition | 4,000,751,370,752 |
| Mac usable filesystem | 4,000,734,773,248 |
| Allocation unit | 1,048,576 |

Before creation, 40 objects in 007's retained root passed native volume/alias checks; root/mount canonical containment and symlink exclusion passed. Exact-root `lsof` found no holders. The new UUID name was absent, the writable/private identity baseline matched, and 100 MiB planned allocation was below 256 MiB. One root was claimed by `mkdirat` through a no-follow mount descriptor; it was opened with `O_DIRECTORY|O_NOFOLLOW`. Effects rechecked root descriptor identity, current volume facts, native containment and allocation headroom. Files use exclusive `openat` creation; no recursive deletion occurs.

Checks bound ordinary controlled operations, not hostile namespace races or a permanent authorization lease. SQLite receives a verified task path after boundary checks; there is no production Mac use-bound transfer lease in this harness. A deliberately created symlink is retained as negative evidence and never used as a write route. No personal directory/media/Catalog inventory was performed.

## Fixture provenance and final integrity

The new root contains two synthetic tables, one CatalogId, one RootId and 16 AssetIds. A closed local 16,384-byte SQLite seed generated canonical metadata through unchanged `ClosedCatalogManifestFormat`, `ClosedCatalogReadiness` and request records. Its application ID/schema are explicitly synthetic, not Lightflow's production schema. The metadata's fixed snapshot UTC is a deterministic fixture value, not an actual operation timestamp.

Physical source lifecycle, SQLite-aware backup and closed restored copy preserve all IDs and all 16 authored rows. Exactly the defined identity note changes to `committed LF013`; an uncommitted note is absent after restart. Full immutable read-only state/integrity comparisons passed, supplementing the physical harness's 26 checks. Lifecycle SHA-256: `852bbddfe67ffc268a2e85cfe8a43e26c5d0008e12a20ba47c65949c1191ff9b`; backup and restore SHA-256: `862e7172f175f26894f7cf4a50e75262026dc4484806e187d05a89c2252be719`.

Final retained fixture: **102,760,448 allocated bytes (98 MiB)**, including directories/sidecars; **415,345 regular-file logical bytes**, 99 objects, one deliberately known symlink and 49 AppleDouble companions. The harness's 415,360 logical-byte diagnostic follows the known symlink and includes its referent once more; the final audit's regular-file count is authoritative. Maximum allocation stayed under 256 MiB. `FINAL-MANIFEST.json` remains on the drive; final read-only audit includes its own hash and every regular-file hash. Published [013/summary.json](013/summary.json) provides the sanitized complete index. Root/manifest exact location and raw private identity/commands remain in `work/evidence`.

## Native publication results

| Operation on physical ExFAT | Observed result | Safe implication/alternative |
| --- | --- | --- |
| Exclusive file and directory creation | Success; repeated claims errno 17 EEXIST | Claim unique staging; collision never overwrites |
| Same-volume `renameat`, absent target | Success, bytes verified | Ordinary rename observed; not no-replace authority |
| `renameatx_np(RENAME_EXCL=4)` | errno 45 for file and directory | Retain staging; no permissive rename fallback |
| `renamex_np(RENAME_EXCL=4)` | errno 45 for file and directory | Alternate API does not restore the missing guarantee |
| `renameatx_np(RENAME_SWAP=2)` | errno 45 | No exchange/rollback guarantee |
| Ordinary `renameat` replacement of owned canary | Success; old descriptor retains old bytes; new inode at name | Namespace replacement observed; replacement violates unused-destination transfer semantics |
| File `fsync` / `F_FULLFSYNC=51` | Both return success | API acknowledgment; controller/power-loss persistence unqualified |
| Directory `fsync` / `F_FULLFSYNC=51` | Both return success | Metadata flush acknowledged; no torn-entry/crash persistence experiment |
| `symlinkat` | Success; `lstat` reports symlink; native assessor marks ambiguous | Reject linked transport/active paths; Windows representation untested |
| `linkat` | errno 45 | No hard-link publication substitute |
| Exclusive copy/readback | Closed bytes match | Candidate logical transport only; full physical container still refused |

`getattrlist` reports valid advisory-lock support and valid **unsupported** RENAME_EXCL/RENAME_SWAP bits (interface bits 1,051,584; valid bits 2,097,151). The results agree with actual calls. Namespace/object identity changed on replacement, so cleanup using the prior identity was refused and evidence retained. This is research identity logic, not an implemented Mac production cleanup port.

The internal case-insensitive APFS control successfully publishes both file and whole directory using RENAME_EXCL; collisions return EEXIST, directory inode survives publication and prior bytes remain. File/directory flushes acknowledge success. This qualifies the bounded local namespace control under the already accepted local APFS profile; it does not implement a Mac transfer adapter or certify hardware crash durability. File/collision timings in the physical summary sometimes include guard subprocess overhead and are not release performance budgets.

Apple's [rename manual source](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/man/man2/rename.2) makes exclusive/swap operations filesystem-dependent. Its general rename semantics do not confer no-replace support. Apple's [fsync manual](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/fsync.2.html) distinguishes host flush from device persistence and describes F_FULLFSYNC's stronger request. Neither a documented request nor an API success proves this bridge/controller's behavior under power loss.

## SQLite lifecycle and controlled recovery

Runtime: macOS 26.6.2 (25G83), arm64; Python 3.9.6/system SQLite **3.51.0**; copied task-local .NET SDK **8.0.425**, runtime **8.0.31**; Apple clang **21.0.0**. No Microsoft.Data.Sqlite provider is used for the new physical SQL fixture. Accepted Windows 007 evidence uses Microsoft.Data.Sqlite 8.0.29 / SQLitePCLRaw 2.1.13 / native SQLite 3.53.3; parity remains limited to the earlier synthetic schema evidence.

WAL and synchronous FULL=2 were observed. A long-lived read transaction retains its old view while a writer commits. TRUNCATE checkpoint reports **(1,1,0)** while that reader is held, then **(0,0,0)** after reader release. This demonstrates why logical mutation drain alone cannot establish complete SQLite closure.

A specifically owned child opens only `lifecycle.db`, begins IMMEDIATE and writes an **uncommitted** note. After its explicit ready signal, SIGKILL terminates/reaps that exact child. Reopen yields integrity `ok`, the prior committed note and stable identities; uncommitted state is absent. Checkpoint succeeds. This is process-failure recovery at one controlled boundary, not device failure or a complete crash matrix. Companion creation occurred; final zero-byte WAL and 32,768-byte SHM remain retained. Companions were not deleted to manufacture success.

SQLite-aware backup writes to an exclusively preclaimed synthetic file; standalone DELETE finalization, close, immutable restored validation and complete state equality pass. Backup bytes remain unchanged throughout failed staging/publication/collision controls. This demonstrates fixture backup preservation, not a production managed-Backups relocation inventory or retention implementation.

## Failure/discovery matrix and the AppleDouble blocker

Each physical container has canonical accepted pending/manifest/readiness bytes where appropriate and `catalog.snapshot`, never `LightflowCatalog.db`. A whole-root backup discovery cannot equate presence or `.incomplete` naming with completion.

| Control | Physical result | Internal declared-byte diagnostic |
| --- | --- | --- |
| Otherwise complete good container | **Refused: extra artifacts** | Shared format/readiness/hash passes |
| Partial payload, 1,024 bytes | Refused (extra artifacts masks length failure) | Payload mismatch |
| Truncated manifest, 20 bytes | JSON refusal | Same refusal |
| Missing readiness | Missing metadata refusal | Same refusal |
| Same-length hash mismatch | Refused (extra artifacts masks hash failure) | Payload mismatch |
| Cancelled staging | Missing manifest/readiness, retained | Refused |
| Interrupted staging | Missing manifest/readiness, retained | Refused |
| Destination collision | EEXIST; prior-good bytes unchanged | No overwrite |
| Pending journal / truncated frame | Recovery required / truncated-frame refusal | Same logical result |

The complete directory actually contains `pending.json`, `catalog.snapshot`, `manifest.json`, `ready.json` **and a `._` file for each**. Sample payload has `com.apple.provenance`; its companion has AppleDouble magic/version `00051607 00020000`, size 4,096. Origin of every metadata write is not traced; provenance and sidecars are observed rather than attributed to a particular program/security subsystem. The root also contains `._interrupted.journal`/`._torn.journal`; a Windows-style `*.journal` scan encounters their invalid frames. This is an additional fail-closed restart-interoperability hazard if such evidence were transported. Production journals remain machine-profile-local, not portable payload; the physical journals here are research fixtures only.

The probe directly calls unchanged shared manifest/readiness validators; directory enumeration/payload checking and bounded frame reading mirror inspected Windows code. It is **not** execution of Windows native leases, discovery or Catalog lifecycle. To isolate causes, the declared four files were copied into a new **internal APFS diagnostic** directory and validated again. Good passes and all six negatives refuse. This explicitly selected byte projection is not a supported importer, physical publication result or permission to silently ignore/delete AppleDouble files. Physical containers remain unchanged and all seven are refused.

An interrupted-staging child reports that it has no fixture handles before controlled termination; parent-created pending/journal/partial evidence models the interrupted boundary. It is not a crash of a production Mac transfer adapter, which does not exist. Pending preflight-intent journal requires recovery; a frame truncated by five bytes fails bounded parsing/digest verification. The 136-test unchanged neutral suite covers failed publication/activation, every durable intent/receipt interruption, safe cancellation, commit cancellation, failed rollback → RecoveryRequired and retained evidence. Changed mount facts are controlled neutral/host facts, not a real remount. Native host exercises actual changed local symlinks and refuses continuation; no physical alias retarget or device interruption was attempted.

## Cross-platform compatibility and exact missing operations

#392 version 1 metadata is portable JSON with canonical property order/encoding, numeric enums, uppercase hashes, exact safe relative names and operation/generation/Catalog identity. Readiness binds canonical manifest digest/count. Journal framing is little-endian length + canonical record + SHA-256, then shared ordering/hash-chain validation. The published examples are produced by the accepted serializer, not a new format. Payload finalization and production SQLite validation stay with existing recovery/lifecycle owners.

The accepted Windows consumer rejects unknown files/directories and links, revalidates native identity, preserves prior backups and uses verified local copyback before restore. No Windows API was run against this new fixture. Accepted Windows results do not imply Darwin guarantees. The physical complete Mac producer currently violates the exact file set due to OS metadata, although its declared metadata bytes are compatible. A journal transported with AppleDouble companions can additionally block restart scans. Do not relax those checks without owner/architecture approval.

Missing Mac effects operations are: exclusive object-bound staging and payload read/write leases; fresh ancestor/file/volume identity checks coupled to actual use; supported no-replace whole-unit local publication; conservative readiness write/flush/readback with complete-entry discovery; qualified local durable profile journal/frame ownership and restart binding; cancellation/disposal and revoked-identity cleanup refusal; verified copyback onto admitted local APFS; existing production snapshot/lifecycle/activation/configuration composition. Actual Finder aliases, managed backup inventory, Windows consumer round trip and metadata-safe portable output still require qualification. No new writer, schema, container/archive format or Mac production integration is supplied.

## Filesystem/support decision

| Option | Evidence-based recommendation |
| --- | --- |
| A. ExFAT active Catalog | **BLOCK** now: unsupported/refused policy, missing no-replace publication, metadata interoperability, no production Mac host and unqualified physical crash durability. Not a permanent rejection based on one API. |
| B. ExFAT closed transport only | **REVISE/CONDITIONAL candidate**: raw closed SQLite bytes work; current Mac-produced accepted container does not. Keep incomplete objects undiscoverable; resolve producer metadata under owner review, then qualify native Windows consumption/local copyback. |
| C. Another cross-platform filesystem | No safe common active filesystem currently qualified. Retain supported local Windows NTFS and bounded internal Mac APFS plus closed transport as the candidate strategy. No reformats, drivers or alternative filesystem qualification were performed. |
| D. Defer direct external active use | **Recommended** until a common filesystem/native effects/product lifecycle/support decision passes independent and owner acceptance. R2 remains incomplete. |

Demonstrated capability means an operation occurred on this device. Qualified local namespace capability is bounded by the APFS control and existing accepted profile. Unknown capability includes controller persistence, physical removal/remount and full application behavior. Unsupported capability includes this ExFAT's exclusive/swap rename/hard-link operations. Product policy is a separate owner decision; neither successful SQLite checks nor unsupported rename alone changes it.

## Remaining owner/qualification gates

1. Owner review of the observed AppleDouble/exact-file-set incompatibility and a bounded metadata strategy. This report proposes no contract exception, driver, deletion or new transport format. Stop physical effects here; any follow-up needs a reviewed scope.
2. Native Mac effects adapter and production lifecycle/snapshot/journal/configuration composition, retaining one shared orchestration owner and existing policy.
3. Successful same-format Mac physical container discovery on Windows and verified local copyback/production schema validation, then supported return. Byte projection is insufficient.
4. Managed backups/dependencies, all authored production rows, reader/pool drain, cancellation, restart/failed rollback, actual aliases/case/Unicode/remount and receiving-machine mappings on both supported hosts.
5. Explicitly scoped hardware durability/removal qualification on disposable hardware if required. None is authorized on owner data by this report.
6. Independent technical review and Jeremy's support-matrix/remaining-risk acceptance; #406/#393/#77 product DoD remains incomplete. Do not create implementation requirements for 012/014 from unverified causes.

## Validation, cleanup and reproduction

Focused unchanged neutral Release suite **136/136**, native Mac adapter **9/9**, standalone native host **52 checks**, zero failures. Host's physical read-only results remain ActiveCatalog Rejected/MissingCapabilities; Media Read Eligible; ClosedTransfer Read RequiresVerifiedStaging. Direct native anchor audit is **0 → 1 → 0**; the known physical symlink is ambiguous. Format probe builds with zero warnings/errors. Internal APFS namespace control passes. Exact-head PR CI is recorded in the PR handoff; local Windows full Release/packaging was not run for this Mac documentation-only scope. No new product executable or production acceptance claim is delivered.

All owned SQLite connections/streams/descriptors close; both children terminate/reap; accepted assessor is disposed and fails closed. Independent final `lsof -nP +D` on each exact physical fixture root returns exit 1/empty stdout/stderr. No task-owned drive holder remains. No eject or safe-disconnect instruction is issued; unrelated OS/user holders were not audited. Agent 007 manifests/reports remain unchanged.

Early local validation diagnostics are retained: first test attempted `--no-restore` before assets existed; an invalid test switch was corrected; sandbox MSBuild IPC denied execution, resolved by authorized native execution; Python 3.9 lacks `os.listxattr`, so read-only `/usr/bin/xattr` names were used. None was a physical-write retry. Physical harness ran once and retains its complete final fixture.

Exact internal commands from this clone:

```sh
git fetch origin main
git rev-parse origin/main
xcrun clang -fobjc-arc -Wall -Wextra -Werror -framework Foundation docs/research/physical-catalog-406/root_safety.m -o work/root-safety
python3 tools/X2CatalogProof/run.py test Lightflow.Application.Tests/Lightflow.Application.Tests.csproj -c Release --disable-build-servers -p:RestoreLockedMode=true -p:RestoreIgnoreFailedSources=true --logger 'trx;LogFileName=lf013-neutral.trx'
python3 tools/X2CatalogProof/run.py test Lightflow.Platform.MacOS.Tests/Lightflow.Platform.MacOS.Tests.csproj -c Release --disable-build-servers -p:RestoreLockedMode=true -p:RestoreIgnoreFailedSources=true --logger 'trx;LogFileName=lf013-native.trx'
python3 tools/X2CatalogProof/run.py run --project tools/MacStorageHost/MacStorageHost.csproj -c Release -p:RestoreLockedMode=true -- --data-root "$PWD/work/native-host-013" --probe '<independently verified mounted JRPhoto4T>'
python3 tools/X2CatalogProof/run.py build docs/research/physical-catalog-406/013/FormatProbe.csproj -c Release --disable-build-servers -m:1 -p:RestoreIgnoreFailedSources=true
python3 -B docs/research/physical-catalog-406/013/local_publication.py
```

Physical execution was `python3 -B docs/research/physical-catalog-406/013/qualify.py` after private safety-baseline/seed/format-plan creation. **Do not rerun it for this assignment**: one retained root already exists and exclusive claims prohibit reuse. Full preparation/reproduction is in [013/REPRODUCE.md](013/REPRODUCE.md), with exact private commands in task-local evidence. Read-only FormatProbe `verify` and the published 007 immutable verifier may be repeated with independently verified root and a new internal output path. CI never runs physical scripts.

## Sanitized evidence index and unsupported claims

- [Summary, native errors, all hashes, footprint, discovery and cleanup](013/summary.json).
- [Canonical manifest](013/manifest-example.json), [pending request](013/pending-example.json), [readiness](013/ready-example.json).
- [Native/lifecycle physical harness](013/qualify.py), [unchanged-format probe](013/Program.cs), [local APFS control](013/local_publication.py), [reproduction](013/REPRODUCE.md).
- Private `work/evidence`: disk/volume/USB and baseline, both 007 verifications, physical manifest/log, format plan/raw validation, AppleDouble observations, full SQLite state audit, native anchors, final hash/holder audit, TRX and command logs. No serial/UUID/mount-epoch values are published.

Explicitly unsupported: active ExFAT admission; successful physical #392 Mac publication; Windows-native consumption of this fixture; production Mac schema/provider/application parity; Settings move/restore/product activation acceptance; a common active filesystem; durable directory-entry survival through crash/power loss; physical removal or repair qualification; trace proof of SQLite flush syscalls; automatic metadata exclusion/cleanup; R2 completion. No production source, format, allowlist or support policy changed. Draft PR remains unmerged for review.
