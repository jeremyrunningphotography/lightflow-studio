# Local interaction iteration after a39c710

Publication, issue/Project reconciliation, and PR updates are deliberately deferred at
Jeremy's request. This iteration makes no additional Flyleaf source/package changes.

## Input and backend diagnosis

The surface gesture owner deliberately waited for the OS double-click interval after
mouse-up. That interval is 500 ms on this machine. A regression failed because the
click command had not executed when `EndGesture` returned. WPF/native surfaces shared
this path; Space, timeline, and dropdown selection did not use this timer.

Single-click now executes on release. The first click of a double-click retains its
normal Play/Pause action; the second only toggles fullscreen. Drag/capture cancellation
still prevents an unfinished gesture from clicking. There is no delayed click command.
Timeline and chrome routing have immediate fake-backend coverage.

Speed/fps/timeline latency was a separate backend path: reconfiguration always paused
and sought, and cadence inspection/restoration could add redundant seeks. Speed-only
changes now use Flyleaf's existing live Speed setter and restart the audio companion.
Cadence changes pause/flush once; timeline seeks configure the eventual paused/playing
cadence before the one requested seek. A prepared cadence avoids another seek at Play.
Paused inspection continues to restore full source frames. Natural EOF during a live
option change is reconciled after suppressed internal notifications.

Generated 640×360 H.264 + PCM diagnostic measurements (not throughput promises):

| Operation | Before | After initial fix |
| --- | ---: | ---: |
| Speed only | 83 ms | 54 ms |
| fps change | 145 ms | 125 ms |
| Restore Source/speed | 147 ms | 127 ms |
| Seek | 130 ms | 153 ms |
| Pause | 61 ms | 59 ms |

These fixtures did not reproduce a full second of decoder latency. The definite
artificial 500 ms surface delay is removed; the remaining native/audio work depends on
the actual source. Tests assert zero native seeks for speed-only changes and one for
cadence changes/timeline seeks, rather than fragile latency thresholds.

## Fullscreen restoration and overlays

The old repeated native-video transition reproduced `UCEERR_RENDERTHREADFAILURE`
(`0x88980406`) on the second entry. Simple still-image identity/layout checks passed,
which explains why the earlier tests missed the native presentation failure.

Fullscreen now leaves the Window's root Grid and shell attached. A root-spanning
presentation slot hosts the same Player above it; exiting removes that slot and returns
the Player to its original parent/index after window style/state restoration. Attached
row/column properties are not rewritten. Native video stays on its existing renderer;
only the transparent WPF overlay HWND uses software composition to isolate it from the
failing layered-child rendering transition. No source reload repairs fullscreen.

Regression coverage measures WPF and native dimensions after three settled cycles,
with paused/playing playback, normal/maximized windows, Esc, explicit exit, toggle exit,
source switching afterward, and real Home Browser/Right Panel context. The shell must
not unload during fullscreen.

Play/Pause feedback now starts its hold timer after the successful command, independent
of WPF's previous render tick. Replacing feedback stops the old hold/animation and
increments its generation; an old completion cannot hide the new glyph. Both glyphs
use the same 0.45-second hold and 0.4-second fade. The stale-fade regression replaces a
glyph while its earlier animation is already fading and verifies the new hold remains.

## Validation evidence

Local TRX evidence is under `artifacts/validation/199-local-*`. Focused input/backend/
layout checks and the final paused/playing overlay transition group pass. The cadence
test now verifies source-frame multiples with timestamp quantization tolerance rather
than assuming the desktop compositor never misses a presentation under load.

No computer-control tooling, GitHub mutation, push, or publication is used.

Final full Release rerun: **1,808 passed, zero failures/skips**. The first full run
passed 1,806 and failed two unchanged checks (Catalog cleanup file lock and live No-LUT
frame capture); both passed in isolation, followed by the clean full rerun. No production
Catalog/Color changes were made for those failures.
