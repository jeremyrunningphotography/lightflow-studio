# Native output RCA — 2026-10-07

Scope: owner-authorized ultra-narrow continuation of Draft #379 from main
7dec8f7372ba0af06bc15969ec0a976de7984900 and proof head
044a9fb8a172a6120235a945fe24104327f89948. Existing failed evidence is unchanged.

## Gate A: A4 — failure no longer reproduces; cause unresolved

Independent `/usr/bin/afplay` completed a deterministic two-second 440 Hz tone
(48 kHz stereo signed Int16 WAV, amplitude 0.08), exit 0 in 2.917 seconds.
This establishes API/tool playback success, not physical sound. Owner-audible
confirmation is recorded separately if received.

The pure generated-PCM AudioQueue started in 55.556 ms, received 23 callbacks,
and stopped/disposed with status 0. Its ASBD exactly matches the device's current
advertised physical/virtual format: 48 kHz stereo Float32 interleaved packed
linear PCM, flags 9, 8 bytes/frame and packet, 1 frame/packet, 32 bits/channel.
Three 4,800-frame buffers (38,400 bytes each) were enqueued before start;
Apple-managed callbacks refill buffers. No FFmpeg, UI, video or scheduler.

Five subsequent silent fresh-process starts all succeeded (33.272–57.306 ms),
each with 30 callbacks and 27 refills. Mono, then 1,024-frame buffers, then
callback-mark/control-thread refill were introduced sequentially; all passed.
The unchanged X1 harness then passed six start/pause/recreate cases (12 starts).
This bounded sample permits the prepared measurement continuation under A4;
it is not a long-term reliability claim or a deterministic recovery mechanism.

The unchanged AudioUnit baseline now delivered 303 callbacks / 142,532 frames
in 3.238698 seconds. The frozen AudioQueue baseline also ran successfully.
No device/rate/default/volume/service configuration was changed, no owner
process was terminated, and no OS/audio-service restart was requested.

No smallest causal failure delta was found. Current success cannot explain the
historical valid-buffer -66681 failures or the prior start-success/zero-callback
condition. The two failures remain separate observations: neither reproduces.
The Apple SDK defines kAudioQueueErr_CannotStart = -66681 and describes only a
queue problem preventing start. This is not a diagnosis of unavailable hardware,
format mismatch, callback ownership or sequencing. Prior device-start-success
and 15-second I/O timeout remain the strongest historical runtime context.
Retry/recreation failed historically and succeeds currently; no universal retry
or service-reset policy is inferred. Broader architecture remains credible.

## Environment

HAL default device 72: Apple built-in output (`bltn`), alive 1, initially running
0, 48 kHz, one stereo output stream. Physical and virtual format match; advertised
rates 44.1/48/88.2/96 kHz. HAL also sees another Apple built-in device without
output streams and a continuity-capture device (`ccwd`) without output streams.
Names, UIDs and model identifiers are normalized; manufacturer retained.
ParrotAudioPlugin.driver exists in /Library/Audio/Plug-Ins/HAL, but the snapshot
shows no virtual output device or evidence that this plugin caused the failure.
No task audio process existed before testing. Native execution used approved
unsandboxed shell access, an ad-hoc command-line binary, no app entitlements.
The earlier sandbox InvalidDevice -66680 is distinct from native -66681.

## X1 versus minimal setup

| Dimension | Minimal | Unchanged X1 candidate |
|---|---|---|
| ASBD | stereo Float32, flags9, bytes/frame/packet8 | mono Float32, flags9, bytes/frame/packet4 |
| Rate / packet frames | 48k / 1 | same |
| Buffers / initial supply | 3 × 4800 frames, 14400 total | 3 × 1024 frames, 3072 total |
| Callback ownership | NULL runloop, Apple internal thread; refill directly | same dispatch; mutex, marks completion, control-thread refill |
| Listeners | none | none |
| Start / prime | enqueue all, Start(NULL); no prime | feed/enqueue then Start(NULL); optional prime disabled |
| Feed / framing | generated Float32 PCM; fixed blocks | incremental AAC/aformat/atempo; bounded FIFO; up to1024 samples/block |
| Packet descriptions | none (PCM) | same |
| Reset / flush | none | no AudioQueue reset/flush; decoded source/filter replaced per epoch |
| Stop / dispose | immediate stop then dispose | same; new queue each epoch/resume |
| AudioSession assumption | none | none |
| Thread / lifetime | C main/control plus internal callbacks; stack/global state until disposal | C++/Objective-C; queue state retained until blocking disposal; proof telemetry allocation in callback |
| Device selection | unchanged default | same |
| Queue gain | default1; quiet tone or zero PCM | explicit0 |
| Clock | raw sample-time/running observations | generation, supplied/completed extent, protected causal source mapping |

Mono, small buffers and deferred refill did not reproduce failure. Gain-one
frozen baseline previously failed and now succeeds, so gain alone is not an
established cause. Full unchanged X1 success is a composite observation, not a
single-delta causal test. No larger proof-harness implementation was changed.

Matrix raw logs contain an early fixed stereo header in all variants; the
`actualChannels`, `actualFramesBuffer`, and `serviceCallbackModel` rows and
command arguments are authoritative for those variants. The final source fixes
that telemetry header. No failing evidence was rewritten to correct it.

## Prepared native measurement continuation

See native-analysis.json and qualification/ for measured rows and offline
assertions. These are actual CoreAudio/FFmpeg/Metal backend runs; rendered frames
are GPU-completed RenderReady snapshots, not Avalonia UIAccepted or physical
scanout. No new owner timing tolerance or subjective acceptance is inferred.
The current native sample does not bound recurrence of the historical outage.

## Reproduction

From the independent task root:

```sh
cd ${X1_WORKSPACE}
mkdir -p work/native-output-rca
clang++ tools/X1AudioCompletion/native-output-rca/environment.mm -framework Foundation -framework CoreAudio -o work/native-output-rca/environment
work/native-output-rca/environment
python3 tools/X1AudioCompletion/native-output-rca/ordinary.py
clang -std=c11 tools/X1AudioCompletion/native-output-rca/minimal.c -framework AudioToolbox -o work/native-output-rca/minimal
work/native-output-rca/minimal
python3 tools/X1AudioCompletion/native-output-rca/matrix.py
```

Ordinary/minimal commands produce a short quiet tone without changing global
volume. Silent matrix uses generated zero PCM. Run with ordinary native audio
access; sandbox device visibility failures are not capability results.

Restore pinned FFmpeg and build using the parent REPRODUCTION.md, then run
x1_start.py and qualify.py. Those tools write fresh native-output-rca evidence,
not historical raw files. For another audit preserve this directory first or
use a fresh clone. analyze.py validates measured rows only; the original
verify.py continues to validate preserved feed and failed-start evidence.
No packaged Windows/Mac Lightflow acceptance executable is produced by this
research-only slice.

## Final native results and recommendation

**G1 REVISE / UNPASSED.** A4 permits useful native measurements but does not
establish the historical startup cause or a bounded recovery policy. No selected
backend replacement is recommended. No larger harness fix was made.

- Actual starvation supplied14,336 samples; end raw34,975, protected13,764,
  invalid, IsRunning1, completed14,336. Detection froze before supplied extent;
  new epoch recovered at source0.28675 in27.932ms. Pause source delta0.
- 10,668 measured-row assertions verify protected<=supplied, monotonicity per
  epoch and source=origin+protected/48k×rate. These do not assert audible output
  latency or mathematical identity with physical played samples.
- 0.5×:300frames/20s; max absolute backend wall offset9.000ms, p95 8.250ms.
  1×:600frames/20s; max9.688ms, p95 7.063ms.
  2×:899frames/15s; max10.271ms, p95 6.896ms.
- Transitions1→2,2→1,1→0.5,0.5→1 rebase exactly at protected source;
  queue/filter recreation times29.023,25.159,23.894,31.543ms. Measured last-to-first
  RenderReady gaps74.677,76.536,84.295,95.169ms (not audible gaps). Subsequent
  maximum absolute wall offsets8.615,7.979,9.000,9.021ms after200ms.
- AV recovery restart129.125ms includes deliberate100ms withholding; first
  post-recovery videoPTS1.033333, audio source1.039854, origin1.033270833.
  No knowingly invalid schedules; later maximum offset8.813ms.
- Eight loop rebases: first video/audio source0, new generation, stale protocol
  rejection. Restarts37.989–41.974ms; RenderReady gaps47.277–52.335ms;
  max absolute backend offset10.313ms. **Loop endpoint is not qualified:** every
  source-ended event occurred at0.980104–0.981188s despite48000samples supplied.
  The clock's completed-buffer/no-outstanding branch invalidates before raw time
  reaches supplied extent, then loop driver immediately disposes/rebases. This
  risks truncating roughly18.8–19.9ms of the source tail. Buffer availability
  callbacks are not physical endpoint acknowledgement. Conservative starvation
  is safe for the causal upper bound, but its use as loop completion needs a
  separate credible endpoint policy. Do not weaken existing endpoint semantics
  or claim full loop acceptance from these rows. No broader redesign/fix here.
- Final12stress cases/24starts, in addition to initial6cases/12starts, all
  succeeded including pause/recreate. Zero counted queues after each disposal.
  RSS8.68→31.80MB includes initialization/caches; it is not a leak-free proof.

Fault matrix: actual withholding/freeze/recovery/pause/seek measured; stale
protocol rejection measured with native queues disposed/joined; decoder EOF
and repeated loop rebase measured with endpoint limitation; .5/1/2 rates and
four live transitions measured. Device loss/switch, sleep/wake and recurrence
of prior -66681/zero-callback remain unqualified. Prior negative files unchanged.

X3 remains stopped; accepted operation-specific UIAccepted + independent release
fencing and same-token capture contract unchanged. This backend-only matrix does
not replace future actual Avalonia callback integration. G2 and #370 untouched.

Remaining architecture-proof blockers: bound historical native startup failure,
and establish truthful drained-source/loop endpoint behavior without tail loss.
Deferred packaged acceptance: representative audible pitch/click/rate/loop
quality, physical device switches, sleep/wake, broader corpus, final UI adapter,
signing/notarization and production realtime/thread safety. No production Player,
Flyleaf/dependency/Catalog changes or M4+. Estimate remains16–28engineer-weeks,
unapproved and excludes UI extraction/broader packaging.

Owner may repeat the ordinary output check with:

```sh
cd ${X1_WORKSPACE}
python3 tools/X1AudioCompletion/native-output-rca/ordinary.py
```

This checks a quiet two-second tone only, not Player subjective acceptance.
The measurement harness intentionally retains gain0. A representative audible
Player acceptance application is not supplied or claimed ready while G1 is
REVISE. No owner action changing system configuration is prescribed without an
established causal test. Stop for owner review; do not merge #379 or resume X3.
