# 4K scheduling RCA

## Measured cause and bounded change

Accepted G1b remains unchanged (~27.3 acknowledged fps with scheduled skips). This derivative instrumented its same decoder/Metal/LUT path. A 12-second native-drawable baseline produced 321 frames /12.032s =26.68 fps, 38 scheduled skips. Mean decode call8.61 ms, producer wait3.70 ms, acknowledgement wait22.01 ms (including consumer completion1.06 ms). Decode and blocking presentation acknowledgement are serialized. Mean render26.26 ms plus decode exceeds the33.33 ms cadence budget. nextDrawable acquisition ~.09 ms is not the measured bottleneck; no evidence blames Avalonia.

For backend capacity only, replace the separate native drawable/physical-callback wait with a **real GPU blit snapshot**, ready-event wait and release signal, and completion wait. Native IOSurface producer, hardware decode and exact frame record remain. The copy costs are included; no zero-copy claim. UIAccepted/actual Avalonia are not implemented here. No asynchronous decode-ahead optimization is necessary to establish this bounded capacity result.

60 seconds each, H.264 yuv420p/VideoToolbox NV12,3840×2160,CFR30 fps,B-frames3,GOP60. The two-second accepted synthetic bitstream is stream-copied into a140-second continuous-PTS fixture, avoiding two-second reopen/restart overhead; repetitive content does not qualify arbitrary real media. Fixture commands, ffprobe characteristics and hashes are in FIXTURES.json. Source cadence at1x30 fps, no60/120 fps4K support inferred.

- Color on:1800 frames,29.9948 render-ready fps,0scheduled skips, decoded1801, queue depth1. CPU10.07% of one core. GPU p954.303 ms, producer+copy render p956.932 ms. RSS98.07–115.72 MB.
- Color off:1800,29.9946 fps,0skips, decoded1801, queue depth1. CPU9.86% one core. GPU p952.176 ms, render p954.821 ms. RSS98.01–115.80 MB.
- 1080p Color on:450/15.009s,29.9827 fps,0skips; render p953.347 ms.

CPU is process user+system divided by measured wall time, not system CPU or GPU utilization. GPU timings are producer command-buffer GPUStart/End, not VideoToolbox internal timing or a GPU trace. Native loops are serial; backpressure slots remain bounded3. Memory samples include predecoded audio, decode surfaces and textures. This is a60-second capacity interval, not a leak soak. Skips count decoded candidate advances that were bypassed by the scheduler, not physical display drops; no separate hardware display deadline counter exists.

The4K fixture uses an independent native audio sample clock with gain0; its repeated40-second PCM clock input is not an end-to-end source-audio causality qualification. AV_SYNC_RESULTS uses the actual audio-sync fixture/decoded PCM for shorter segments. Final UI composition capacity is separately accepted X3 evidence, not multiplied into a new end-to-end FPS claim.

## Instrumentation correction

Initial derivative accidentally initialized first-frame decode duration to the absolute monotonic timestamp. Raw values remain archived; analysis explicitly excludes values>=1000 ms for decode_ms only, and records excluded counts. No other metric is discarded. Final source initializes it to0; a5-second corrected repeat produced150 frames,29.918 fps,0skips. Initial decoder/open startup latency was not independently measured. An additional render-delay/AudioQueue run failed at queue start with CannotStart(-66681); preserve it, do not count it as throughput evidence. A separate GPU-only delay check succeeded.

## Conclusion

Blocking native presentation acknowledgement is the principal measured serial-harness limitation. Strong logical UI acceptance semantics can decouple physical-display observations, but the new callback still needs actual X3 adapter qualification.4K backend render-ready capacity is credible for this target. Streaming audio causality/underrun/rebase remains a material G1 question, independent of this result.
