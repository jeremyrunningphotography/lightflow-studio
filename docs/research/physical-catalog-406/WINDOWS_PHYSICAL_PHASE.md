# Windows physical qualification — LF-WIN-RES-007

Assignment 007 · Issue #406 · Draft #407 · 2026-10-09

**CONDITIONAL. Windows bounded physical fixtures passed 30/30; neutral contracts passed 68/68. Ordinary Windows safe removal succeeded. Return-to-Mac verification is pending LF-MAC-RES-007.** ExFAT active Lightflow Catalog support remains unqualified and refused; no production implementation or support decision is supplied.

## Authority and provenance

Jeremy explicitly authorized execution of `DARKMATTER_CONTINUATION.txt`, independently verified incoming evidence, bounded Windows SQLite writes inside the existing root, a separate Windows-authored fixture, ordinary safe removal and the Mac return handoff. His later session designation takes precedence over the older LF-BOTH name in that file. **LF-WIN-RES-007** identifies Windows findings; **LF-MAC-RES-007** identifies the Mac session. Historical Mac reports retain their recorded **LF-BOTH-RES-007** labels and provenance unchanged. This is one assignment, number 007, one issue and one Draft PR.

Independent full clone: `C:\Git\Agents\LF-BOTH-RES-007-ExternalCatalog`. Source/evidence base: `4acdf3c80243bc1188b01f48c768e2265e7ed6a7`; origin/main fetched before execution. Private commands, incoming audit, inventories, exact physical identifiers, pinned-provider research harness, source/binary hashes, failed pre-write attempt, TRX and final audit are retained in `.cache/windows-406` in this clone. No other agent workspace or canonical checkout was modified. All previous Mac research files remain unchanged.

## Physical identity and incoming audit

DARKMATTER runs Windows 11 Pro **10.0.26300 / build 26300**. Native CIM/storage/PnP and Win32 APIs identify a **SanDisk Extreme Pro 55AF**, external USB/UAS, GPT, approximately 4 TB, ExFAT, JRPhoto4T, assigned E: during testing. Get-Disk size **4,000,753,467,904 bytes**; partition size **4,000,751,370,752 bytes**; ExFAT usable volume size **4,000,733,724,672 bytes**; initial free space **2,286,829,961,216 bytes**. These different layer sizes are not presented as identical capacity measurements. Windows GPT partition GUID and volume-GUID identifier matched the supplied Mac GPT partition UUID; the separate Mac filesystem UUID uses a different namespace and was not equated to it. The prior Mac reported size differs by about 18.7 MB from Windows Get-Disk; physical model/USB/GPT identity, exact partition match, ownership and all fixture hashes establish continuity rather than assuming those API sizes are equivalent.

Win32 drive type is local/fixed (3), not network. Filesystem flags **0x00020206**, allocation cluster **1,048,576 bytes**. Volume/root native handles supplied the current volume serial, directory file ID and device number for use-boundary rechecks. Exact private identifiers are omitted here. Root and all task descendants have no reparse points. Root ownership JSON matches the existing run and 256 MiB limit. All **24 incoming files** matched both length and SHA-256 before writes, including Mac metadata sidecars, DB/WAL/SHM, backups, known-good closed copy and negative staging controls. Incoming closed-fixture integrity and every identity/authored field matched the Mac manifest. Final independent audit again matched all 24 originals and all 14 Windows artifacts.

The initial native guard stopped **before any physical write** because its comparison lowercased the returned volume GUID but retained uppercase `Volume` in the expected prefix. Read-only diagnosis established identical identifiers; only the comparison was corrected. No device mismatch was ignored. An initial safe-removal script also stopped before the API call because Windows PowerShell 5 does not accept `0u`; corrected `[uint32]0` reached the normal API once. Both diagnostic failures are disclosed; neither is counted as a filesystem failure or successful removal.

## Policy and provider boundaries

The documentation/main source has no accepted Windows native assessor. PR #403 / Issue #389 remains blocked/unaccepted at reviewed head `b63f98c797fecc181c80eed6dbc92ecfbf18172d`; it was inspected, not installed or treated as shipping support. This research uses a small native metadata guard plus **unchanged accepted R2-A StorageLocationPolicy**, not the #389 production adapter. The observation epoch is research-local, not qualification of production mount/rebind lifecycle. Read/write facts are native metadata, later corroborated by task-file operations; Catalog qualification/durability/locking/companion capability declarations remain Unknown. Deterministic policy results:

| Role / operation | Decision / reason |
| --- | --- |
| ActiveCatalog / Open | Rejected / MissingCapabilities |
| MediaSource / Read | Eligible / Eligible |
| ClosedCatalogBackupTransfer / Read | RequiresVerifiedStaging / VerifiedStagingRequired |

The fixture is deliberately independent of product admission and does not bypass it. No temporary ExFAT allowlist or production code change occurred.

**Windows provider:** Microsoft.Data.Sqlite **8.0.29.0**, pinned package `[8.0.29]`, SQLitePCLRaw.bundle_e_sqlite3 **[2.1.13]**; native SQLite **3.53.3**, source ID `2026-06-26 20:14:12 d4c0e51e4aeb96955b99185ab9cde75c339e2c29c3f3f12428d364a10d782c62`. These package versions match production source. **Mac provider:** Python/system SQLite **3.51.0**. Opening its compatible synthetic schema directly with the pinned Windows provider passed; the schema is two synthetic tables, not Lightflow's Catalog schema or CatalogDatabaseService. Provider parity does not imply product composition parity.

## Windows results

See [all assertions and full logical state](windows-summary.json), [every closed file hash](windows-file-audit.json), [safe-removal result](windows-safe-removal.json) and [private tool source/binary hashes](windows-tool-hashes.json).

| Check | Exact observation |
| --- | --- |
| WAL / FULL | journal_mode=wal; synchronous=2; foreign_keys ON; temp_store MEMORY; pooling disabled; writer timeout 1 second; wal_autocheckpoint=0 for bounded sidecar observation |
| Cross-process writer | Parent BEGIN IMMEDIATE; independent pinned-provider child receives SQLITE_BUSY=5, extended=5, `database is locked`; exit 0, no stderr; connection closed |
| Active sidecars | WAL 4,152 bytes; SHM 32,768 bytes |
| Checkpoint | TRUNCATE `(0,0,0)`; normal close; integrity `ok` on reopen |
| Final sidecars | Windows-authored and restored DBs retain zero-byte WAL and 32 KiB SHM after read-only verification; not deleted to obtain success |
| Logical state | CatalogId, RootId and all 32 AssetIds unchanged; one defined note change; every other field unchanged |
| Backup / restore | SQLite BackupDatabase API to task-owned backup and restored DB; every row equal; different backup byte hash is allowed and disclosed |
| Closed copy / local round trip | Windows DB, closed external artifact and external→task-local→external closed copy have identical SHA-256 |
| Negative transfer controls | 1,024-byte truncated and 45-byte mismatch staging rejected by length/hash; neither activated; prior closed copy and source retained unchanged |
| Nonreplacing rename | Windows File.Move(overwrite:false) of a separate task-owned canary succeeded; no cross-OS or crash-atomic-publication guarantee inferred |
| Flush | FileStream.Flush(true) acknowledged task canary writes; no trace of SQLite internal flushes, hardware write-cache or power-loss proof |
| Final audit | 340,050 logical bytes; **38,797,312 allocated bytes including directories (37 MiB)**; 38 files (24 originals + 14 Windows files); no reparse points |

Guard rechecks occurred at **24 parent boundaries**, plus child checks: volume GUID/filesystem/device/root file ID, incoming hashes, containment, reparse exclusion and footprint headroom. Windows artifacts remain exclusively in the existing root's `LF-WIN-RES-007` subdirectory. These checks reduce ordinary substitution risk; they do not establish universal atomic check-and-use guarantees. Allocation is computed from verified 1 MiB ExFAT clusters and includes directories and retained sidecars; no sparse files or external allocation changes were introduced.

One-run small-fixture timings: incoming guarded copy **5.5598 ms**, commit **2.2059 ms**, SQLite backup **11.5867 ms**, close **2.9848 ms**, guarded closed copy **4.2765 ms**, flush-canary operation including guard/write/Flush(true) **9.1697 ms**. The last number is not an isolated flush latency. No representative large-Catalog budget or timed checkpoint/open claim.

Neutral `Lightflow.Application.Tests` Release run: **68 passed / 0 failed / 0 skipped**, TRX `.cache/windows-406/neutral-results/windows-406-neutral.trx`. Pure logic suite entered no WPF/native UI host. Research compilation passed with zero warnings/errors. No unrelated full Release, installer, hands-on application or package validation was required for this documentation-only qualification; this is not a PR-ready production executable handoff.

## Authored delta and hashes

CatalogId: `d5a5cf99-1a76-4347-9fe6-d0e8c9d5c086`; RootId: `3f01e9f4-8fe6-4fdb-9188-825da780085b`.

AssetId `015db1c9-1b7f-41d9-8d07-8e3a73fbee9c`, note:

`Mac physical authored delta` → `Windows physical authored delta LF-WIN-RES-007`.

Original Mac good copy retains the Mac delta. The Windows derivative intentionally changes that one field, not an identity or another metadata field. The full baseline and final rows are in `windows-summary.json` and physical `LF-WIN-RES-007/expected-state.json`.

| Artifact | SHA-256 |
| --- | --- |
| Original Mac LightflowCatalog.db / closed-copy.db | `d9a9010a5733d8342c57cece8a75896ade106265b3c205d250e7f35f88352008` |
| Windows windows-authored.db / closed-windows.db / local-return.db | `a1e7a33dba9dde85b0d60c4f6baf3b5d90ee4f615a586d421c5c4adb2e634e30` |
| Windows backup.db / restored.db | `98d608943dd5d22bb76939afdacf1b20507ce698a9abc9a47e1fb216c15b3b30` |
| Windows expected-state.json | `e811a545b501a5eeb32f66fcd27856a4b75d072a4df133780a93feb250fe64f2` |

## Cleanup and safe removal

All transactions/checkpoints finished, pooling disabled/cleared, SQLite/backup/read-only connections, streams, volume/root handles and writer child closed. Parent harness exited 0; independent task-process inventory found none. Final audit precedes removal. Disk child itself lacks CM_DEVCAP_REMOVABLE; its directly verified UAS parent has it. Only that parent was selected, not its hub/controller.

**2026-10-09 20:16:31.6706740 UTC (13:16:31 PDT):** ordinary [CM_Request_Device_EjectW](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_request_device_ejectw) returned **CR_SUCCESS=0**, veto type 0, empty veto name; E: became inaccessible. No force, dismount, repair, process killing, driver or security-setting change. Owner was told it was safe to disconnect and reconnect to Mac. Mac eject is **owner-reported success** in the supplied continuation, not agent-observed proof. Mac return/reconnect has not been executed on Windows.

## Recommendation and remaining gates

ExFAT on this device is a **CONDITIONAL closed synthetic-transfer candidate**. Physical SQLite WAL/FULL and single-writer behavior were observed on both OSs with different providers. Active Lightflow support remains **UNQUALIFIED/refused**. No supported filesystem matrix is accepted here. NTFS/APFS cross-OS candidates were not tested on this owner drive; no reformat or third-party driver is warranted by this phase.

Remaining: LF-MAC-RES-007 must independently identify the returned physical drive, verify all original/Windows hashes, immutable integrity and every expected row/identity, then release all native anchors. Complete production Catalog/schema/service, Settings relocation, restore/activation, missing-media/root mapping, broader case/Unicode/alias collisions and reconnect/remount lifecycle remain #389/#391/#392 integration gates. Cancellation, unavailable/read-only remapping, real interrupted transfer/disconnect and physical fault recovery remain unqualified; staging controls simulate invalid artifacts only. Mac exclusive rename errno 45 remains authoritative negative evidence. Windows canary rename does not establish cross-platform atomic publication. Controller/bridge write-cache behavior, surprise removal, hardware power loss and persistent durability remain unqualified.

Owner files were neither enumerated nor opened/hashed/modified. This is confinement evidence, not a before/after integrity audit of all personal data. #406 remains Open/In Progress/Catalog/Priority unset; parent #77 and blocker #393 remain, and neither Epic/R2 DoD is complete. PR #407 remains Draft/unmerged, pending independent review, return evidence and Jeremy's acceptance.

Return instructions and read-only verifier: [MAC_RETURN_HANDOFF.md](MAC_RETURN_HANDOFF.md).
