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

`MediaRange` separately represents source duration, optional In/Out timestamps, and effective duration. Current Encoding creates full-source ranges. A future 45-second selection from a 30-minute source can therefore be represented and weighted as 45 seconds without redesigning job progress. Frame-accurate trim command behavior remains deferred to Issues #52–#55.

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

### Infrastructure

- Settings and transient application-state persistence
- Rotating activity logs
- File-system and executable discovery
- Dependency health checks
- Release packaging and integrity verification

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
