# G1b presentation primitive for a future X3 fixture

Recommendation: a **BGRA8 IOSurface imported into Avalonia's composition render tree with Metal shared-event timeline synchronization**. X3 remains paused; this document does not authorize it to resume or implement production integration.

## Evidence and candidate disposition

**A — naive native NSView/CAMetalLayer child: reject for shared UI composition.** Owner-accepted X3 evidence reports clipping punch-through, native content above overlays and invisible overlay hit targets. G1b demonstrates a native shown drawable and lifecycle behavior but provides no specific NSView hosting repair for those three failures. A separate video window is not a solution.

**B — UI-consumed GPU surface: credible, specific API exists.** Native G1b creates IOSurface-backed BGRA8 textures on producer and consumer Metal device handles, matching registry id `4294968764`, and synchronizes two queues with `MTLSharedEvent`. No CPU copy is needed between those native queues. Held-frame pixels survive later draws; three-slot exhaustion fails explicitly without overwriting.

Avalonia **11.3.8**, commit `6dd9eb473b74a56cc42e5bc118cfe918b48a940b`, was independently cloned from the official repository and inspected. It was not executed, modified or borrowed from X3. Relevant pinned primary source:

- [Compositor GPU interop](https://github.com/AvaloniaUI/Avalonia/blob/6dd9eb473b74a56cc42e5bc118cfe918b48a940b/src/Avalonia.Base/Rendering/Composition/Compositor.cs): `TryGetCompositionGpuInterop()`.
- [Native Metal adapter](https://github.com/AvaloniaUI/Avalonia/blob/6dd9eb473b74a56cc42e5bc118cfe918b48a940b/src/Avalonia.Native/Metal.cs): supported handle descriptors `IOSurfaceRef` and `MetalSharedEvent`; BGRA/RGBA8; timeline semaphores; device LUID.
- [Native imports](https://github.com/AvaloniaUI/Avalonia/blob/6dd9eb473b74a56cc42e5bc118cfe918b48a940b/native/Avalonia.Native/src/OSX/metal.mm): IOSurface creates a Metal texture; shared-event import accepts the **Objective-C event instance pointer**, exports its `MTLSharedEventHandle`, and imports it on the UI device.
- [Composition surface](https://github.com/AvaloniaUI/Avalonia/blob/6dd9eb473b74a56cc42e5bc118cfe918b48a940b/src/Avalonia.Base/Rendering/Composition/CompositionDrawingSurface.cs): `UpdateWithTimelineSemaphoresAsync` completes after the server update, when the caller may dispose the imported image.
- [Skia Metal import](https://github.com/AvaloniaUI/Avalonia/blob/6dd9eb473b74a56cc42e5bc118cfe918b48a940b/src/Skia/Avalonia.Skia/Gpu/Metal/SkiaMetalExternalObjectsFeature.cs): flush, wait, wrap Metal render target, `SKSurface.Snapshot`, flush, signal. This is not evidence of zero total GPU copies; snapshot/copy-on-write and later composition must be measured.

**C — CPU/readback bitmap fallback:** a concrete bounded-copy compatibility/proof bridge, not the selected 4K route. The 4K experiment reads back and uploads every full-resolution BGRA8 frame; mean cost is about 9.1 ms and observed acknowledgement throughput about 21.4 fps. Actual Avalonia bitmap allocation/upload adds unmeasured cost. See [performance results](PERFORMANCE_RESULTS.json).

## Contract X3 can qualify

1. Backend emits the [immutable FrameOffer](PRESENTED_FRAME_CONTRACT.md), with a retained `IOSurfaceRef` pointer, BGRA8 pixel bounds/top-left origin, explicit SDR color description, source/frame authority, surface generation, device registry identity/LUID and ready/release timelines. IOSurface numerical ID is a diagnostic, not the pointer to import.
2. On its dispatcher/compositor path, UI obtains `TryGetCompositionGpuInterop`, verifies supported handle types/synchronization and compatible device, and calls `ImportImage(properties, PlatformHandle(surfacePointer, "IOSurfaceRef"))`. It imports readiness/release **event instance pointers** with descriptor `"MetalSharedEvent"`.
3. UI calls `CompositionDrawingSurface.UpdateWithTimelineSemaphoresAsync(image, readyEvent, readyValue, releaseEvent, releaseValue)` and uses that drawing surface in a normal composition visual. Producer readiness, GPU-release signal and image-update Task have distinct meanings. Reuse waits for release and every capture/frame lease; a task completion alone is not a display acknowledgement.
4. A top-level UI presentation acknowledgement must associate the actual composed surface snapshot with the offer serial/generation before shared authoring state claims it displayed. The pinned public import/update API does not establish that association. Qualifying a render-target callback/native extension or another truthful visible-frame acknowledgement is a remaining bounded integration task. Never relabel “surface updated” as “presented.”
5. UI owns clipping, overlay z-order, viewport/zoom/pan transforms, visible hit-testing, keyboard/pointer/focus and logical sizing. Native backend owns source-orientation/Color pixels and capture; UI overlays stay in its render tree. X3 must test clipping AND matching hit targets, not just an image appearing.
6. Source-resolution surfaces stay independent of logical bounds/Retina scale. UI transforms/composes into its target pixels; resize/backing-scale changes cannot change source PTS or Color identity. Reallocate only on actual output-format/dimension/device changes, increment surface generation, and retire old surfaces after leases/fences.
7. UI owns fullscreen/Spaces, reparent/visibility and focus restoration without reopening the media session or restarting audio. Visibility/rehost loss invalidates current visible acknowledgement. Native fullscreen style and callbacks are measured here; full Avalonia behavior is unqualified.
8. Capture acquires the acknowledged backend record and ready fence, returning source PTS/identity with transformed full-resolution pixels. UI-overlay screenshots are a separate operation. Device/surface loss invalidates the generation, reports unavailability and rebuilds resources before a fresh acknowledgement; no silent reuse of an obsolete pointer.

Threading is deliberate: decode/tempo/output workers do not mutate UI visuals; UI dispatcher marshals offers, compositor consumes imported images, completion callbacks only enqueue generation-checked acknowledgements. The current serial native harness models ownership and fences but does not prove this concurrent production implementation.

## API floor and qualification boundary

Pinned Avalonia's Metal shared-event import/wait/signal path checks **macOS 12.0**. That is a hard floor for this particular interop seam, not a decision on Lightflow's final supported OS. Availability/build deployment on the chosen minimum OS still needs execution. No paid control/runtime or production Avalonia project was added.

Clipping, overlays, transforms, input, GPU snapshot cost and the final compositor-to-frame acknowledgement remain X3/future bounded proof obligations. The seam is evidence-backed; those behavioral claims are not inferred from API existence. G1b selects **C**, because the concrete UI seam is available while core audio/frame/lifecycle/performance qualification remains incomplete. Overall G1 stays REVISE / UNPASSED.
