# LF-MAC-DEV-028 — Mac Browser UI Parity

## Phases 1–3 accepted / Phase 4 I01 qualification (2026-11-01)

Jeremy accepted all seven P3-I01 hands-on checks at source2aced9f/builda9a37e8;
prior P1/P2 provenance and historical statuses remain below.
[Final inspection and phase regression gates](mac-browser-462-p4.md),
[DARKMATTER exact-source companion instructions](mac-browser-462-darkmatter.md).
No confirmed production polish correction; preserve accepted P1–3 source unchanged.
Final source/build/CI evidence lives in P4 staging manifest. Phase4 is not accepted;
no merge/close/release/tag. StageA desktop prohibition and fresh capture gate apply.

## Phases 1/2 accepted / Phase 3 I01 Stage A (2026-11-01)

Jeremy accepted Phase1 I03 at source02a996e6c0e8c4fbc0307f95bcaa8cb7b4b4c584,
build50639853af3227e6cd1d842077e5540769244f9f02950c5a9a6a8730e5b40ad3;
and Phase2 I01 at sourcedd6a50f6b0f1eaf6fdb8b965d59c8899b36f3511,
build1cd4a5436f3bbad53de2022c00792cfaa8f37b52be73aa21b0ec4eba5ab2b6df.
Historical sections below preserve earlier unaccepted status at their original dates.

[Phase3 interaction audit](mac-browser-462-p3-interactions.md) preceded changes.
BrowserViewModel and all Application/Catalog/discovery/native/Windows behavior owners
remain unchanged. BrowserPane publishes changed location paths even while focused;
unrelated selection/loading notifications preserve a draft. Explicit navigation/refresh
abandons draft; Enter/Go submits nonblank text, Escape restores committed location.
Native text editing retains Option-arrow. One shell handler routes exact modifiers to
existing functions: Command+I/L/R on Mac, Control elsewhere, F5, existing Alt-history
outside address editing. No new semantic resolver, shortcut profile, feature actions or
broad Ctrl-to-Command mapping. Hidden right splitter/Stop focus has visible fallback;
Catalog return restores surviving Browser focus. AX labels omit internal GUIDs.
Shared Button theme keeps its warm focus border without a duplicate stock adorner.

Accepted geometry/panes/defaults/lifecycle, icon/master/packaging, palette/density and
model safety guards remain unchanged. Native desktop checks are pending fresh permission;
no GUI launch or Computer Use during Stage A, no VoiceOver qualification claim.
Staging: artifacts/handoffs/LF-MAC-DEV-028-P3-I01; manifest records exact source/build.
Final ZIP waits for actual capture and scenario outcomes. Phase3 unaccepted, Phase4
unauthorized. Local rapid iteration is unpushed; published PR/CI remain historical.
Final exact-head CI and separate DARKMATTER Windows private-desktop package validation
remain gates. Project fields/native parent/priorities still inaccessible; no values guessed.

## Phase 1 accepted checkpoint / Phase 2 I01 Stage A (2026-11-01)

Jeremy accepted all five I03 hands-on checks. Immutable accepted source is
`02a996e6c0e8c4fbc0307f95bcaa8cb7b4b4c584`, build identity
`50639853af3227e6cd1d842077e5540769244f9f02950c5a9a6a8730e5b40ad3`,
from P1-I03 manifest. Earlier published Draft#464 head is not that checkpoint.
Phase1 geometry/lifecycle and preference owners are unchanged in Phase2. The previous
I03 status below is historical; Phase1 is now owner-accepted, not Phase2–4 or PR merge.

Phase2 establishes reusable shared Avalonia resources in Themes/Lightflow.axaml and
LightflowTheme rather than a Mac-only view. Windows source and DESIGN-SYSTEM.md are
references. Surface hierarchy, neutral grouping, text roles, shared Button template,
quiet/secondary/primary/danger families, input/list/hover/focus/disabled/active chrome,
32-point hit targets, icons and selected folder appearance are centralized. Structural
shell/pane/window dimensions remain accepted values. No new feature or interaction owner.

Mac bundle previously omitted CFBundleIconFile and native icon resource, despite having
a WindowIcon. The approved v4 archive contains the needed1024master; its Windows ICO
matches repo SHA256 exactly. Copy the master byte-for-byte into Mac packaging assets,
perform only resolution/format conversion with Apple sips/iconutil, package .icns under
Resources and declare CFBundleIconFile. Native decode checks10representations and exact
1024bitmap equivalence. Source is opaque, so no alpha mask/background redesign is made.
Bundle version derives from source commit count to distinguish builds; use unique versioned
app path for capture. Do not reset normal user icon caches or terminate Dock/Finder.
Actual Finder/Dock/switcher confirmation is pending new Stage C authorization.

P2-I01 Stage A uses headless UI/native regression tests, icon decode and fresh exact-source
self-contained package without visible desktop/app launch. Staging manifest is authoritative
for current source/build/tests; prior P1 screenshots are historical comparison only. Final
ZIP waits for actual captures. Phase2 unaccepted; no Phase3/4, merge/release/tag. Final
exact-head CI and independent DARKMATTER Windows packaged qualification remain gates.

## Phase 1 / iteration I03 — lifecycle correction, Stage A (2026-11-01)

Owner I02 hands-on supersedes the screenshot-only inference of successful lifecycle
qualification: footer and retained controls passed, but post-zoom manual resize oscillated
and snapped back. Both native zoom/unzoom in one session and reopened-zoom/unzoom failed;
ordinary restored launch/resize succeeded. Phase1 remains unaccepted.

Root conflict: I02's WindowState Normal handler queued ApplyWindowPlacement, which
assigned saved Width, Height and Position. It could therefore overwrite newer live
geometry after a native state notification, even when no preferences changed. Two
public-API headless cases (ordinary zoom and reopened zoom preference) reproduce this:
request1400×850 after unzoom, drain dispatcher, observe1300 saved width under I02.
Both fail before the correction and pass afterward. This reproduces the application
feedback path, not macOS pointer motion or the owner's full physical oscillation.

Avalonia11.3.8 AvnWindow.windowDidResize invokes native state reconciliation;
WindowImpl evaluates isZoomed and reports Normal/Maximized separately from fullscreen.
Thus a Normal observation is not an instruction to restore saved geometry. I03 removes
that entire queued state-triggered placement. AppKit owns zoom/unzoom and live resize;
state observations persist the ordinary zoom choice only. Startup still establishes
safe normal geometry once before the existing native zoom request. Genuine changed
work-area/topology notifications retain safe placement, with equal-area notifications
ignored. Preference-save timer remains observation-only; no added delays, native
window manager, reflection workaround, resize suppression or forced Windows behavior.

Other geometry writers inspected: constructor/Opened startup and actual display
callbacks only; Mac host reads view preferences once and writes optional snapshots,
no feedback subscription reload; Browser commits preserve shell geometry/zoom. Window
minimums remain1120×720 subject to smaller usable displays, no fixed maximum or
SizeToContent constraint added.1440×900 target, display clamps and existing normal
preferences are unchanged. Footer, panes, right visibility and provisional controls
are untouched in I03. No Application/Catalog/native platform/Windows shipping changes.

Optional LIGHTFLOW_WINDOW_TRACE=1 app-host logging writes window-geometry.log only
under the explicit isolated profile. It records native-reported Avalonia state,
client/outer frame, live dimensions/position/minimums, render scale and placement
count. It neither changes state nor controls the desktop. Capture should establish
that placement count stays constant throughout unzoom and subsequent manual drags,
and that saved normal bounds remain independent of native zoom.

Stage A headless/native tests and package verification do not prove smooth AppKit
manual resizing. Stage C requires interaction sequences A/B/C, not just end-state
screenshots: ordinary resize/restart; zoom→unzoom→multi-step resize; zoomed quit/reopen
→unzoom→multi-step resize; fresh startup; panes/footer regression. Record requested,
intermediate and final dimensions and snap-back/oscillation observations. Native CUA
capabilities currently document still screenshots, not video capture; no unapproved
recording substitute. Finalize I03 ZIP only after new explicit capture permission.
The I02 record below is historical and its unzoom placement policy is superseded.

## Phase 1 / iteration I02 — Stage A (2026-11-01)

I01 owner hands-on PASS: panes, right visibility, window resizing, restored geometry,
native zoom. Phase1 remains unaccepted. I02 adds a version3 optional Zoomed boolean,
independent of normal geometry. Missing old values defaultfalse. Normal startup places
safe clamped restored bounds before requesting Avalonia Maximized; the11.3.8 native
backend uses AppKit performZoom and reports fullscreen separately. Closing fullscreen
never persists fullscreen (reopens normal); minimized preserves the preceding zoom choice.
Returning to normal queues safe placement after the native state callback, avoiding
reentrant frame mutation. Existing pane events preserve shell zoom and normal preferences.

Startup investigation: I01 actual screenshot2880×1856 corresponds to1440×900 client
plus28-point titlebar at2× capture density. Subsequent owner/test profile preferences
are saved1200×800, which correctly override first-run dimensions. A noninteractive
AppKit read reports frame1800×1169, visible area1800×1054, backing scale2. The target
plus titlebar fits. No evidence justifies increasing1440×900 or deleting saved geometry;
no arbitrary size change made. Focused fresh-profile tests verify1440×900 and saved
smaller bounds. Actual new-profile I02 startup/reopen must be observed during Stage C;
owner's too-small symptom is not claimed independently reproduced or visually resolved.

Footer reference: MainWindow.xaml1917–1991 and UpdateBrowserStatusText use global
health left/fill; Browser count/selection/activity right; Jobs opens an implemented Jobs
surface. I02 matches left/fill health and right context, adds selection count and singular
item copy, hides empty contextual messages, keeps progress/Stop/error/partial warnings.
No Jobs/Preview-size/mode/activity button is copied because those features are unavailable.
Global Ready reflects authoritative Catalog readiness only, not fabricated FFmpeg checks.

Provisional inventory:

| Element | Classification | I02 disposition |
| --- | --- | --- |
| Catalog header button and Catalog gate create/open/retry/confirm/quit | Essential temporary functionality | Retain working lifecycle and safe prepared-folder flow; no replacement Catalog system. |
| Persistent raw profile path in gate | Internal implementation detail | Move to storage-guidance tooltip; native chooser still opens at safe profile root. |
| Catalog-ready/footer label plus diagnostic directory tooltip | Redundant presentation | Concise Ready with health dot, plain-language readiness tooltip, Browser context docked right. |
| Source available / Catalog metadata shown below | Redundant prototype copy | Source available only; all unavailable/error/source-check warnings retained. |
| Selection information panel and empty selection guidance | Essential temporary functionality | Retain real read-only filename/type/size/time/location and source availability; no Inspector label, tabs or promises. |
| Folder address/Go/refresh/Back/Forward/Up/scope control | Essential functionality | Retain working navigation and accepted Finder Locations. |
| Name/Kind/Size text list | Essential temporary functionality | Retain actual Catalog assets/selection pending separately authorized Browser views. |
| Jobs/Preview/Player/Collections/Inspector placeholders | Future functionality | None added; no nonfunctional controls. |
| Native diagnostic exceptions/IDs | Internal diagnostics | Remain outside normal UI; existing sanitized error/cancel behavior retained. |

Stage A uses headless tests and fresh versioned bundle only. No native app launch,
visible desktop control or screenshot claim. Stage C must demonstrate fresh startup,
normal restore, zoom-close/reopen and unzoom, panels, footer normal/warning, retained
Catalog controls. Build/source identity and tests are in versioned I02 staging manifest.
Final exact-head CI and DARKMATTER packaged Windows qualification remain separate.
The following I01 sections are historical; its native zoom non-persistence is superseded.

## Phase 1 / iteration I01 — current local implementation (2026-11-01)

Jeremy approved 1440×900 logical first-run geometry where usable display space permits,
1120×720 minimum subject to native constraints, preferred Locations 280 / right 360,
and right initially collapsed. Locations remains visible. The central cell receives
surplus width. Right-panel toggle is in the header beside the retained Catalog entry;
existing Windows logo/header surround and one global/contextual footer are used.

Normal window size/position are remembered separately from effective display clamps.
Native zoom/fullscreen state is not assigned or restored as a Windows-style substitute.
Pane preferences survive temporary narrowing. Existing 180-point navigation and
200-point right-pane lower bounds remain compatible; extreme native constraints can
shrink these temporarily without saving the clamp. User splitter commits remain
keyboard accessible. Initial draft ratio preferences are migrated using its 1280-point
reference window (1236 usable points), since that format contained no absolute widths.
Malformed optional preferences cannot block startup. These are view preferences only,
not Catalog schema or persistence changes.

Stage A uses focused headless builds/tests and an exact-source local commit/package.
Routine iterations do not push every visual adjustment or repeat full CI. Draft #464
remains at the prior published head until a deliberate later publication; its historical
CI does not qualify this local iteration. Final exact-head CI, separate DARKMATTER
package qualification and owner acceptance remain required gates.

Current review staging directory:
`artifacts/handoffs/LF-MAC-DEV-028-P1-I01/`.
Its manifest identifies the exact local source, build identity, isolated app/data-root,
validation and pending capture inventory. No actual screenshots, capture timestamp or
final ZIP are claimed during Stage A. Headless renders are kept separately as synthetic
layout checks, not included as actual-application screenshot evidence. After explicit
permission and availability of the applicable Computer Use skill, capture the default,
restored, expanded/collapsed right panel, adjusted panes and minimum shell. Validate
native window move/resize/reopen, zoom/fullscreen and display/scaling behavior then.

Phase 1 remains unaccepted; Phases 2–4 are not implemented. Shared style consolidation,
broader action/focus policy, Preview/Details/Player/Inspector and Windows shipping/default
view changes remain outside this iteration. No merge/release/tag. The following sections
preserve the initial d6bb866 draft's qualification record; its old geometry and package
paths are historical, not this iteration's current handoff.

Bounded #462 under migration Epic #366, related R4 #395. Base main:
`eb95296c6b46422ad372652490bc437823991988`. Draft review only; no merge,
release/tag or full R2/R3/R4 acceptance. Catalog, discovery/reconciliation and
Windows shipping inputs are unchanged. No Preview, thumbnails, Player or editing.

## Hygiene before implementation

Read root AGENTS.md and allocation registry; 028 absent from fetched main and
open PR inventory (#457/#448/#360), then reserved. #460 verified Closed/Completed;
#366/#395 open/incomplete. Current connector has no Project/native-parent/dependency
operations; terminal Git has no authenticated credential and gh is unavailable.
Computer Use is forbidden. #460 Project Done, #462 Project membership/status/Browser/
existing Priority/native #366 parent, current priorities and active Epic audit
remain unverified/unreconciled. Historical dispositions are not fresh verification.
No unrelated roadmap choices changed. This limitation was reported before code,
saved in #462 and subsequently in #366/#395. Owner kickoff explicitly permits
continuation with reported tooling limitations. Wider DoD/dependencies remain intact.

## Windows / Mac presentation comparison

Primary source references at the baseline: `LightflowStudio/MainWindow.xaml`
(lines 820–1062, 1382–1387), `Themes/LightflowShell.xaml`, `ContextualRightPanel.xaml`,
`WorkspaceState.cs`, and MainWindow's pane-layout/keyboard handlers. The #461
review's written Windows/Mac findings were read. Supplied screenshot attachments
were not exposed in the retrieved discussion; no screenshot inspection is claimed.
The comparison below is source-based. Offscreen Mac rendering is additional evidence,
not Jeremy's visual acceptance or a native desktop screenshot.

| Surface | Windows reference | Mac implementation / deliberate difference |
| --- | --- | --- |
| Shell | 60 header / 34 footer, existing 240×48 brand, 14/8/14/12 workspace inset | Same dimensions/brand/insets; native Mac titlebar and system type retained. Catalog chooser remains the bounded Mac entry point. |
| Regions | 8-point navigation and right splitters, flexible center | Two real 8-point GridSplitters; arrow-key increments 16, drag increments 1; double-click resets proportions. Browser stretches with window; no fixed-height stack. |
| Pane constraints | Navigation 220–520; right 280–600; constrained center layout | Navigation 180–520, right 200–480, center minimum 340. Smaller side minimums retain a usable path toolbar at 900-point window width. |
| View preferences | WPF workspace state stores pane sizes/open state | Mac host atomically stores only normalized pane proportions/right visibility in isolated `browser-layout.json`; malformed/unavailable preferences fall back safely. No Catalog schema, navigation or authored state ownership. |
| Navigation | Compact Back/Forward/Up/path/Go/Refresh and recursive-scope control | Same implemented actions; vector icons avoid missing Windows-only Segoe glyphs. Scope/panel controls occupy compact second toolbar row. Labels/tooltips and focus remain available. |
| Locations | Folder outlines, orange filled active folders, disclosure | Windows filled silhouette reused; yellow vector outline equivalent; selected folder orange, retained 32×32 disclosure target. Finder-style Home/Volumes and native permission/package handling preserved. Per-node inherited recursive icon semantics are deferred; existing model only exposes current scope. |
| Hierarchy | Dark canvas/surface/panel/raised/divider, orange focus, muted selection | Exact WPF palette tokens, compact 13-point rows, 9-point panel padding, 8-point corners. Fluent selected presenter explicitly uses Lightflow selection instead of default blue. |
| Assets | Mature Grid/Details/Preview and authored-state controls | Filename/Kind/Size list only. Stable AssetId single selection and existing keyboard behavior; no fake Grid/Preview/view-mode controls. |
| Right panel | Contextual tabs and implemented Inspector/actions | Collapsible Selection foundation with existing read-only file/source summary. No Inspector tab or functionality is fabricated. Raw IDs removed from normal view. |
| Status | Compact progress/status | Thin indeterminate progress while loading, inline Cancel, concise existing actionable messages, quiet success plus item count. Full diagnostics remain logged; partial scans remain truthful. |
| Window | Responsive application workspace | Initial 1280×800 capped at 94% of primary work area using screen scale; minimum 900×560 capped by work area. Native close/Command-Q unchanged; Quit button remains in Catalog chooser. |

The Mac store is a small host adapter for optional view preferences. The existing
shared BrowserViewModel, folder session, selection, refresh retention and sole
Catalog lifecycle remain the behavior owners. It is not a parallel Browser system.

## Local validation and evidence

31 Release headless Avalonia/Skia tests pass, including actual splitter keyboard
resize, hide/restore, minimum size, retained selection, full-height shell layout,
valid/invalid preferences and existing navigation/disclosure/stale-result/status
regressions. 41 native Release adapter/Catalog/Browser tests pass unchanged.
No native GUI app or Computer Use was launched. A first headless resize fixture
failed because it checked before size events settled; corrected fixture waits for
the dispatcher. Offscreen inspection also found/corrected a panel row placement and
Fluent blue selection presenter. Original logs/TRX remain in task-owned evidence.

Task-local `artifacts/lf028-evidence/` contains logs, offscreen 1280/900 renders,
acceptance Catalog creation receipt and final bundle verification. Exact source,
branch, clean state and SHA-256 for every bundle file are in
`artifacts/mac-shell/provenance.json`; final SHA/CI status are recorded on the PR.
Skia renders prove the tested layout, not native macOS chrome, permission dialogs,
VoiceOver, physical pointer hit testing or hands-on acceptance.

## Fresh isolated Mac handoff

Workspace: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-028-BrowserParity`

Executable:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-028-BrowserParity/artifacts/mac-shell/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac`

Data root:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-028-BrowserParity/artifacts/lf028-owner-acceptance`

The prepared Catalog is created through the existing noninteractive native lifecycle
host; media are tiny task-owned fixtures. The app has not been launched by the agent.
Build with the existing `scripts/Build-MacShell.sh`; self-contained osx-arm64,
unsigned development app. The final provenance receipt is authoritative.

Ready-to-copy Terminal command (Jeremy runs this for interactive review):

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-028-BrowserParity/artifacts/mac-shell/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-028-BrowserParity/artifacts/lf028-owner-acceptance"
```

1. Confirm prepared Catalog opens. Command-L, paste the data root's `/Media` path,
   Enter. Expect Alpine sunrise.png and Quiet morning.wav; notes.txt excluded.
2. Drag both pane boundaries, then Tab to each splitter and use arrow keys.
   Check left/main/right minimums, no overlap, selected file and navigation context
   retained. Double-click a divider to reset; collapse/restore Right Panel with its
   icon and Command-I. Resize from minimum to large/fullscreen.
3. Select files with mouse and arrow keys. Inspect selected/focused styling, readable
   filename/Kind/Size, read-only summary and long-path clipping/tooltips. Verify the
   Windows-like dark hierarchy, orange folder/selection accents and compact controls.
4. Expand Locations using the disclosure target; select folders separately. Leaves
   have no caret. Back/Forward/Up, path Go/Enter and Command-R preserve accepted
   semantics and valid selection/expanded branches. Command-L selects the path.
5. Include Subfolders adds Trip/Forest trail.png. Refresh/cancel and a nonexistent
   task-owned path retain concise truthful guidance. No successful scan diagnostics,
   Preview, thumbnails or Player should appear.
6. Quit with Command-Q or native close. Relaunch the exact command; verify saved pane
   proportions/right visibility and Catalog identity. Navigation itself is not newly
   persisted by this slice. Check the same visual layout against Windows and the
   owner's #460 screenshots; report any remaining density/icon/panel discrepancy.

Native foreground appearance, mouse affordances and functional/visual acceptance
remain Jeremy's gate. No agent interactive PASS is claimed.

## Separate DARKMATTER qualification — required, not performed on this Mac

Use a full independent clone `C:\Git\Agents\LF-WIN-DEV-028-WindowsQualification`
at the **exact PR head** from the final handoff. LF-WIN-DEV-028 is this allocation's
Windows qualification companion, not a new number or parallel feature assignment.
Preserve source/package/provenance/logs and original failures in that clone.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -Filter "FullyQualifiedName~Browser|FullyQualifiedName~Catalog" -ResultsName lf028-browser-catalog.trx
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller
```

The scripts must enter the private desktop boundary; do not bypass or switch it into
view. Require focused regression results, packaged startup/readiness/graceful exit,
Catalog backup/restore protection, dependency validation and zero remaining owned
smoke processes. Verify executable timestamp newer than exact source commit and
retain executable SHA-256. Existing Windows shipping code must remain unchanged.
CI does not substitute for this local package qualification.

Owner's Windows launch after that build:

```powershell
& "C:\Git\Agents\LF-WIN-DEV-028-WindowsQualification\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-DEV-028-WindowsQualification\artifacts\lf028-windows-owner-acceptance"
```

Report actual results, exact source/executable/data-root, smoke/dependency evidence,
hash/timestamp and any failures. No Windows PASS is inferred from this Mac.

## Review gate

Draft PR remains unmerged for exact-head CI, Project/parent reconciliation, separate
Windows package qualification and Jeremy's explicit functional/visual acceptance.
No release/tag. Chronicle assessment: ordinary presentation follow-up; no separate
history entry proposed before acceptance. A later accepted shared Browser milestone
may include this issue alongside #460 with evidence and scope limits.
