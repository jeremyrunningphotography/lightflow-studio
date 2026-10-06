# Acknowledgement disposition C — another explicit handshake is required

The unchanged accepted [X1 contract](../x1-player-g1b/PRESENTATION_INTEROP_CONTRACT.md)
requires a final composed-frame acknowledgement associated with offer serial and UI-host
 generation. Actual import/composition works, but pinned Avalonia 11.3.8 exposes no public
CAMetalDrawable presented-time callback or render-target-to-offer serial mapping.

| Boundary | Available evidence | Meaning |
|---|---|---|
| ImportCompleted | Real image/event imports completed | Native resources imported; not readiness or presentation |
| Producer ready event(value) | GPU producer encodes signal after writes | Matching surface writes ready |
| Surface update Task | Real delayed-producer Task 7.81ms, release 211.18ms | Server snapshot update enqueued/completed; GPU can still be waiting |
| Release event(value) | UI GPU queue signals after snapshot work | Import snapshot access finished; explicit frame/capture leases still constrain reuse |
| Composition visual snapshot | Twelve captured patterns decoded to expected serial | Offscreen composed pixel identity, not the window drawable or scanout |
| Composition commit/update callback | Source/API exists | UI/server processing boundary, not display acknowledgement |
| Native drawable presentation | Avalonia native session presents its drawable | Public callback/time/serial association unavailable in pinned seam |
| Physical scanout | None | Not claimed; even nonzero native presentedTime is Core Animation evidence, not panel sensing |

Primary source pinned at Avalonia commit
`6dd9eb473b74a56cc42e5bc118cfe918b48a940b`: Compositor.cs,
CompositionDrawingSurface.cs, ServerCompositionDrawingSurface.cs,
SkiaMetalExternalObjectsFeature.cs and native/Avalonia.Native/src/OSX/metal.mm.
The native Metal session destructor creates a command buffer, calls presentDrawable
and commits; it does not export addPresentedHandler/presentedTime or a frame token.
CreateCompositionVisualSnapshot renders an offscreen target and makes a non-affined
snapshot. That inspection plus runtime fence ordering rules out A.

B is insufficient: X1's standalone native drawable is not the final Avalonia-composed
drawable and cannot acknowledge overlays, viewport, rehost or visible UI state.
D is not established: a render-target extension/upstream API could provide the seam.

Proposed owner-review handshake: carry immutable offer generation/serial and UI-host
 generation into the same render transaction that selects its snapshot; attach those
 tokens to the final drawable submission; report nonzero Core Animation presentedTime
 only if those generations still match. Preserve Pending/LastConfirmed/Unavailable;
 invalidate current visible authority on detach, close, rehost, visibility/device loss;
 zero/missing callbacks/timeouts cannot publish. Coalescing must bind the actually drawn
 snapshot rather than the latest mutable offer. This needs a supported Avalonia/native
 render-target callback or carefully pinned extension and concurrent fault/cadence proof.
It is a proposed addition for owner/X1 review, **not a silent change to PresentedFrame**.
Its support/engineering cost is unresolved, so this slice returns I3, not forced I1/I2.
The harness never sets PresentedFrame/visiblePresented true or executes authoring actions.
