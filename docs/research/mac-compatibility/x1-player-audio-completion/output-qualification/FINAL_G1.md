# Final bounded audio proof: technical G1 PASS recommendation

Owner acceptance is pending. #368/#366 remain Open / In Progress; #379 remains
Draft and must not merge. Main7dec8f7372ba0af06bc15969ec0a976de7984900; continuation
starts at published75e00cb19e15ad7eeba9ebaa31a314a61f2c15b7. No production Player,
Flyleaf, dependency, Catalog, X3, #370 or M4+ change.

## Bounded startup RCA

Diagnostic classification: **A1 environment/session-associated native I/O
unavailability**, restored by the owner unlocking this Mac. Earlier A4 findings
are preserved; the later recurrence supplies the missing same-binary comparison.

While CGSession reported screenLocked=true, the built-in default device72 was
alive at48k, but the task AudioQueue and independent minimal queue returned
-66681 after about15s. Signed OS-native afplay also returned AudioQueueStart
-66681 after15.145s. Independent DefaultOutput AudioUnit returned start0 but
0callbacks/frames during3.239106s. This rules out FFmpeg, Metal, Player scheduling,
new EOF/drain handling and task unsigned binary identity as sufficient causes.
Historical logs and recurrence show AudioDeviceStart(err0) followed by AQME I/O
progress timeout; buffer creation/enqueue succeeded. This is a native I/O
transport/callback availability failure, not a failure to generate valid PCM.

After the owner unlocked, with no binary, device/default/rate/volume/service
change, minimal start returned0 in46.901ms and delivered16callbacks during the
poll interval; AudioUnit delivered296callbacks/139239frames over3.156090s;
afplay returned0 in1.874s for a one-second silent fixture. Device72/rate48k
remain unchanged. The session lock property is absent after unlock, not an
invented API false value; owner physical confirmation supplies the unlocked
state. Binary hashes and both device snapshots are preserved.

This establishes a bounded observed execution-state boundary and a successful
recovery action. It does **not** establish a universal macOS policy that locked
sessions cannot play audio, nor distinguish lock itself from associated
session/display/service transitions. The internal OS cause and independent
lock/relock causality remain unqualified. Do not change system configuration or
restart services based on speculation. Actual packaged/background-lock behavior
must be qualified later, just as sleep/wake and device switching must be.

Twelve further silent setup probes varied explicit selection of the same device,
task activity assertion, and withholding refills, returning to baseline between
changes; all starts succeeded while output was available. Default queue UID is
an Apple default-device alias, so its string differs from the HAL physical UID;
this is not a routing mismatch. DeviceID72/512-frame hardware size in successful
and failing transport logs match. Priming, gain, main-runloop, format/buffer and
feed explanations are not supported by the preserved controls.

The general Apple -66681 definition supplies no deeper cause:
https://developer.apple.com/documentation/audiotoolbox/kaudioqueueerr_cannotstart
Both start-failure and start-success/no-callback observations remain distinct
native API outcomes of the measured unavailable-output state. A reusable buffer
callback during Stop/disposal is not played-audio evidence.

## Isolated endpoint cause and task-only correction

The earlier loop run disposed/rebased about19ms early because it treated all
buffer-acquisition callbacks as completed playback. Apple explicitly separates
buffer reuse from played sound:
https://developer.apple.com/documentation/audiotoolbox/audioqueueoutputcallback

First change only the EOF clock condition: three loops reach source1.0. Then add
AudioQueueStop(false) after all filter output is submitted and await IsRunning0
before publishing drained-source completion and rebasing. Native APIs define
this asynchronous stop as draining queued output. The clock still caps progress
at supplied output/decoded source extent and keeps conservative underrun
invalidation. Raw time may be unavailable after stop; the completed drain API
state supplies the endpoint, not a raw-counter extrapolation.

Final range checks additionally keep the exclusive video end out of the retained
frame set. At2x, the intermediate drain version could publish PTS1.0 for a
[0,1) range; that failed-semantic observation remains in loops-double.jsonl.
The final scheduler guard excludes that next-range frame. No accepted production
range semantics were weakened.

Tempo transforms do not produce an exact mathematical duration ratio at EOF.
For one second of input,0.5x produces95040samples (nominal mapped clock0.99),
1x48000,2x24384 (clock capped to source1.0). The final contract distinguishes
protected played-sample progress from **drained decoded-source endpoint1.0**.
It never fabricates output samples to make the nominal clock reach an endpoint.
After actual drain, all48000decoded source samples are accounted for and the
range policy uses the exact source endpoint. This separate endpoint authority
supports truthful rebasing while preserving the conservative causal clock.

## Measured final matrix

The protected clock passes10917 independent assertions on native rows:
protected<=supplied, monotonic within epoch, source mapping, success statuses,
stale protocol rejection, zero knowingly invalid scheduling, drain and loop
identity. These are not simulated ledger assertions; the prior9300simulated
checks remain separately labelled. Rendering is GPU-completed RenderReady,
not actual Avalonia UIAccepted or physical scanout.

- Actual withheld-refill underrun: supplied14336; raw34972; protected13765;
  IsRunning1, invalid, no eligible scheduling. Detection is conservative when
  no queued buffers remain; it is not an exact physical speaker silence time.
- New epoch recovery at source0.286770833 in27.858ms; paused source delta0;
  seek/rebase and stale-generation rejection work. AV recovery128.209ms includes
  deliberate100ms delay;145frames, max absolute backend wall offset8.146ms.
-0.5x:300frames/20s, max absolute wall offset9.854ms, p95 8.625ms.
-1x:600frames/20s, max8.771ms, p95 6.708ms.
-2x:899frames/15s, max8.104ms, p95 6.583ms.
- Live1→2→1→0.5→1: origins bind exactly to protected source. Recreation
  times29.027/25.383/24.065/32.030ms; last-to-first RenderReady gaps
  75.135/74.248/82.435/95.362ms. These are not measured audible gaps.
  Post200ms maximum offsets7.604/7.813/9.083/8.115ms.
- Eight1x graceful rebases: source end1, new generation, first audio/video0,
  stale rejection, max backend wall offset8.313ms. Restarts19.947–30.788ms;
  RenderReady gaps124.617–138.910ms include truthful drain wait.
- Final exclusive-range checks:3half/3one/6double-speed loops, all stop/drain
  before rebase, source endpoint1, decoded48000, last videoPTS0.966666667,
  first video/audio0. No frame at or beyond exclusive end1 is retained.
- Final stress12cases/24starts all succeed with pause/recreation and zero
  counted queues after each disposal. Prior successful bounded cases and failed
  locked starts remain separate. RSS initialization/caches are not a leak-free
  proof. Process exit and final cleanup independently end native ownership.

## Disposition and limits

Recommend **technical G1 PASS for the selected bounded architecture proof**:
controlled streaming FFmpeg + Metal/CoreAudio + IOSurface + future Avalonia
adapter is credible. Actual causal clock, truthful protected underrun state,
new-epoch recovery, speed continuity, drained source endpoint and exact loop
range identity now have native evidence. The start failure is bounded to the
observed unavailable native-output environment, with successful owner-unlock
recovery across independent APIs, rather than concealed as a harness success.
No unresolved result demands changing the selected backend architecture.

This is a recommendation, not recorded owner acceptance or production readiness.
Remaining packaged acceptance includes physical audible quality, background
lock/session transitions, device changes, sleep/wake, broader corpus and actual
Avalonia UIAccepted integration. Startup must run off the production UI thread,
fail closed while output is unavailable, avoid treating IsRunning/start0 alone
as causal progress, and use bounded recovery. Production realtime callback
allocation/mutex policy and long resource soak are later implementation work.
The16–28engineer-week estimate remains unapproved, excluding UI extraction and
broader packaging. No numerical product tolerance was silently approved.

## Owner listening command

```sh
python3 "${X1_WORKSPACE}/tools/X1AudioCompletion/output-qualification/listen.py" --data-root "${X1_WORKSPACE}/work/owner-listening/data-root"
```

Set X1_WORKSPACE to the independent task checkout. The script prepares an
original40-second rhythmic four-note AAC fixture using the OS encoder, rebuilds
the same pinned minimal LGPL dependency/harness, and plays0.5/1/2x, four live
changes and repeated loops. Queue gain0.5 is opt-in via X1_AUDIBLE=1; global
system volume/device/rate are unchanged. It supplies task-owned data roots and
removes its dependency/harness builds on exit. Decoder/tempo preparation passed
without audible playback. Subjective pitch/click/continuity remains Jeremy's
acceptance; no subjective pass is recorded. The separate pure-tone afplay API
success also has no owner-audible confirmation.

X3 remains stopped; accepted UIAccepted, independent resource release and
same-token capture handshake unchanged. Do not merge #379 before owner review.
