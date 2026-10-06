# G1 backend comparison

Primary disposition: **B — controlled FFmpeg + Metal/CoreAudio is a credible engineering route; the tested stock libmpv integration is insufficient.** Acceptance recommendation: **REVISE, G1 unpassed**. Credibility of native components is distinct from an integrated Player meeting every semantic and presentation requirement.

| Criterion | libmpv 0.41.0 bounded proof | Controlled native route |
|---|---|---|
| Paused decoded order | Full forward/reverse PTS and retained-pixel sweeps match corpus; VFR supported in this version | Exact rational decoded PTS preserved in software and VideoToolbox; immediate predecessor found by earlier decode/scan |
| Authoritative displayed-frame PTS | Public `time-pos` leads retained rendered pixels during playback; `video-pts` unavailable; `video-frame-info` has picture/interlace/timecode fields, not source PTS; render frame info supplies wall-clock target, not media PTS | Application can carry AVFrame PTS and pixel/texture identity together until GPU completion and presentation acknowledgement. Components proven; full integrated acknowledgement/clock protocol still to qualify |
| Nonzero start | This MOV fixture starts at raw 5 s even with default rebasing; absolute 1.7 s seeks to first frame. Wrapper needs explicit stream-relative mapping | Rational raw 5 s PTS plus explicit stream start; all native sequences match oracle |
| Native rendering | Tested software render API produces an owned pixel buffer; OpenGL API exists, but not exercised. No direct public Metal render API in pinned render.h | VideoToolbox NV12 planes create Metal textures. Native NSView/CAMetalLayer obtains Retina drawable without separate visible video window |
| Rotation | Corrected matrix-bearing fixture aborts the tested software renderer (`mp_image_crop` assertion); alternative render paths unqualified | Native decoder reads the source matrix; independent Metal quarter-turn math passes. End-to-end oriented capture/presentation remains unqualified |
| Color/capture | Stock shader/filter path not qualified for exact Lightflow two-stage contract. Screenshot pixels lack atomic source-PTS association in tested public interface | Two ordered domain-clamped/trilinear LUT stages, bypass, quarter-turn rotation, viewport/overlay and readback match CPU reference in bounded Metal proof |
| Audio | Native CoreAudio and six speeds/loop measured muted; engine-estimated sync good on short synthetic corpus | FFmpeg PCM decode plus independent native CoreAudio sample-clock output measured; integrated AV master-clock policy still required |
| License of tested binaries | Homebrew build uses normal GPL mpv and FFmpeg GPL3+, plus GPL dependencies; not an approved Lightflow distribution | Source-built minimal LGPL2.1+ FFmpeg, dynamic libraries, no GPL/nonfree/version3/external codec libraries; redistribution compliance still required |
| Engineering | A libmpv fork/new public frame-PTS hook plus its own qualification could change the verdict; this spike does not claim libmpv can never work | Demux/decode/queue/reverse/audio clock and lifetime are Lightflow-owned responsibilities. Higher cost, explicit frame authority and compositable Metal ownership |

The stock libmpv failure is **not reverse stepping** on this corpus: that passed. The failure is unproved atomic display/capture PTS authority and a directly observed mismatch if `time-pos` is used as that authority. Polling, adding nominal frame duration or accepting one-frame error would weaken the contract. A new upstream API or fork is possible future evidence, not an assumption that satisfies G1 now.

The software render integration polls updates, renders into an owned buffer and reports swaps. It does not prove every possible OpenGL/scheduling integration has the same lag. A sample race or alternative scheduling may affect individual deltas; the public API still lacks an atomic source-PTS/pixel pair. The conclusion is bounded to this exact tested integration/configuration.

No alternate backend is preferable on current evidence. AVFoundation capability alone would not prove the same authoring/frame contract, and this spike does not expand into another player investigation.

Primary sources: [pinned mpv render API](https://github.com/mpv-player/mpv/blob/v0.41.0/include/mpv/render.h), [mpv licensing](https://github.com/mpv-player/mpv/blob/v0.41.0/Copyright), [FFmpeg licensing/compliance](https://ffmpeg.org/legal.html). Native measurements and pinned distribution/build provenance are adjacent JSON/JSONL files.
