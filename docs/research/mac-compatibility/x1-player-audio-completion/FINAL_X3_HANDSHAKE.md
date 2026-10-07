# Final operation-specific X3 handshake — candidate implementation contract

Owner-accepted C semantics from #378 remain authoritative. This document clarifies the handoff; it does not change prior accepted contracts or claim the audio gate passed. Do not resume X3.

Decoded: immutable rational sourcePTS/frame identity and session generation. RenderReady: completed same-token rotation/Color pixels, Color revision identity, BGRA8 IOSurface lease and producer MetalSharedEvent timeline/value. UIAccepted: X3's semantic callback confirms that the matching current-generation token/revision was accepted by its render-tree presentation transaction. That callback publishes current/paused identity and settled step/seek; stale generation/revision cannot publish. Review/authored timing/In/Out/markers/Subclips use the accepted token's sourcePTS, never the audio sample clock as a frame substitute.

X3 must carry token generation/revision through surface import, clip/overlay/input/render-tree integration and signal logical acceptance only for the selected committed token. Pending decoded/render-ready work is not current. Color/Compare produces a new RenderReady revision and then UIAccepted. Keep surface/timeline/device and resize/scale lifecycle rules from the accepted interop contracts; no naive child-window fallback is introduced.

Resource-release fencing is independent: wait for producer-ready timeline, retain surface/token lease through UI GPU readers and capture, signal/join actual completion before reuse. UIAccepted alone cannot recycle the texture. Capture reads GPU-completed orientation/Color pixels belonging to the same accepted token/revision and retains its lease through readback; it must not sample whichever new texture happens to be current later.

Audio queue epochs govern scheduling eligibility separately from immutable source frame identity. An invalid causal clock holds the accepted frame; successful recovery/rebase cannot publish stale source work. A new audio epoch does not authorize a mismatched UI token.

X3 does NOT need physical scanout timing or speaker/display synchronization to satisfy the blanket semantic contract. Surface-update/resource Task completion is not automatically UIAccepted; X3 must wire/test the explicit semantic callback. Actual Avalonia integration is future owner-authorized qualification, without weakening same-token capture/semantic timing.
