# ADR 0004: Player frame identity and resource ownership

- **Status:** Accepted bounded semantic contract and target direction; production integration unqualified
- **Date:** 2026-10-08
- **Authority:** Epic #366, accepted M0/G1/G2/G3 records and owner architecture-publication instruction; [evidence and status](../architecture/convergence-evidence.md).
- **Publication gate:** This documentation Draft PR requires explicit owner acceptance before merge. Accepted principles do not authorize implementation.

## Context

G1 accepted controlled FFmpeg/Metal/CoreAudio and G3 accepted actual Mac Avalonia composition. Earlier interop/component proofs had incomplete acknowledgement/audio integration. [Immutable contract/evidence index](../architecture/convergence-evidence.md) preserves their chronology.

## Decision

Preserve Decoded, RenderReady and UIAccepted as distinct operation-specific stages. Current/paused/settled authority uses generation-checked source-frame identity, decoded source PTS and matching UIAccepted token. Audio schedules candidates; it is not current-frame identity. Requested seek time and nominal FPS cannot substitute for decoded source PTS.

Color/Compare creates a render revision of the same source frame and requires matching acceptance. Capture retains GPU-completed oriented/Color pixels from that same accepted token. Independent release fencing and all retained leases govern reuse/disposal. UIAccepted is logical composition acceptance, not physical scanout; physical output remains a separate qualification concern.

Mac target follows controlled FFmpeg → Metal → BGRA8 IOSurface/MetalSharedEvent → Avalonia composition with CoreAudio. UI owns clipping/overlays/transforms/input; native adapters own resource/fence lifetime. Preserve protected causal clock, starvation invalidation, new-epoch recovery and drained/exclusive-range loop semantics. Exact operation matrix and interop fields remain in the accepted linked contracts, not redefined here.

Windows retains its accepted Flyleaf/WPF implementation during migration. Do not fabricate UIAccepted from its existing FramePresented event. WQ must qualify the Windows seam before production presenter integration; no new Windows backend is selected by this ADR.

## Alternatives considered

Naive child NSView/CAMetalLayer hosting failed shared composition requirements. CPU readback is a reduced-performance fallback, not the selected 4K path. Tested stock libmpv integration was insufficient, not universally impossible.

## Consequences

Production threading/callback safety, concurrency, long soak, Windows import, audible quality and physical AV remain unqualified. G1 offsets are RenderReady measurements, not approved product tolerances. Audio -66681 recovery bounds an observed session-associated state, not an internal OS cause/universal lock policy.

## Follow-up work

Separately authorize WQ and production contracts/implementation. Preserve [Player actions](../PLAYER_ACTIONS.md) and [shortcuts](../KEYBOARD_SHORTCUTS.md). Select numeric limits, broader corpus and lifecycle tests before release acceptance.
