# Architecture

## Current platform

- Native Windows desktop application
- C# / .NET 8
- WPF user interface
- FFmpeg and FFprobe as bundled processing dependencies
- Installer and portable release packaging
- Local JSON settings and state
- Rotating local activity logs

## Architectural direction

Lightflow Studio should evolve from a video-focused application into a capability-based
media utility while preserving one consistent processing experience.

## Proposed layers

### 1. Presentation

WPF views, view models, navigation, dialogs, validation messages, and accessibility behavior.

Responsibilities:

- Display tool-specific options
- Present batch plans
- Surface job progress and results
- Never directly construct command lines
- Never perform blocking media work on the UI thread

### 2. Application services

Coordinates use cases and connects views to domain services.

Examples:

- Create a batch plan
- Validate selected inputs
- Resolve output paths
- Start, pause, cancel, or resume jobs
- Save and load presets
- Produce completion summaries

### 3. Job framework

A shared execution model for all capabilities.

Core concepts:

- `JobDefinition`
- `JobItem`
- `JobPlan`
- `JobExecution`
- `JobResult`
- `JobProgress`
- `JobCancellation`
- `JobHistoryEntry`

Each tool supplies typed settings and an executor, but shares:

- Queue behavior
- Progress reporting
- Cancellation
- Retry
- Skip/resume
- Logging
- Result summaries
- Output collision handling

### 4. Capability modules

Each major product area owns its UI, settings, validators, and executors.

Proposed modules:

- Video
- Images
- Files
- Metadata
- Integrity
- Workflow
- Publishing
- AI

A capability module should depend on shared abstractions, not on another module's UI.

### 5. Media adapters

Wrappers around external engines and native libraries.

Initial adapters:

- FFmpeg
- FFprobe
- ExifTool
- RAW conversion engine to be selected
- Image encoding library to be selected
- Hashing and file-system services

Adapters should:

- Build commands from typed requests
- Capture structured progress
- Preserve raw output in logs
- Translate known errors
- Report engine versions and capabilities

### 6. Infrastructure

- Settings persistence
- State persistence
- Logging
- File-system access
- Process execution
- Dependency discovery
- Update and migration support
- Release packaging

## Shared batch planning

Before execution, every operation should produce a plan containing:

- Included and excluded inputs
- Source type and detected metadata
- Proposed output path
- Collision decision
- Estimated work where possible
- Warnings and validation failures
- Whether an operation is reversible

This plan powers the preflight screen and later becomes part of job history.

## Output safety

Default rules:

- Never overwrite source media
- Detect identical source and output paths
- Require explicit confirmation for overwrite mode
- Prefer deterministic suffixing when collisions occur
- Use temporary output files and atomic moves where possible
- Delete incomplete temporary output after a failed job unless diagnostics require retention

## Pipeline compatibility

Every mature capability should eventually expose one or more pipeline steps.

A pipeline step requires:

- Typed input contract
- Typed output contract
- Settings schema
- Validation
- Executor
- Progress reporting
- Failure behavior
- Serialization format with versioning

## Dependency packaging

External binaries must be:

- Version pinned
- Integrity verified
- License documented
- Discoverable through settings for development builds
- Included predictably in installer and portable packages

## Testing strategy

### Unit tests

- Naming templates
- Destination resolution
- Validation rules
- Settings migration
- Job state transitions
- Command construction
- Collision handling

### Integration tests

- FFmpeg/FFprobe invocation
- Representative media fixtures
- Cancellation and cleanup
- Corrupt media behavior
- Settings persistence
- Pipeline serialization

### UI tests

Focus on high-value flows:

- Selecting inputs
- Reviewing a plan
- Starting and cancelling work
- Resolving validation errors
- Restoring presets
- Navigating with keyboard only

## Decisions still requiring ADRs

- MVVM framework or continued in-house patterns
- Dependency injection container
- Job persistence storage format
- RAW conversion engine
- Image processing library
- ExifTool packaging approach
- Plugin boundary and whether third-party plugins will ever be supported
- Pipeline graph format and versioning
