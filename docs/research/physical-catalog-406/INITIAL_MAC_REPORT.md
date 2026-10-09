# Physical Catalog portability — initial Mac phase

LF-BOTH-RES-007 · Issue [#406](https://github.com/jeremyrunningphotography/lightflow-studio/issues/406) · 2026-10-09

**CONDITIONAL for this initial phase only.** Read-only physical identification and production Mac assessment completed; internal disposable APFS baseline passed 24 assertions. Direct physical active-Catalog use and closed physical transfer remain UNQUALIFIED. The current production policy rejects this external ExFAT configuration. No filesystem support, allowlist change, Windows execution, external write, formatting, physical eject or full portability acceptance is claimed.

Jeremy's kickoff authorizes read-only JRPhoto4T inventory/assessment, task-owned internal images and fixtures, GitHub progress and documentation. External-drive writes, owner-data operations and Windows execution remain gated. Independent full clone is named `LF-BOTH-RES-007-ExternalCatalog`; canonical checkout is untouched. Production source and accepted evidence are unchanged. Base: fetched main `c30601870d1c9736f6c6229e84b51546bf8f118e`. Assignment registry reservation is proposed through this Draft documentation PR; no merge authorization.

## Verified native inventory

| Property | Observed fact |
| --- | --- |
| Label / filesystem | JRPhoto4T / ExFAT (`exfat`) |
| Capacity / available | 4,000,734,773,248 bytes (~4.00 TB) / 2,286,854,078,464 bytes (~2.29 TB), inventory snapshot |
| Partition / allocation block | GUID partition scheme; Microsoft Basic Data; 1,048,576-byte allocation blocks |
| Physical device | External physical solid-state SanDisk Extreme Pro 55AF; disk metadata agrees with USBHost vendor/model |
| Connection | USB; USBHost reports 10 Gb/s negotiated link, not measured throughput |
| Mount state / locality | Mounted local, read/write; external; not a network provider; native access checks support Read/Write as metadata |
| Mount flags | `exfat, local, nodev, nosuid, noowners, noatime, fskit`; `statfs` flags `0x10201218`, no MNT_RDONLY |
| Encryption | Foundation volume encryption reports false; no filesystem-level encryption observed. Controller encryption/firmware security is not established |
| Native capability flags | Valid format mask `0x00ffffff`, interfaces `0x001fffff`; actual format `0x00402e22`, interfaces `0x00100bc0`; remaining groups zero |
| Decoded relevant native facts | Case sensitivity false, case preservation true; POSIX advisory locking advertised; filesystem journaling and persistent-object-ID capability bits absent. These are OS capability declarations, not executed locking/durability tests |

Private evidence records the actual mount, BSD partition/physical parent identifiers and volume UUID. `diskutil info` label/mount/device, partition list, Foundation volume UUID and statfs source agree. USB location agrees with disk DeviceTreePath. Device identifiers may change on reconnect. Serial numbers and unrelated devices are not published. Personal directories/files were neither listed nor opened or hashed. The assessor's exact-spelling check reads ancestor directory entries only to resolve the verified mount; it does not enumerate contents inside that root.

The internal task workspace was independently observed as local internal APFS through statfs/Foundation; its internal/encrypted flags were true. Image files and copied SDK therefore remained on internal storage.

Environment: Apple Silicon arm64; macOS 26.6.2 build 25G83; Command Line Tools SDK 26.5; task-local .NET SDK 8.0.425/runtime 8.0.31. An immutable SDK installation was copied to this task's own toolchain; no other task's build outputs, fixtures or caches were reused. All assemblies/native libraries were freshly built here. Exact version output remains private task evidence. `SPUSBDataType` returned an empty array on this OS; discovery of available profiler types identified `SPUSBHostDataType`, which returned the device information above.

## Accepted native policy, exercised without mutation

Task-only console harness references the unchanged accepted `MacStorageLocationAssessor` and shared `StorageLocationPolicy`. ActiveCatalog/Open asks for metadata, without opening SQLite or acquiring a writer. MediaSource/Read and ClosedCatalogBackupTransfer/Read likewise perform no write. An existing task-local protected ownership boundary is supplied; no user profile or Catalog is opened.

| Role | Native result / policy |
| --- | --- |
| All three | Complete, Local, available path/volume, resolved aliases and containment, consistent filesystem/mount identity |
| Active Catalog / Open | Read and Write Supported as metadata; qualification, durable writes and companion files Unknown; locking Supported as capability declaration. Rejected / MissingCapabilities |
| Media / Read | Eligible |
| Closed transfer / Read | RequiresVerifiedStaging / VerifiedStagingRequired; no transfer executed |

Provider diagnostic: native metadata snapshot; linked/cloud/unknown containment fails closed; bounded APFS qualification is not a power-loss guarantee; fresh actual-use guards remain required.

Native descriptor count was zero before and after deterministic assessor disposal. Subsequent assessment on the disposed instance returned Failed. All probe JSON buffers and context anchors were released. No physical eject was requested. The task image native controls also released anchors before each successful ordinary image detach. Zero descriptor count demonstrates this task's resource cleanup, not that unrelated applications have released the drive.

The first physical probe correctly failed closed with AmbiguousContainment because an interrupted build/setup had not yet created the internal protected boundary. The result is retained; creating that boundary internally and repeating the same nonmutating physical requests produced the final results. No policy facts were overridden.

## Internal synthetic baseline

New 256 MiB sparse APFS image stored internally, mounted at a task-owned internal path. System/Python SQLite 3.51.0 is deliberately labeled a synthetic engine baseline, **not** the pinned production Microsoft.Data.Sqlite/e_sqlite3 provider or Lightflow Catalog schema. Fixture contains 32 synthetic asset IDs, CatalogId/RootId, ratings, keywords, collection, note, marker and subclip values. No owner media.

Final result: 24/24 assertions, integrity `ok`, authored-state/IDs equal after reopen, backup, restore, closed copy and read-only remount. WAL/FULL=2, sidecar presence, cross-process writer refusal during BEGIN IMMEDIATE, checkpoint(TRUNCATE) `(0,0,0)`, clean close, SHA-256 equality, negative read-only authoring, deterministic partial-copy rejection, hash-mismatch rejection and retained-source/prior-destination rollback passed. Read-only native image reports Write false. A fresh binding after ordinary detach/readonly attach has a different epoch; a new context also changes epochs, so this is not proof of every remount-reuse race.

Measured tiny-fixture timings: create/commit 7.30 ms, checkpoint/close 13.62 ms, closed 16 KiB copy 0.15 ms. These are one-run controls without a performance budget, hardware throughput or representative large Catalog claim. Hashes, PRAGMAs, assertion names and cleanup are in [summary.json](summary.json).

Two failed image runs are retained. First incorrectly expected sidecars to disappear: this system build retained a zero-byte WAL and 32 KiB SHM after close, while checkpoint succeeded. Corrected test requires no uncheckpointed WAL and independently verifies closed DB bytes/state; it does not delete sidecars to make a test pass. Second plain `mode=ro` WAL copy refused authoring with CANTOPEN rather than the expected readonly error. Final immutable closed-file read control yields the explicit readonly rejection; the plain-open limitation remains disclosed. Immutable mode is used only on closed, verified fixtures and is not a proposed production workaround. `fullfsync=0`, `checkpoint_fullfsync=1`, busy timeout 200 ms (short locking test). No production PRAGMA changes are proposed; ADR 0001's production 5-second timeout remains authoritative.

Partial-copy and rollback tests are bounded synthetic control logic: write a truncated operation-owned staging artifact, reject mismatched hash and preserve source/prior destination. They do not execute #392's future production orchestration, real disconnect, Settings relocation or failed product activation. Backup bytes differ from source bytes but parsed authored state and identity match; closed raw copy hashes match exactly. All images detached ordinarily; zero native descriptors remained. Images/logs/fixtures remain internally for audit.

## Suitability and evidence limits

| Scenario | Preliminary assessment |
| --- | --- |
| Direct active Catalog on Mac | Plausible OS-level local readable/writable candidate, but **not admitted or qualified** by accepted Mac policy; ExFAT qualification/durability/companions unknown |
| Direct active Catalog on Windows | Native ExFAT access is plausible; DARKMATTER not tested. PR #403's conservative accepted-evidence boundary is NTFS; no ExFAT product admission claim |
| Closed Windows/Mac transfer | Plausible candidate without reformatting, subject to closed finalization, hashing, safe eject/reconnect and native Windows identity/read/write validation |

Apple documents ExFAT as Windows-compatible; Microsoft lists native ExFAT functionality, including no metadata journaling and no filesystem-level encryption. This supports a transfer candidate, not a SQLite support decision. [Apple formats](https://support.apple.com/en-ie/guide/disk-utility/dsku19ed921c/mac), [Microsoft comparison](https://learn.microsoft.com/en-us/windows/win32/fileio/filesystem-functionality-comparison).

SQLite WAL requires local shared-memory/locking semantics, sidecars and correct synchronization. FULL adds WAL commit synchronization; it cannot demonstrate honesty of a physical bridge/controller's cache flush. `SQLITE_SYNC_FULL` and PRAGMA synchronous FULL are different controls; macOS fullfsync requires separate observation. [WAL](https://www.sqlite.org/wal.html), [PRAGMAs](https://www.sqlite.org/pragma.html), [sync flags](https://sqlite.org/c3ref/c_sync_dataonly.html). No fsync/write-cache, controller firmware, hardware power-loss, physical locking, journal/SHM creation or durability experiment occurred on JRPhoto4T. Lack of ExFAT metadata journaling is a relevant risk, not automatic SQLite rejection.

APFS's bounded internal Mac evidence and NTFS's bounded Windows evidence do not establish native cross-platform writable interchange for either format. No third-party driver is selected. ExFAT case/Unicode lookup, portable names, volume/path identity across OSs, authored root remapping and collision rejection need native physical evidence and #391 integration. No spelling normalization or stable-ID rewriting is proposed.

Accepted G2 evidence is preserved: actual closed Windows NTFS → Mac APFS → Windows NTFS authored-state/ID round trip, 23 production tables, local SQLite-aware finalization and verified transport; it did not qualify this physical drive. Prior positive ExFAT-image controls also did not qualify hardware. References: [G2 report](../mac-catalog-proof/local-portability/P2_FINAL_RECOMMENDATION.md), ADRs 0001–0007, [#388 contracts](../../architecture/shared-storage-contracts-388.md), [#390 adapter](../../architecture/mac-storage-adapter-390.md), #389/Draft #403, merged #404, #391 and #392. Complete production acceptance remains dependent on the separately accepted adapters/identity/transfer integration. R2 #393 remains incomplete and blocked by #406; native parent #77 under #289 is preserved; #366 consumes the qualification.

## Next bounded owner decision

Recommend a **closed-artifact physical transfer first**, followed by explicitly authorized fixture-only ExFAT WAL tests. JRPhoto4T can be tested in its existing filesystem without reformatting. No hardware support recommendation follows until both platforms and review are complete.

Proposed external modifications, all currently unexecuted: create exactly one new collision-checked directory `<verified mount>/LF-BOTH-RES-007-Qualification-<run UUID>`; cap total synthetic content at 256 MiB; copy a closed synthetic Catalog plus manifest/hash file into operation-owned `.partial` names, verify, then rename within that new directory; later create/update fixture SQLite DB, WAL/SHM, backup and restored fixture files there. Hash/read only task-owned files. Delete only operation-owned partials on failed verification; retain verified fixtures unless owner later approves cleanup. No existing directory/file overwrite, permissions change, formatting, partitioning, forced eject or disconnect. Any unexpected owner-data impact stops the run.

A Mac-first disposable physical fixture validates safe writes/close before handoff. The eventual product-origin Windows → Mac → Windows same-Catalog sequence remains required; the Mac-first control cannot substitute for it. See [WINDOWS_HANDOFF.md](WINDOWS_HANDOFF.md). No drive movement is requested now. External writes and Windows execution require Jeremy's next explicit approval; support-matrix acceptance and any production allowlist implementation remain separate decisions.
