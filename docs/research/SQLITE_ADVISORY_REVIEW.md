# Agent U: SQLite advisory decision gate

> **Owner-accepted disposition (added after research completion):** Recommendation **B — Scheduled maintenance remediation recommended / P2** is accepted. Follow-up implementation and qualification are commissioned through [#363 — Dependencies: remediate affected bundled SQLite runtime](https://github.com/jeremyrunningphotography/lightflow-studio/issues/363) for Agent W. This report preserves the historical research state below; Agent U has performed no remediation or patched-application qualification. Raw task-local evidence remains preserved in `C:\Git\Agents\Agent-U-SQLite` until accepted/merged implementation no longer needs it.

Research date: October 5, 2026 (America/Los_Angeles). Owner review required before implementation.

## Decision

**B. SCHEDULED MAINTENANCE REMEDIATION RECOMMENDED. Lightflow priority: P2.**

Lightflow's freshly packaged main contains affected SQLite **3.41.2**. No SQL-injection path or oversized aggregate query was found in ordinary application operations. Bound media/user values cannot create the required aggregate expression tree. This does not establish immunity to a tampered SQLite schema in a restored or replaced Catalog. A supported, small remediation now exists: retain Microsoft.Data.Sqlite 8.0.29 and add an exact SQLitePCLRaw.bundle_e_sqlite3 **2.1.13** reference, resolving its provider/core/native family to 2.1.13. Its Windows x64 native binary reports SQLite **3.53.3**.

Evidence threshold: affected runtime directly measured; ordinary SQL grammar boundaries reviewed; database-schema trust remains a residual concern; patched same-family distribution directly measured and published by the existing maintainer. These support scheduled preventive maintenance without claiming an exploitable Lightflow attack. They do not support an emergency, unconditional safety declaration, or a completed compatibility qualification.

**Agent W should be commissioned only after owner approval of this report and its proposed scope. Agent W has not been started.** No implementation issue, PR, Project edit, commit, push, or dependency upgrade was performed.

## Source and workspace

- Audit starting main: `a827470ab0338790c5697d9fcfaebe9231a70670`.
- Independent remote clone and subsequent `git fetch origin main` both confirmed the same commit. No parallel maintenance merge was present at the final research fetch.
- Research clone: `C:\Git\Agents\Agent-U-SQLite` (full independent clone, not a worktree).
- Canonical checkout: inspected read-only for orientation; the initial fetch there was denied by sandbox protection of `.git/FETCH_HEAD`. All successful fetch/build/research work subsequently used the independent clone.
- Read root AGENTS.md in full; reviewed Catalog/storage architecture, ADR 0001, release qualification, project graph and locks, Catalog/Preview initialization and persistence, recovery, migrations, package scripts and relevant tests.
- Source version is 1.0.0, application target `net8.0-windows`; Catalog schema 19. No current dependency changes were applied.

## Complete dependency chain

The application's exact direct package is Microsoft.Data.Sqlite `[8.0.29]`:

```text
LightflowStudio (net8.0-windows)
  Microsoft.Data.Sqlite 8.0.29
    Microsoft.Data.Sqlite.Core 8.0.29
      SQLitePCLRaw.core 2.1.6
        System.Memory 4.5.3
    SQLitePCLRaw.bundle_e_sqlite3 2.1.6
      SQLitePCLRaw.lib.e_sqlite3 2.1.6
        runtimes/win-x64/native/e_sqlite3.dll
      SQLitePCLRaw.provider.e_sqlite3 2.1.6
        SQLitePCLRaw.core 2.1.6
```

LightflowStudio references Lightflow.Actions (`net8.0`), which introduces no SQLite package. LightflowStudio.Tests references LightflowStudio and inherits the same SQLite graph. Its direct packages are the test SDK and xUnit infrastructure; it has no second SQLite provider. The app and test lock files confirm the family versions. No EF Core ORM or System.Data.SQLite stack is used. Catalog and the separate Preview database both use Microsoft.Data.Sqlite through this native runtime. Microsoft.Data.Sqlite automatically initializes its bundle; no custom production `SetProvider` or library swap was found. [Microsoft's custom-version documentation](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions) describes this arrangement.

Machine-readable subset of the three lock files: `work/dependency-tree.json`. The self-contained publish creates its own separate `artifacts/release/publish.packages.lock.json`; this is generated packaging output, not an edited tracked lock file.

## Actual packaged runtime

Executed the unchanged repository command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller
```

The first sandboxed restore could not reach NuGet (NU1301). Repeating with reviewed network access succeeded. The task-owned package is:

`C:\Git\Agents\Agent-U-SQLite\artifacts\release\LightflowStudio\LightflowStudio.exe`

Its timestamp is October 5, 2026 16:51:24 UTC, later than source commit October 5 02:32:15 UTC. Package startup, splash/presentation handoff, Jobs activation, graceful shutdown, Catalog backup/restore protection, FFmpeg/playback dependency preparation and staged package validation passed. PR mode deliberately skipped the portable archive; `-SkipInstaller` skipped installer generation. Neither is claimed tested here. Packaging took 133.1 seconds.

A second packaged `--verify-catalog-runtime` run with a task-owned `DOTNET_BUNDLE_EXTRACT_BASE_DIR` exited 0. Inspected the native DLL extracted from that EXE using a diagnostic that calls its exports by absolute path:

| Property | Measured result |
| --- | --- |
| Library | `e_sqlite3.dll` |
| Native version | **3.41.2** via `sqlite3_libversion()` |
| Source ID | `2023-03-22 11:56:21 0d1fc92f94cb6b76bffe3ec34d69cffde2924203304e8ffc4155597af0c191da` |
| Architecture | PE machine `0x8664`, Windows x64 |
| Size | 1,691,648 bytes |
| SHA-256 | `DCCBABB2BC7E7D4302C44D9CE41B70721A7D0914FA4D289E2F340D39766AD102` |
| Provenance | SQLitePCLRaw.lib.e_sqlite3 2.1.6; extracted hash matches restored NuGet native asset |
| Relevant options | `MAX_COLUMN=2000`, `MAX_EXPR_DEPTH=1000`, `MAX_VARIABLE_NUMBER=32766`, `THREADSAFE=1`, FTS3/4/5 and RTree enabled; no `OMIT_WINDOWFUNC` or `OMIT_TRIGGER` reported |

Evidence: `work/native-packaged.json`, `work/Inspect-NativeSqlite.ps1`, package build log and extracted DLL under `work/bundle-extract`. This measures the library shipped in a successful executable verifier run; it does not add a diagnostic endpoint to production or claim a debugger-observed normal-session module trace.

## Advisory and exact trigger

[GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) tracks **CVE-2025-6965**, CWE-197 numeric truncation. GitHub's reviewed record, updated June 18, 2026, rates it **High, CVSS v4 7.2** and lists NuGet native package versions through 2.1.11 as affected, with no patched version entered in that record. Its native-version boundary is before SQLite 3.50.2. The advisory database's missing patched-package entry is not proof that newer native packages remain vulnerable.

[Google's original technical advisory](https://github.com/google/security-research/security/advisories/GHSA-qj7j-3jp8-8ccv) identifies aggregate-context column indices exceeding 32,767: a 32-bit index becomes signed 16-bit `Expr.iAgg`, turns negative and is used outside an aggregate bookkeeping array. Debug assertions and release-build heap accesses differ; Google describes potential code execution. Its examples use many distinct references across aliases, large HAVING expressions, or CASE/aggregate expressions with a window-function subquery. The number of rows or the size of a bound string is not this trigger.

[SQLite's CVE table](https://sqlite.org/cves.html) describes this CVE in terms of attacker-supplied arbitrary SQL and records 3.50.2 as fixed. [The June 27 upstream fix](https://www.sqlite.org/src/info/5508b56fd24016c13981ec280ecdd833007c9d8dd595edb295b984c2b487b5c8) and [3.50.2 release notes](https://sqlite.org/releaselog/3_50_2.html) establish the repair. Local diff of upstream 3.50.1 and 3.50.2 `expr.c` shows aggregate column/function tracking capped against `SQLITE_LIMIT_COLUMN` before signed-short assignment. This is compiler/expression-analysis logic, not a media parser or a malformed-row decoder. The original report's timeline differs slightly from the upstream commit/release dates; the latter govern the fixed-release decision.

The advisory says `<3.50.2`; it does not establish the first historically introduced version, so no lower bound is invented. Distribution backports are possible, but this shipped DLL reports the original 3.41.2 source identity, not a demonstrated security backport. Treat it as affected. Runtime `MAX_COLUMN=2000` alone is not a defense in the old code: distinct references across many table aliases can exceed a per-table/result-column limit. Debug/ASan settings aid reproducing faults, not a prerequisite for a release-build defect. No optional extension gate is required for the column-index case.

Required attacker influence is over the compiled SQL expression structure, either directly supplied query text or relevant schema SQL. Bound values in Lightflow's existing queries cannot grow the number of column references or aggregate terms. Prepared statements do not sanitize an attacker-controlled database schema. A crafted database may supply views/triggers that are expanded/compiled under a fixed outer query. That is SQL structure control through the database, not a counterexample in which ordinary bound row values alone trigger this bug.

No crashing exploit or weaponized Catalog was run. A concrete CVE exploit surviving all Lightflow open/restore checks has **not** been demonstrated. The schema route below is a source-supported possibility, not a claimed working exploit.

## SQL inventory and boundaries

Whole-source search includes application, Actions, tests, scripts, docs and SQL files; generated outputs/upstream downloads are separate evidence. Production C# contains 146 `CommandText` assignment locations and 279 lines matching SQL statement markers. These are search counts, not parsed statement counts. `work/sql-inventory.txt` preserves source/line hits; `work/dynamic-sql-boundaries.txt` records interpolation/helper boundaries. Tests/diagnostic SQL is distinguished from production input paths.

Classes: A fixed SQL + bound data; B fixed SQL + bounded internal fragments; C composition from validated internal tokens; D user-controlled syntax; E uncertain. Generated numeric parameter names are C (not caller text). Plain fixed DDL without data parameters also belongs to A.

| Production source/area | Class | Boundary and grammar |
| --- | --- | --- |
| CatalogSqliteConnectionFactory, CatalogDatabaseService, CatalogDatabaseContracts | A/B | Authored identity/history/readiness SELECTs and PRAGMAs; integer timeout/app ID/schema version from internal constants/migration definitions; no caller SQL |
| CatalogMigrations | A/B | 19 authored transactional migrations, tables/indexes/triggers, ALTER/DROP where required; context identity/time values bound; never executes stored migration SQL |
| MediaAssets | A/B/C | Fixed projection and path-range WHERE fragment chosen by empty/nonempty normalized prefix; root/path/status bound; batched IN placeholder names generated internally; ORDER BY authored |
| MediaRoots, BrowserRecursiveRoots | A | Root physical/display paths, names and recursive folder scope bound; SQL sort columns fixed |
| CatalogCollections | A/B/C | Table/id/scope identifiers from literal call sites or internal Set/Collection branch; bound names/IDs/revisions; recursive ancestry CTE, scalar count/max, fixed UNION and sorts |
| CatalogSmartCollections | A | Source IDs/folder and QueryJson bound; JSON is typed Browser intent, not stored SQL |
| CatalogAssetState, CatalogClassifications, CatalogMarkers | A/B/C | Bound review values/marker names; bounded IN parameter generation (usually chunks of 400); fixed group-count projections |
| CatalogDescriptions | A/C | Description values bound; update columns from AssetDescriptionField after `Enum.IsDefined`; only five defined fields; generated assignments do not accept arbitrary column text |
| CatalogMediaRanges, CatalogSubclips, CatalogPreviewFrames, VideoOrientation | A/C | Authored reads/upserts/deletes; ticks/names/rotation/revisions bound; generated batched ID parameters; no dynamic aggregate projection |
| ColorManagement | A/C | LUT identities/display values bound; EnsureExists table/column names supplied only by literal MediaAssets/LutResources call sites; IN placeholders generated, joins fixed |
| AssetCopying | A/C | CopyRows table/column/select arguments are authored literals, row values rebound; inserts use generated positional parameter names; fixed max ordinal aggregate |
| PremiereHandoff, PremiereMarkers | A/B | Three literal projection tables selected from operation kind; JSON and project/bin/marker/receipt data bound; fixed rowid sort/limits |
| PreviewPersistence | A/B/C | Separate store; metadata JSON/path/retry/state values bound; thumbnail/standard-preview field prefixes from two authored alternatives; WAL/DELETE chosen from location type; fixed count/schema reads |
| CatalogRecovery, CatalogExitBackup, StorageManagement, StartupStoreEvidence | A/B or no SQL | Integrity/identity reads, native backup API, authored journal/checkpoint PRAGMAs; exit backup and relocation orchestrate these services; file paths enter connection-string builder, not SQL text |
| BrowserQuery, BrowserQueryIntent, BrowserGrid, MainWindow.SmartCollections | No SQL compiler | Typed filtering/search/sort over resident tiles; enums and JSON validation; smart intent feeds the same engine |
| WorkspaceState/settings/action routes | No SQL compiler | JSON application state/typed actions; not arbitrary query execution |

No production D query-text boundary found. Generic private helpers receiving `string sql` are not public SQL consoles; inspected callers supply authored text. SQL-like text in strings, filenames or metadata never flows into those helpers as command text. Migration triggers are real and must continue working; disabling all triggers as a quick workaround would break existing Catalog invariants.

### Aggregate/CTE/subquery findings

- CatalogAssetState.GetAsync: lines 107/120, grouped `COUNT(*)` for Subclips and TimelineMarkers. CatalogMarkerService.SummariesAsync: CatalogMarkers.cs line 56, grouped count. One fixed aggregate per statement; user controls only ID membership.
- CatalogCollections: DeleteCollectionSetAsync/collection deletion protection and EnsureParentExists/EnsureCollectionExists use single `count(*)`; EnsureAcyclic uses fixed recursive Ancestors CTE with one count; NextOrdinal/NextHierarchyOrdinal use one max and a fixed union. Internal table tokens do not change expression count.
- CatalogMigrations.ApplyVersion11: correlated sibling count during ordinal rewrite, fixed structure. Schema triggers use EXISTS/RAISE to enforce authored rules, not giant aggregates.
- AssetCopyDataService.CloneCatalog: one max ordinal in membership insert.
- CatalogAssetColorService.EnsureExists: one count with literal identifier callers.
- PreviewStoreService.GetUsageAsync and ValidateForMigration: fixed single counts of records/schema indexes.
- No authored oversized aggregate-reference construct, SQL HAVING clause, window `OVER(...)` expression, arbitrary aggregate builder, FTS query, extension loading or CREATE VIEW found in production SQL. Ordinary joins/subqueries/recursive CTEs are present, but with a small fixed set of referenced columns. Math.Min/Max, LINQ counts and HLSL functions are not SQL aggregates.

Lightflow **uses aggregates**, but **does not author the vulnerable oversized construct**. A collection with millions of rows, hundreds of IDs in an IN list, or many filter alternatives does not turn these fixed columns into tens of thousands of aggregate-context references.

## User-input tracing

Input classes here: A bound SQL value; B fixed authored fragment selector; C validated token; D raw syntax; N no SQL use; E unresolved. These refer to application boundaries; hostile database schema is discussed separately.

| Input | Result and source boundary |
| --- | --- |
| Filenames, full paths, folders, removable/downloaded-media locations | A/N: MediaRoots and MediaAssets bind physical/relative/display/path keys; filesystem resolution and Browser presentation are separate |
| Media metadata, camera/lens, codec/container, capture date | A/N: PreviewPersistence binds normalized/raw JSON; BrowserQuery extracts and compares typed metadata in memory |
| Descriptions, title, notes, creator/credit | A; field choice C: CatalogDescriptions validates the field enum, binds its value |
| Keywords | A/N: classification store binds each keyword; Browser keyword matching in memory |
| Collection/Collection Set/Smart Collection names | A: organization writes bind normalized names; no identifier derived from a name |
| Smart search text/filter values/source folder | A/N: bound QueryJson/source columns; BrowserQueryIntent validates/deserializes then ToQuery, never SQL translation |
| Browser search, sort, metadata field selection, aspect ratio, filter alternatives | N: BrowserQueryEngine uses .NET predicates/comparers, BrowserGrid calls it; sort enums do not become SQL ORDER BY text |
| Ratings, flags, color labels | A/N: typed/validated classification values bound; in-memory filtering |
| Marker names/data, Subclip names, working ranges | A: CatalogMarkers/Subclips/MediaRanges bind names/ticks/revisions |
| LUT/color and rotation intent | A/B: IDs/degrees bound; internal literal table/field choices |
| Workspace state | N: JSON state; saved query intent becomes typed in-memory predicates, not SQL |
| Premiere bridge/Companion payloads | A/N: typed protocol dispatch; Catalog projection JSON/IDs/receipts bound; operation type selects literal table |
| Older Catalog row data during migrations | A or ordinary source rows: authored migration SQL reads data; row content never becomes migration command text |
| Restored/imported/replaced Catalog database | Distinct schema boundary: D-equivalent embedded schema SQL is possible; E for a demonstrated CVE exploit accepted by all Lightflow checks |
| Tampered Preview database | Same schema concern; rebuildable does not imply harmless native compilation |

Quoted SQL-looking text, very large metadata strings and malformed JSON may cause validation/resource issues, but no route to this CVE's expression structure was found. This investigation does not assert that all media decoders, bridge actions or resource limits are free of unrelated defects.

## Trust model and reachability by operation

Catalog is user-owned durable local state, not a hostile multi-tenant SQL service. Media from cameras, downloads and removable storage is untrusted data; Lightflow does not expose arbitrary SQL to it. NLE payloads are typed operational data. Portable/shared Catalog design is future work, not an existing universal safe-import promise.

Startup checks application ID, schema version, migration history, identity and integrity/readiness. Backup/restore inspects integrity and identity, then copies through SQLite's native backup API. These checks detect corruption/format mistakes; they do not authenticate schema provenance or compare every CREATE statement against an allowlist. CatalogRuntimePolicy does not set trusted_schema off or disable views/triggers. Read-only inspection still compiles schema-dependent reads. Clean-start certificates improve lifecycle qualification, not hostile-schema authentication.

[SQLite's security guidance](https://sqlite.org/security.html) explicitly treats database files of uncertain provenance as a separate trust boundary and discusses tampered schema, views and triggers. Its trusted-schema advice concerning custom functions is not a complete fix for this aggregate CVE, and Lightflow's legitimate triggers cannot simply be switched off.

| Operation | Ordinary authored database | Crafted-schema scenario |
| --- | --- | --- |
| Startup | Fixed PRAGMA and small identity/history/readiness reads; no oversized aggregate | An expected table name can potentially resolve to a malicious view; schema reads occur before any comprehensive schema authenticity check |
| Browser/Smart Collections | Fixed scope/ID queries plus in-memory filters; safe grammar boundary for listed inputs | Referenced views or schema expressions could change compilation; no complete exploit demonstrated |
| Migrations | Fixed SQL and fixed triggers; stored values cannot add terms | Existing malicious schema/trigger could be compiled while migration reads/writes execute |
| Backup/restore | Native snapshot plus fixed validation SQL; normal backups do not gain query text from values | A structurally valid malicious schema can survive an SQLite backup; inspection is itself a native parsing boundary |
| Metadata ingestion/Preview | Bound JSON/upserts and small count queries | Previously tampered Preview schema remains a separate concern |
| Ordinary Catalog writes | Fixed writes invoke authored triggers | An added attacker trigger can cause compilation of embedded SQL during a fixed write |

Realistic residual scenarios: a user restores a backup supplied/altered by another party, sync/removable storage carries an altered Catalog, or a less-trusted local process can replace the database but does not already control the application executable. The attacker must achieve database **schema/SQL control**, not merely insert a funny filename/metadata value. Integrity checks can pass a semantically malicious schema. Specific huge expression acceptance and CVE memory corruption through these exact Lightflow paths remain unproven.

With equivalent full local-user control, the attacker already can destroy/change Catalog data and often run code directly, making incremental impact small. With only a supplied backup or a narrower file-write capability, possible native memory corruption in Lightflow would cross a more meaningful boundary: denial of service, or potentially execution with the application's user rights. Lightflow is not an elevated service; no privilege-escalation claim is made.

Ruled out for this CVE under authored schemas: SQL structure from filenames, media metadata, Browser/Smart search/filter/sort, collection names, descriptions, ratings/flags/labels/keywords, range/marker/subclip values and Companion JSON. Normal row count/string length growth does not meet the trigger. A crafted SQLite file is **not** ruled out merely because the outer application query is fixed.

## Remediation options and ranking

Inspected upstream NuGet archives without referencing them from the application or running patched queries. Native export inspection alone is not a patched-application compatibility test.

| Option | Verified distribution / support | Assessment |
| --- | --- | --- |
| 1. Existing family, bundle 2.1.13 | lib/provider/core 2.1.13 dependencies; win-x64 lib is **3.53.3** | **Preferred**, smallest coherent-family change; retain Microsoft.Data.Sqlite 8.0.29 and automatic initialization |
| 1a. Bundle 2.1.12 | win-x64 native **3.53.3**, same SHA-256 as 2.1.13 | Patched, but prefer 2.1.13's packaging correction; no reason to stop at 2.1.11 |
| 2. Microsoft.Data.Sqlite 10.0.12 | NuGet manifest requires bundle/core >=2.1.12; netstandard package compatible with net8; core 10.0.0 inspected has net8/netstandard assets | Patched native can resolve via 2.1.12/13, but changing provider major adds scope and conflicts with ADR's .NET 8/provider-major convention without an owner decision |
| 3. Bundle 3.0.5 | Manifest depends on config.e_sqlite3 3.0.5 and **SQLite 3.53.4**, whose x64 DLL directly reports 3.53.4 | Supported next-generation family; more graph/packaging change than needed for this advisory |
| 3a. Core 8.0.29 + config.e_sqlite3 3.0.5 + SQLite 3.53.4 | Upstream configuration/native separation and Microsoft's Core/custom-bundle route | Viable explicit-native-version architecture; replace convenience Microsoft package, preserve config auto-init after verification; larger dependency declaration change |
| 4. System SQLite | provider.winsqlite3 or sqlite3; machine OS controls native version | Reject for this Windows fix: no uniform patched version, feature/OS servicing drift, Windows-specific provider debt |
| 5. Own patched native distribution | SQLitePCLRaw supports custom libraries/providers; config.e_sqlite3 can bind a correctly named e_sqlite3 build | Technically supported binding, but build/provenance/CPU/OS/redistribution/signing/updates become Lightflow obligations; disproportionate here |
| 6. Temporary acceptance | Current affected runtime, constrained application query grammar | Reasonable only as short owner-reviewed scheduling interval; indefinite wait for upstream is unnecessary now |

[Bundle 2.1.13 metadata](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.13) and [native package metadata](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/2.1.13) confirm published package identity and graph. Native 2.1.13 SHA-256 is `B7385D722C83FB52142A00477A726723745916D22A555711EE89834C1111FB2E`, source ID `2026-06-26 20:14:12 d4c0e51e4aeb96955b99185ab9cde75c339e2c29c3f3f12428d364a10d782c62`. [Maintainer issue 678](https://github.com/ericsink/SQLitePCL.raw/issues/678) explains the 2.1.13 correction for an obsolete win-arm .NET Framework target. Lightflow uses net8/win-x64, but the corrected release is preferable.

[Microsoft.Data.Sqlite 10.0.12](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.12) differs from the inspected 10.0.0 manifest: 10.0.0 still requested affected bundle 2.1.11. A newer Microsoft provider version is not itself sufficient evidence of native remediation. The 10.0.12 graph is patched by its newer native dependency, not by its managed API version.

[Upstream v3 migration notes](https://raw.githubusercontent.com/ericsink/SQLitePCL.raw/main/v3.md) describe retaining bundle auto-initialization and separating configuration from native distribution. They illustrate SourceGear.sqlite3; inspected **current 3.0.5 manifest uses SQLite 3.53.4**, not that older example's native package name. [SQLite 3.53.4 package metadata](https://www.nuget.org/packages/SQLite/3.53.4) names e_sqlite3 native builds and maps its version to SQLite. SourceGear.sqlite3 3.53.4 is also published; neither should be installed alongside another duplicate native asset provider. Avoid unsupported ad hoc mixtures of 2.1.6 managed pieces and replacement DLLs.

### Compatibility matrix

W means coherent bundle 2.1.13; M means Microsoft 10.0.12 plus coherent 2.1.13; V means bundle 3.0.5 (or explicit config/native variant); O means own native distribution with a supported provider.

| Dimension | W (preferred) | M | V | O |
| --- | --- | --- | --- | --- |
| Native SQLite | 3.53.3 measured | 3.53.3 if 2.1.13 selected | 3.53.4 measured | Chosen fixed release; must measure |
| Upstream-supported pieces | Existing bundle/provider/native family | Published Microsoft + family, API qualification pending | Maintainer-documented architecture | Binding supported; builds Lightflow-owned |
| Production code | No initialization/data-access change expected | API review; no necessary Catalog rewrite established | Auto-init verify; Core variant changes references | Usually explicit initialization/loading seam |
| Package references | Add exact bundle 2.1.13 | Change Microsoft major + explicit coherent bundle | Add/replace bundle; Core/config/native variant larger | Core/provider + own asset packaging |
| Tracked locks | Regenerate app/test graphs, Actions unchanged | Regenerate graph with provider changes | Replace native/config graph | Replace native asset graph |
| Windows x64 | Native RID present; candidate package test needed | Same native qualification | RID present | Build/ABI/VFS/CPU/minimum OS qualification |
| Installer | Same embedded-native topology; rebuild/test | Same plus managed graph | Different native package provenance/notices | New asset/signing/license responsibility |
| Portable | Same self-contained topology; extract/run test | Same | Same expected, verify | Manual RID inclusion/load path test |
| Catalog format | No schema change intended | No schema change intended | No schema change intended | Preserve format/options; verify |
| Migrations | Current 1–19 remain | Current 1–19 remain | Current 1–19 remain | Current 1–19 remain, compile options matter |
| Existing databases | Expected compatible; round-trip required | Same + API behavior | Same + wider runtime changes | More build-option risk |
| Backup/restore | Existing BackupDatabase API retained; test | Managed/native API qualification | Same explicit qualification | Snapshot/WAL/VFS qualification |
| Tests | Native/version + storage regressions | W plus provider API regressions | W plus config/graph verification | W plus own build matrix |
| Future macOS | **A**, osx-x64/arm64 assets present | **A**, managed Catalog neutral | **A**, osx-x64/arm64 assets present | **B** if deliberate multi-platform builds; **C** if Windows-only DLL hack |
| SQLite graph node count | Same six SQLite/Microsoft nodes | Same six expected | Seven with convenience Microsoft+bundle/config/native/core/provider | Typically four plus own asset project/package |
| Maintenance | Low; family pin + native assertion | Medium; provider major lifecycle | Low/medium; native pin independently possible | High; own build/release cadence |
| Rollback | Prior graph + protected Catalog | Prior managed/native graph | Prior graph + packaging revert | Asset/loader revert and protected Catalog |
| Safety/support rank | **1** | **3** | **2** | **4** |
| Implementation complexity | Low | Medium | Low/medium | High |
| Regression risk | Low/moderate, native jump is material | Moderate | Moderate | Highest |

All format compatibility entries are expectations grounded in used features, not completed patched-runtime tests. System SQLite is not a viable uniform Windows remediation; acceptance changes no code/package/file format but retains the defect and requires reassessment. Encryption bundles would change requirements and potentially database compatibility; excluded as disproportionate.

## Catalog and downgrade compatibility

Catalog uses ordinary tables/indexes, foreign keys/CHECK constraints, authored triggers, upserts and transactional migrations; no FTS virtual table, loadable extension, encrypted format or newly introduced SQL feature is needed for the remediation. Schema remains 19; runtime upgrade alone must not introduce migration 20. Preview schema/payload versions must also remain unchanged.

Catalog policy: WAL, synchronous FULL, foreign keys ON, busy timeout 5000 ms. Preview policy: WAL locally/DELETE on UNC, synchronous NORMAL, timeout 5000 ms. Recovery snapshots finalize DELETE journaling; shutdown/relocation checkpoint WAL. Preserve these choices.

[SQLite's format compatibility statement](https://sqlite.org/formatchng.html) distinguishes newer-reader compatibility from older-reader compatibility, which depends on features used. Both 3.41.2 and 3.53.3 support the authored schema and existing WAL design. [The database/WAL format specification](https://www.sqlite.org/fileformat.html) also distinguishes transient SHM state from persistent database/WAL state. A clean shutdown/checkpoint and SQLite-aware backup remain necessary; copying a live main .db alone is not a safe rollback plan.

Expected result: an unchanged-schema Catalog opened/written by W should remain readable by current v1.0.x/current main and existing backup tooling. **This exact forward/downgrade round-trip was not tested**, and this report does not qualify every v1.0.x artifact. Agent W must test the actual accepted release artifact as well as a827470, preserving identity, rows, revision semantics, constraints, migration history and backup behavior.

Measured native option differences matter: 2.1.13 has `DQS=0`, `LIKE_DOESNT_MATCH_BLOBS`, `DIRECT_OVERFLOW_READ`, additional GEOPOLY/SESSION/PREUPDATE features, `MAX_FUNCTION_ARG=1000` (old 127), newer compiler and larger max page count. Authored SQL uses single-quoted string literals and fixed identifiers, not double-quoted string literals; no SQL LIKE-on-BLOB or session extension usage found. These observations reduce risk but do not replace storage regression tests. Inspect the full compile-option JSONs and compare all changed defaults. Retaining the provider/init seam avoids unnecessary macOS debt; RID presence is not macOS execution/signing qualification.

## Proposed Agent W scope, contingent on owner approval

1. Work in a fresh full independent clone under C:\Git\Agents. Owner first records the accepted scope in a durable issue and reconciles its actual Project/Epic relationships; no guessed parent/priority. Start from/target current main. If owner wants released v1.0 maintenance, use its actual release branch and record reconciliation into main according to AGENTS.md. Do not touch PR #360/TourBox or concurrent S/T/V changes.
2. Keep Microsoft.Data.Sqlite `[8.0.29]`. Add **`SQLitePCLRaw.bundle_e_sqlite3` Version=`[2.1.13]`** to LightflowStudio.csproj. Expected bundle/lib/provider/core all 2.1.13; System.Memory may follow the new core manifest. Resolve normally and inspect the complete graph; no old/native duplicate, no unexplained dependencies, no managed provider major/framework change. Do not merely override lib while retaining old managed family.
3. Expected Windows x64 native **3.53.3**, source ID and SHA-256 above. Do not settle for a scanner disappearing or package version changing. No custom SetProvider/init change expected. No Catalog/Preview migration, storage-authority change, replacement database or extension/encryption addition.
4. Expected changed files: LightflowStudio/LightflowStudio.csproj; app/test packages.lock.json; root THIRD-PARTY-NOTICES.md; scripts/Test-PackageContents.ps1 (currently explicitly asserts `SQLitePCLRaw 2.1.6`); CatalogPackageRuntimeVerifier.cs and focused runtime verification tests as needed; dependency/storage documentation and the owner-approved issue. Correct license text as well as version: inspected current 2.1.6 core and candidate 2.1.13 manifests declare Apache-2.0, whereas current notices describe SQLitePCLRaw as MIT. This is an existing notice mismatch, not an established license transition. Verify each newly resolved package's exact license/NOTICE obligations and retain SQLite's public-domain notice. Actions lock should stay unchanged unless actual graph evidence requires it. Publish lock is generated output, never an experimental tracked source edit.
5. Add a packaged runtime assertion through the existing verifier: initialize the real Microsoft.Data.Sqlite provider, open Catalog and Preview, obtain `sqlite_version()` and `sqlite_source_id()`/ServerVersion, and fail on unexpected/past-vulnerable native version. Record version evidence into a packaging artifact. Verify win-x64 extraction provenance/hash against selected NuGet native asset; enumerate native payloads so stale copies cannot shadow the selected one. Keep diagnostics in verifier/test flow, not normal user UI.
6. Run relevant existing Catalog, collections/smart query, markers, descriptions, classifications, subclips/ranges/color/rotation, asset-copy, Preview persistence, startup evidence, recovery, backup-on-exit, long-path restore and mutation/relocation tests. Add meaningful regressions where the native-option delta affects actual SQL, particularly DQS behavior and legitimate triggers/constraint enforcement. Do not copy an exploit into production tests merely to prove a scanner finding.
7. Existing Catalog qualification: use a task-owned copy from the accepted release/current main, populate representative authored/derived data; open/read/write/close with W; reopen a separate copy using old packaged release and a827470 builds; check schema/history/identity/constraints/data/revisions. Include WAL checkpoint/reopen and no implicit migrations. Preserve original fixtures.
8. Migration qualification: older supported Catalog fixture (including schema 7/package migration verifier path) through current migrations to 19; pre-migration backup validation, rollback/failure preservation, reopen under W. Never use Jeremy's live Catalog.
9. Backup/restore qualification: online WAL snapshot, explicit exit backup, restore with current protection, long-path scenario, rollback/rejected-corrupt/wrong-identity/future-schema behavior, and old-build inspection of resulting backups. No schema bump or silent data conversion.
10. Packaging: unchanged CI/build topology, updated exact notices/graph assertions. Build both installer and portable via Release mode; install/run in task-owned acceptance storage and extract portable to a fresh directory. Verify actual native runtime in both, self-extraction/load path, required files/licenses, Windows x64/no arbitrary system SQLite dependency, and supported Windows baseline. Before handoff rerun the mandated PullRequest/SkipInstaller build **after the final commit**, confirm timestamp freshness and no leftover smoke process.
11. macOS boundary: shared Catalog stays Microsoft.Data.Sqlite-neutral; use published osx-x64/osx-arm64 assets through the same provider seam when that port is implemented. Do not implement a macOS port or promise untested signing/CPU compatibility here.
12. Rollback: revert dependency/notice/verifier commits and rebuild; close W cleanly/checkpoint, protect Catalog via SQLite-aware backup, validate older reader before reopening. Restore the pre-upgrade backup if any compatibility problem occurs, preserving newer files for diagnosis. Never point an old build at an uncheckpointed live database or assume downgrade after a future independent migration.
13. Draft PR only, with exact workspace/package/isolated acceptance paths, test results, native evidence and owner hands-on acceptance. Jeremy checks Browser/smart collections, normal annotations, reopen and backup/restore in the packaged build. Existing styling is unchanged. Remediation is not merge authorization.

No patched application prototype was executed. W's qualification is intentionally outstanding, rather than smuggled into this research task.

## Baseline validation, handoff and reassessment

- Existing focused test run: **348 passed, 0 failed, 0 skipped**, Catalog/PreviewStore/BrowserQuery/DependencyHealth name filter, 23 seconds. This is baseline coverage only; tests whose names do not match were not claimed run. TRX preserved.
- Fresh unchanged packaged build and dependency validation passed; no LightflowStudio process remained at the post-validation process inspection. No installer/portable release artifact acceptance performed.
- Normal user storage was not used for launches. Packaging used its disposable smoke roots; verifier internals also use GUID-named disposable temporary stores. Diagnostic bundle extraction and acceptance path are task-owned. No user-started process was terminated.
- Optional baseline hands-on command (this is **current affected main**, not a remediation build):

```powershell
& "C:\Git\Agents\Agent-U-SQLite\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\Agent-U-SQLite\work\acceptance-data"
```

The recommendation is P2 because no ordinary untrusted-value attack path was found, full schema control is a stronger prerequisite, exploitation through Lightflow is unproven, and a coherent existing-family fix is available. GitHub's High severity is a separate upstream assessment. Do not relabel this as a confirmed remote Lightflow exploit or dismiss it as impossible.

If owner defers scheduling, reassess when a concrete accepted-Catalog exploit is demonstrated, a query console/raw-SQL feature is proposed, SQL filter translation replaces the in-memory Browser engine, portable/external Catalog import expands the trust boundary, or candidate compatibility tests fail. Those events may change urgency or remediation choice. No recurring automation was requested or created.

GitHub read-only searches for SQLite and the exact GHSA returned no issue specifically representing this advisory (exact-GHSA result `[]`). Related storage/portability Epics are not automatically this advisory's issue. Search snapshots are preserved; no issue/Project/PR mutation occurred.

## Artifacts and limits

All paths below are relative to `C:\Git\Agents\Agent-U-SQLite`:

- `docs/research/SQLITE_ADVISORY_REVIEW.md`: this report, uncommitted/task-local.
- `work/dependency-tree.json`, `work/source-sql-hits.json`: machine-readable dependency/source evidence.
- `work/sql-inventory.txt`, `work/dynamic-sql-boundaries.txt`, `work/aggregate-inventory.txt`: whole-source hits; false positives retained as search evidence, not asserted SQL.
- `work/native-packaged.json`, `work/native-2.1.12.json`, `work/native-2.1.13.json`, `work/native-sqlite-3.53.4.json`: export/version/source/hash/architecture/compile options.
- `work/Inspect-NativeSqlite.ps1`: reproducible native inspection; no production modification.
- `work/upstream/`: downloaded nupkg archives/extracted manifests, SQLite pre/fix source and diff; upstream URLs in this report govern provenance.
- `work/package-build.log`, `work/baseline-tests.log`, `work/test-results/sqlite-baseline.trx`: baseline validation.
- `work/sqlite-issues.json`, `work/advisory-issues.json`: read-only GitHub search snapshots.
- `work/verification.json`: source/status/hash/freshness summary.

Unexpected findings: patched 2.1.12/13 are now available; advisory metadata still says no patched NuGet version; newer Microsoft 10.0.0 alone is not patched; current v3 bundle native package name differs from v3 documentation examples; package notices/tests hardcode old SQLitePCLRaw version; current notices say MIT but current core/candidate manifests declare Apache-2.0, so notice work includes correcting an existing mismatch; native compile defaults changed materially.

Final integrity gate: no tracked production code, package references, lock files or repository documentation changed. Only untracked task-local research/diagnostic evidence was added. Build/test/generated packaging files remain in the independent clone. No PR or implementation issue was created; no Project state was changed; no implementation package was installed; no commit/push/merge occurred. Source research does not prove a working hostile-Catalog exploit or candidate downgrade compatibility, and neither is represented as validated.
