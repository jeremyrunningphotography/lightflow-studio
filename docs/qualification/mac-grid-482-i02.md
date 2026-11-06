# LF-MAC-DEV-038 — Grid Iteration 02

November 6, 2026. Existing clone/branch/identity retained. Owner accepted I01 at
`8b3cb49`: Preview, Info, Hybrid, switching, resize/Right Panel, scrolling, navigation/
Refresh, real still/RAW/video and Locations/Catalog/thumbnail performance. Arrow,
Shift+Arrow and Command+Arrow selection semantics were explicitly confirmed working.
Only selection/focus rendering and permanent mode-control placement change here.
No PR, qualification agent, merge, closure, release or #483–#485 work is authorized.

## White keyboard border: cause and correction

A pre-fix real-control test found the intended `Chrome` Border absent. Avalonia's
style key was `Lightflow.UI.BrowserGridCard`, while scoped card styles selected
`Button.lf-grid-card`. The derived card therefore rendered the fallback Fluent
`ContentPresenter#PART_ContentPresenter`, bypassing the custom template/focus rules.
The property-level orange selection brush alone did not govern that rendering.

The card now explicitly uses the Button style key so its scoped template and focus
rules apply. Selected hover has explicit orange precedence; keyboard focus adds
border thickness without replacing the orange selected outline. Focused unselected
cards use a neutral outline and the ordinary unselected surface. No selection
handler, shared selection model, keyboard modifier interpretation, card dimensions,
Preview identity/scheduler, Catalog, discovery, decoder or Windows source changes.
The existing template/focus correction is scoped to `lf-grid-card`.

The regression checks actual rendered `Chrome.BorderBrush`, mouse selection,
keyboard arrows, Shift range, Command+Arrow, pointer overlap, mode changes,
focused-unselected distinction, Refresh, resize and scroll/recycling. Intermediate
test-harness failures (API arguments, typed icon binding and assumptions about
Command-selected identity/recycled containers) remain in the evidence; final tests
use the existing selection semantics and visible recycled card lookup.

## Windows status-bar comparison

Windows `MainWindow.xaml` lines 1932–1959 place a horizontal group at the far right:
Preview, Info, Hybrid; then a separator, regeneration and thumbnail-size controls.
`BrowserViewModeButtonStyle` uses 27×24 buttons, four-pixel padding, 16×14 vector
artwork, quiet backgrounds, rounded four-pixel corners, warm active border/icon,
hover/pressed feedback and keyboard focus. Tooltips and automation say Preview view,
Info view and Hybrid view. Grid/Details selection is separately in the upper
Browser toolbar (lines 1077–1082), not in this bottom group.

Mac now matches that icon order, size, drawing vocabulary and status-bar alignment.
One group is appended after the existing Browser count/status indicators at bottom
right; the upper Preview/Info/Hybrid toolbar row is removed. It remains outside the
Browser canvas/Right Panel width so those panels do not move the controls. An empty,
collapsed trailing `BrowserThumbnailSizeSlot` is the #483 integration seam; adding
size controls to its right will not relocate modes. No size slider, regeneration
operation, Grid/Details control, preference or persistence is implemented here.
No duplicate controls and no unrelated status-bar redesign. Tests cover 1120/1440/
1800 widths, Right Panel open/closed, active state, accessible names/tooltips, vector
foreground binding and bounds. The original named Windows screenshot remains
unavailable locally; composition follows shipping source, not an invented image.

## Validation and handoff

Final Mac UI Release suite: **102 passed**, 0 failed/skipped. Includes all existing
I01 UI regressions and two focused I02 real-control tests. New source introduces no
dependency or schema changes. Package/native GUI/preflight/integrity observations,
exact commit/tree/build hashes and retained-Catalog compatibility receipt are in
the portable I02 ZIP. The existing #482 retained Lightflow Test Catalog is preserved;
automated and GUI smoke checks use separate disposable roots and synthetic media.
Owner I01 acceptance remains valid except the two corrected presentation areas.

## Focused owner I02 acceptance

1. Arrow selection: orange selected border remains consistent — PASS/FAIL.
2. Shift+Arrow: range works and leading selection stays orange — PASS/FAIL.
3. Command+Arrow: existing behavior and styling remain correct — PASS/FAIL.
4. Focused but unselected: distinguishable without false selection — PASS/FAIL.
5. Status placement: modes occupy permanent bottom-right location — PASS/FAIL.
6. Mode controls: Preview/Info/Hybrid icons, switching and active states — PASS/FAIL.
7. Layout: resize and Right Panel keep status controls usable — PASS/FAIL.
8. Regression: nine accepted I01 behaviors remain intact — PASS/FAIL.

Only focused owner feedback is requested; no full I01 repetition unless a material
regression is discovered. Wait for owner I02 acceptance before qualification or PR.
Historical maintenance: **Not warranted** for this bounded visual correction;
existing Chronicle/Source Index milestone entries stay intact.
