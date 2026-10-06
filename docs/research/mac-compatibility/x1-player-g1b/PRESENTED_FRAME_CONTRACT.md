# G1b presented-frame contract

This is a task-local executable contract, not a production API. Authority is the current [Player semantic contract](../x1-player/PLAYER_SEMANTIC_CONTRACT.md), checked against main `7b7f1c2cac5426f7df145921d8890adde6f9c490`. Existing action/domain responsibilities remain above the backend.

## Implemented authority

`Authority` owns a cloned decoded AVFrame, its exact raw `int64 pts` and rational timebase, decode sequence, presentation generation and monotonically increasing serial, immutable effective clockwise quarter-turn/Camera/Creative/Compare state, and a strong Metal output texture reference. The emitted `surface_generation` is the pool allocation epoch, not a stable per-slot generation; a future interop adapter needs the latter. The task has one source per process; JSON additionally binds the source path, LUT table hashes, IOSurface identity/pool allocation epoch, capture hash/serial and acknowledgement to that same record. This does not implement production AssetId/session replacement or a concurrent shared Player service.

The renderer reserves one of three IOSurface slots only when no frame lease owns it. A producer queue renders the decoded frame, signals `ready(serial)`, and a consumer queue waits before sampling the imported BGRA8 texture into a CAMetalDrawable. The consumer signals `released(serial)` after encoding its work. The serialized experiment waits for completion as well; it is deliberately not an optimized asynchronous queue.

A record becomes `retained` only after its drawable callback returns a **nonzero** `presentedTime` and its generation still matches. Decode completion, producer GPU completion, consumer GPU completion and callback invocation with timestamp zero are insufficient. Pending/stale work cannot publish a new serial or report a successful capture. Generation cancellation is injected after producer completion and before display submission. It preserves the prior retained record; this is not interruption of an in-progress decoder seek.

## What acknowledgement means

The native callback is evidence that Core Animation reports the drawable presented. It is not a sensor measurement of panel scanout, a guarantee of window visibility, or speaker/pixel synchronization. First draws, occlusion/lock and rehosting can yield zero timestamps or missing callbacks. Those outcomes are preserved, not scored as success. A timeout must leave presentation uncertain; retries are not universally successful in this probe.

The current harness exposes the last confirmed record while waiting. A future adapter must separately expose `Pending`, `LastConfirmed`, `VisiblePresented`, `Unavailable` and `Failed` as needed: **invalidate current visible authority when the host/session/generation is replaced or visibility is lost**. It must not claim that an old acknowledged drawable is showing in a new host. This lifecycle invalidation is not fully implemented here.

The Avalonia surface-update Task is a snapshot/lifetime acknowledgement, not a drawable presentation acknowledgement. It cannot substitute for this native callback. The final UI-compositor-to-frame serial acknowledgement remains an open integration requirement.

## Capture and authoring

The tested capture reads the full-resolution transformed backend surface associated with the candidate record, then records successful `capture_serial` only when that record receives a valid native acknowledgement. Software rotation/Color variants pass independent CPU comparisons; hardware GPU pixels are compared separately, with presentation results explicitly reported. Capture does not seek or modify source PTS. A held acknowledged texture remains pixel-identical across six later draws.

A production asynchronous capture must acquire the acknowledged record and its lease first, wait for readiness, read that exact texture, and return its Asset/session/generation/serial/raw PTS/effective state together with pixels. Concurrent pending-capture cancellation and source replacement are not qualified by this serial experiment. Capture here excludes UI overlays and viewport transforms.

The authoring adapter example takes In, marker, Subclip In and inclusive Out from the retained record; exclusive Out is the actual next decoded PTS. It does not run production authoring actions. Shared actions retain ranges, marker IDs, traversal/cadence/loop policy, Color assignments, source-plus-authored rotation policy, zoom/pan, fullscreen intent and Catalog persistence. Native code supplies decoded frames, exact predecessors, audio clock/output, GPU surfaces, fences and lifecycle errors.

## Required future record

For an X3 fixture, publish an immutable versioned **FrameOffer**, carrying AssetId, session id, source stream identity and `startPts`; generation/serial; exact PTS/timebase/decode identity; IOSurface lease and surface generation; dimensions/BGRA8/top-left/color description; effective rotation and immutable LUT content hashes/Compare state; ready event/value, release event/value and Metal device identity. Publish a separate acknowledgement tied to the same serial and UI-compositor generation. Capture leases the corresponding record rather than a mutable “current texture.”

Raw PTS must remain intact. Source-relative time is `(pts - startPts) * timebase`; conversion to the existing 100 ns domain must use explicit integer rational rescaling. G1b's authoring example uses a zero-start fixture. The accepted G1 nonzero decode evidence remains unchanged; nonzero-start integrated audio/authoring normalization still needs qualification.

A bounded queue must coalesce/drop an obsolete offer or pause scheduling when all slots are leased. The probe exits with explicit backpressure rather than overwriting; graceful production recovery is not implemented. Device loss invalidates the generation and visible authority, drains/cancels leases, recreates resources and requires a fresh acknowledgement. That recovery is specified, not proven.
