# M1 / #368 — bounded G1b integrated Player proof

**Primary disposition: G1b-C — presentation contract credible, core backend integration unresolved. Overall G1: REVISE / UNPASSED. Stop for owner review.**

Controlled FFmpeg + Metal/CoreAudio remains the preferred credible direction. This spike establishes an executable native immutable frame/surface/acknowledgement pattern, exact longer-file predecessor behavior, integrated sample-clock scheduling, end-to-end software/hardware rotation/Color/capture, and a specific Avalonia-importable surface seam. It does **not** establish production Player parity or sufficient audio/performance/lifecycle qualification to pass G1. Candidate B's import seam exists in pinned official source; final UI-compositor presentation acknowledgement remains unresolved.

Source: fetched main **`7b7f1c2cac5426f7df145921d8890adde6f9c490`**. New branch **`codex/368-mac-player-g1b`**, independent workspace `/Users/jeremyrunning/Git/agents/Agent-X1-Mac-Player`, 2026-10-06. Only `docs/research/mac-compatibility/x1-player-g1b/` and `tools/X1MacPlayerG1b/` are added. Accepted G1 evidence/tools, production projects, Catalog and X2/X3 work remain unchanged. No M4+, PR merge or X3 resumption is authorized.

## Preserved accepted G1

PR #372, accepted head `aa609e3d2701ab426253a809e21c1f6fa1909744`, merged as `aed6c2906637c8a5a9d71c1c18ac24bed48a8724`. Its component results and limitations remain intact. Tested stock libmpv was insufficient: 205 clock-versus-retained-pixel mismatches and a software rotation abort, despite seven successful paused step sweeps. This is not universal libmpv impossibility. G1's 1,570 exact PTS observations, 42 predecessor checks and zero-error synthetic LUT/rotation component results are distinct from the new integrated comparisons below.

## Frame, review, Color and capture

[Presented-frame contract](PRESENTED_FRAME_CONTRACT.md) and [machine authority checks](FRAME_AUTHORITY_RESULTS.json) bind raw rational source PTS/decode identity to immutable orientation/Color state, a leased IOSurface-backed texture, serial/generation, capture and native acknowledgement. The successful paused retry has PTS0; forward is512; reverse returns0; seek reaches30720 (timebase1/15360). A generation cancellation injected after producer GPU completion preserves the prior retained record and suppresses publication. The authoring adapter takes In/marker/Subclip/inclusive Out from retained PTS and exclusive Out from the actual next decoded frame; production action execution is not inferred.

Producer completion signals a shared-event timeline; consumer waits and samples the same IOSurface, then signals release. Player publication additionally requires a nonzero drawable `presentedTime`, not decode/GPU completion. No observed acknowledged row violated its serial/PTS/capture association checks. This is bounded single-source, serialized evidence. Large playback textures have no per-frame CPU pixel oracle; concurrent capture/source replacement, nonzero-start integrated normalization and UI display acknowledgement remain open.

All **20 software variants** (four effective quarter turns × no LUT/Camera/Creative/both/Compare) acknowledge and preserve PTS, with maximum **1** 8-bit channel error against an independent CPU transform oracle. After the owner unlocked the Mac, all **20 hardware variants** also acknowledge, with maximum **2** channel error. Camera precedes Creative, with red-fastest trilinear interpolation/domain clamp; Compare bypasses both. Source display-matrix orientation is combined exactly once with authored quarter turns. Native capture is full-resolution transformed backend pixels, excluding overlays/viewport.

These new nonzero errors differ from G1's synthetic zero-error component oracle: software quantization and hardware NV12 conversion are not identical to that earlier experiment. The hardware path is explicitly limited-range 8-bit NV12/BT.601 with nearest chroma; broader matrices/ranges/HDR/ICC are unqualified. Zero error in a large noncapture row means **not evaluated**, not a golden match.

A held acknowledged frame's texture hash stays unchanged across six later draws. Holding all three surface slots produces explicit exit13 instead of overwriting leased pixels. Graceful backpressure recovery and stable per-slot generation still need implementation; emitted `surface_generation` is a pool allocation epoch. See [Color/capture](COLOR_CAPTURE_RESULTS.json).

Long reverse: two **180-second** 320×180 H264/B-frame fixtures, GOP300, CFR5400 frames and VFR3000 frames. Native linear pixel oracles match independent ffprobe PTS arrays. **600/600** PTS/pixel predecessor queries pass, including fixed neighborhoods at EOF, mid-file and a GOP boundary, reverse after seek and forward after reverse. Each case has294 cache hits/6 misses; conservative retained-cache accounting peaks16,588,800 bytes below16MiB. Typical hit latency is~.28ms; worst miss43.92ms CFR/29.16ms VFR, decoding up to529/301 frames. Peak sampled reverse RSS is~62.8/52.6MiB. This is not exhaustive random review, 4K reverse memory or active decoder cancellation proof. [Reverse results](REVERSE_STEP_RESULTS.json).

## Integrated audio and scheduling

FFmpeg-native AAC PCM from the accepted 40-second fixture feeds LGPL `atempo`, then mono F32/48kHz AudioQueue output, three1024-sample buffers (64ms queued capacity), gain0. Video selects decoded source PTS against `seekOrigin + playedSamples/48000 * speed` with a fixed1/60-second lead scaled into source time. The source-frame record remains authoritative; the audio clock schedules it, never authors frame identity.

Measured source PTS minus sample clock after presentation callback:

| Segment | Duration | Range, ms | Mean, ms | Audio-clock vs wall slope |
|---|---:|---:|---:|---:|
| 1× | 30s | −21.81 to −3.83 | −12.83 | .00742ms/s |
| .5× | 15s | −12.05 to −3.09 | −7.55 | .00363ms/s |
| 2× | 15s | −61.08 to −8.50 | −40.63 | .01466ms/s |

Pause clock deltas are zero and resume advances. Silent seek stops the queue, settles video, then restarts at source origin1s. Explicit queue interruption returns invalid clock−1; disposal/recreation advances from the saved origin. Volume parameter .25 roundtrips, then immediately returns to0. No audible amplitude, selected-stream, persisted volume or physical output-device claim follows.

These are sample-clock/frame scheduling measurements, **not physical lip-sync drift**. Constant sine/muted output cannot establish packet/sample causality or pitch. PCM and tempo are predecoded/prepared before queue start, so streaming decode/tempo latency, affine seek origin accuracy and live speed changes remain unproved. Representative speeds are separate opens; .125/.25/4 and mid-playback changes are not qualified here. At2×, the serial harness skips382 source frames while presenting about34.3fps rather than the nominal60 source-frame cadence.

Performance loops use a2-second video with parallel40-second PCM; queue disposal/restart is an explicit discontinuity. They do not prove seamless same-stream AV looping. There is no underrun detector or callback-starvation test. The fixed60Hz lead has no adaptive phase/latency calibration. [Audio results and limits](AUDIO_SYNC_RESULTS.json).

## Integrated performance

EOF loops, VideoToolbox/software decode, Metal processing, IOSurface composition and active muted AudioQueue output run together. Acknowledged throughput includes startup/restart gaps and is not a pure decoder benchmark. Scheduled skips exclude unacknowledged startup draws and are not a complete physical panel drop counter.

| Case | Seconds | Ack fps | Scheduled skips | Mean producer GPU, ms | CPU % of one core | Peak playback RSS, MiB |
|---|---:|---:|---:|---:|---:|---:|
| 1080-hw-off | 20 | 29.50 | 0 | 0.46 | 15.0 | 112.6 |
| 1080-hw-color | 20 | 29.50 | 0 | 0.87 | 15.1 | 113.5 |
| 1080-hw-sustained | 60 | 29.52 | 0 | 0.89 | 14.5 | 114.6 |
| 1080-hw-unlocked | 20 | 29.50 | 0 | 0.80 | 17.8 | 113.0 |
| 4k-hw-off | 30 | 29.33 | 5 | 1.64 | 14.5 | 205.0 |
| 4k-hw-color | 30 | 28.03 | 43 | 3.35 | 14.7 | 182.9 |
| 4k-hw-sustained | 120 | 27.32 | 258 | 3.36 | 13.6 | 218.6 |
| 4k-hw-unlocked | 30 | 27.23 | 65 | 3.07 | 16.6 | 182.8 |
| 1080-sw | 15 | 26.93 | 36 | 0.71 | 52.6 | 143.5 |
| 4k-sw | 15 | 11.87 | 252 | 2.99 | 89.1 | 342.9 |
| 4k-cpu-copy | 15 | 21.40 | 118 | 3.36 | 28.2 | 332.0 |

The 60/120-second runs show no OS thermal-state escalation (all sampled state0/nominal), but are not thermal saturation tests. 4K first/last10-second acknowledgement counts are278/276; producer GPU means3.24/3.26ms. Playback RSS rises from a first10-second mean~173.9MiB to~218.3MiB late, then appears to plateau; final seek/capture temporarily raises RSS to~339.5MiB. 1080p late playback RSS is~113.9MiB. RSS cannot prove allocation leak absence; allocation growth was not separately profiled.

Two decoded frames and three output slots bound the explicit frame queue. CPU utilization comes from whole-process `time -l`; GPU values cover producer commands only. `decode_batch_ms` also includes scheduling wait and is not pure decode cost. No audio-underrun count is available. Broad formats, multi-hour playback, power/thermal throttling and production concurrency are outside this corpus.

4K CPU readback/upload adds mean~9.1ms (before any Avalonia bitmap cost) and reduces throughput to~21.4fps; software4K reaches~11.9fps. Those tested fallback modes are inadequate for a30fps4K target. A GPU interop path and better asynchronous scheduling are required for that target; these failures do not establish that controlled hardware decode/Metal is inherently incapable. The producer command is only~3.36ms, while blocking acknowledgement/queue restart contributes to the end-to-end limitation. [Performance results](PERFORMANCE_RESULTS.json).

## Shown lifecycle and visibility caveat

Actual native host resize records640×360 logical/1280-pixel width on scale2. Fullscreen enters the real NSWindow fullscreen style, resizes to backing dimensions, receives nonzero acknowledgements after retries, exits, then closes/reopens four times. In the post-unlock run all four reopen retries acknowledge; every first reopen draw is unacknowledged. Programmatic deactivate left `NSApp.active` true, so an actual deactivate/reactivate transition was not proven. Spaces navigation/focus restoration, cross-display scale change, sleep/wake and physical device loss/change were not tested.

Computer-use inspection **after the initial matrices** reported the Mac locked. The exact lock transition is unknown, so original long measurements have no continuous visible-desktop qualification. Two short hardware Color runs recorded all-zero timestamps; they are preserved as failures/unknown. After owner unlock, all20 hardware variants pass and bounded1080p/4K repeats still show the performance distinction above. Lock/occlusion may explain prior zero callbacks; it was not isolated causally. This is evidence that GPU completion and callback invocation alone cannot mean visible presentation. [Lifecycle results](LIFECYCLE_RESULTS.json), [fault matrix](FAULT_MATRIX.json).

## Presentation recommendation and remaining gate

Use BGRA8 **IOSurfaceRef + MetalSharedEvent timeline** imported into a normal Avalonia composition visual; reject naive native child hosting for the owner-reported clipping/z-order/input failures. Pinned Avalonia11.3.8 source exposes this exact import mechanism and requires macOS12.0 for its Metal shared-event path. Its Skia snapshot path may copy on GPU; zero total copy is not established. Surface update completion means image snapshot/lifetime completion, **not display acknowledgement**. [Concrete X3 ownership/thread/fence/resize/input/fullscreen/capture/device-loss contract](PRESENTATION_INTEROP_CONTRACT.md).

G1b-C is selected because this specific UI seam is credible, while core audio causality/tempo/seamless-loop/recovery, concurrent frame authority, visibility/rehost invalidation and real-time4K/2× scheduling remain incomplete. Neither a full integrated-backend pass (A) nor a claim that the controlled architecture is invalid (D) is supported. X3 composition qualification is still necessary, but was not resumed. G1 can eventually pass before every G3 UI result, once the backend and truthful presentation acknowledgement are sufficiently established; API existence alone cannot pass it.

Research estimate **increased from12–24 to16–28 engineer-weeks** for backend implementation and qualification, excluding shared UI extraction and broader packaging. Extra work centers on streaming sample/tempo origins and discontinuities, asynchronous scheduling/backpressure, visibility/compositor acknowledgement and explicit format/color handling. This is uncertain planning, not an approved budget.

Recommended future acceptance discussions: exact source PTS/predecessor/capture identity with **zero frame-identity substitutions**; independently timed flash/audio impulses for AV offset (propose±20ms wall-time steady offset and≤1ms/min clock drift, subject to owner agreement); all six speeds/live transitions; seamless-loop/restart semantics; target1080p/4K cadence with declared drop/latency limits; an explicit reverse/cache memory budget at4K; measured display/capture pixel tolerances rather than silently accepting the1/2-channel differences. Owner must set corpus/duration/numeric limits; none is presented as approved.

## Durable validation and delivery

Native process matrices, expected injected exits, exact PTS/pixel/lease checks, CPU/golden comparisons, archived-row SHA256 and source/input/dependency pins are preserved. [Reproduction](REPRODUCTION.md), [dependency configuration/licensing](DEPENDENCY_PROVENANCE.md), [raw evidence manifest](RAW_EVIDENCE_MANIFEST.json), and [delivery/cleanup state](DELIVERY.json) provide the audit path. Dynamic LGPL libraries introduce no GPL-only or paid runtime dependency; final distribution/source/license compliance remains work.

#368 remains Open/In Progress, G1 DoD incomplete; Epic#366 remains Open/In Progress with M0/M2 complete and G1/G3 incomplete. Evidence goes to a Draft PR for owner review, not a product-ready packaged build. Native probes stop and disposable task caches/executables are removed, while exact fixtures, captures, source and archived measurements remain. No NAS/test storage, production migration, Catalog work, M4+, X3 edits/messages or merge.
