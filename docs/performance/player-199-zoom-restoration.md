# Local #199 zoom and fullscreen follow-up

Jeremy confirmed the interaction delay is gone, but reported hidden controls after
fullscreen exit and repeated magnification when combining 100% zoom, Previous Frame,
and pan. The immediate input and speed/cadence paths are unchanged.

## Zoom diagnosis

Flyleaf's D3D live viewport sets a clipped source rectangle when zoomed or panned.
Snapshot capture previously replaced only destination/output rectangles. That baked
the live source crop into a native-sized bitmap, which the retained WPF image then
zoomed again. The local Flyleaf correction saves the source rectangle, uses the full
visible source for snapshot rendering, and restores all live rectangles in `finally`.
It keeps source crop, rotation, and Color processing in the renderer.

The 1920x1440 native regression failed before the dependency fix: the same paused
frame's capture pixels changed after zoom/pan. It passes with the corrected package.
It also steps backward from two seconds repeatedly, pans, checks decreasing timestamps,
and verifies both the native viewport and transformed retained image stay 1920 pixels wide.

Flyleaf source commit: `a9ca2937a95952790efa87b94f56cd735024d52c`.
Local package: `3.11.2-lightflow.3`; hash is pinned in `dependencies/flyleaf.json`.
Rebuild with `scripts/Build-FlyleafPackage.ps1 -SourceDirectory artifacts/validation/flyleaf-199`.
When testing a new normalized package, refresh old bin copies: their newer filesystem
timestamps can cause MSBuild to retain the previous DLL despite restoring the new package.

## Fullscreen restoration

The reported missing chrome has not reproduced in deterministic WPF/native tests.
The previous tests checked width but not native position/height. They now verify the
native origin and height match the media view after every exit, plus visible header
and transport. Paused/playing, normal/maximized windows, and all three exit paths pass.

Restoration now hides the Player/native child throughout reparenting/window changes,
restores chrome after returning to the normal host, and completes layout before showing
the Player again. Hands-on confirmation of this mitigation remains necessary; passing
layout tests alone is not proof that the reported visual corruption is eliminated.

## Validation

Focused playback, Home context, and packaging checks: 36 passed. TRX evidence is in
`artifacts/validation/199-zoom-restore`. No computer control or publication was used.

Final full Release suite: **1,808 passed, zero failures/skips**. The first full run
passed 1,807 checks and caught a fixed-delay assumption in the new stepping test.
The test now waits for the settled predecessor timestamp; the full rerun passed.
