# Bounded isolated-profile startup (#340)

## Safety contract and origin

`2884111` introduced `--data-root` for task-owned development and packaged acceptance. `ApplicationDataProfile.Initialize` rejected reparse points throughout the selected tree and its ancestors, then created/probed five owned directories. App called it before logging/IPC/splash; storage called it again before settings. Recursive inspection was a conservative way to reject every pre-existing descendant alias without having to inventory lazy readers and writers. It did not establish an atomic filesystem snapshot or defend against another process replacing a path afterward.

The required invariant is: automatic profile-owned state must not escape an explicitly selected isolated root via path traversal or filesystem links. This is task isolation, not a hostile-process sandbox. The old walk did not detect NTFS hard-link aliases or guarantee security against concurrent rename/link substitution; this change makes no stronger claim.

Separate mechanisms remain responsible for other invariants:

- Pure argument resolution rejects nonabsolute/drive-relative, UNC/device, alternate-stream and ambiguous trailing-dot/space roots. It normalizes dot segments and trailing separators, and rejects the default application-data directory and either ancestor/descendant overlap. It never opens the canonical profile.
- Containment uses normalized path-segment ancestry with case-insensitive Windows comparison, not a raw string prefix; sibling prefixes and different drive roots do not count as descendants. Reparse attributes are checked from volume toward leaf so a child is never inspected through an unchecked junction. Missing paths are allowed for creation; permission/I/O failures other than missing entries fail closed. Long local paths retain the app's long-path support. UNC profiles remain unsupported.
- Persisted Catalog/Preview overrides and relocation destinations remain within the isolated profile. Database leaves and SQLite sidecars at configured alternate locations are checked after reading settings, before evidence/SQLite access. This proves a different invariant from validating the fixed default paths, and is not another profile initialization.
- Media Roots are source selections, not profile ownership. The old validator neither rejected a Media Root containing the profile nor scanned external source trees. `OwnedStoragePaths` continues excluding application/Catalog/Preview/temporary storage from discovery and monitoring. No root-mapping or watcher policy changes.
- Backup destinations retain their separate containment/separation checks. Explicit user-selected media and exports remain real filesystem operations; no output or backup policy changes.

## Targeted validation and access

Initialization inspects the fixed `LightflowStorageLocations` path inventory, known legacy/default files, SQLite/evidence sidecars, rotated logs, and the owning roots for derived resources. A per-invocation set deduplicates ancestor metadata calls. Five directory write/flush probes remain. No enumeration API exists on the initialization filesystem seam. Cost depends on this fixed inventory and path depth, never the number of cached files.

After successful isolated initialization, the process registers that root for access checks. Normal profiles are not registered; an ordinary installed process does no isolation filesystem work. Registered roots persist for the process lifetime so later operations and headless sessions cannot accidentally lose protection when a coordinator closes.

Lazy owned access rejects reparse points at the actual path and its ancestors. This covers persisted Preview paths through the shared contained-path resolver, generated thumbnails/standard previews, Visual Index generation files and position frames, Subclip posters, Encoding LUT resources and output-identity cache files. Fixed JSON/log/database leaves were checked before opening them. Premiere pairing already checks each contained path. Recovery candidates are checked before reading; explicit backup/cache enumeration checks children before traversal/use. Explicit maintenance can still enumerate for its requested task, but startup never invokes that enumeration.

A link inside an unused cache bucket no longer blocks the whole application merely by existing. Attempting to use that bucket fails before following it; a link at an active ownership boundary or fixed state file fails startup. Tests use real Windows junctions, deterministic file-reparse attributes, late link creation, and an external sentinel to verify no outside access/write. Like the previous implementation, checks and file operations are not an atomic defense against hostile concurrent substitution. Generated unpredictable staging names and existing replacement semantics remain unchanged.

## One owner and early presentation

`App.OnStartup` resolves the profile and performs primary-instance selection, renders the existing embedded splash, then initializes the isolated profile on a worker. Logging is created only after success. App passes `InitializedDataProfile` to `LightflowStorageCoordinator.StartAsync`; storage verifies it matches the requested profile and reuses it. A headless caller without a result causes storage to initialize once. Explicit verification modes similarly own/reuse their result for the same profile; independent diagnostic profiles validate separately.

The result reports actual process-local isolated-validator invocation count, distinct inspected paths, zero descendant enumerations and elapsed time. Activity Log additionally records process-age at rendered splash and Browser usability. These are diagnostics, not startup timing gates. A failing preflight closes the splash and exits with code 2 without falling back to normal storage.

Splash artwork, footprint, styles and accepted exceptional validation wording remain unchanged. No special isolated-phase wording is needed for the measured bounded operation; no time estimates, delays, animations or duration thresholds were added. Existing Catalog/Preview clean-state, migration, recovery, anomaly and shutdown behavior is unchanged.

## Regression and acceptance evidence

The operation-budget test compares an empty profile with populated nested cache content using exactly the same inspected path set and five probes. A descendant sentinel fails if initialization reaches beneath the owning cache directory. Startup/storage tests cover result reuse, one headless invocation, mismatched result rejection, zero normal-profile inspection, normalized/case/long paths, forbidden default-profile overlap, sibling/drive boundaries, active file/directory reparse points, deep cache junctions, generated paths, and source-discovery exclusions. The existing WPF source contract now checks that the splash precedes filesystem validation and that logging follows it. Packaged acceptance asserts the real process invocation count is one.

Accepted before evidence (source `7c8ba4377e2022e26ee592876b16b4f80f367a83`): two walks of 1,104,698 entries, 36.26 s + 35.22 s, 74.78 s process-to-usable; 95.6% in isolation walks. Both databases already used CleanShutdown, 30.3/6.9 ms readiness, no integrity scans. Default workspace state still took 72.96 s. The existing partial-cache acceptance profile is retained, not copied or extended for this implementation.

Final package measurements, focused test counts, read-only database health and authored/source preservation evidence are recorded in the local acceptance handoff under `outputs/`. Health checks necessarily read database pages; cache state is uncontrolled and no artificial cache eviction/warming is used. Architectural acceptance is bounded path work, one initialization, prompt splash, and preserved clean database readiness, rather than a machine-specific time threshold.
