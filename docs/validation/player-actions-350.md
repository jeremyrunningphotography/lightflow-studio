# Player range, review, Subclip and marker actions (#350)

Starting remote main: `5de78c9dc0d3e4c03d81a43c8758c455d5139c74` (accepted #349 / merged PR #355).
This slice extends the existing neutral action boundary; see [action policy](../PLAYER_ACTIONS.md).

## Deterministic acceptance evidence

- `PlayerReviewActionTests`: direct fake-controller invocation through the real Windows Player port for all five
  actions; exact ticks; cross-boundary replacement; restored/partial/full ranges; duplicate stable Subclip identity
  and reveal/selection; point-marker identity/order/no wrap/no markers; captured subset and paused destination In;
  Inspector transition guard; unavailable video/image member traversal; stale target rejection for every action;
  delayed Catalog mutation and resolver completion after replacement; single-flight Busy; native I/O/S/M repeat suppression.
- `PlayerActionDecodedFrameTests`: actual CFR/VFR decoded PTS after backward stepping, retained presentation,
  semantic In/Out with no frame-duration estimation; first-slice paused/silent/boundary stepping retained.
- `PlayerSemanticActionTests`: neutral assembly references, eight discoverable descriptors, invalid argument
  shapes/undefined enums, repeat/execution/phase metadata; prior playback/Color/session lifecycle tests retained.
- Existing Player lease, filmstrip, marker, range, Subclip, folder-tree #344 and keyboard-focus tests remain in
  focused/full validation. Zoom completion, Inspector successful/failed/cancelled Apply, editors/rename, closed/open
  dropdowns, sliders/menus/lists, deliberate keyboard Button focus, mouse Button completion and fullscreen remain covered.

Focused command:

```powershell
dotnet test .\LightflowStudio.Tests\LightflowStudio.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PlayerViewerHostLeaseTests|FullyQualifiedName~PlayerSemanticActionTests|FullyQualifiedName~BrowserReviewSetTests|FullyQualifiedName~SemanticController_" --logger "trx;LogFileName=350-focused-final.trx"
```

Full command uses Release `--no-build`, TRX and the established five-minute hang diagnostic. Companion uses
`node --test PremiereCompanion/*.test.cjs`. Neutral build uses `dotnet build Lightflow.Actions/Lightflow.Actions.csproj -c Release`.
Focused Release: 162 passed. Targeted source-assertion/Inspector/Button recheck: 23 passed.
Companion: 90 passed. Independent net8.0 build: zero warnings/errors.
Final full Release: 2,598 passed, zero failed, one expected installed-Companion skip (2,599 total).
Package evidence is recorded in the Draft PR handoff. Test results remain in the task checkout's TestResults.

An initial NuGet restore was blocked by sandbox network access; unrestricted restore succeeded. Initial new
Subclip selection assertions used an unhosted panel, so those fixtures now use the established isolated/offscreen
window and close it in finally. One combined focused run observed the existing deliberate Button activation
assertion fail; isolated two-case and subsequent combined focused validation passed without production focus changes.
The initial full run reported one obsolete async-void boundary handler assertion (updated to verify the shared
boundary method and typed UI routes) and one unrelated Browser dirty-Inspector selection assertion. All 23 targeted
source-shape/Inspector/Button cases passed on recheck; no Browser or Inspector production change was made.

## Owner hands-on acceptance

Use the isolated task-owned data root and freshly packaged executable command recorded in the Draft PR.

1. Open a video; I sets In, O sets Out, S creates one Subclip. Hold I/O/S to verify no repeated writes/creations.
2. Ctrl+Left/Right traverses the captured review set without wrapping; destination pauses at accepted restored In.
3. M creates a point marker at the presented frame. Hold M to verify no stream of duplicates.
4. Alt+Left/Right navigates strictly previous/next markers without wrapping.
5. Repeat shortcuts after Zoom, Inspector editing/Apply, Set In/Out mouse clicks, Subclip creation and folder-tree focus.
6. Text/rename/editable ComboBox editors retain typing and modifier navigation; sliders, open dropdowns, menus and
   deliberately keyboard-focused Buttons retain their local keys.
7. Space, plain Left/Right, C hold/release and fullscreen retain #349 behavior.

No TourBox transport/preset, shortcut Settings, Browser migration, Export change, saved Subclip trim/naming,
range markers, Favorites, Resolve integration or independent near-start backward-presentation fix is included.
Do not merge or remove package/acceptance data before explicit owner acceptance.
