# X2 / M2 Mac Catalog portability checkpoint

2026-10-05, America/Los_Angeles. Authoritative issue [#369](https://github.com/jeremyrunningphotography/lightflow-studio/issues/369), Epic [#366](https://github.com/jeremyrunningphotography/lightflow-studio/issues/366), completed prerequisite #367. Historical `../mac-compatibility` research is preserved unchanged.

**Verdict: native SQLite and bounded persistence operations work on this arm64 host; current path identity is unsafe for general Mac roots. G2 remains open. Stop before production identity/schema implementation for Jeremy's architecture decision.** Windows→Mac→Windows is pending its Windows-origin fixture. This checkpoint is not a Mac application, package, release or combined G1/G2/G3 acceptance.

## Ownership and agreed bounds

- Independent clone: `/Users/jeremyrunning/Git/agents/Agent-X2-Mac-Catalog-Proof-20261005-01a10e68`.
- Branch: `codex/x2-mac-catalog-proof-20261005`; cloned and fetched origin/main `e302a20d888161d6face6e311610d67eab9c1512`, identical to Windows baseline.
- Host: MacBook Pro Mac14,9, Apple M2 Pro, 12 cores, 32 GB, macOS 26.6.2 build 25G83; .NET SDK8.0.423, runtime8.0.29, process arm64. Hardware serial/UUID omitted from durable artifacts.
- Jeremy agreed 32 synthetic assets and one working day of engineering plus Windows transfer/return. Semantic requirements are zero silent identity merge/duplicate, authored-state loss, corruption or destructive path interpretation. No performance acceptance threshold was invented.
- Data roots: `work/data/local-probe-02`, `work/data/case-probe`, `work/data/local-control-02`; media fixtures and all SDK/NuGet/tmp/native assets are inside this clone. No owner media/Catalog or normal application storage is opened.
- Case-sensitive volume: task-owned512MB sparse image `work/data/x2-case-sensitive-512.sparseimage`; mountpoint `work/data/case-mount`. Evidence is copied before scoped detach. No existing disk was formatted.

## Measured runtime and operations

Microsoft.Data.Sqlite/Core8.0.29 and resolved SQLitePCLRaw bundle/core/provider/lib2.1.13; System.Memory4.5.3. Lock and assets establish the resolved graph. NuGet downloaded core2.1.6 while resolving Microsoft's minimum dependency; it is cache history, not a selected graph/output component.

Actual provider queries report SQLite3.53.3 and source ID `2026-06-26 20:14:12 d4c0e51e4aeb96955b99185ab9cde75c339e2c29c3f3f12428d364a10d782c62`. Dyld enumerates the task-owned arm64 `libe_sqlite3.dylib`, SHA256 `09FE20A6C050912A7944B2F2F2981DEBE31679F7919F523C87251AE8CF0F2DAA`,1696624 bytes, matching NuGet. Native package SHA256 `D07D13B4779123CFA56B3348B504D57A2AB6FC10C764FB6E2C9D787987E39201` matches historical static evidence.

Dyld also lists Apple's system SQLite and PoirotSQLite framework from host dependencies. They are not selected by the e_sqlite3 provider; shared-cache images are not ordinary readable on-disk files. This is explicitly not a claim that only one SQLite image exists in the entire host. Catalog/Preview version/source equality, selected provider graph and e_sqlite3 payload provenance are the qualification boundary.

Catalog19: WAL, synchronous2/FULL, foreign_keys1, busy_timeout5000, integrity `ok`; all19 production migrations used. Preview4: same version/source, WAL, synchronous1/NORMAL,5000ms, integrity `ok`; production metadata/artifact-path state survives close/reopen. Artifact bytes/image decoding are not qualified. Compile options retained in JSON include DQS0, LIKE_DOESNT_MATCH_BLOBS, DIRECT_OVERFLOW_READ, MAX_FUNCTION_ARG1000, THREADSAFE1, MUTEX_PTHREADS, clang17.

Each filesystem probe completed16 persistence/availability/checkpoint/recovery assertions. They are operation checks, **not16 path-safety passes**: hazardous outcomes are recorded separately. Production online backup, protected restore installation/rollback with closed ownership, reopening and every-table equality passed on both controls. The32-asset local seed completed36 assertions. A SIGKILL of the exclusively owned crash child (exit-9; WAL retained) followed by production reopen preserved the committed Notes edit, rejected its uncommitted overwrite and passed integrity/checkpoint:4 assertions. No power-loss/hardware fault is simulated.

## Observed identity failures

See [PATH_IDENTITY_MATRIX.md](PATH_IDENTITY_MATRIX.md) and raw service results plus stat device/inode evidence. On case-sensitive APFS distinct case-only files fold to one identity; second creation returns AlreadyExists and subsequent lookup resolves the first file's bytes. NFC/NFD aliases on both tested APFS variants produce different keys/AssetIds for the same physical file. Literal backslash and surrounding-space names resolve to distinct existing files' identities. A symlink outside the logical root is accepted; its target remains safely inside the task workspace. Mac accepts several Windows-invalid names without portable-root rejection.

Current MediaRootMappings separates machines, and a second machine starts Unmapped, remaps the existing RootId and leaves IDs intact. Offline/restored directory fixtures retain asset rows and become Online after return. This is a local rename simulation, not physical removable-drive or SMB remount proof. Windows drive paths are foreign mapping strings on Mac; no actual Windows mapping transfer has occurred yet. Stable stat IDs are observations only, not a portable cross-volume identity design.

## Harness limits and fixture ownership

`tools/X2CatalogProof` is a standalone proof project, with unchanged linked production sources. Production TFMs/packages/schema/locks are untouched. Compile-only Windows instance identity constant and Preview retry cooldown are disclosed in Seams.cs. BrowserScope evaluation deliberately throws; only the real recursive-scope SQL repository is used. No WPF UI, startup instance coordinator or shell lifecycle is launched. Real StartupStoreEvidence source compiles but its kernel32 fingerprint/clean certificate path is not invoked. Data profile paths use real CreateAtRoot with an explicit harness workspace boundary; isolated-profile Initialize/registered symlink guard is not invoked. Thus the symlink result exercises normal MediaRoot/Asset semantics, not a bypass of a qualified isolated-profile guard.

Catalog creation/migrations, roots/assets/fingerprints, classifications/keywords, descriptions/revisions, collections/sets/membership, markers, rotation, recursive repository and recovery/Preview use production logic. Supplemental SQL seeds ranges/Subclips, preferred frames, valid hash-linked cube/Color assignment, Smart query document and inert Premiere projection journals; persistence of these rows is measured, their full authoring/query/NLE service behavior is not. Synthetic `.mov` fixtures are bounded text bytes, not playable video. The local-control fixture is not a Windows→Mac leg. No metadata media sidecars are used; manifests belong to the proof archive.

## Failures and remaining gates

Initial sandbox clone/fetch/SDK/NuGet network failures were retried with approved network access. The first build's missing BrowserScope compile closure was fixed only in harness seams. The first runtime reporter tried hashing shared-cache system SQLite as a file and failed after its operations; it remains an incomplete run, superseded by separately named02 control. Image128MB setup failed;512MB creation succeeded. These are setup/reporting failures, not production Catalog failures; retained in `evidence/SETUP_FAILURES.md`.

Physical NAS/share location was supplied and bounded filesystem/closed-backup transport measured; live database and fault behavior remain unqualified. Removable hardware location remains pending Jeremy. See [NAS_STORAGE_ASSESSMENT.md](NAS_STORAGE_ASSESSMENT.md). Production Mac stable identity/exclusive ownership/durable replacement remains unqualified. Signing/notarization, packaged startup and Windows return fixture are outstanding. No PR is ready because cross-OS/owner gates and required Windows packaged validation are incomplete. No merge or M4+ work occurs.

## Owner decision

[PROPOSED_ROOT_IDENTITY_ARCHITECTURE.md](PROPOSED_ROOT_IDENTITY_ARCHITECTURE.md) proposes a small initial fail-closed portability preflight with exact-name preservation and no automatic repair. Rejecting case-sensitive roots alone cannot solve the measured Unicode/trim/backslash hazards. A future versioned identity migration needs explicit review and older-reader compatibility; none is implemented. Decisions/findings are recorded on #369 and summarized on #366. Current Project In Progress is inherited from the verified Windows checkpoint; this Mac connector exposes no Project-v2/native relationship read/write operation, so independent live field/relationship re-verification must be performed by Windows X2 before any completion claim.

Reproduction and the exact Windows handoff are in [CROSS_OS_ROUNDTRIP_MANIFEST.md](CROSS_OS_ROUNDTRIP_MANIFEST.md). Source/harness commit, transfer archive and raw evidence hashes are supplied in the generated task-local `work/evidence/HANDOFF_CHECKPOINT.md` after commit/archive creation. All acceptance gates stay open.

## Resumed live NAS investigation

See [LIVE_NAS_QUALIFICATION.md](LIVE_NAS_QUALIFICATION.md) for current-main production WAL failure, proof-only DELETE controls/recovery and Outcome D with proposed B/C direction. Earlier pending-live-test statements describe prior checkpoints, superseded by this follow-up; historical raw observations remain unchanged.
