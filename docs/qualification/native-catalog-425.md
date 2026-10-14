# LF-MAC-DEV-015 native Catalog lifecycle qualification

Issue #425 remains open for owner review. This implementation consumes accepted #432 / PR #435 and provides an Apple Silicon CLI using the current production schema. Recommendation: **REVISE** before full #425 acceptance because automatic missing-directory creation, closed-transfer capacity and handle-bound no-replace publication remain unqualified. The bounded create/open/author/close/reopen acceptance flow is runnable. R2 #393 and #406 remain incomplete; #366 remains In Progress.

## Ownership and composition

`CatalogLifecycle` remains the sole owner of startup, selection, admission, mutation sessions, activation, configuration readback, rollback, shutdown, backup/restore, transfers and recovery. No shared workflow, SQLite provider, schema, WAL/FULL policy or Windows consumer changed. The sole shared edit grants the native tests existing internal store access.

`MacCatalogLifecycleEffects` supplies isolated locations, configuration and admission using the existing Mac storage assessor. Catalog-only consumers represent absent monitoring/cache/derived consumers; they never attest Preview completion. No Preview database exists. Profile evidence remains Dirty/unpaired, Catalog evidence records Validated provenance, and subsequent startups follow conservative deep validation.

`MacCatalogNativeEffects` plus `native/catalog.c` supply Darwin descriptors, device/inode/generation identity, link/type checks, atomically acquired `O_EXLOCK`/`O_SHLOCK`, guarded reads, exclusive journal creation, fsync plus `F_FULLFSYNC` and parent fsync. The Catalog journal also holds the parent-directory lock, preventing a second cooperative host from bypassing exclusion by renaming/recreating the journal. Active main-file identity is rechecked on authoring. Shared restore activation can rebind only an already admitted native descriptor; the prior descriptor survives until shared completion or rollback. Synchronous and asynchronous disposal close parent, main and rollback descriptors.

Darwin locks are advisory. Identity checks are point-in-time checks, not Windows no-delete namespace semantics; this is not proof against noncooperating concurrent namespace mutation. Active storage remains bounded by the existing local case-insensitive APFS admission policy. Symlinks, hard-linked files, unsupported paths and outside-root destinations are refused. No JRPhoto4T writes or physical-device experiments occurred.

Configuration uses a flushed temporary file, atomic config replacement, parent directory fsync and the shared save/readback/rollback sequence. This is configuration persistence, not qualification of Catalog directory publication. Missing-to-created directory identity transitions are refused; first creation uses an explicitly prepared empty directory.

## Native production behavior and authored state

Native tests create schema 19 and verify CatalogId, WAL, FULL and foreign keys. Reopen retains identity and authored state; stale sessions, missing/invalid/future-schema files, replaced main-file identity, torn/pending journals and symbolic paths are refused. The existing storage suite supplies unsupported filesystem policy/refusal coverage without mounting unsupported media.

The richer native fixture authors through existing shared stores: Collections and membership; ratings, flags, color labels, keywords and descriptions; In/Out, subclips, markers, preferred-frame intent; camera/creative LUT references and authored-state copy; managed backup metadata. It compares every logical table across reopen, changes only description Notes, then proves all other tables unchanged and the change persisted. RootId, AssetId and machine mapping rows remain stable. Root and mapping baseline inserts are test setup under admitted mutation ownership, not qualification of Mac root remapping or media import. Preferred-frame timestamps are contract fixtures, not decoder or Preview qualification. A real valid .cube is used; transfer of external LUT bytes is not claimed.

Smart Collection query codec/operations and cross-machine Root remapping are unavailable in this CLI. No operation changes saved query meaning or clears missing references. No Windows return test, physical fault, external-media transport or full authored-schema cross-machine round trip was performed.

## Writer, recovery and transfer evidence

The process harness uses real independent hosts and a readiness handshake, without race delays/retries. It proves both Catalog lease refusal independently of the profile lease and ordinary competing-host refusal, release after clean close, reopen after killing only its spawned host, unchanged logical state and unpaired evidence. This is process interruption, not power-loss qualification.

Native tests exercise shared framed journals and durable readback, pending-intent refusal before SQLite, managed backup identity/schema/integrity/metadata, shared restore, injected activation and configuration-save faults, safe rollback and source retention. Restore inputs outside managed retention are byte-compared after rollback; existing daily/monthly retention is unchanged.

`AvailableSpace` returns unknown; the public closed-backup workflow therefore refuses at SnapshotFinalized rather than accepting unverified capacity. It leaves a failed terminal journal and the source reopens with unchanged logical state. Publication requests refuse because safe handle-bound no-replace publication is unqualified; Move has no path-based rename fallback. These are explicit limitations, not successful transfer qualification.

## Local validation and performance

Apple Silicon macOS; .NET SDK 8.0.425 arm64 / runtime 8.0.31; task-owned local APFS fixtures. Final native run: 24 passed, 0 failed, 0 skipped. Shared Catalog: 44 passed. Application: 136 passed. Original Mac storage host: passed. Process qualification: 23 checks passed. Release host build: zero warnings/errors. Apphost and native dylib are Mach-O arm64; native dependencies are system frameworks/libSystem, with a portable @rpath install name. Framework-dependent deployment requires the provided task .NET runtime or installed .NET 8.

`process-qualified-3` records complete stdout/stderr and elapsed samples. Creation (n=1) startup 614.47 ms, ownership open 0.57 ms, disposal 1.80 ms. Existing deep-validation inspect reopens (n=6) startup 92.92–124.50 ms, median 95.17 ms; ownership open 0.40–0.44 ms; disposal 2.00–2.29 ms. Startup includes recovery preflight; no independent preflight timing is claimed. Metrics exclude command process launch for startup/disposal, and ownership open excludes certificate writes. Small warmed local fixtures are observations, not performance budgets or Windows parity.

Failed diagnostics are retained under task artifacts. Early failures identified /var symlink aliases, descriptor-counter concurrency, protected-path defaults, restore inode rebinding and retention-fixture placement; fixes preserve original safety assertions. A prior direct-backup process probe left zero-byte WAL and SHM companions; the strict standalone comparison refused those bytes. The public command now uses the shared closed-backup lifecycle instead of bypassing it. This observation is not a proven handle leak or durability conclusion.

macOS CI now runs native tests, the original storage host and the process qualification, uploading TRX and JSON evidence. CI status for the final PR head must be checked separately; local results do not attest CI.

## Exact owner acceptance commands

The workspace is `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-015-CatalogLifecycle`. Published executable: `artifacts/lf015-native-host/MacCatalogHost`. Prepared owner root is reserved for hands-on acceptance and separate from qualification fixtures.

```sh
export DOTNET_ROOT="/Users/jeremyrunning/Git/agents/LF-MAC-DEV-015-CatalogLifecycle/work/toolchain/dotnet"
mkdir -p "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-015-CatalogLifecycle/artifacts/lf015-owner-acceptance/Catalog"
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-015-CatalogLifecycle/artifacts/lf015-native-host/MacCatalogHost" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-015-CatalogLifecycle/artifacts/lf015-owner-acceptance" create
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-015-CatalogLifecycle/artifacts/lf015-native-host/MacCatalogHost" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-015-CatalogLifecycle/artifacts/lf015-owner-acceptance" inspect
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-015-CatalogLifecycle/artifacts/lf015-native-host/MacCatalogHost" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-015-CatalogLifecycle/artifacts/lf015-owner-acceptance" collection-add "Owner acceptance"
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-015-CatalogLifecycle/artifacts/lf015-native-host/MacCatalogHost" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-015-CatalogLifecycle/artifacts/lf015-owner-acceptance" inspect
```

Each command closes through the lifecycle. Verify the same CatalogId/schema 19, valid integrity and persisted Owner acceptance Collection. If creation was already run, use inspect rather than replacing the fixture. No manual process race or device fault is required. No full GUI, Preview peer, optimized paired reopen, unsupported filesystem support, R2 completion, merge or release is claimed.
