# Visual Index #293 validation

Implementation: shared contextual Right Panel, 12/24/48 temporal samples (24 default), Original-color position
frames shared with markers, passive nearest-position indication, workspace count/tab continuation. See the
architecture section and authoritative issue #293 for policy and the future #287 orientation seam.

Automated tests construct detached controls and fake Player backends without showing a window. Additional real
FFmpeg tests use the existing packaged renderer/probe boundaries. Tests cover deterministic bounded plans,
short/unknown/changing duration, unique nominal slots, exact seeking, source/density/visibility cancellation,
uncooperative stale completions, stable cards, responsive measure, workspace persistence, shared cache identity,
offline hits/misses, Preview deletion/rebuild, temporary cleanup, and maintenance admission.

The legacy complete suite and packaged smoke run on a task-owned private Windows desktop that is never switched
to the user's input desktop. This is validation isolation for #284, not an implementation of that broader issue.
All dependency downloads, logs, captures, packaged binaries and test data belong to this independent clone.

Hands-on review after automated validation:

1. Launch the packaged executable with this clone's explicit isolated data root.
2. Open a short and a longer video, then Right Panel > Visual Index. Check progressive frames, Original color,
   readable timestamps and the grid at narrow/default/wide panel sizes.
3. Activate frames by pointer and keyboard. Confirm the existing Player seeks and Browser/review-set context
   remains intact. Play normally: the Current indication should move without scrolling the panel.
4. Change 12/24/48 density and rapidly switch assets while frames generate. No old frames should appear in the
   new plan. Close/reopen the panel and restart: count/preferred tab should continue through workspace state.
5. After generating frames, disconnect a source and reopen it. Cached frames should remain useful; missing
   samples should say Unavailable. Unknown duration should remain an explanatory empty state.
6. Clear Previews through existing Settings maintenance and reopen Visual Index online; frames should rebuild.
   Check normal Inspector, markers, Subclips and Jobs alongside the added surface.

No merge or issue closure is authorized by automated validation. Jeremy's architecture and packaged visual /
functional acceptance remain required.
