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
