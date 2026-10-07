# Final X3 handshake — audio qualification facet

X3 remains stopped. No message or #370 change is made.

Accepted IOSurface BGRA8 + MetalSharedEvent transport, exact immutable source PTS,
Decoded→RenderReady→UIAccepted operation-specific acknowledgement, independent
resource release and same-token capture remain unchanged. Actual Avalonia adapter
callback is still a future integration check, not simulated into these results.

Native audio proof now supplies a protected output-sample/source clock, explicit
invalid/depleted state, epoch/rebase on recovery/rate/seek, and a separate drained
source-range endpoint. UI scheduling may consume only eligible protected progress;
EOF loop transition uses actual drained-output completion and exact decoded-source
boundary. Buffer reuse is not a played endpoint. Exclusive next-range video frames
must not be retained. A tempo filter's finite output-duration ratio cannot silently
rewrite the shared source endpoint or create fictitious played output samples.

Locked-session native I/O unavailability is observed across independent playback
APIs; owner unlock restores it without changing this transport or backend. Packaged
background-session/sleep/device behavior remains later qualification. Technical G1
PASS is recommended for owner review; no production/UI migration is authorized.
