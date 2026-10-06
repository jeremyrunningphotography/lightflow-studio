# X3 / #370 — bounded IOSurface composition proof

**I3 — Avalonia composition remains unresolved at the final acknowledgement boundary.**
Actual free Metal/IOSurface import, timeline ordering and render-tree composition are
credible. The prior naive native-child clipping/overlay/input failure is avoided by this
path in deterministic fixtures. It removes that specific render-tree obstacle; it does
not remove the complete Player presentation architecture gate. **G3 remains C — REVISE;
G1 remains REVISE / UNPASSED.** X1 presentation risk is reduced for import/composition and
unresolved for final visible frame authority. Do not resume X1 or change #368 here.

## Scope and recovery

Fetched authoritative main `0991cedd319c59d310f8028b2abe6c738f0e69ea`, accepted G1b merge,
2026-10-06. Fresh branch `codex/370-avalonia-iosurface-proof`, independent clone
`/Users/jeremyrunning/Git/agents/Agent-X3-Avalonia-Proof`. Root AGENTS and #366/#368/#370,
accepted G1b interop/PresentedFrame/integrated/fault/performance evidence and previous
X3 history were read before native implementation. Prior branch remains frozen at
`d25e2ad591547d3a012175606f930c9f4af414fe` locally (its Git push was credential-blocked;
remote prior branch remains d55c376). No old files/branch history were rewritten.
New source and evidence live only in tools/X3AvaloniaIOSurface and this directory.

Mac14,9 MacBook Pro, M2 Pro, 32GB; macOS26.6.2 build25G83; .NET8.0.425/runtime8.0.31,
Arm64, Apple CLI clang++/ARC, native NSWindow and actual backing scale2. Exclusive Mac
execution was owner-authorized. No cross-display transition, physical device removal,
production UI/Player/FFmpeg/Catalog change, Windows implementation, paid tooling or M4+.
A read-only browser Project check ran during the final matrix; continuous visible-window
qualification is absent and no presented FPS claim is made.

## Contract and version alignment

Authoritative [accepted X1 interop](../x1-player-g1b/PRESENTATION_INTEROP_CONTRACT.md) and
[PresentedFrame](../x1-player-g1b/PRESENTED_FRAME_CONTRACT.md) remain byte-for-byte unchanged.
Synthetic immutable FrameOffer carries asset/session/stream, startPts, raw PTS/rational
 timebase/decode identity, presentation generation/serial, per-pool surface generation,
 IOSurface pointer/BGRA8/top-left/SDR state, rotation/LUT hashes/Compare representation,
 device identity, ready/release instance pointers and timeline values. No decoder, LUT
 engine or backend authoring implementation was duplicated. Synthetic PTS/action patterns
 qualify association only, not actual decoded step/seek or display-color correctness.
Each three-slot allocation has its own events with globally increasing offer serial
values; generation metadata is checked separately. This is a valid FrameOffer event/value
 topology, not a proposal to change X1's global event-pair implementation.

Pinned **Avalonia Desktop/Native/Skia/Fluent 11.3.8**, official source commit
`6dd9eb473b74a56cc42e5bc118cfe918b48a940b`; **SkiaSharp/native2.88.9**,
**HarfBuzzSharp8.3.1.1**. Exact lock/archive/license/binary hashes are in
[provenance](DEPENDENCY_PROVENANCE.json). All package licenses are recorded from NuGet
metadata; Apple frameworks use Apple SDK/system terms. No mandatory commercial feature.
The accepted Metal shared-event seam requires macOS12; minimum-OS deployment is untested.
Prior X3 used Avalonia12.1.3 / SkiaSharp3.119.4 and free core TableView introduced in12.1.
This isolated 11.3.8 proof cannot be substituted for that control proof: a future unified
version must qualify this interop on12.1.3, or rework the Details control if selecting11.3.8.
No production dependency version was changed, and no upgrade was used to force a pass.

## Actual runtime path, fences and leases

Objective-C++ bridge creates three BGRA8 IOSurfaces with aligned row stride (7680 at1080p,
15360 at4K), IOSurface-backed shared Metal textures and MTLSharedEvents. A deterministic
GPU shader writes serial bits and serial-dependent colors, then signals ready(serial).
No CPU bitmap supplies Candidate B. Native texture.iosurface identity and pixel format
are checked, IOSurface numerical IDs logged as diagnostics; pointer descriptors are used
for import. Producer registry id4294968764 matches Avalonia LUID00000001000005BC.

Compositor.TryGetCompositionGpuInterop advertises IOSurfaceRef/MetalSharedEvent/timeline.
ImportImage/ImportSemaphore ImportCompleted succeed. CompositionDrawingSurface.
UpdateWithTimelineSemaphoresAsync waits for ready and signals released; normal composition
surface visuals live beneath normal Avalonia controls. No child NSView or separate video
window is used. Source texture dimensions remain independent of logical/display bounds.

Delayed producer200ms: Task7.81ms versus release211.18ms. Stale ready value14 cannot satisfy
expected17; correct frame17 pixels follow readiness. Delayed consumer150ms leaves release
below its offer value and rejects premature acquire. A held source lease rejects reuse
and its full source hash remains unchanged across six later frames. An old-generation
 offer is explicitly rejected; the prior snapshot is retained, no presentation authority
published. Snapshot18 remains18 after the producer reuses that released source slot for19;
then updating the surface yields19. That supports an independent GPU snapshot/copy, not
zero total copy. Explicit backend capture/current-frame leases must still be retained;
this UI-only harness does not implement production authoring/capture ownership.

Twelve source-composition captures decode to the expected serial, including pause,
forward/reverse, seek-like generation, Color-state representation, delayed readiness,
reuse and reopen. Per-update rows bind serial/generation/PTS/timebase/surface/timelines.
No pixel identity oracle is claimed for every sustained displayed frame. See
[identity](FRAME_IDENTITY_RESULTS.json), [timeline/leases](TIMELINE_RESULTS.json) and
compressed [raw rows](RUNTIME_ROWS.jsonl.gz).

## Composition, overlays and input

Independent standard-library PNG decoder asserts rounded/rectangular clipping, dark
background below video, green central overlay above it, opacity/rotation changes beneath
stable overlays and detach/reattach pixels. Text/status, edge Review and magenta temporary
interaction overlays stay in the shared Grid; oversized edge fixtures are deliberately
clipped, not product layout acceptance. Status updates independently during overlay-on
cadence. Captures are **real offscreen GPU compositor snapshots**, not mockups or native
scanout screenshots. [Pixel oracle](OVERLAY_CLIPPING_RESULTS.json), normal.png,
opacity-transform.png, context-menu.png and performance-3840-True.png preserve appearance.
Scroll-container behavior is not separately exercised; ordinary container clipping is.

The composition-only Control initially lacked a hit-test region; that negative input
fixture is preserved in INITIAL_INPUT_FAILURE.json. Adding a shared transparent drawing
region makes visible geometry participate in normal hit-testing. Final window-bound raw
input traverses the actual TopLevel input pipeline through a pinned private test hook:
visible play button/video clicks pass, clipped exterior hits Grid, pointer moves2/wheel1,
right-click context menu opens and appears above video; focused video Space invokes the
fixture semantic action while focused editor Space/text stays local. Editor focus stays
local across frame update. This proves the composition path need not steal focus or create
an invisible native hierarchy. It does not qualify OS key translation, hardware gestures,
full production action routing, drag/drop or earlier Browser shortcut/context-menu gaps.
No global input automation supplied these assertions. Test-only private reflection must
not become a production dependency. [Input](INPUT_RESULTS.json).

## Retina, lifecycle and faults

Native window backing scale2; logical resize repetitions preserve source dimensions.
Source-format replacement creates fresh three-slot pools/epochs/generations; overlays
align in logical coordinates. Benchmark capture targets are1920x1080 and3840x2160 at2x,
but the physical window/display clips the oversized4K logical scene; no physical4K panel
scanout claim follows. Aspect-fit is limited to the fixture's16:9 viewport; broad fit/pan
zoom/aspect combinations and cross-display transitions remain unqualified.

Actual native NSWindow fullscreen style/backing scale is queried, then exit, visual
 detach/reattach, close while200ms producer pending, release/drain, new window/reopen and
fresh frame identity are exercised. Pending close never sets visiblePresented. Final
native producer/surface counts are0/0; pools stay at3 during sustained tests. GPU/Skia/OS
internal allocation leak absence is not proved by native counts or short RSS samples.
A launch failed before window creation with native render timer-6661; error is preserved.
Later accessible-desktop retry succeeded without changing settings; causal attribution
(lock/sleep/display/session/framework) is unresolved. Deactivate/reactivate, physical
GPU/device loss and sleep/wake remain untested. Producer-loss availability is a simulated
record, not a complete UI error/recovery state machine. [Lifecycle](LIFECYCLE_RESULTS.json)
and [fault matrix](FAULT_MATRIX.json) distinguish proven/simulated/reasoned/unresolved.

## Performance and copies

Deterministic GPU producer, target30Hz, 10s per1080p mode and30s per4K mode. No FFmpeg/audio
or X1 backend is running. Metrics measure **update Task plus release-fence capacity**, not
final composed/presented FPS; a callback proving every displayed frame is unavailable.
Native shader+per-frame worker, UI polling, JSON serialization and allocations are harness
cost. No allocation stack/GPU trace or long soak. The measured command cost covers producer,
not consumer GPU copy/compositor render. Snapshots after phases add explicit audit readback.

| Source / overlays | Cycles | update/release fps | late cycles* | p95 ms | CPU one-core % | managed allocation MB | ending RSS MB |
|---|---:|---:|---:|---:|---:|---:|---:|
|1080p off|301|30.075|0|9.07|21.02|2.85|443.56|
|1080p on|301|30.088|0|10.42|19.31|4.73|462.47|
|4K off|901|30.021|0|14.19|32.39|9.33|334.68|
|4K on|901|30.023|0|15.24|34.32|14.67|435.55|

*Late means cumulative update/release schedule exceeds target by >one30Hz period. Not
physical dropped/late-frame counts. Startup-count inclusion explains slight >30 rates;
CPU differences are observations, not controlled causal effects. Memory samples and
producer GPU timings are in PERFORMANCE_RESULTS.json; varying RSS across pool replacements
cannot establish a leak or absence of one. Import/snapshot capacity did not itself prevent
30Hz4K in this bounded run. Eventual actual4K presented cadence remains open. X1's accepted
~27.3fps backend limitation is separate and neither blamed on nor solved by Avalonia.

Candidate B uses no per-frame CPU readback/upload in normal path. Texture association is
shared memory, but retained snapshot independence and Skia source support a GPU snapshot/
copy. **GPU-to-GPU snapshot path; exact copy count/cost unknown; not zero-copy.** Audit
captures/hash checks deliberately read back. No GPU tracer isolates incremental copy/render
cost or yields a pure Avalonia-vs-native baseline. Candidate C's cheap30-frame4K test
includes CPU texture readback, Bitmap allocation/copy and full compositor snapshot/readback:
median33.13ms, p9544.47ms, inverse-mean ceiling28.27fps. Its audit overhead differs from
X1's9.1ms/21.4fps end-to-end fallback; do not subtract or directly compare them. Preserve
fallback, do not select it over successful IOSurface import.

## Remaining architecture and disposition

Shared layout/overlays/controls/actions/viewport use one C# view. A narrow platform adapter
owns IOSurface pointers, Metal events/device identity/imports, leases and lifecycle. Future
Windows supplies its own surface/fence adapter behind the same semantic FrameOffer;
shared Player layout must not reference IOSurface/Metal descriptors. This proof adds no
Windows implementation or large Mac view fork. The small transparent surface host is
shared; ~native bridge/presentation adapter is Mac-specific proof code, not a production
API or complete playback backend. Final acknowledgement may need framework/native extension
engineering outside that narrow adapter; estimate not yet responsibly bounded.

[Acknowledgement C](ACKNOWLEDGEMENT_ANALYSIS.md) is the decisive unresolved seam. Never
publish source-update/GPU completion/offscreen capture as PresentedFrame. Proposed tokened
final-drawable handshake requires owner review and further qualification; accepted X1
contract remains unchanged. Therefore I3; G3 C — REVISE. Prior Details churn, selection
anchor, semantic shortcut capture/Browser menus, useful AX item/cell associations,
TIFF/ImageIO policy, EXIF normalization and display-color parity remain untouched. No
production migration, paid dependency, M4+, X1 backend change or Catalog revisit.
Stop native experiments at this evidence gate. Draft PR is evidence for owner review,
not a product-ready WPF/Mac package, merge or gate acceptance.
