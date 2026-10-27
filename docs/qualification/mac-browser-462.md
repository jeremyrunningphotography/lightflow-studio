# LF-MAC-DEV-028 — Mac Browser UI Parity

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
