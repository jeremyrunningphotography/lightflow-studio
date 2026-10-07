# Bounded Player completion proof

**Recommendation: G1 REVISE.** Acknowledgement requirement **C**, operation-specific logical renderer/UI acceptance plus completed same-token capture; physical scanout is not a requirement established by current Windows semantics. Preferred controlled FFmpeg + Metal/CoreAudio backend and accepted IOSurface/Avalonia architecture remain. Tested stock libmpv remains insufficient for this contract; no universal libmpv impossibility claim.

Source main f07053588912cf3ee71ba9e26273021f58764420; independent Agent-X1-Player-Completion clone, branch codex/368-mac-player-completion. Mac14,9 M2Pro32 GB,macOS26.6.2/25G83,arm64,CLT clang21. No production linkage, Windows execution, owner data or system output-device/sleep change. Native runs serial, AudioQueue gain0. No .NET/Avalonia runtime added to this backend slice. Prior G1/G1b/X3 evidence is unchanged.

## What this slice settles

Windows inspection supports renderer-owned decoded PTS and completed renderer capture, not physical scanout. See ACKNOWLEDGEMENT_SEMANTICS.md and OPERATION_STATE_MATRIX.json. Proposed states:Decoded → RenderReady → UIAccepted. Source PTS remains rational immutable identity; lease/release is separate. Pending work never becomes current. In/Out/markers/review/Subclip use the accepted token's source timing; source-resolution capture additionally requires its completed orientation/Color pixels. Optional physical timing must never become a second authority. PRESENTED_FRAME_FINAL_CONTRACT.md proposes the narrow version2 backend/UI semantic publication callback for later owner-authorized X3 work; old contracts are untouched.

Backend GPU-completed semantic fixture covers pause/forward/reverse/seek/20rotation-Color-Compare combinations/capture, rejects cancelled stale generation and preserves authored PTS.25 capture records have matching serial/published token and CPU pixel oracle max1 channel error. The protocol oracle has42accepted/975rejected tokens and630operation identity assertions, explicitly **simulation**, not actual Avalonia UI acceptance. Prior600/600long reverse, hardware/software Color/capture and exact stepping tests are reused, not rerun. Current-frame identity is credible at backend/protocol boundaries but not yet qualified through the final UIAccepted callback during every operation.

4K RCA demonstrates the serial native presentation wait, rather than a fundamental decode/Metal limitation. Color on/off each1800/60s,~29.995GPU-render-readyfps,zero scheduled skips. CPU~10% one core, RSS~98–116 MB; producerGPU/render latency in4K_PERFORMANCE_RESULTS.json. Baseline26.68 fps/38skips over12s,~22 ms acknowledgement wait. ExactGPUcopy count for Avalonia stays unknown; this derivative explicitly adds aGPUblit snapshot and includes its cost. UI composition and physical scanout remain separate accepted/unqualified evidence respectively.

## Integrated timing and remaining failures

Source-video PTS minus AudioQueue sample-clock at completed backend render:

|Rate|Duration|Frames|Mean source-ms|Equivalent wall-ms|First5s→last5s source-ms|
|---|---:|---:|---:|---:|---|
|.5x|20s|300|-3.601|-7.202|-3.444→-3.524|
|1x|20s|600|-4.409|-4.409|-4.115→-5.172|
|2x|15s|899|-9.834|-4.917|-9.287→-9.878|

Zero scheduled skips in these three segments. These are sample-clock/render readiness comparisons, not measured speaker/scanout sync. Old2x-40.63 source-ms was amplified by serialized physical callback delay and by expressing the error in source-time at2x. The new result reduces that scheduling component. It does not establish exact audio content PTS: native atempo burst-envelope oracle finds at.5x -5.5 source-ms,1x0,2x-4/-9/-12/-11 source-ms. Simple processedSamples×speed is therefore an approximation. Source sample origins, filter buffering and streaming segment mappings remain required.

Live transitions1→2→1→.5→1 were actually issued mid-session, but use queue recreation and predecoded atempo. Restart costs63.45/29.73/126.69/32.02 ms. Source origins/generations explicit, no scheduled candidate skip in the bounded run; pitch/audibility and genuinely streaming same-queue transitions remain unproved. A queued-audio tail cannot be dismissed as instant pause: one120 ms pause injection allowed58.37 ms sample-clock advance. Freeze/protected-state policy must account for queue latency, not falsely claim immediate physical silence.

Seven one-second loops each ended at.966667s and began at0 with incremented generation. Queue restart16.43–45.43 ms; final-to-first render-ready intervals67.22–88.94 ms. Predictable identity/rebase demonstrated; seamless or perceptually acceptable looping not established. Output muted, no click/gap listening oracle. Repeated-loop records remain in LOOP_RESULTS.json.

**Actual underrun gap:** all3AudioQueue refills withheld.12288 samples supplied(~256 ms); sample clock advanced from.18598to.49456s while IsRunning remained1. A timer/running flag can report source progress for absent audio. Stop/recreate rebased at.18598 and resumed clock.26983,7 callbacks; this is operational recovery, not proven causal underrun detection/played-sample accounting. Queue-depletion stop, decode starvation100 ms and render-completion delay120 ms are injected/simulated causes, separately labelled. One starvation recovery scheduling case skipped1candidate; no captured stale-generation authority violation established, but final UI publication is not exercised.

GPU-only delay retained an old surface hash unchanged while a new serial completed; dimension/surface replacement then captured with max0pixel error. Physicaldevice loss and actualdecoder starvation recovery remain unqualified. LaterAudioQueue start returned CannotStart(-66681); reason not established, no lock/device claim inferred. No disruptive device/sleep experiments forced.

## Gate and feedback

G1 remainsREVISE because source-audio/tempo causality, buffer-depletion detection/protected clock/rebase and credible continuous speed/loop recovery remain material architecture questions. The explicit native queue-start failure also needs bounded diagnosis. The final UIAccepted publication must be implemented/tested later by X3; it is no longer waiting for a physical-scanout API as a product prerequisite. X3 should preserve its accepted architecture and consume the proposed tokened logical acceptance/capture contract only after owner review, without resuming now.

Remaining packaged acceptance:physicalAVsync,perceptual loop/rate/pitch,output-device change,sleep/wake,cross-display/Spaces,broaderHDR/range/10bit/codec coverage,fullproduct controls/capture and minimumOS deployment. PriorSDRrotation/LUT/Compare/hardware/software evidence supports the backend architecture but does not qualify those broader formats. macOS 12 remains theinteropAPI floor only.

Recommend later candidate limits for owner discussion:exact identity/no stale publication mandatory; sustain tested30 fps4K cadence with zero unexplained skips; sample-clock offsets within one frame interval in **wall time**, drift within5 ms/min once stable, and explicitly classified rebase gaps. PhysicalAV/perceptual limits require a loopback/display oracle and owner agreement; these are not approved thresholds or current PASS criteria. Current short sample-clock segments do not prove5 ms/min. Estimate16–28 engineer-weeks remains uncertain, excludes shared UI extraction/broader packaging/full migration and is not an approved budget. This slice narrows presentation/scheduling risk but does not justify a numerical reduction.

Stop for owner review of Draft PR. #368/#366 remainOpen / In Progress; G1 unpassed, M0/M2 complete, acceptedP2/G2 unchanged, #370unchanged/G3 C unpassed. No production migration,M4+,TourBox/Resolve/release work or merge. No X3 resumption.
