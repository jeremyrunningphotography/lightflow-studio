# Acknowledgement semantics derived from Windows

Candidate **C: operations require distinct levels; physical scanout is not a correctness prerequisite established by current Lightflow**. This is a research recommendation for owner review, not a production contract change.

Authoritative source is main f07053588912cf3ee71ba9e26273021f58764420:

- `LightflowStudio/FlyleafPlaybackBackend.cs:365`: forward step pauses, calls synchronous ShowFrameNext and reads CurTime; reverse reconstructs exact decoded predecessor and settles at its timestamp. No physical-display callback.
- `FlyleafPlaybackBackend.cs:548`: seek awaits SeekCompleted and reads CurTime; renderer readiness is explicitly required before GetFrameAsync seeks/captures (line421).
- `FlyleafPlaybackBackend.cs:451`: capture takes renderer snapshot plus CurTime on the same UI dispatcher. It is neither a monitor screenshot nor physical-scanout capture.
- `FlyleafPlaybackBackend.cs:515`: FramePresented is raised on CurTime property change. The event name alone does not establish physical presentation.
- `MediaPlaybackService.cs:277`: backend event publishes DisplayedTimestamp; source transitions and command serialization protect ownership.
- `PlayerViewerHost.xaml.cs:892`: reverse reconstruction hides the native host behind a retained bitmap; dispatcher Render priority handoff provides UI ordering, not scanout proof.
- `PlayerViewerHost.xaml.cs:1007`, `PlayerViewerHost.Markers.cs:96`, and `PlayerViewerHost.Actions.cs:109`: ranges and markers require decoded PTS from retained step or current snapshot, not a nominal clock or requested seek target.
- `PlayerViewerHost.xaml.cs:1093`: screengrab is paused-only, awaits step queue, captures retained/native-size renderer frame; no seek/audio mutation. Capture must preserve completed orientation/Color pixels for the same record.
- `PlayerActionDecodedFrameTests.cs`: hidden offscreen Windows host tests exact CFR/VFR PTS, paused silent boundaries and authored range equality. These tests could not establish physical scanout, and their correctness does not depend on it.

- `FrameScreengrab.cs`: saves supplied native-size pixels independently of screen coordinates. `FlyleafPlaybackBackend.cs:83` requests a render after Color changes; `LightflowColorPostProcessor.cs` snapshots Camera/Creative/bypass revision before GPU processing.

## Minimal proposed state model

1. **Decoded**: decoder-owned immutable source identity (asset/session generation, rational PTS/timebase, sequence). Pending only; no user current/authored authority.
2. **RenderReady**: backend completed source orientation and Camera/Creative/Compare rendering, checked GPU completion/ready fence; source-resolution capture is possible under a retained lease. Still not UI current solely from decode/render readiness.
3. **UIAccepted**: dispatcher transaction accepts this exact ready offer for the active presentation host, installs its composition surface and enqueues/integrates the visual update, generation-checks and atomically publishes its immutable authority token. This is logical renderer/UI acceptance, not physical scanout or an acknowledgement of the final overlay composite reaching hardware.

GPU release remains an independent lifetime signal. Offscreen composition/capture and physical presentation are optional observations, not extra mutable current-frame authorities. For a capture or authored command initiated from the Player, freeze the UIAccepted token and require its same RenderReady lease; a newer offer never silently substitutes. On pending step/seek/Color, protect/retain the previous accepted frame and mark command pending until atomic replacement. Reject obsolete source, surface, host, Color revision or serial callbacks.

The current Windows code establishes a renderer-owned identity contract, but does not universally demonstrate race freedom during every playing snapshot; that implementation weakness cannot authorize a mismatched new adapter. This proof must keep stronger atomic identity protection without adding unsupported physical-display guarantees.

## Operation requirements

Current/paused UI, completed seek/step/review and current-frame authored actions require **UIAccepted identity**. In/Out/marker/Subclip values bind to its **source PTS**, independent of display hardware. Capture additionally requires **RenderReady completed oriented/Color pixels** from that same token; UI overlays and physical scanout are not screengrab inputs. Playback audio schedules candidate frames but is not itself current-frame authority. Color/Compare changes create new render revisions and atomically replace accepted identity; they never rewrite pixels beneath a retained token.

## Qualification limits

Accepted X3 runtime evidence proves import, GPU snapshot/release, composition and input. It does not yet execute this new semantic publication callback/transaction. A protocol simulation can prove rejection/identity logic; it cannot be promoted to actual Avalonia UIAccepted execution. Backend completion measurements below therefore remain backend/capture readiness, not final shared Player current-state acceptance. No X3 source/evidence or old X1 contract is changed.
