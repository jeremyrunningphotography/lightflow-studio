# Architecture

## Current platform

- Native Windows desktop application
- C# / .NET 8
- WPF user interface
- FFmpeg and FFprobe as bundled processing dependencies
- Installer and portable release packaging
- Local JSON settings and state
- Rotating local activity logs

Lightflow Studio is evolving from a video-focused application into a capability-based media utility. The architecture changes incrementally: existing capabilities move onto shared contracts as they mature instead of requiring a flag-day rewrite.

## Layers

### Presentation

WPF views and code-behind currently own navigation, dialogs, accessibility behavior, and control updates. Presentation code may gather user choices and display plans, progress, and results. It must not put UI controls into durable job definitions or construct FFmpeg syntax itself.

`MainWindow` still coordinates the Encoding page while that feature is migrated incrementally. Its process pause/close behavior is intentionally retained rather than introducing a broad MVVM rewrite.

### Application services

Application-level services coordinate planning and execution. Current examples include:

- `EncodingJobPlanner`
- `OutputDestinationPlanner`
- `EncodingPathPlanner`
- `ExistingOutputPolicy`
- shared job execution and progress types

Planning is separate from execution. Planners may inspect files and metadata, but they do not create outputs or start processing engines.

### Shared job model

The shared model lives in `JobModel.cs` and `JobExecution.cs`. It owns lifecycle concepts rather than media-engine details.

```text
User request
    ↓
JobDefinition<TOptions>
    ↓
Planning and validation
    ↓
JobPlan<TOptions>
    ↓
JobExecution<TOptions, TResult>
    ↓
Per-item JobItemResult<TResult>
    ↓
JobResult<TResult>
```

#### Durable concepts

These types contain data only and are suitable for later capability-specific serialization:

- `JobDefinition<TOptions>`: stable job ID, capability ID, creation time, typed options, and ordered item definitions.
- `JobItemDefinition`: stable item ID, source identity, optional source size, and optional media range.
- `JobPlan<TOptions>`: planned time, ordered item plans, output paths, validation issues, skip decisions, and work unit.
- `JobItemResult<TResult>` and `JobResult<TResult>`: terminal state, outputs, warnings, errors, typed capability result data, timestamps, and aggregate counts.

Definitions and results never contain WPF controls, delegates, cancellation sources, or process handles. Persistent history in Issue #35 can store capability-specific definitions and results without serializing runtime machinery.

### Persistent job history

Completed Encoding executions are recorded in the local, human-readable `job-history.json` beside settings and state. A schema-versioned document holds up to the 100 newest records in reverse completion order. Each record stores the shared durable typed definition and plan together with the terminal typed `JobResult<EncodingItemResult>`; it therefore preserves job identity and timing, Encoding options, ordered source identities, planned outputs, requested and resolved media ranges, effective durations, per-item states, warnings, errors, and aggregate summary counts. Runtime `JobExecution`, cancellation, process, playback, and presentation objects are never serialized.

### Storage locations and Catalog persistence

All per-user persistence defaults now resolve through the Lightflow-owned `ILightflowStorageLocations` boundary. `LightflowStorageLocations` provides the application-data root; current settings, state, trim history, job history, output-identity, and log paths; and separated Catalog, Catalog backup, Previews, and temporary-workspace locations. `CatalogDirectory` names the durable directory/relocation unit; `CatalogDatabasePath` names the actual `LightflowCatalog.db` file. Resolution is side-effect free. Catalog and Previews accept independent absolute-directory overrides for the protected configuration/migration workflow in #79; feature code must not construct these paths directly.

The durable Catalog architecture is defined by [ADR 0001](decisions/0001-lightflow-catalog-persistence.md). The central invariant is that Catalog identity and user-authored knowledge are precious, while Previews and temporary data are fully rebuildable/disposable. The local, single-process SQLite Catalog uses a pinned `Microsoft.Data.Sqlite` provider, explicit schema migrations, transactional writes, foreign keys, WAL plus Catalog-only `synchronous=FULL`, bounded lock waits, integrity checks, a SQLite-aware backup seam, and no silent fallback or empty-Catalog recreation when configured storage is unavailable or corrupt. Direct concurrent access to a live Catalog on network storage is unsupported; network backup/export destinations are a separate future concern.

`CatalogDatabaseService` now owns the first production Catalog lifecycle. Callers must choose either `CreateNewAsync` (explicit first-Catalog authority) or `OpenExistingAsync`; a missing file during ordinary open is never creation authority. Results distinguish creation/reopen, existing or missing Catalogs, unavailable or unreadable storage, corruption, foreign application identity, unsupported future schemas, and migration/backup failures. Catalog creation and open use `ILightflowStorageLocations.CatalogDatabasePath`, so #79 can supply a protected custom location later without moving path policy into the database layer.

SQLite remains behind Lightflow-owned lifecycle, session, identity, result, and backup-seam contracts. `CatalogSqliteConnectionFactory` opens pooled connections per operation and verifies `foreign_keys=ON`, WAL, Catalog-only `synchronous=FULL`, and a 5,000 ms busy timeout on every connection. Sessions retain no open database handle; disposal clears provider pools so #79 relocation and #83 restore can quiesce the file. SQLite commands, connections, readers, exceptions, and migration SQL remain internal to the database implementation rather than WPF or feature models.

Self-contained packaging runs the published executable in a non-UI `--verify-catalog-runtime` mode. That smoke check creates, closes, reopens, and removes an isolated temporary Catalog through the same service, proving that the locked managed provider and embedded native `e_sqlite3` library load from the actual packaged executable rather than merely compiling on the build machine.

Schema version 1 contains only `CatalogInfo`, append-only `SchemaMigrations`, `MediaRoots`, `MediaRootMappings`, and `MediaAssets`. Stable GUID strings are relational identity. Assets use a logical RootId plus a forward-slash relative path and a separately normalized lookup key; absolute filesystem mappings live only in the machine/root mapping table. Size, UTC last-write ticks, optional versioned source fingerprint, source/root availability status, and fixed UTC ISO-8601 timestamps establish the durable patterns consumed by #81 and #82. Foreign keys, root/path and root/machine uniqueness, status/path checks, and expected lookup indexes are database-enforced. Ratings, labels, collections, derived metadata, and Preview data are intentionally absent.

`PRAGMA user_version` is authoritative. Migrations are contiguous, ordered, forward-only, and run one version per explicit transaction; successful versions append a diagnostic ledger row inside the same transaction. Version 1 establishes both the SQLite header application ID and the `CatalogInfo` identity row in its transaction. New databases traverse the version-zero migration chain. Existing older Catalogs first receive a full integrity check and must pass the provider-neutral `ICatalogMigrationBackup` seam before any migration starts. #83 still owns the actual SQLite-aware backup implementation, so upgrades are safely blocked—not falsely backed up—until that service exists. Ordinary open validates SQLite readability, the header-level Lightflow application ID, supported schema, migration ledger, Catalog identity row, and `quick_check` through a read-only preflight before applying mutable runtime policy. Non-SQLite input is classified as unreadable, while a failed SQLite integrity check is classified as corruption; both preserve the original file and never trigger replacement creation.

#79 still owns location persistence, destination validation, relocation, atomic configuration switching, and storage UI. #81 owns Media Root behavior and relative-path normalization APIs. #82 owns Asset repositories, fingerprint calculation, and reconciliation rules. #83 owns automatic backup cadence/retention, pre-migration backup implementation, full recovery/restore, and user-facing recovery diagnostics.

#78 introduced the path/configuration contracts and #80 adds the database lifecycle and foundational schema. Neither exposes location UI, performs relocation, or starts Catalog-dependent product features; those responsibilities remain with #79 and #81–#83.

History deserialization dispatches by schema version and capability. The initial schema accepts the typed `video.encode` payload; future capabilities add explicit typed record adapters rather than generic property bags. A missing, malformed, inaccessible, or unsupported document produces an empty History view, while malformed individual records are skipped. Writes serialize to a same-directory temporary file and replace the destination, with temporary cleanup remaining best-effort.

The History page presents summaries and detailed per-item outcomes. **Review & Rerun** never executes work: it revalidates each original source against its recorded path, size, and last-write timestamp, restores only unchanged sources and their normalized ranges, restores Encoding choices, uses the historic resolved output root as an explicit destination, and returns to Batch Encode for normal validation and user review. Missing or changed sources remain excluded and are reported. Historic output success and identity are never treated as current validity.

#### Runtime concepts

- `JobExecution<TOptions, TResult>` tracks item state, aggregate state, progress, start time, and result construction.
- `JobItemExecution<TResult>` enforces allowed state transitions and supports retrying failed or cancelled work.
- `JobCancellation` owns the cooperative cancellation token used by application and adapter code.
- `SequentialJobRunner` is available for straightforward capabilities that execute one planned item at a time. Encoding currently manages its loop in the WPF adapter because pause and close-after-current behavior remain UI-integrated.

Runtime objects are transient and are not part of persisted job definitions.

#### State vocabulary

Job and item states use one vocabulary:

- `Planned`
- `Queued`
- `Running`
- `Completed`
- `CompletedWithWarnings`
- `Skipped`
- `Cancelled`
- `Failed`

Items transition explicitly. Failed and cancelled items may return to `Queued` for retry. Aggregate job state is derived from item states; failures take precedence over cancellation, cancellation over warnings, and warnings over clean completion. Result summaries retain every per-item count so mixed outcomes are not hidden by the aggregate state.

#### Progress semantics

Each plan selects a meaningful `JobWorkUnit`:

- `Items`
- `Bytes`
- `MediaDuration`

Items carry a determinate or indeterminate `JobWorkEstimate`. Aggregate progress weights each item by that estimate rather than assuming equal file counts. Encoding uses effective media duration when all durations are known and falls back to item weighting when they are not. Other capabilities can choose bytes or item counts without changing the lifecycle.

`MediaRange` represents normalized displayed source duration and optional In/Out timestamps. Encoding resolves an applied range before planning: the UI Out timestamp is the last included decoded frame, so FFprobe supplies the following decoded presentation timestamp as the exclusive processing boundary. The resulting `ResolvedMediaRange` carries the source start timestamp, absolute filter boundaries, and authoritative effective duration. Job work estimates, per-file progress, overall weighted progress, ETA, logs, and results use that resolved duration.

#### Cancellation semantics

Cancellation has three guarantees:

1. Stop scheduling new items.
2. Signal active work cooperatively through `CancellationToken`.
3. Preserve completed, warned, skipped, and failed results while classifying unfinished items as cancelled.

The Encoding adapter also terminates an active FFmpeg process when the user requests immediate cancellation. Process termination is an adapter concern; the shared job model does not know FFmpeg or command-line syntax.

### Capability modules

Each capability owns typed options, planning rules, command requests, and typed result data. The shared framework does not use a generic property bag.

Encoding currently supplies:

- `EncodingJobOptions`
- `EncodingSource`
- `EncodingItemResult`
- `EncodingJobPlanner`

`EncodingJobPlanner` produces deterministic item ordering, output paths, collision errors, existing-output skip decisions, media-range validation, and work estimates before execution. The Encoding page consumes the plan while preserving LUT/No-LUT behavior, resolution and frame-rate filters, codec settings, salvage/video-only modes, logging, pause, close-after-current, and cancellation.

#### Encoding output lifecycle

The planner exposes only the intended final media path and remains side-effect free. Runtime execution creates an `EncodingOutputLifecycle` for each item. The lifecycle derives one exact, same-directory application-owned partial path by appending `.lightflow` to the complete final filename: for example, `clip.mp4.lightflow`, `clip.mov.lightflow`, or `clip.mkv.lightflow`.

Immediately before an item starts, runtime hygiene removes only that exact sibling partial path. A locked or otherwise undeletable stale partial blocks the item instead of allowing FFmpeg to overwrite or mingle with it. Lightflow does not scan arbitrary folders or delete files based on a generic substring. Broader startup cleanup is intentionally deferred because current execution is serial and item-scoped ownership is the safer boundary.

FFmpeg writes directly to the sibling partial in the final destination directory. Because `.lightflow` is deliberately the terminal extension, `FfmpegCommandBuilder` explicitly selects the output muxer from the typed `OutputContainer` (`mp4`, `mov`, or `matroska`) instead of relying on filename inference. FFprobe validates the partial artifact. Only a successful encode and successful validation permit finalization:

```text
planned final path
    -> exact sibling .lightflow path
    -> stale-partial hygiene
    -> FFmpeg with explicit muxer
    -> FFprobe validation of partial
    -> same-directory move or atomic replacement
    -> output identity + successful job result/history
```

For a new output, finalization is a same-directory filesystem move. When overwrite is enabled and a final output already exists, `File.Replace` atomically replaces it on the supported Windows filesystem; the old valid file is never deleted before the replacement is completely encoded and validated. A finalization failure leaves the item failed and the prior final intact. Cancellation, FFmpeg failure, or validation failure never promotes the partial. Cleanup is best-effort so a locked artifact remains visibly incomplete under its `.lightflow` name and is reported in the activity log.

Resume and existing-output decisions inspect only the planned final path. Successful output identity is saved only after finalization. Failed attempts clear identity only when no pre-existing final remains; identity for an untouched prior valid output is preserved. Job history continues to describe the planned final path, while diagnostics may identify a retained partial when cleanup fails.

The Tools-tab Inspect, Verify, Rewrap, Proxy, and Contact Sheet commands intentionally remain on their legacy one-off process path. They can migrate later by adding typed capability options/results and using the same planning and execution contracts. Issue #34 does not force unrelated tools through a premature abstraction.

### Media adapters

FFmpeg and FFprobe remain external-engine adapters:

- `FfmpegCommandBuilder` creates command arguments from typed Encoding choices.
- `FfmpegProgressParser` translates FFmpeg progress records.
- `MainWindow` currently hosts process execution and detailed logging.
- `DependencyHealthCheck` independently probes installed/bundled executables and encoders.

The job model never contains FFmpeg arguments. This boundary permits future jobs such as bulk rename, copy verification, metadata operations, image processing, and workflow coordination.

### Interactive video playback

Interactive playback is a separate application service, not a shared batch Job. The subsystem consists of Lightflow-owned contracts in `MediaPlayback.cs`, lifecycle and latest-request coordination in `MediaPlaybackService`, global ownership in `MediaPlaybackCoordinator`, and an internal `FlyleafPlaybackBackend`. Flyleaf types do not appear in application-facing playback state, timestamps, frames, errors, stream information, browser/trim contracts, or the shared Job model.

```text
Future browser or trim surface
        ↓
IMediaPlaybackService + MediaPlaybackView
        ↓
MediaPlaybackService (Lightflow state and cancellation)
        ↓
IMediaPlaybackBackend
        ↓
FlyleafPlaybackBackend
        ↓
Flyleaf 3.10.4 + FFmpeg 8.1 shared libraries + Direct3D 11/XAudio2
```

#### Ownership and source lifecycle

`MediaPlaybackCoordinator` owns one playback service and transfers an explicit lease between consumers. A new owner closes the previous source before receiving the service, preventing independent audible preview engines. The service assigns a lifetime token to each loaded source and a monotonically increasing operation generation to every source-specific action. Replacement or close invalidates the old source token immediately, before waiting for the serialized backend gate. Seek, play, pause, stepping, and extraction all participate in that gate and generation model. Only the current generation may publish state or frame notifications. Frame extraction suppresses its internal seek/restore frames and never restores position or playback after its source token is invalidated. Source replacement disposes the prior Flyleaf player, decoder contexts, audio voices, renderer attachment, event subscriptions, and pending cancellation state before publishing the new paused source.

Sources open asynchronously and settle on the first decoded frame while paused. Play, pause, seek, frame-step, close, failure recovery, and extraction remain off the WPF UI thread except for the small renderer/host operations that WPF requires on its dispatcher. Frame queues and decode buffering are owned and bounded by Flyleaf; Lightflow does not introduce another unbounded queue.

#### Timestamp semantics

`MediaPresentationTimestamp.Position` is the decoded/displayed frame timestamp exposed by Flyleaf in .NET ticks after Flyleaf converts FFmpeg's `best_effort_timestamp`. It is not an estimated frame number. `MediaPlaybackSourceInfo.StartTimestamp` records the stream start offset exposed by the backend. Consumers use the displayed timestamp for frame selection and later translate it with source-start metadata when constructing source ranges.

For VFR input, forward stepping advances through decoded frames and publishes each frame's actual timestamp. Flyleaf's built-in backward-step fallback converts time through nominal frame duration and is therefore deliberately not used. Lightflow reconstructs a backward step by seeking to an earlier point, decoding forward through actual presentation timestamps, and settling on the immediate predecessor. If an exact predecessor cannot be established, the operation fails rather than returning an estimated boundary. Integration tests generate genuine VFR media and compare forward, backward, seek, and extraction results with FFprobe frame timestamps.

Flyleaf accepts millisecond seek targets, so arbitrary seek requests are target approximations; the timestamp returned after seek is always the actual frame Flyleaf displayed, not the requested value. Future trim UI must store the returned displayed timestamp. Sources with missing timestamps may cause FFmpeg/Flyleaf to synthesize timestamps; such values are reported as decoded presentation timing but should be diagnosed before they are used as edit boundaries.

#### Seeking and frame extraction

Interactive seeks use latest-request-wins cancellation. Flyleaf also coalesces its internal paused seek stack. Seeking preserves whether Lightflow was paused or playing, and only the settled decoded timestamp is published. Backward stepping uses a bounded expanding search window and a bounded forward decode count.

`GetFrameAsync` returns `MediaDecodedFrame`, a Lightflow-owned BGRA pixel buffer with dimensions, stride, and actual displayed timestamp. It uses the same decoder and color-conversion path as playback. A small off-screen renderer is created only when extraction is requested without an attached preview surface; it is released with the backend. No thumbnail cache, filmstrip, or browser is implemented.

#### Video and audio behavior

Flyleaf owns the playback clock, synchronized XAudio2 output, decode queues, Direct3D 11 presentation, and resynchronization after seeking. It selects one deterministic default audio stream and Lightflow exposes all discovered streams plus the selected index for later Issue #38 work. Audio initialization failure is reported separately where possible and does not redefine video timestamps.

Hardware video decoding is requested automatically. Flyleaf falls back to software decoding when the source or device cannot use hardware acceleration; the application contract reports which path actually opened. Playback correctness does not depend on a GPU vendor. The current Flyleaf renderer supports HDR-to-SDR processing, but Issue #53 does not add display calibration, HDR output signaling, or user controls. Flyleaf currently outputs unusual multichannel audio layouts through its stereo XAudio2 output; advanced routing remains out of scope.

#### Packaging and licensing

Encoding continues to invoke the existing pinned LGPL FFmpeg and FFprobe command-line executables. Playback separately uses FlyleafLib 3.10.4, Flyleaf.FFmpeg.Bindings 8.1.0, and the pinned BtbN FFmpeg 8.1 `lgpl-shared` archive. The archive SHA-256, exact source revision, build-project URL, variant, and versions are recorded in `dependencies/ffmpeg-playback.json` and copied into release artifacts. GPL and nonfree FFmpeg variants are rejected by the dependency-preparation script.

Installer and portable staging place playback DLLs under `playback/ffmpeg/bin`, with the package manifest, corresponding-source/build links, and upstream license files alongside them. Managed package versions and their transitive graph are locked by NuGet lock files. `THIRD-PARTY-NOTICES.md` documents Flyleaf, its bindings, FFmpeg, Vortice, and SharpGen obligations.

#### Future consumers

Issue #54 can consume playback, actual displayed timestamps, and the Lightflow view without learning Flyleaf types. Issue #55 remains responsible for translating selected timestamps into FFmpeg's inclusive/exclusive range semantics. A future media browser can repeatedly transfer the one global playback lease and issue A → B → C load requests; only the latest generation is allowed to publish state, frames, or audio. Playback-speed state is not exposed in this UI iteration, but Flyleaf's clock and the backend boundary can add it without changing source, timestamp, or frame contracts.

### Per-file trim editing

The Encoding input row owns zero or one applied `MediaRange` through `BatchFileOption.TrimRange`. `TrimSelection` is a dialog-local draft initialized from that applied range. Set In, Set Out, and Reset modify only the draft; Apply converts the draft into a timestamp-backed `MediaRange`, while Cancel returns without mutating the row. Applying the complete source produces `null`, the canonical untrimmed state.

`TrimEditorWindow` acquires the single interactive playback session through `TrimEditorPlayback` and `MediaPlaybackCoordinator`, embeds the Lightflow-owned `MediaPlaybackView`, and releases its lease before closing. It loads paused and exposes ordinary play/pause, responsive seeking, previous/next decoded-frame stepping, and Space-key play/pause. In and Out are copied only from the settled `MediaPresentationTimestamp.Position` reported by playback. They remain normalized display-timeline timestamps; the separately reported source start is not added here.

Dialog completion is sequenced after playback cleanup: Apply preserves its requested successful result while the presentation and coordinator lease are released, then assigns `DialogResult`. This prevents WPF's cancellable close cycle from discarding a successful Apply before `MainWindow` commits the draft. Cancel follows the same cleanup path without mutating applied state.

`MediaPlaybackView` owns a disposable Lightflow presentation lease. Closing an editor clears the view, closes the player lease, and explicitly disposes that view's Flyleaf host. A later editor receives a fresh WPF/Direct3D presentation host attached to the same reusable backend service and global coordinator. The player/session architecture remains singular, while HWND-bound renderer state is never reused after its window closes.

Applied trims are stored in the versioned local JSON file `trim-history.json`, beside Lightflow's other local application data. `TrimHistoryStore` identifies a source by normalized full path, byte length, and UTC last-write ticks. All three values must match before silent restoration. Missing or malformed storage and malformed individual records are ignored. Writes use a same-directory temporary file followed by replacement. Records expire after 90 days without use; restore and reapply refresh `lastUsedUtc`, and normal store access quietly removes expired entries. A source identity is checked both before and after editing so a file changed while the dialog is open cannot inherit the draft.

The input row renders a thin neutral duration line and, for an active trim, a proportional orange segment derived from normalized In, Out, and known duration. The row action reads Trim or Edit Trim; a silently restored trim is indistinguishable from a manually applied trim. When timing is unavailable, the row does not invent proportions and the editor remains unavailable until usable timing can be established.

`BatchFileOption.TrimRange` flows into `EncodingSource` only after the persisted file identity is revalidated. FFprobe reads video packet presentation timestamps in narrow windows around In and Out; those timestamps are matched to the authoritative displayed timestamps and the packet following Out supplies the exclusive endpoint without nominal-FPS arithmetic. Real CFR/VFR fixtures verify packet PTS against decoded presentation timestamps. Unusual media with missing or unusable packet PTS falls back to a bounded decoded-frame timestamp probe. `MediaMetadata.StartTimestamp` maps normalized display positions onto the container timeline. Trimmed FFmpeg commands use an input `-ss` expressed in the normalized media timeline to seek near In efficiently, preserve the original container timestamps with `-copyts`, then apply decoded-stream `trim`/`atrim` filters using the absolute boundaries and reset output timestamps with `setpts`/`asetpts`. The seek is only an optimization—the decoded timestamp filters remain authoritative for exact VFR boundaries and aligned audio. Existing LUT, scaling, deinterlace, frame-rate, salvage, and video-only behavior remains in the same filter path. Audio copy is intentionally promoted to AAC encoding for trimmed files because packet-copy boundaries cannot guarantee alignment.

Successful outputs are FFprobe-validated for readable video, expected audio, and effective duration before the job reports completion. A persistent central cache under the application's local-data directory records source path/size/write time, trim timestamps, and an encoding-options hash in an entry keyed by normalized output path. When **Overwrite existing files** is off, every non-empty existing output is preserved; an identity mismatch is reported as a warning rather than silently replacing the file. When overwrite is on, the selected trim is processed normally and replaces the planned output. Legacy `.lightflow.json` files beside outputs are migrated into the cache and removed when encountered. Failed, cancelled, or invalid output removes both its cache entry and any legacy sidecar. Untrimmed Encoding remains command-compatible, and Tools-tab rewrap, proxy, verify, and contact-sheet operations remain full-source.

### Infrastructure

- Settings and transient application-state persistence
- Rotating activity logs
- File-system and executable discovery
- Dependency health checks
- Release packaging and integrity verification

### Configurable Catalog and Preview storage

Startup first loads the existing local `settings.json` from the stable application-data root, then constructs one `LightflowStorageCoordinator`. The coordinator owns the effective `ILightflowStorageLocations` and the active `CatalogDatabaseSession`; WPF receives that instance rather than capturing default paths during static initialization. Persisted configuration contains only selected Catalog/Previews directories and the expected Catalog ID. Database filename and backup paths remain derived contracts.

Catalog relocation is a protected SQLite operation: validate an empty local non-overlapping destination, quiesce the session/pool, use SQLite online backup into a staged database, validate schema and Catalog identity through `CatalogDatabaseService`, atomically save configuration, activate the validated session, and retain the complete original Catalog directory. Copy, validation, configuration, cancellation, or post-switch activation failure restores the original configuration/session; an unlikely rollback-write failure keeps the already validated destination active so disk configuration and memory cannot diverge. Preview relocation is separate and rebuildable, supporting staged move or switch/rebuild. A failed staged Preview move removes only the destination data owned by that attempt before allowing a retry; switch/rebuild never deletes the old cache. A missing, unavailable, or identity-mismatched configured Catalog never triggers fallback or replacement creation; it returns a nonfatal unavailable coordinator so Catalog-independent features remain usable. Preview availability is reported independently. Backup history and restore policy remain deferred to #83.

## Shared batch planning

Before execution, a mature capability should produce a plan containing:

- included inputs in deterministic order
- source metadata and optional media range
- proposed output paths
- collisions and validation errors
- skipped items
- warnings
- effective work estimates

The plan is side-effect free with respect to outputs: it does not create directories, write output files, or start an engine. It can later power richer preflight UI and persistent history.

## Output safety

Default rules:

- Never overwrite source media.
- Detect identical source and output paths.
- Require explicit overwrite choice.
- Detect output collisions before execution.
- Prefer deterministic suffixing.
- Use temporary outputs and atomic moves where a capability supports them.
- Remove incomplete temporary output after failure unless diagnostics require retention.

## Future boundaries

### Persistent history — Issue #35

History will serialize durable, capability-specific job definitions/plans/results. It should not serialize `JobExecution`, `JobCancellation`, delegates, controls, or process objects.

### Frame-accurate trimming — Issues #52–#55

Playback and trim UI remain separate features. Timestamp-backed `MediaRange` and effective-duration weighting are already represented. The future Encoding adapter will translate the selected range into accurate FFmpeg behavior and result metadata.

### Workflow pipelines — Issue #47

A pipeline step can produce an ordinary typed Lightflow job and consume its result. The current model is not a workflow graph, plugin framework, or pipeline engine, and does not claim step serialization/versioning yet.

## Testing strategy

### Unit tests

- Job and item construction
- Planning without execution
- Validation and collision handling
- State transitions and retry
- Weighted and indeterminate progress
- Cancellation and mixed terminal outcomes
- Result summaries
- Encoding path and command construction
- No-LUT regression behavior

### Integration tests

- FFmpeg/FFprobe invocation
- Representative media fixtures
- Cancellation and incomplete-output cleanup
- Corrupt media behavior
- Release packaging

### UI tests

Focus on high-value flows such as selecting inputs, reviewing a plan, starting/cancelling work, resolving validation errors, and keyboard navigation.

## Decisions still requiring ADRs

- MVVM framework or continued in-house patterns
- Dependency injection container
- Job history storage format and schema versioning
- RAW conversion engine
- Image processing library
- ExifTool packaging approach
- Plugin boundary and third-party extensibility
- Pipeline graph format and versioning
