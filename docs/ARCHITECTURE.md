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

`MainWindow` is also the permanent Lightflow application shell. `ShellWorkspace` gives Browser, Encoding, Media Tools, History, Premiere Helper, Settings, and About stable semantic identities while the content-only host preserves each existing workspace. Browser is index zero and the startup default; compact horizontal application navigation and workspace selection remain two-way synchronized. Features that intentionally return to Encoding use the semantic workspace contract rather than a numeric tab index. The application navigation occupies a restrained top header rather than a permanent side rail, preserving the Browser's left edge for Media Roots/folders and maximizing width for future media plus Inspector presentation. Upcoming Browser and Player slices extend this host instead of creating another long-lived top-level window.

The shell is intentionally dark-only. Shared neutral canvas, surface, panel, raised, divider, focus, and selection resources live in `Themes/LightflowShell.xaml`; existing reusable control styles remain application-scoped. Warm brand color is reserved for focus, primary action, small identity details, and status—not broad content surfaces—so photography and video can remain visually authoritative. Keyboard focus, redundant selected-state weight, minimum window dimensions, and resizable grid regions are part of the shell contract. See [Dark-only design system](DESIGN-SYSTEM.md).

Issue #107 adds Explorer-familiar, filesystem-first navigation without changing Catalog identity. `IBrowserStorageProvider` presents ready/unavailable local, removable, and mapped volumes alongside managed/pinned Media Roots; the editable current-path control also accepts eligible absolute local, mapped, and UNC/share paths. `IBrowserLocationResolver` converts every absolute folder transition into a logical `RootId` plus normalized relative folder before reconciliation. It reuses the most-specific existing containing mapping; otherwise it establishes one stable natural anchor at the volume root or UNC share root. Automatic anchor creation is idempotent and does not enumerate or index anything. Only the explicitly visited folder is passed to `IMediaDiscoveryRefreshService` and the #98 enumerator, so establishing an anchor never triggers recursive drive/share indexing.

Natural volume/share anchors may coexist with a more-specific managed library. Resolution always selects the most-specific containing root, preserving that library's stable asset identity within its boundary while the broader anchor serves other folders. Manual overlap protection remains for unrelated managed roots; natural anchors are the narrowly scoped exception needed for low-friction browsing and managed-root reconnect. Catalog assets continue to use stable `RootId + relative path`; WPF never writes Catalog rows directly.

`BrowserNavigationSession` retains accepted logical locations and history while the user visits other Lightflow workspaces. Back, Forward, Up, and Refresh operate on filesystem-familiar absolute locations but resolve through the logical-root boundary again when crossing folders. Refresh does not add history. A per-request generation and linked cancellation token make navigation latest-request-wins and suppress late results. Unavailable volumes/roots, bad paths, and provider-neutral enumeration failures remain non-destructive workspace states. Path containment and reparse-point rejection stay behind existing services.

The Browser's left navigation owns the complete folder hierarchy. Selecting or expanding a drive, share, managed library, or child folder loads that location through the same authoritative navigation pipeline. Accepted Back, Forward, Up, and direct-path transitions expand and select the corresponding hierarchy node, including dynamically presenting an eligible UNC share first reached through the path field. The central #107 surface filters enumeration results to supported files only; directories never appear as central rows. `BrowserTreeModel` keeps this synchronization and file filtering independent of WPF. Issue #107 deliberately does not add thumbnail presentation, media selection, sorting/filtering/search, Player/Viewer presentation, Inspector/Color, or Browser-to-Encoding handoff; Issues #108–#112 extend this workspace.

Materializing a folder's tree node requires its own real folder enumeration; `BrowserTreeModel` deliberately keeps that boundary (it holds no `IMediaFolderEnumerator` reference and performs no I/O). Ordinary click-driven expansion already gets this for free — `MainWindow`'s `Expanded` handler navigates into whatever node is expanded, so every ancestor along a manually-explored path is enumerated as the user reaches it. Direct-path entry and other programmatic navigation that jump straight to a deep, never-visited folder do not have that per-level opportunity: `BrowserTreeModel.EnsurePathChain` still needs to create identity nodes for every intermediate segment immediately (so the target is selectable and scrollable right away), but those intermediate nodes start as `IsMaterialized = false` structural stubs — each has only the single child leading toward the target, not that folder's real sibling list. `BrowserTreeNode.IsMaterialized` distinguishes a real listing (set by `BrowserTreeModel.ApplyDirectoryListing`, which backs the existing `ReplaceDirectories` reconciliation) from that structural stub. After every successful navigation, `MainWindow.RevealBrowserTreeAncestorsAsync` calls `BrowserTreeModel.GetUnmaterializedAncestors` and, for each returned node only, performs one `IMediaFolderEnumerator.EnumerateAsync` call (the same reusable #98 enumerator navigation already uses — no second enumeration path) and applies the result. Ancestors already materialized by ordinary navigation are skipped, so this is a no-op in the common case; it only does real work the first time a never-visited deep path is reached directly.

Issue #108 turns the central surface into a virtualized media thumbnail grid without introducing any parallel enumeration, Catalog, Preview, or filesystem-watching path. Lightflow's Browser is a media browser, not a general-purpose file browser: `BrowserGridModel.IsPresentable` reads the classification the existing #98 `IMediaTypeRegistry`-backed registry already assigned to each entry and admits only supported still image, RAW image, and video categories into the grid; folders, standalone audio, and unknown/unsupported files (documents, archives, executables, sidecars such as DJI's `.LRF`/`.SRT` companions, etc.) are excluded from presentation entirely rather than shown as a placeholder tile. This is a presentation-only filter over the existing classification — it does not maintain a second, WPF-owned extension list, and it does not change which entries become Catalog assets or which folder entries `BrowserTreeModel`/the current-path toolbar show.

`BrowserNavigationSession` already calls `IMediaDiscoveryRefreshService.RefreshAsync` at `DerivedWorkPriority.Visible` for every folder it loads; `BrowserFolderState` now also carries the `IDerivedWorkBatch` that call schedules, so the grid consumes the batch navigation already produced rather than resubmitting work. `BrowserGridModel` builds one `BrowserGridTile` per presentable entry in the existing deterministic enumeration order, matches each tile to its stable Catalog `AssetId` using `IDerivedWorkBatch.Reconciliation.Items` (no separate `IMediaAssetService` lookups), and applies a generated thumbnail's resolved absolute path for any asset the batch reports with a `Succeeded`, `Current`, or `NotNeeded` thumbnail-component outcome — `NotNeeded` is what a previously-cached, still-current thumbnail reports, since the scheduler skips calling the generator again rather than reporting `Current`, and the grid must still resolve that already-existing cached path rather than leave the tile on its placeholder — read once through `IPreviewStoreService.GetAsync`. Thumbnails update tiles in place; `BrowserGridModel.Populate` reuses existing tile instances by stable `RelativePathKey` across a non-destructive refresh so already-resolved thumbnails and selection survive it. RAW assets are presentable but currently never receive a generated thumbnail (and permanently show their category glyph instead), since thumbnail generation remains scoped to `image`/`video` `MediaAsset.MediaType` values; a supported asset that later becomes missing/offline/corrupt keeps whatever representation existing Catalog/Preview durability behavior already gives it — Issue #108 does not add new handling for that case.

The grid is virtualized by grouping tiles into fixed-width rows (`BrowserGridLayout`) and letting a native `VirtualizingStackPanel` recycle only the realized rows; each row's own tile `ItemsControl` is a non-wrapping horizontal `StackPanel`, deliberately not a `WrapPanel` — a row already contains exactly as many tiles as `BrowserGridLayout.ComputeColumns` decided fit, and a panel that can independently re-wrap that content risks disagreeing with that count and producing a misaligned row. `ComputeColumns` sizes every tile's footprint as `TileWidth + TileSpacing`, matching that every tile's trailing WPF margin (including the last tile in a row) is real, counted layout space, not spacing that only exists between tiles. `MainWindow` recomputes the column count from the media canvas's available width on layout changes, so both application resizing and dragging the Locations splitter reflow the grid without introducing horizontal scrolling. Selection (`BrowserGridSelection`) is keyed by stable `RelativePathKey` rather than visual containers, so it survives recycling and refresh; single-click, Ctrl-toggle, Shift-range, Select All, and clear-on-empty-canvas are implemented directly against tile indices rather than relying on a `Selector`'s built-in multi-select model, since tiles are nested two levels below the virtualized row list.

`IMediaRootMonitoringService` gained a `FolderRefreshed` event, raised with the same `(RootId, RelativeFolder)` key it already computes internally when its debounced authoritative refresh completes. `MainWindow` subscribes and re-runs the existing `BrowserNavigationSession.RefreshAsync` path when the changed folder matches the one currently open, so external filesystem changes reach an open Browser view without any Browser-owned `FileSystemWatcher`; explicit Refresh remains the authoritative recovery path regardless. Issue #108 deliberately does not add sorting/filtering/search, Player/Viewer, filmstrip navigation, Inspector, Color/LUT controls, Collections, ratings/tags, Compare, or a user-facing thumbnail-size control; Issues #109–#112 extend this workspace.

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

Self-contained packaging runs the published executable in a non-UI `--verify-catalog-runtime` mode. That smoke check creates, closes, reopens, and removes an isolated temporary Catalog and also persists/reopens a Preview record through the production services, proving that the locked managed provider and embedded native `e_sqlite3` library load from the actual packaged executable rather than merely compiling on the build machine.

Schema version 1 contains only `CatalogInfo`, append-only `SchemaMigrations`, `MediaRoots`, `MediaRootMappings`, and `MediaAssets`. Stable GUID strings are relational identity. Assets use a logical RootId plus a forward-slash relative path and a separately normalized lookup key; absolute filesystem mappings live only in the machine/root mapping table. Size, UTC last-write ticks, optional versioned source fingerprint, source/root availability status, and fixed UTC ISO-8601 timestamps establish the durable patterns consumed by #81 and #82. Foreign keys, root/path and root/machine uniqueness, status/path checks, and expected lookup indexes are database-enforced. Ratings, labels, collections, derived metadata, and Preview data are intentionally absent.

`PRAGMA user_version` is authoritative. Migrations are contiguous, ordered, forward-only, and run one version per explicit transaction; successful versions append a diagnostic ledger row inside the same transaction. Version 1 establishes both the SQLite header application ID and the `CatalogInfo` identity row in its transaction. New databases traverse the version-zero migration chain. Existing older Catalogs first receive a full integrity check and must pass the provider-neutral `ICatalogMigrationBackup` seam before any migration starts. `SqliteCatalogRecoveryService` now fills that seam with a validated SQLite online backup; a failed backup blocks migration. Ordinary open validates SQLite readability, the header-level Lightflow application ID, supported schema, migration ledger, Catalog identity row, and `quick_check` through a read-only preflight before applying mutable runtime policy. Non-SQLite input is classified as unreadable, while a failed SQLite integrity check is classified as corruption; both preserve the original file and never trigger replacement creation.

#79 owns location persistence, destination validation, relocation, atomic configuration switching, and storage UI. #81 owns Media Root behavior and relative-path normalization APIs. #82 owns Asset repositories, fingerprint calculation, and source-observation semantics. #83 adds the recovery lifecycle described below.

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

The Inno Setup distribution is always a per-machine installation under `{autopf}\Lightflow Studio` and therefore requests standard UAC elevation. Its stable AppId preserves normal per-machine upgrade/reinstall behavior. Before installation, it detects the older same-AppId registration in the current user's uninstall registry and invokes that legacy per-user uninstaller; failure is explicit rather than leaving an ambiguous duplicate registration. Installer/uninstaller operations own only packaged application files and shortcuts. Lightflow's Catalog, Previews, settings, histories, caches, and logs continue to resolve through `LightflowStorageLocations` outside Program Files and are never installer uninstall targets. The portable ZIP uses the same validated staging tree without installer registration.

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

Catalog relocation is a protected SQLite operation: validate an empty local non-overlapping destination, quiesce the session/pool, use SQLite online backup into a staged database, validate schema and Catalog identity through `CatalogDatabaseService`, atomically save configuration, activate the validated session, and retain the complete original Catalog directory. Copy, validation, configuration, cancellation, or post-switch activation failure restores the original configuration/session; an unlikely rollback-write failure keeps the already validated destination active so disk configuration and memory cannot diverge. Preview relocation is separate and rebuildable, supporting staged move or switch/rebuild. A failed staged Preview move removes only the destination data owned by that attempt before allowing a retry; switch/rebuild never deletes the old cache. A missing, unavailable, or identity-mismatched configured Catalog never triggers fallback or replacement creation; it returns a nonfatal unavailable coordinator so Catalog-independent features remain usable. Preview availability is reported independently.

### Catalog backup, integrity, and recovery

`ICatalogRecoveryService` is the provider-neutral application boundary; `SqliteCatalogRecoveryService` contains SQLite connections, online backup, full integrity checks, identity/schema validation, retention, and file finalization. After a successful Catalog open, Lightflow ensures at most one validated automatic backup for that UTC day. Existing-Catalog migration calls the same service and cannot begin unless its dedicated validated backup succeeds. Manual backups use the same staged-online-backup path. Backups are written beside the custom or default Catalog in `Catalog/Backups`, staged under an application-owned temporary name, fully validated, then renamed to `LightflowCatalog-v{schema}-{timestamp}.db`; a small adjacent metadata document records the backup reason. Retention recognizes only that exact database convention and keeps the union of the newest 10 daily backups and 3 monthly anchors; unrelated files are never cleanup candidates.

The storage coordinator exclusively owns restore lifecycle and session quiescing. It validates the selected backup and Catalog identity before mutation, creates a validated recovery backup of an available current Catalog, disposes the session/clears SQLite pools, stages the selected database through SQLite online backup, and preserves the displaced live files. Filesystem installation is provisional: the replacement must pass the normal Lightflow open, migration, identity, and session-activation lifecycle before the restore transaction commits and removes displaced files. Post-install failure preserves the rejected replacement, rolls back the displaced Catalog, and reopens/reactivates it; rollback failure diagnostics identify every retained artifact rather than deleting evidence or creating an empty Catalog. A corrupt/unreadable startup remains nonfatal for Encoding and exposes existing backups in the minimal Settings recovery UI. Restore never reads, copies, deletes, or relocates Previews.

### Logical Media Roots and machine mappings

A Media Root is a durable Catalog identity (`RootId` plus a user-facing name), not a physical folder. `MediaRootMappings` connects that identity to one absolute path for the current Lightflow machine identity. The machine identity is a locally persisted random GUID; it contains no computer name, account name, hardware identifier, or network address. Only an absent identity file grants authority to atomically create one. An existing malformed or unreadable identity fails Media Root operations explicitly and is never replaced, preserving both its diagnostic evidence and Catalog mappings. Moving or reconnecting a root changes only that machine's mapping and never rewrites the root or its assets. Another computer opening the same Catalog sees the logical root as unmapped until it supplies its own path.

Asset identity is `(RootId, RelativePathKey)`. Display paths are normalized to forward-slash relative form while preserving user-visible casing. Keys use invariant uppercase comparison to model Windows case-insensitive identity. Rooted, drive-qualified, UNC-relative, empty, and parent-traversal values are rejected. Resolution combines the mapped root with the normalized relative path and verifies segment-aware containment before touching the filesystem.

The Lightflow-owned `IMediaRootService` isolates UI and future discovery code from SQLite. Root creation and remapping probe the selected folder off the UI thread before a short transaction. Equivalent, ancestor, and descendant mappings on the same machine are rejected using normalized segment-aware Windows path comparisons; similarly named sibling folders remain valid. Catalog schema v1 already contains the required root/mapping split, so Issue #81 adds no migration.

Availability is an observation, not destructive Catalog state. A missing mapping is **Unmapped**; a mapped folder that cannot currently be reached is **Unavailable**; an accessible mapping is **Online**. A missing child beneath an online root remains distinct from an offline root. Failed probes do not delete mappings, assets, metadata, or prior Catalog state. Media Root mappings may point to removable, mapped-drive, or UNC storage, with normal network latency and availability caveats; this does not relax the separate rule that the live SQLite Catalog itself is local-only in v1.

Settings provides only root listing, naming, add, and reconnect operations. Asset discovery/import, metadata probing, browser presentation, duplicate handling, and preview generation remain deferred to later Catalog work.

### Stable Catalog assets and source fingerprints

`AssetId` is a random, durable Lightflow identity and the target for future user-authored Catalog relationships. `(RootId, RelativePathKey)` is the asset's unique current logical location, not immutable byte identity. Asset creation normalizes the relative path through the #81 path contract, resolves it through the current machine's root mapping, observes an existing source, and transactionally inserts the new identity. Reopening the Catalog, updating observed facts, or remapping the root never regenerates `AssetId`.

The Lightflow-owned `IMediaAssetService` and `IMediaAssetRepository` keep SQLite out of feature callers. The service supports create, ID lookup, logical-location lookup, current resolution, and explicit observation. Observation updates file size, UTC last-write ticks, fingerprint, last-seen time, and source status while retaining identity. A missing child under an online root is persisted as **Missing**. An unavailable or unmapped root is returned as a separate runtime condition and does not mass-change asset state.

Source fingerprint version 1 is SHA-256 over a domain/version marker, file length, and bounded 64 KiB head/tail samples. Small files may be read completely; large media is never full-file hashed. This cheaply detects many common mutations and supplies layered evidence for future relocation/reconciliation, but it is not proof of byte-for-byte equality and changes confined to an unsampled middle region can retain the same fingerprint. Size and last-write time remain independent evidence. Observations compare filesystem facts before and after sampling and retry once rather than persisting a mixed state from a concurrently changing source.

Issue #82 does not add discovery scanning, derived media metadata, thumbnails, browser UI, or relocation/reconciliation. Those remain later Catalog capabilities.

### Media classification and folder enumeration

`IMediaTypeRegistry` is the centralized Lightflow-owned classification boundary for discovery. The default registry is an ordered classifier chain whose initial provider recognizes ordinary still images, camera RAW images, video, audio, and unknown/unsupported files by case-insensitive extension. Callers receive Lightflow-owned categories and format keys rather than extension lists. The classification context also carries optional declared-content-type and header evidence, and additional classifiers can precede the extension provider, allowing future decisions to use stronger evidence without changing discovery/browser contracts.

`IMediaFolderEnumerator` provides reusable non-recursive, asynchronous folder listing independent of WPF, Catalog mutation, and Preview work. A request uses a logical `RootId` and optional normalized relative folder. The service resolves the current machine mapping through `IMediaRootService`, applies #81's traversal and segment-aware containment rules, and returns directories plus files with logical relative paths, lookup keys, inexpensive filesystem size/write-time facts, and centralized media classification. Enumeration performs one bounded background filesystem operation—never one task per file—and checks cancellation while materializing and normalizing entries. Results are deterministic: directories first, then case-insensitive name and ordinal relative-path tie-breaking.

The provider-neutral result distinguishes a missing root, unavailable/unmapped root, missing child folder, access denial, invalid logical path, transient I/O failure, rejected filesystem-link path, and invalid out-of-root provider output. Unavailable roots are rejected before filesystem enumeration and are never interpreted as deleted assets. For the initial discovery contract, the filesystem provider checks every requested folder component beneath the mapped root before and after listing; a reparse point, junction, or symbolic link rejects that request. Reparse-point child files and directories are conservatively skipped with a diagnostic rather than followed or exposed as ordinary media. Explicit link traversal remains deferred. Enumeration performs no fingerprinting, metadata probing, thumbnail generation, Catalog reconciliation, or monitoring; Issues #99–#101 own those later stages.

### Explicit Catalog reconciliation

`ICatalogReconciliationService` is the authoritative explicit Refresh boundary between #98 folder enumeration and durable Catalog assets. Its provider-neutral result reports new, unchanged, changed, and missing assets with stable `AssetId` values, unsupported-file counts, and actionable root/folder/failure/cancellation status. The initial operation is deliberately non-recursive and reconciles only direct media-file children of the requested logical folder. Directories and unknown/unsupported files remain visible to enumeration but do not become Catalog assets.

Reconciliation must receive a complete successful enumeration before it infers absence. New supported files are created through `IMediaAssetService`; existing files are re-observed using the versioned bounded fingerprint path from #82. An available source whose size, write time, fingerprint version, and fingerprint value still match is unchanged; any changed evidence or recovery from Missing is changed. Changes at an existing `(RootId, RelativePathKey)` retain the original `AssetId`. Missing candidates are scoped to that exact folder and committed through one repository transaction only after every enumerated supported source has reconciled successfully. Cancellation rolls that missing transaction back. Earlier successful new/change observations may remain after a later failure, but an unavailable or unmapped root, enumeration error, operation failure, or cancellation can never turn unseen assets into missing assets.

Reconciliation itself performs no move/rename candidate matching, metadata or thumbnail generation, filesystem watching, recursive indexing, or Browser presentation. The discovery composition below owns asynchronous scheduling; Issues #76, #101, and later Browser work retain the other capabilities.

### Asynchronous derived work from discovery

`IMediaDiscoveryRefreshService` composes the authoritative reconciliation boundary with rebuildable Preview work without delaying Catalog visibility. `RefreshAsync` awaits and returns the #99 reconciliation result, then submits its successful new/changed/unchanged asset set to `IDerivedWorkScheduler`; the returned derived-work batch continues independently under its own cancellation token. Failed, canceled, unavailable, or invalid reconciliation never schedules derived work, and a missing/unavailable Preview store leaves the completed Catalog result usable with a provider-neutral diagnostic.

Scheduler acceptance is explicit and disposal-tolerant. Queue acceptance and the disposed-state check share the scheduler lock; if Catalog/Preview relocation, restore, or shutdown disposes the selected scheduler between reconciliation and submission, Refresh still returns the successful Catalog result with no derived batch and a lifecycle diagnostic. It does not retry against a replacement scheduler automatically; a later explicit Refresh re-evaluates still-needed rebuildable work.

The scheduler is deliberately specific to discovery rather than a general job framework. A fixed worker pool drains three priority queues (**Visible**, **Normal**, **Background**) and invokes the existing #91 metadata and #92 thumbnail services. Repeated refreshes share one queued/in-flight work item per `AssetId`; a higher-priority duplicate promotes queued work, while each submitting batch receives its own progress/completion view. Progress reports pending, running, generated, current/reused, failed, skipped-unavailable, and canceled counts without depending on WPF.

Workers compare the current Catalog source identity with the Preview record and generator versions/states. Current matching components are reused; missing, stale, failed, version-mismatched, or source-mismatched components are regenerated. Thumbnail scheduling is limited to the ordinary image/video types supported by #92, while metadata retains #91's broader media handling. Missing or offline sources are skipped without clearing cached Preview data. Metadata and thumbnail outcomes are isolated per component and asset: one failure cannot fail reconciliation, suppress the other component, or block unrelated assets, and a later refresh can retry failed rebuildable work.

The storage coordinator owns the active scheduler and its generator instances. Preview operations reuse #93's coordination boundary. Catalog restore/relocation and Preview relocation first cancel and drain the old scheduler before replacing a database/session/store, then bind a new scheduler to the active storage. No derived work mutates Catalog identity or user-authored Catalog data. Browser presentation and new probing/rendering engines remain out of scope.

### Filesystem change monitoring

`IMediaRootMonitoringService` is a responsiveness layer over the authoritative discovery pipeline, never a Catalog source of truth. The storage coordinator starts one monitor for mapped, online Media Roots. Its provider wraps `FileSystemWatcher` behind Lightflow-owned contracts; root remapping, temporary unavailability/reconnection, and shutdown dispose and recreate watcher registrations without exposing provider types to discovery or UI code. Explicit calls to `IMediaDiscoveryRefreshService` remain fully functional when monitoring is stopped or degraded.

Create, change, and delete notifications are normalized to their containing logical folder; rename notifications refresh both old and new containing folders. A bounded pending-key set debounces and coalesces repeated hints before calling the existing #99/#100 refresh composition at Background priority. Watcher errors, buffer overflow, ambiguous/out-of-root paths, and rejected reparse-point paths request a conservative root-level authoritative refresh rather than inferring identity or directly changing Catalog rows. A fixed single refresh worker prevents notification storms from creating unbounded concurrent reconciliation or duplicate derived work; #100 provides downstream per-asset deduplication.

Monitoring periodically resynchronizes its registrations with current Media Root mappings. A known root returning online or changing its physical mapping receives a root fallback refresh, while an initially configured root does not trigger an unsolicited startup scan. Unavailable/unmapped roots are not watched and never cause missing inference. The #98 enumerator revalidates containment and rejects filesystem links again during every authoritative refresh, so watcher paths cannot weaken the Media Root boundary. Move/rename identity matching remains deferred to #76.

### Rebuildable Preview persistence and cache

The configured `PreviewsDirectory` owns a completely rebuildable store: `previews.db`, a partitioned `thumbnails` tree, and a partitioned `previews` tree for future larger representations. `LightflowStorageLocations` derives these paths independently from the Catalog. The Catalog never contains Preview tables or generated-image BLOBs, and Preview storage contains no user-authored knowledge. Deleting the entire Preview directory therefore loses only derivable data and cannot mutate Catalog identity, assets, roots, or organization.

`IPreviewStoreService` is the Lightflow-owned provider boundary. Its SQLite implementation uses a separate application ID and schema version, per-operation non-pooled connections, local WAL (DELETE journaling for explicit UNC locations), `synchronous=NORMAL`, and a bounded busy timeout. This throughput-oriented policy is deliberately separate from the precious Catalog's `synchronous=FULL` durability. Existing databases are validated read-only before any schema or identity mutation; a foreign or unsupported database is rejected with rebuild guidance rather than adopted. Preview relocation quiesces and checkpoints the service before #79 copies or switches the store, then binds a new service to the selected location.

Schema version 1 keys one `PreviewRecords` row to the stable Catalog `AssetId` without a cross-database foreign key. It records the observed source size, UTC write ticks, fingerprint and fingerprint version; source availability; independent metadata-probe, thumbnail-generator, and standard-preview-generator versions and states; optional normalized/raw metadata JSON; generated relative paths; and UTC creation/update timestamps. Re-observing changed source identity marks derived components stale without changing or deleting the record. Marking a source missing or unavailable changes only its availability observation, so an offline Media Root never destroys retained Preview state.

Generated artifact paths are deterministic from `AssetId`, representation class, generator version, and a hashed versioned source identity. They use two directory partitions from the first four normalized AssetId characters, for example `thumbnails/00/11/...`, avoiding millions of siblings in one directory. Paths remain under the Preview root and stored artifact references must be normalized relative paths. Issue #90 establishes identities and persistence; #91 owns probing/normalization; #92 owns thumbnail generation; and #93 owns quota, pruning, clear/rebuild, usage reporting, and maintenance UX.

### Derived media metadata

`IDerivedMediaMetadataService` is the reusable, UI-independent entry point for technical metadata. It accepts a stable Catalog `AssetId`, resolves and observes the source through the existing Media Asset and Media Root services, converts that observation into the #90 Preview source identity, and writes only to the rebuildable Preview store. Catalog rows retain logical identity and inexpensive source observations; normalized technical metadata and raw provider snapshots never enter the precious Catalog.

Video and audio use the already distributed FFprobe command-line dependency behind `IMediaMetadataProbe` and `IProbeProcessRunner`. FFprobe JSON is normalized into Lightflow-owned container, primary video, and primary audio models while the complete JSON remains available as a reconstructable raw snapshot. Ordinary JPEG, PNG, TIFF, BMP, GIF, WDP, and JXR images use Windows Imaging Component behind `IImageMetadataReader`; normalized fields include dimensions, pixel depth, orientation, camera make/model, lens model, and capture-time text when present. RAW-specific libraries and decode support remain deferred.

Probe version 1 is stored in `PreviewRecords.MetadataProbeVersion`. A current matching source identity and probe version is reused without invoking a provider. A source-identity change is marked stale by the Preview store and a generator-version mismatch triggers reprobe. Work is asynchronous, cancellable, off the UI thread, and bounded by a service semaphore (two concurrent probes by default). Process cancellation terminates the FFprobe process tree so obsolete work does not continue consuming resources.

Missing sources and unavailable Media Roots update only Preview source availability and retain prior normalized/raw metadata. Unsupported, malformed, and temporary provider failures produce provider-neutral status and diagnostics; an attempted refresh marks the metadata component failed without erasing a previously successful payload. There is no scanning or automatic scheduling in #91—future discovery/browser work chooses which stable assets to probe. Thumbnail generation (#92), cache maintenance (#93), discovery scanning (#71), and Inspector presentation (#73) remain separate concerns.

### Persistent thumbnail generation

`IThumbnailGenerationService` is the reusable, UI-independent thumbnail entry point. Requests use stable Catalog `AssetId` values and resolve sources through the existing Media Asset/Media Root boundary. The service reuses a valid Current thumbnail when its source identity and generator version match, and otherwise publishes generator-version-1 JPEGs into #90's deterministic partitioned cache. Catalog records and files are never modified by thumbnail generation.

Ordinary Windows-supported images are decoded off the UI thread with Windows Imaging Component. EXIF orientation is applied before proportional downscaling. Video frames are decoded by the already packaged FFmpeg command-line dependency at a representative timestamp derived from #91 duration metadata (ten percent of duration, bounded to 1–30 seconds, with short/unknown-duration fallbacks). This intentionally does not acquire #53's globally owned interactive playback session: background thumbnail work therefore cannot replace a user's active preview source or audio session, while both paths continue using Lightflow-owned contracts and self-contained media dependencies.

Generation is cancellable and bounded to two concurrent operations by default. A priority-aware queue admits visible-item requests ahead of queued normal/background work without coupling generation to a Browser UI. Output is written to a unique `.lightflow` file beside its intended cache path, decoded once to validate it, and moved over the deterministic final path only after success. Cancellation, renderer failure, or invalid output removes the operation-owned temporary file and never publishes it as Current.

Immediately before publication the service re-observes size, last-write time, fingerprint version, and fingerprint value using #82. A changed source marks existing Preview state stale and discards the obsolete generated result; missing or offline sources retain any prior cached thumbnail and availability state. Preview maintenance is defined below; discovery and Browser presentation remain #71 and later UI work; RAW-specific decoding remains deferred.

### Preview maintenance and retention

`IPreviewMaintenanceService` owns usage reporting, cleanup, Clear, and Rebuild behavior for the independently configured Preview location. Usage includes the Preview database/WAL files, generated thumbnail and standard-preview trees, operation-owned temporary files, record/file counts, and unreferenced files. None of these operations write to the Catalog or source media.

The default cache quota is 20 GiB and is configurable from Settings (1–1024 GiB). Automatic cleanup removes unreferenced generated files only after a 24-hour safety window, then removes stale/failed artifacts older than 30 days when their source is available. If the cache still exceeds quota, the oldest available-source current artifacts are removed until the target is met. Missing and unavailable sources are excluded from automatic stale/quota pruning, so temporary root outages retain useful cached data; cleanup reports when protected offline data keeps usage above quota.

`PreviewOperationCoordinator` provides shared leases for metadata/thumbnail work and an exclusive lease for destructive maintenance. Writer admission closes a turnstile before waiting for active generation, preventing cleanup starvation and ensuring cleanup cannot classify a just-published file as orphaned. Preview relocation and application shutdown also enter this exclusive lifecycle. Generated artifacts are removed only from the Lightflow-owned `thumbnails` and `previews` trees.

Clear is explicit and confirmed in Settings. It atomically moves the two cache trees into an operation-owned staging directory, transactionally clears rebuildable Preview records, restores the moved trees if the database reset fails, and deletes committed staging data best-effort. Rebuild performs Clear, enumerates stable Catalog assets through the provider-neutral Media Asset service, then invokes the existing #91 metadata and #92 thumbnail services with progress and cancellation. Cancellation leaves completed regenerated entries valid and the store retryable. Browser/discovery scheduling remains outside #93.

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
