# #318 — Catalog backup and restore path boundary

Baseline: `fdad073199d34522687f4f82017935cec21db89f` (fetched origin/main).
Workspace: `C:\Git\Agents\issue-318-catalog-backup-long-paths`.
Scope: #318 only; no v0.40 upgrade profiles or release acceptance were used.

## Packaged baseline reproduction

The baseline executable was built with Build-Release.ps1 and passed startup, graceful shutdown, and dependency validation. A diagnostic-only CLI entry point was then packaged with the existing recovery implementation. It called the same `LightflowStorageCoordinator.BackupForExitAsync` operation as the close dialog, wrote/read a durable Collection Set, and recorded SQLite staging paths and first-chance error codes. This is packaged process/service acceptance, not a claim of manual dialog interaction.

Original evidence is retained in task-owned `artifacts/b/backup-path-verification.jsonl` (boundary probes) and `artifacts/c/backup-path-verification.jsonl` (fresh first-backup and restore cases). The baseline diagnostic executable/source are retained in ignored artifacts. No normal Catalog was opened.

Exact first failure at the reported class (2026-09-25):

- Data root: `C:\Git\Agents\issue-318-catalog-backup-long-paths\artifacts\c\case-262\pppppppppppppppppppppppppppppppppppppppppppppppppppppppppppp\pppppppppppppppppppppppp` (156 characters).
- Catalog: root plus `\Catalog\LightflowCatalog.db` (184).
- Backup directory: root plus `\Catalog Backups` (172).
- Final filename: `LightflowCatalog-User-v19-20260925T205846Z.db` (full path 218).
- Staging filename: final filename plus `.aff35a002ea241a7a9f9585bac498b14.incomplete` (full path 262).
- First failing operation: `destination.Open()` in `BackupDatabase`; source open succeeded. `Microsoft.Data.Sqlite.SqliteException`, SQLite error/extended error `14/14`, "unable to open database file".
- No staging artifact created, no completed backup, no previous backup at first attempt. Live integrity remained valid and the committed Collection Set remained present.

## Measured boundary and host difference

The same packaged process tested ordinary .NET file creation followed by SQLite open/online backup/integrity at deliberate full path lengths. Components were short (directory components at most 60 characters), so this was not the 255-character component limit.

| Unprefixed database path | First failing operation | Artifact before cleanup | Explicit extended path |
| --- | --- | --- | --- |
| 248, 250, 251 | None | Valid database | Pass |
| 252–255, 258, 259 | Online backup | Empty database, 0 bytes | Pass |
| 260, 262, 270, 320, 400 | Destination open | None | Pass |

.NET file writes succeeded at every tested length. The online-backup boundary is consistent with the appended eight-character `-journal` filename crossing MAX_PATH (251 + 8 = 259; 252 + 8 = 260). The measured failure seam is the SQLite call, not File.Move, integrity checking, retention, or .NET file copy. The native Windows API call was not separately traced.

Provider: Microsoft.Data.Sqlite 8.0.29 / SQLitePCLRaw 2.1.6, unchanged. Windows `LongPathsEnabled=1`. Lightflow's app manifest has no `longPathAware` declaration; the actual VSTest `testhost.exe` contains `<ws2:longPathAware>true</ws2:longPathAware>`. This explains why ordinary long-path tests could pass while the packaged application failed.

[Windows documents](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation) both the extended-path syntax and the separate registry/manifest opt-in. [SQLite documents](https://www.sqlite.org/vfs.html) its Windows VFS variants. No VFS switch, provider upgrade, or application-wide manifest change was required: the pinned stack passed with explicit extended paths at the narrow recovery boundary.

Restore independently reproduced both related failures: at the user-staging 270 class, current-Catalog protection failed with a 258-character managed staging path; at the 320 class, restore staging itself was 285 characters and failed to open. Both preserved the current Catalog. Restore at the 262 user-backup class happened to work because its internal paths were shorter; this did not establish safety at greater supported directory depths.

## Design and invariants

`RecoverySqlitePath` normalizes a filesystem path before passing its Windows extended form to the recovery service's SQLite connections. It handles drive and UNC syntax and preserves an already-extended path. This applies to source open, destination open, integrity checks, and restore candidates. Filesystem publication paths, user-visible filenames, backup destinations, and ordinary Catalog connection policy remain unchanged.

Shorter staging names would shift the boundary but leave long final backups unreadable. Staging elsewhere could change same-volume/atomic promotion guarantees or require another full copy. The selected fix adds neither a copy nor a background worker and does not change schema, retention, settings, or UI.

Backup still uses SQLite online backup, validates the closed staged copy and identity/schema, then publishes by same-directory rename. User backups retain explicit flush and cancellation boundaries. Managed backups now also check cancellation immediately before publication. Failed/cancelled new copies preserve prior backups and clean their owned staging/SQLite companions, including rollback journals. Errors identify the failing operation, full source/staging paths, lengths, and SQLite primary/extended codes.

Restore retains candidate validation, staged-copy validation, current-state protection, displaced-file preservation, normal activation, and rollback. Staged identity/schema must match the candidate. Cancellation is accepted before replacement starts; once files move, installed validation runs without cancellation so it cannot strand a displaced Catalog. Staging cleanup covers SQLite companions. These are safety-boundary corrections, not a new atomicity architecture.

## Regression and packaged acceptance

`CatalogBackupPathTests` runs the real storage/coordinator path at actual user-staging lengths 220, 250, 251, 252, 259, 260, 262, 270, 297, 320, and 400. It captures the actual paths handed to SQLite and asserts their lengths. The 297 case also asserts an actual 262-character restore staging path. The 400 case extends the configurable backup destination while retaining an ordinarily supported live Catalog path; this issue does not expand arbitrary live-Catalog location support.

Additional tests cover corrupt/cancelled staged copies, byte-for-byte prior-backup preservation, reopened mutation admission, managed retention, cancelled publication, failed current-state protection, and cancellation during installed restore validation. Existing lifecycle/exit/recovery tests cover whole-operation draining, rollback, and deterministic generation naming. Focused validation: 59 passed, zero failures/skips, three runs (the final run includes actual staging-path assertions). Full suite is deferred until user acceptance as requested.

`Build-Release.ps1` now invokes `--verify-catalog-backup-paths` inside the packaged executable, using a disposable task-owned isolated root and preserving `artifacts/release/backup-path-verification.jsonl`. It checks first backup, durable state/reopen, second generation, long-path integrity, corrupt rejection, restore protection and authored-state restoration, previous backup preservation, cleanup, and shutdown. This executable-host check is essential because the unit-test host has different manifest policy.

For persistent local acceptance evidence, run the same packaged switch with a fresh short task-owned parent (for example `<workspace>\artifacts\a`). It leaves each profile and an exact-path JSONL report for inspection. Run the resulting case profile normally with `--data-root` for hands-on dialog acceptance. No PR, push, merge, issue closure, or release-gate clearance is implied.
