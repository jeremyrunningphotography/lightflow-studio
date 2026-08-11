# ADR 0001: Lightflow Catalog persistence

- **Status:** Accepted
- **Date:** 2026-08-10
- **Issues:** #69, #78; foundation for #79–#83

## Context

Lightflow needs a durable Catalog for stable media identity and future user-authored organization. That data may represent years of ratings, labels, keywords, notes, collections, and root mappings. It cannot be reconstructed from source media. At the same time, thumbnails, normalized probe results, and processing scratch data can be regenerated and should not inherit the Catalog's backup and recovery cost.

The current application has several small JSON stores under per-user Local AppData. They are suitable for their present scope but do not provide the relational constraints, migrations, transactional updates, scalable queries, online backup, or integrity tooling needed by a long-lived media catalog.

## Decision

### 1. Ownership and durability classes

Lightflow uses three explicit storage classes:

| Class | Owns | Guarantee |
| --- | --- | --- |
| **Catalog** | Stable Catalog/Root/Asset IDs, logical roots and mappings, relative asset paths, source identity facts required for durable reconciliation, and all current or future user-authored media knowledge | Precious. Committed transactions must be durable, migration and restore must preserve the prior recoverable state, and failures must be visible. Never silently recreated or replaced. |
| **Previews** | Thumbnails, preview renders, normalized technical metadata, FFprobe/EXIF results, and other source-derived indexes | Fully rebuildable. May be deleted, relocated, or rebuilt without Catalog data loss. Must not be the sole home of user-authored state or durable identity. |
| **Temporary workspace** | Scratch files, incomplete processing data, transient imports/exports, and disposable work products | Disposable. May be cleaned after failure or startup according to feature-specific ownership rules. |

Logs, preferences, operational history, and output resume caches remain application support data. They are centrally located but are not part of the Catalog or its backup set.

The existing same-directory `*.lightflow` Encoding partial is intentionally governed by the output lifecycle, not the general temporary workspace, because same-filesystem finalization is its safety boundary.

### 2. SQLite and provider

SQLite is appropriate for the expected single-user desktop workload: one application process, large row counts queried incrementally, transactional relational updates, and no requirement for a database server.

Issue #80 adds a direct, exact reference to **`Microsoft.Data.Sqlite` 8.0.29**. Lightflow uses its ADO.NET API directly rather than Entity Framework Core. This keeps the data-access boundary explicit, migration SQL reviewable, package size lower, and schema evolution under Lightflow's control.

`Microsoft.Data.Sqlite` is MIT licensed. Its default bundle uses SQLitePCLRaw and the native `e_sqlite3` library; SQLite is dedicated to the public domain. When #80 adds the binary dependency, the exact package graph and applicable MIT notices must be added to `THIRD-PARTY-NOTICES.md` and verified in self-contained/installer packaging. The package is not added by #78 because no production database code exists yet; an unused native dependency would add payload without validating the eventual package path.

The provider major follows the application's .NET 8 target. A provider upgrade is an intentional dependency change with normal test/package validation; code must not float to an unpinned version. The exact package graph is locked, and self-contained packaging embeds the SQLitePCLRaw `e_sqlite3` native library for runtime extraction.

### 3. File layout and centralized locations

All paths flow through `ILightflowStorageLocations`. Features must not independently combine `%LOCALAPPDATA%`, brand folders, or configured Catalog/Preview paths.

Default Windows layout:

```text
%LOCALAPPDATA%\Jeremy Running Photography\Lightflow Studio\
├── Catalog\
│   ├── LightflowCatalog.db
│   └── Backups\
├── Previews\
├── Temporary\
├── output-identities\
├── settings.json
├── state.json
├── trim-history.json
├── job-history.json
└── activity.log
```

Catalog and Previews directories are independently overridable. `CatalogDirectory` is the durable relocation/backup ownership unit. `CatalogDatabasePath` is the actual SQLite database file within it and always resolves to the stable filename `LightflowCatalog.db`; it must never be interpreted as a directory. Selecting a Catalog location therefore selects its containing directory, not an arbitrary database filename. `CatalogBackupsDirectory` is the `Backups` child of that same durable unit. These explicit names prevent #79 relocation and #83 recovery code from treating a file as a directory or migrating only part of the Catalog. Preview and temporary directories are outside the Catalog directory so cache deletion cannot touch precious files.

Catalog, Previews, and Temporary directories may not be equal or nested within one another. This is enforced by path resolution before filesystem mutation, preventing a Preview purge or temporary cleanup from crossing the precious-data boundary.

`LightflowStorageLocationOptions` is a path-resolution contract only. #79 owns persistence of configured overrides, destination validation, protected Catalog relocation, Preview move/rebuild behavior, and UI. A Catalog path change must never be applied as a raw settings edit.

Issue #79 persists only `CatalogDirectory`, `PreviewsDirectory`, and the expected Catalog identity in the existing atomically replaced `settings.json`. Derived paths remain owned by `ILightflowStorageLocations`. Application startup loads this bootstrap configuration before constructing the active location service or opening the Catalog. The identity marker distinguishes approved first creation from a missing previously initialized Catalog.

### 4. Unavailable configured storage

Path resolution is side-effect free and does not create directories. The service that opens a Catalog must distinguish:

- explicit first-run/new-Catalog initialization at a validated, writable location where configuration records that no Catalog has previously existed;
- a configured directory is temporarily unavailable;
- access is denied or the location is read-only;
- a Catalog file exists but is unreadable, corrupt, or too new;
- migration or recovery is required.

The absence of the database file alone is never sufficient evidence of first-time use: a disconnected drive or unavailable share can also appear absent. When a configured Catalog location is unavailable or its expected database cannot be opened, Lightflow retains that configuration, does not fall back to the default, and does not create a second empty Catalog. Catalog-dependent features enter an unavailable/read-only state with actionable diagnostics; independent features such as Encoding may continue. A Preview location failure may disable previews or select an explicitly approved rebuild destination, but it must not affect Catalog data.

The application composition root now owns the active location coordinator and Catalog session. First creation is permitted only when no custom Catalog directory and no prior Catalog identity are configured. Missing, unavailable, unreadable, corrupt, foreign, or future-schema Catalogs remain explicit startup states. A configured Preview directory that disappears is reported independently and never changes Catalog availability.

### Protected relocation behavior

Catalog relocation rejects relative, overlapping, equivalent, non-empty, conflicting, and obvious UNC live-database destinations. It verifies write/staging access, disposes the active session and clears SQLite pools, uses `SqliteConnection.BackupDatabase` into a same-destination staging file, opens the result through the production Catalog lifecycle, and compares Catalog identity and schema before atomically persisting the new directory. Only then is the validated session activated. A post-switch activation failure atomically restores the source configuration and reopens the source. The old Catalog directory is never deleted or overwritten: its original database and any SQLite-managed companion files remain as the known-good recovery source. Any copy, validation, configuration, or cancellation failure removes only operation-owned staging/destination files where possible, restores the original configuration, and reopens the source Catalog. This retained source is not entered into backup history or retention; that belongs to #83.

Preview changes use an independent workflow. “Move existing” copies into an operation-owned staging directory, switches the cache directory, then removes the old rebuildable cache best-effort. “Switch and rebuild” selects an empty writable destination and deliberately leaves the old cache untouched. Neither workflow reads or mutates Catalog data.

### 5. Connection and transaction lifecycle

- Data access remains behind Lightflow-owned Catalog service/repository contracts. WPF and feature models receive durable Lightflow models, not `SqliteConnection`, commands, readers, or raw SQL.
- Open a pooled connection per service operation/unit of work and dispose it promptly. Do not keep a connection on a WPF window or UI thread.
- Reads use short-lived connections. Multi-statement writes use an explicit transaction. User-authored changes are acknowledged only after commit succeeds.
- Migrations, root remaps, bulk reconciliation, and restore operations define explicit transaction boundaries and must not hold transactions while doing filesystem scans, media probing, or UI work.
- SQLite work runs off the WPF UI thread. Cancellation is honored before a transaction and at safe boundaries; cancellation never reports success for an uncommitted write.
- One Lightflow process is the supported writer. Later in-process background readers use the same service policy rather than independent connection strings.

### 6. SQLite runtime policy

Every opened Catalog connection must enforce the same policy through a connection factory owned by #80:

- `PRAGMA foreign_keys = ON` on every connection.
- `PRAGMA journal_mode = WAL` for the local Catalog.
- `PRAGMA synchronous = FULL` because Catalog writes may contain irreplaceable user data.
- A bounded **5-second busy timeout**, followed by a clear busy/unavailable result rather than unbounded UI hangs.
- Short write transactions, with immediate writer acquisition where a workflow benefits from failing before expensive work.
- Normal connection pooling is allowed. The Catalog lifecycle must clear/close pools before relocation, restore, or file replacement.
- WAL checkpointing uses SQLite's normal autocheckpoint initially; backup, close, relocation, and maintenance operations perform an explicit checkpoint when required by their operation.

`synchronous=FULL` adds filesystem synchronization and therefore higher write latency than `NORMAL`, particularly during frequent small commits. Lightflow intentionally accepts that cost for precious Catalog writes so a reported commit receives SQLite's strongest practical WAL durability against operating-system crash or power loss. Features should still group related changes into short transactions rather than trading durability away. This policy is scoped to the Catalog. It does not prescribe FULL synchronization, WAL, or even SQLite for rebuildable Preview persistence, which may choose a throughput-oriented design in its own implementation.

These are accepted runtime requirements, not behavior implemented by #78. #80 implements the provider, connection factory, PRAGMA application/verification, schema, migrations, and integration tests. The service is not connected to startup or location UI until the protected workflows in #79 are available.

### 7. Schema versions and migrations

- The database's `PRAGMA user_version` is the authoritative integer schema version.
- #80 also keeps an append-only migration record containing migration ID/version and UTC application time for diagnostics.
- Migrations are ordered, forward-only, deterministic, and included in application code. Production code never performs ad hoc schema repair.
- New Catalog creation applies the same ordered migration chain from version 0.
- Each migration runs in a transaction where SQLite permits it. A failure rolls back and leaves the original Catalog recoverable.
- A pre-migration SQLite-aware backup is mandatory before changing an existing Catalog.
- A schema newer than the application supports is opened neither writable nor silently downgraded; the user receives an explicit “newer Lightflow version required” result.
- Migration failure never triggers empty-Catalog creation.

### 8. Integrity and corruption behavior

- On Catalog open, validate the SQLite header/open operation, expected application identity, supported schema version, and run `PRAGMA quick_check` once per application session for that Catalog.
- Run full `PRAGMA integrity_check` before migration, before accepting a restore, after suspicious I/O/database errors, and as an explicit maintenance/recovery action. It is not required on every ordinary query.
- Enforce foreign keys and treat failed constraints as application/data errors, not records to ignore.
- An unreadable or corrupt Catalog is preserved byte-for-byte. Lightflow does not rename it away and create a blank replacement.
- The open result identifies the failure and available validated backups. Recovery is explicit; Catalog-dependent writes remain disabled until a valid Catalog is selected or restored.

### 9. Backup and recovery

#83 will implement backups with `SqliteConnection.BackupDatabase`, not a raw copy of an active database/WAL pair.

- Create a validated backup before every migration of an existing Catalog.
- Create at most one automatic backup per UTC day after a successful integrity/open check and before the first write that day; a clean shutdown may ensure that day's backup exists rather than creating another.
- Write a backup to a temporary name, close it, run integrity/schema checks, then rename it to its final name.
- Name backups `LightflowCatalog-v{schema}-{yyyyMMddTHHmmssZ}.db`.
- Retain the newest 10 daily backups and 3 monthly anchors. Retention deletion applies only to files matching the exact Lightflow backup convention.
- Restore validates the candidate, protects the current Catalog with a recovery backup, quiesces access, replaces the Catalog in the same directory where possible, reopens and validates it, and rolls back on failure.
- Catalog backups never include Previews or Temporary data. Users should still include the Catalog directory in external backups.

### 10. Network paths and concurrency

V1 supports one local Catalog used by one Lightflow process. Direct use of a live SQLite Catalog on UNC/network storage is unsupported: WAL requires shared-memory/locking semantics that ordinary network filesystems do not reliably provide. #79 must reject obvious UNC live-Catalog destinations and warn that mapped drives cannot always be distinguished reliably. This restriction does not prohibit #83 from supporting validated backup/restore files on network destinations, nor future explicit export/import workflows; those files are not opened there as the live database. Previews may reside on network storage subject to availability/performance warnings.

Future multi-computer portability must use an explicit copy/synchronization/export design or a service architecture. It must not enable concurrent direct access to one SQLite file on a NAS. Stable Catalog/Root/Asset IDs are designed to make that future possible without redefining identity.

## Existing persistence inventory

No existing store is migrated by this decision.

| Store | Current role | Catalog relationship / likely future |
| --- | --- | --- |
| `settings.json` | User preferences and tool/default folders | Remains configuration. #79 may add references to protected Catalog/Preview location configuration, but not a freely editable live Catalog path. |
| `state.json` | Last-used Batch Encode choices | Ephemeral UI state; does not belong in Catalog. |
| `trim-history.json` | Path/size/last-write keyed trim ranges, 90-day retention | Candidate to attach durable ranges to AssetId after #82, if product semantics call for it. Do not migrate before stable Asset identity exists. |
| `job-history.json` | Bounded typed Encoding execution history | May later reference AssetIds/RootIds, but remains operational history rather than precious media knowledge for now. |
| `output-identities/*.json` | Resume/skip identity cache for finalized outputs | Rebuildable operational cache; must not become precious Catalog data. Could move under a future general cache boundary, not the Catalog. |
| legacy `*.lightflow.json` sidecars | Compatibility source for old output identities | Continue best-effort migration into the central output-identity cache; never import wholesale into Catalog. |
| `activity.log` and rotations | Diagnostics | Logs only; outside Catalog backup/recovery. |
| `dependencies/*.json` and packaged manifests | Build/runtime dependency provenance | Product/package metadata, not per-user persistence. |

Until migration decisions are made, new Catalog work must not introduce another absolute-path durable identity store. Existing path-bound trim/output history remains compatible but is not the model for new asset identity.

## Alternatives considered

### Continue with JSON files

Rejected for the Catalog. JSON remains appropriate for small preferences and bounded operational documents, but whole-document rewrites, weak relational constraints, and poor incremental query/migration behavior do not fit a large precious catalog.

### Entity Framework Core with SQLite

Rejected for v1. It adds a larger abstraction/package surface and generated migration conventions without removing the need to understand SQLite durability, backup, and locking. Direct `Microsoft.Data.Sqlite` plus Lightflow repositories is sufficient and easier to audit.

### System.Data.SQLite

Rejected. It has a larger legacy/provider surface and more complex native packaging for this self-contained application. `Microsoft.Data.Sqlite` aligns with modern .NET and provides the required backup API.

### Catalog on a shared NAS

Rejected for direct live access. Network locking and WAL semantics do not provide the durability/concurrency guarantee required for precious data. Future sharing requires an explicit architecture.

### Store user-authored data with Previews

Rejected categorically. Preview deletion/rebuild must never lose a rating, label, keyword, note, collection, stable identity, or logical-root mapping.

## Consequences

- Later Catalog work has a fixed provider, durability policy, path contract, migration convention, and failure model.
- Catalog operations require more care than caches: explicit transactions, backups, integrity checks, and user-visible failures.
- The application carries a native SQLite library through its self-contained single-file package; licensing notices and package checks cover that dependency.
- Direct live-Catalog access on network storage and multi-client writers are intentionally unsupported in v1; validated backup/export destinations are not prohibited.
- Existing JSON stores remain stable and can migrate incrementally after stable Asset IDs exist.

## Follow-up work

- **#79:** Persist independent location choices, validate destinations, implement protected Catalog relocation and Preview move/rebuild UX, and surface unavailable storage.
- **#80:** Implemented the pinned provider/package and notices, connection factory/runtime PRAGMAs, initial schema, application identity, migration runner, and repository boundaries.
- **#81:** Add logical Media Roots and machine-specific mappings.
- **#82:** Add stable Asset IDs, relative paths, and initial source fingerprints.
- **#83:** Implement SQLite-aware backup retention, integrity lifecycle, recovery/restore services, and user-facing diagnostics.
