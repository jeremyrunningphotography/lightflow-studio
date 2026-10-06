# NAS / central storage assessment

2026-10-05. Separate documented upstream behavior, current source policy, measured local results and future product decisions.

| Access model | Current evidence / policy | Recommendation / gap |
|---|---|---|
| Windows→Mac→Windows sequential local Catalog | SQLite-aware snapshot/recovery primitives measured locally; actual cross-OS fixture pending | Preserve IDs/authored rows, closed ownership and explicit mappings; complete same-fixture round trip |
| Catalog stored centrally on NAS, sequential direct open | Owner product target; current ADR is local Catalog and rejects obvious UNC; Mac mounts evade that lexical test | No safety claim. Qualify storage detection and choose an explicit architecture before direct use |
| Multiple machines/writers against one NAS SQLite file | Not authorized or tested | Remains unsupported; one successful open or DELETE mode is insufficient |
| NAS backup/export destination | Standalone SQLite-aware finalized snapshots are distinct from live access | Mounted SMB closed-snapshot write/fsync/readback/rename/local return passed; disconnect/fault/durability qualification pending |
| NAS media root / Preview cache | Root resolution and Preview policies depend on path semantics; Preview only recognizes UNC for DELETE | Mounted SMB protocol/volume detection and physical disconnect/permission testing remain open |

SQLite [WAL guidance](https://sqlite.org/wal.html) requires same-host shared memory and excludes network filesystem use. [Network caveats](https://sqlite.org/useovernet.html) explain filesystem locking/durability hazards and favor keeping database access close to its owning host. Changing journal mode does not establish correct remote locking or sync semantics. [Corruption guidance](https://sqlite.org/howtocorrupt.html) includes open-file rename/unlink and hot-journal loss hazards. [Backup API](https://sqlite.org/backup.html) describes consistent snapshots. These are upstream constraints, not measurements of Jeremy's NAS.

Current source: CatalogSqliteConnectionFactory enforces WAL/FULL/FK/5000ms. StorageManagement rejects obvious UNC live destinations. PreviewStoreService.IsUnc chooses DELETE only for backslash UNC spellings; an SMB path under `/Volumes/...` does not match. macOS mounts do not become qualified simply because they appear absolute/local. The owner-directed central Catalog goal supersedes the older local-only product aspiration, but no safe central access mechanism has been chosen or implemented.

Jeremy authorized smb://JRVault/Development and its codex folder. Mac verified the SMB mount at /Volumes/Development and tested only the uniquely owned subfolder documented below. No removable hardware has been touched. Server configuration, SMB dialect/locking capability, removable device and disruptive disconnect permission remain unqualified. No mounted SMB/local SQLite open has been presented as NAS safety. The APFS image is a synthetic case-sensitive filesystem control, not removable hardware. Offline/remount was simulated by a local directory rename.

Potential owner-selected future route: centrally retained validated snapshots with explicit checkout/return ownership and conflict handling, or a server owning local SQLite with clients using a service. This is an architecture proposal; no production protocol, lock implementation or schema change is authorized. G2 and central-location acceptance remain open.

## Physical SMB follow-up, 2026-10-05

Owner provided NAS-side `/volume1/Development/codex`; verified Mac mount is `/Volumes/Development/codex` on `smbfs` for JRVault/Development. Files retained only in `/Volumes/Development/codex/x2-nas-b38645db11f54a40b1ace5e7fb373d8e`. Reproduce with `python3 tools/X2CatalogProof/nas_probe.py` (writes outside workspace require sandbox approval). Every run exclusively creates a new UUID folder.

Measured filesystem behavior: case-only and NFC/NFD names alias (exclusive second create reports existing name; same client device/inode/hash). Literal backslash and leading/trailing spaces are distinct files. CON, colon, question mark, trailing dot and emoji names were accepted through this Mac SMB client. These are filesystem observations, not production MediaRoot-service tests or claims about the NAS backing filesystem.

A finalized SQLite-aware Mac-only32-asset Catalog backup was copied closed to SMB, flushed/fsynced, reopened as bytes, renamed within the same folder and copied back locally. Source/NAS/return SHA256 all `d42eda27981e41379d094087588b7140088ac7589dc38be0436d9fcb9cfa798b`. The pinned production proof provider read the returned LOCAL DB; all-table snapshot exactly matched. No live database, WAL or SQLite connection was placed on the NAS. Raw evidence: `evidence/NAS_PROBE.json`; local returned DB/snapshot: `work/evidence/x2-nas-b38645db11f54a40b1ace5e7fb373d8e`.

No owner files were inspected, no shared mount disconnected, no permissions altered and no cleanup performed. Successful fsync is not remote power-loss evidence. Offline/disconnect/permission faults, live Catalog/Preview, simultaneous writers, production root behavior and Windows same-fixture legs remain pending. G2 and the architecture stop gate remain open.

## Resumed live NAS investigation

See [LIVE_NAS_QUALIFICATION.md](LIVE_NAS_QUALIFICATION.md) for current-main production WAL failure, proof-only DELETE controls/recovery and Outcome D with proposed B/C direction. Earlier pending-live-test statements describe prior checkpoints, superseded by this follow-up; historical raw observations remain unchanged.
