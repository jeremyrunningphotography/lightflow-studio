# Local Catalog portability — owner decision and bounded proof

2026-10-06 · M2 / #369 · parent #366 · **G2: PASS recommended for P2 initial scope; owner acceptance pending**

**Owner decision — 2026-10-06: Active Lightflow Catalogs on NAS/network filesystems are unsupported. Catalog portability between Windows and macOS will target supported local/removable storage and safe closed-Catalog transfer. NAS/network media remains in scope.**

Recommend **P2, verified closed-Catalog transfer between native local filesystems, as the initial portability policy**. Keep P1, direct portable exFAT SSD operation, a qualification candidate. The Mac exFAT disk-image results are encouraging; they do not establish physical SSD or Windows interoperability. P1 is deferred and is not an initial-scope blocker. The actual Windows → Mac → Windows round trip now passed (36 / 106 / 34 checks); see [final recommendation](local-portability/P2_FINAL_RECOMMENDATION.md). Neither the unfinished authority service nor generic network SQLite support remains a G2 requirement.

## Baseline and cancelled work

Authoritative main and this proof's baseline: `21adb19f760e4ec07c17270e995cb5588044396c`, the normal merge of accepted B2 PR #374 (accepted head `83711fe0324314902282d56c8aabe453fd137051`). Its required Unit tests and Windows installer/portable-package checks passed before merge. The merged protocol branch was retired.

The subsequent authority-service branch was stopped on the owner's new instruction. It had completed 37 primary and 13 supplemental assertions in each of two one-Mac storage configurations, plus six publication samples each. It had not established distinct-principal NAS ACL protection or a deployable product topology. No authority service was deployed to the NAS or integrated into Lightflow. Its source and normalized results are retained in the private/local historical branch `codex/x2-authority-service-proof-20261006`, checkpoint `18ae5f2` (superseded; not selected for merging). Raw fixtures/logs remain retained. No further service proof or Windows service handoff is required.

Historical [Outcome D](LIVE_NAS_QUALIFICATION.md), [architecture comparison](CENTRAL_CATALOG_ARCHITECTURE.md), and [B2 fencing findings](CENTRAL_CATALOG_PROTOCOL_PROOF.md) remain unchanged. They explain the support boundary: production requires WAL, the tested SMB mount could not establish it, DELETE controls did not qualify network durability, and trustworthy central publication introduced unjustified service complexity. SQLite remains the accepted Catalog database. No PostgreSQL/X4, sidecars, production policy/UI/schema/provider/runtime change, or M4+ work occurred.

## Filesystem evidence and proposed support

| Active Catalog location | Evidence | Initial recommendation |
|---|---|---|
| Native Mac APFS, default case-insensitive | Real Apple Silicon production-linked create/open/WAL/FULL, 32-asset authored read/write, backup/restore, cross-process contention and Preview rebuild control | Preferred Mac-local storage, subject to normal application acceptance |
| Native Windows NTFS | Owner-executed origin and return, local NTFS Fixed/Healthy; 36/34 checks | Preferred Windows-local storage; tested P2 round trip passed |
| Direct external exFAT SSD | Owner supplied a writable USB SSD folder; filesystem identified as exFAT/local. New test-subfolder creation timed out before Catalog tests | Not qualified; no physical WAL/crash/eject result claimed |
| exFAT disk image on this Mac | Verified mounted exFAT; production WAL/FULL, competing writer rejection, SIGKILL recovery, clean detach/remount, backup/local restore | Positive Mac-driver evidence only; insufficient for P1 product support |
| APFS external SSD | Apple supports APFS on direct-attached storage; physical device not tested here | Mac-only candidate, not native Windows interchange |
| NTFS attached to Mac | Native write limitations; no driver installed/tested | Do not require third-party NTFS drivers for portability |
| Case-sensitive APFS | Earlier media identity tests demonstrated case-folding collisions; active Catalog on CS volume not separately qualified here | Initially exclude ambiguous/case-sensitive media roots; do not infer Catalog-volume qualification from media tests |
| SMB/NAS/NFS/other network filesystem | Owner decision; prior WAL/fencing research | Reject active Catalog create/open; network media and closed backup storage are distinct roles |

Apple documents [APFS and Windows-compatible exFAT formats](https://support.apple.com/guide/disk-utility/file-system-formats-dsku19ed921c/mac) and [NTFS write limitations](https://support.apple.com/en-in/101830). Microsoft's [filesystem comparison](https://learn.microsoft.com/en-us/windows/win32/fileio/filesystem-functionality-comparison) lists exFAT without metadata journaling and contrasts NTFS capabilities. Read/write interoperability alone does not qualify SQLite durability. No alternative filesystem survey or third-party driver requirement is proposed.

## Executed Catalog/runtime checks

The console harness links unchanged production persistence/recovery/root/authoring sources; it excludes WPF, the production Windows instance coordinator, and rendering/NLE dispatch. It does not establish packaged Mac application support.

Actual selected arm64 SQLite is **3.53.3**, schema **19**, Microsoft.Data.Sqlite **8.0.29**, SQLitePCLRaw **2.1.13**, .NET **8.0.29** / SDK **8.0.423**. Selected native library SHA256: `09fe20a6c050912a7944b2f2f2981debe31679f7919f523c87251ae8cf0f2daa`. Runtime reports WAL, synchronous FULL=2, foreign keys=1, busy timeout=5000 and integrity `ok`. No package or production runtime policy changed.

[APFS_CONTROL.json](local-portability/APFS_CONTROL.json) and [EXFAT_IMAGE.json](local-portability/EXFAT_IMAGE.json) each contain 48 successful assertions, including every incoming authored table, stable CatalogId/32 AssetIds, a defined Notes edit, unchanged other authored tables, backup/local restore equality, clean close and reopen. The exFAT image's source and physical mount identity are recorded separately; it is not the USB SSD. [VOLUME_CLASSIFICATION.json](local-portability/VOLUME_CLASSIFICATION.json) confirms `exfat` + local for the image and physical drive, `apfs` + local for the control, and `smbfs` + nonlocal for the NAS.

A separate .NET process could read the 32 assets while the first held `BEGIN IMMEDIATE`; its write attempt returned SQLITE_BUSY=5, and writing resumed after release. This establishes same-host SQLite contention, not application-wide ownership UX or cross-host simultaneous access.

[EXFAT_IMAGE_CRASH.json](local-portability/EXFAT_IMAGE_CRASH.json) records a real SIGKILL of the spawned child with a nonempty WAL. Production-linked reopen retained the committed Notes edit, rolled back the uncommitted edit, and passed integrity. This is process-crash evidence, not OS crash, power loss, device-cache flush truthfulness or cable-pull evidence. SQLite's [WAL documentation](https://www.sqlite.org/wal.html) explains local shared-memory/locking requirements; [atomic commit](https://www.sqlite.org/atomiccommit.html) describes the storage assumptions underlying durability. A successful pragma or process recovery cannot establish all those assumptions.

Exploratory 32-asset single-run timings: APFS authored commit 4.231 ms / local backup 5.152 ms; exFAT-image authored commit 4.776 ms / local backup 8.127 ms. These warm, small-fixture values are not physical SSD throughput, a performance threshold or a durability SLA.

## Backups and closed transfer

A meaningful negative result is retained: **direct production SQLite-aware backup from an APFS WAL Catalog to SMB failed while finalizing the destination journal**, with SQLITE_CANTOPEN=14. It reproduced through a workspace alias and a direct task-owned destination. See [DIRECT_NAS_BACKUP.json](local-portability/DIRECT_NAS_BACKUP.json). Earlier successful DELETE-source controls remain valid historical evidence; they do not qualify this live-WAL source path.

The bounded alternative **passed**: use production SQLite-aware backup on local storage; finish its standalone journal locally; copy the closed file to a unique NAS staging name; flush and compare SHA256; publish the closed name; copy back to local storage; restore through production recovery and compare every table. All three hashes matched, and the local restored Catalog opened with WAL/FULL/integrity `ok`. No SQLite database was opened on NAS for this closed-copy check. [CLOSED_NAS_TRANSFER.json](local-portability/CLOSED_NAS_TRANSFER.json) records six restore assertions and hashes. This is same-Mac evidence, not cross-platform restore or network-interruption qualification.

Recommend this local-staging pattern for future network backup support; do not silently claim the existing direct-to-NAS backup path works. Keep previous verified backups when a new transfer fails. A partially copied file must not become the selected backup. A transfer manifest should bind CatalogId, schema, source snapshot hash and authored-state manifest; opening happens only after a verified local copy. The [SQLite backup API](https://www.sqlite.org/backup.html) supplies consistent snapshots. Raw-copying an active database or manually discarding WAL is not the transfer procedure.

External-image Catalog → local backup/restore passed. Physical external SSD → NAS backup remains deferred. Actual Windows-created restore on Mac and Mac-created restore on Windows now passed; see the final P2 report. The observed NAS backup failure does not independently block SQLite as the local Catalog database; closed staging addresses the storage role without restoring central-authority architecture.

## Paths, RootId and authored identity

Current schema already separates stable `MediaRoots.RootId`, machine-specific `MediaRootMappings(RootId, MachineId, PhysicalPath)` and `MediaAssets(RootId, RelativePath, RelativePathKey, AssetId)`. A second simulated machine was Unmapped until remapping the same RootId. The completed real Windows/Mac round trip also preserved that RootId and added exactly one Mac mapping. No schema migration is needed merely to assign a Windows path and a Mac mount path to one logical root. Never identify a Catalog or asset by drive letter/mount spelling, or automatically reuse another machine's physical path.

The current path key is uppercased after trimming and treating backslash as separator. It is not a complete cross-platform identity contract. [Existing APFS measurements](PATH_IDENTITY_MATRIX.md) and [new exFAT-image service probes](local-portability/EXFAT_IMAGE_PATHS.json), with [independent file identities](local-portability/EXFAT_IMAGE_IDENTITIES.json), show:

- Case-only names alias on the tested case-insensitive volumes. Earlier case-sensitive APFS distinct files silently collide under the current key.
- NFC/NFD names alias physically on this Mac's tested exFAT driver, yet both imports can create separate AssetIds. Windows behavior has not been measured.
- Literal backslash and surrounding-space names can be distinct files while the normalized lookup selects the other file's bytes.
- Windows-reserved/ambiguous names were accepted by the Mac driver. A symlink was also created and imported through to a task-owned outside target. Treat this as observed Mac-driver behavior, not a guarantee of portable exFAT symlinks; Microsoft's Windows capability table differs.

Initial proposed policy: preflight both new and existing roots for portable-name/case/Unicode ambiguity; reject affected roots or offending names with a specific diagnostic, preserving all original rows and files. Do not normalize two physical entries into one AssetId, silently merge existing duplicate IDs, trim legal names, or reinterpret a literal Unix backslash. Initially reject symlink/reparse traversal (including linked ancestors/root aliases) unless an explicitly validated canonical containment policy is later approved. Runtime operations must revalidate, because a prior scan does not prevent a link being replaced later. Raw original spelling must remain available for diagnosis.

Restrictive admission can use the current schema. General support for case-sensitive roots and versioned Unicode/exact-name keys would need a separately reviewed key-policy/migration design: audit collisions first, preserve stable IDs and relationships, protect a backup, and never rewrite conflicting rows automatically. No migration or production guard is implemented here. Actual cross-platform verification is complete. Recommend G2 PASS for the selected P2 scope with the fail-closed policy; owner acceptance remains pending.

## Preview, close and safe removal

Preview/cache stays **machine-local**. The proof renamed its own Preview directory, created a fresh store and repopulated metadata; all Catalog tables stayed identical. This demonstrates persistence separation, not a full thumbnail/render regeneration test. Ratings, flags, labels, descriptions, keywords, collections/sets/smart definitions, ranges, subclips, markers, rotation and LUT assignments remain Catalog-owned. Keep the referenced LUT resource with a closed transfer manifest; a persisted assignment does not recreate a missing LUT file. No media-folder sidecars are introduced.

For portable storage: stop authored mutations/background work, complete SQLite-aware backup as needed, dispose all Catalog sessions and pools, checkpoint successfully, verify there is no uncheckpointed WAL, release all handles, then request normal OS eject. A busy checkpoint/eject must block a claim of safe removal; never delete WAL to force it. After unexpected removal, preserve surviving files and restore/verify a local copy before authoring. Recovery guidance must distinguish committed work from edits never durably stored.

[IMAGE_EJECT.json](local-portability/IMAGE_EJECT.json) records clean WAL/SHM absence and identical DB hashes after normal disk-image detach/remount. No forced detach or physical ejection was performed. The user-authorized physical drive contains other data: folder access did not authorize cable-pull, formatting, partitioning or whole-drive disruption. Those remain explicit evidence gaps.

## Network-location rejection contract — proposal only

Use a shared result such as `CatalogLocationAssessment { role, canonicalLocation, volumeIdentity, filesystem, locality, writable, capabilities, policyVersion, decision, reason }`. Roles distinguish active Catalog, Preview, media and closed backup. Decision is Allowed, UnsupportedNetwork, UnsupportedFilesystem, ReadOnly, Unavailable or Unknown. Unknown does not silently pass active authoring.

The Mac adapter should resolve the actual existing parent/handle and inspect volume identity/type plus `statfs` MNT_LOCAL or NSURLVolumeIsLocalKey. This [Apple guidance](https://developer.apple.com/library/archive/qa/nw09/_index.html) also cautions that local does not imply every required capability. The read-only research adapter measured the expected APFS/exFAT/SMB distinction. No production detector was added.

The Windows adapter should recognize UNC/device-UNC and mapped network drives, resolve reparse/mount targets, and inspect actual volume/drive type and filesystem. [GetDriveTypeW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getdrivetypew) and [volume information](https://learn.microsoft.com/en-us/windows/win32/fileio/obtaining-volume-information) are inputs, not a string-prefix-only guarantee. USB SSDs may report fixed media; that alone must not reject them. Evaluate resolved paths, not just configured strings. Cloud-sync/offline/network-backed virtual locations cannot be accepted solely because an underlying path says local. Revalidate on open/create and volume change; reject unknown aliases or replaced mounts without touching a database.

Proposed message: “Lightflow Catalogs must be stored on a supported local drive. Network locations are supported for media and Catalog backups, but not for an active Catalog.” No UI or production rejection implementation occurred.

## Gate, handoff and reproducibility

**P2 passed for the tested fixture; P1 deferred/unqualified; G2 PASS recommended for P2 initial research scope.** The completed 36/106/34-check round trip and [final disposition](local-portability/P2_FINAL_RECOMMENDATION.md) supersede the earlier pending-Windows gate. No evidence blocker remains inside that bounded research scope. #369 remains Open / In Progress pending owner acceptance. Path/network guards, staged backup UX and packaged application acceptance remain later implementation/qualification obligations; no implementation is claimed. Physical SSD, active network Catalogs and authority-service work do not block this recommendation.

See [WINDOWS_HANDOFF.md](local-portability/WINDOWS_HANDOFF.md) for a bounded Windows-origin closed-transfer procedure, and [REPRODUCE.md](local-portability/REPRODUCE.md) for Mac commands and the filesystem precondition. The exact published branch/commit is recorded in the Draft PR/#369 handoff. [HASHES.json](local-portability/HASHES.json) binds public evidence and harness files.

Unexpected setup findings are preserved, not promoted to filesystem conclusions: two invalid hdiutil option combinations and an overlong volume label were corrected; a stalled external setup was terminated, after which the shell mistakenly continued into a local control (retained explicitly as APFS, never exFAT evidence); the crash runner initially looked for the wrong DB basename, then was corrected and recovery rerun. The external-folder creation timeout is not proof that exFAT WAL fails. The exact private fixtures and operational paths stay local; published artifacts generalize the SSD/NAS identity without removing engineering evidence.
