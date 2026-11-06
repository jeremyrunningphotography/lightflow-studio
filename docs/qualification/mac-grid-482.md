# Mac Browser Grid Cards — LF-MAC-DEV-038

Local implementation checkpoint, November 5, 2026. Conversation:
**LF-MAC-DEV-038 — Mac Browser Grid Cards**. Not owner-accepted, qualified on
Windows, merged or released. Exact source/tree, package hashes, measurements,
launch commands and validation logs are supplied in the portable evidence ZIP.

## Orientation and workflow

Verified remote main `1b37d62fe1bb153e6988661ce65453648d937551`; independent
full clone `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-038-MacBrowserGridCards`,
branch `codex/038-mac-browser-grid-cards`. Global 038 absent in fetched remote
registries and matching GitHub search; 036/037 Done. Native hierarchy verified
#366 → #395 → #480 → #482 and #481 Completed. Authenticated Project #4 readback:
#481 Done, #480 Backlog (1/5), #482 In Progress, #483–#485 Backlog; Browser area,
unset Priority. No inferred native blocking edge. Initial workflow reconciliation
was the first modification and committed as `494d499` before production edits.
See [before/after policy validation](mac-grid-482-workflow.md).

## Architecture and source parity

Inspected Windows `BrowserGrid.cs`, `MainWindow.xaml`, resource assignments in
`MainWindow.xaml.cs`, shared `BrowserGridSelection`, existing Avalonia BrowserPane,
ViewModel, thumbnail cells/demand, discovery/Preview acceptance reports and the
[design system](../DESIGN-SYSTEM.md). Original `Screenshot 2026-11-05 053037.png`
was unavailable in task attachments/Desktop/Downloads. No screenshot comparison
with that reference is claimed; original image is still needed for direct parity
review. Source is authoritative for composition; the design document's description
of unframed Preview is broader than the actual current Windows template, which
retains a 168-wide padded outer border. This implementation follows that template.

| Concern | Windows source and native implementation |
| --- | --- |
| Geometry | Medium 168 width, trailing 12 gutter, 7 padding + 1 border, radius 6; preview area 108, Info 78; scrollbar allowance 20; Uniform image fit |
| Preview | Image-focused; no filename footer or Info indicator frames; tooltip/automation identify file |
| Info | #1B2028 surface, 22 upper frame, inset image, 24 lower frame and centered 12-point trimmed filename |
| Hybrid | Full image and filename; no Info frames; authored-state overlay data reserved for #485, no fabricated metadata |
| State | Existing cached pixels and truthful Loading/Checking/unavailable/unsupported/error text; no alternate generator/cache |
| Selection | Existing shared BrowserGridSelection via ViewModel; click, Command/Control toggle, Shift range, primary-modifier A; selected surface/focus border |
| Keyboard | Left/Right one item; Up/Down visual column count; Home/End; Page Up/Down visible rows; focus and scrolling follow asset identity |
| Refresh | Surviving stable selected keys retained; obsolete range anchor cleared after reordered snapshot; new navigation clears selection |
| Resize | Lazy O(1) row-list projection over the same asset snapshot; no rebuilding asset collection or metadata/Preview queries |
| Virtualization | Existing Avalonia VirtualizingStackPanel realizes rows, same architectural approach as Windows; only realized cards own detached bitmaps |
| Preview demand | Existing 60ms debounce, 64 visible / 192 total bounds, visible priority before overscan; cards supply viewport geometry |
| Accessibility | Native button cards, per-file metadata names/tooltips, selected help text, visible keyboard focus, mode toggle names/checked state |

No new dependency, Preview/Catalog schema, discovery writer, decoder, Windows
source or persistence preference changes. Mode switches update realized cards in
place and retain image controls. Reflow recycles presentation, not media identity.
Existing cached-first provisional publication and authoritative generation/epoch
checks remain in use. Native ImageIO/AVFoundation bounded/cancelled readers and
DJI tail-moov performance path are untouched.

## Validation and acceptance boundary

Focused tests exercise 20,000-item lazy row projection; column/gutter boundaries;
10,000-item real virtualized control, mode changes, resize, keyboard, end scroll,
Refresh identity and visual bounds; shared range/toggle selection and Refresh.
Existing bitmap retention/detach, cache priority, stale-result cancellation,
Locations, shell and Catalog/Preview regression tests also run. Exact results and
native package observations are in the evidence ZIP; passing local tests do not
claim independent Windows execution or full required hosted CI.

Create the issue's new retained **Lightflow Test** Catalog through the existing
supported `scripts/Mac-Acceptance.sh initialize` workflow after packaging. Its
`acceptance/persistent` root is outside package directories. Before reuse run
package-bound `check`; never reset, silently migrate, repair or delete it. #481
Catalogs are untouched. Disposable GUI smoke, automated fixtures and fresh-start
roots remain separate. Evidence excludes media, databases and retained caches.

## Owner acceptance checklist

1. Preview: thumbnail fit, spacing, selection and filename tooltip; persistent
   filename intentionally absent, matching Windows Preview.
2. Info: smaller inset thumbnail, quiet upper/lower frames, readable filename.
   Rich authored indicators/metadata remain #485.
3. Hybrid: larger thumbnail plus filename, no Info frames.
4. Switch all three modes: selection/cached thumbnails remain stable.
5. Resize window and Right Panel: columns reflow, no clipping/horizontal scroll.
6. Scroll a large folder: smooth rows, bounded demand, no wholesale regeneration.
7. Navigate and Refresh: stable surviving selections and cached-first display.
8. Keyboard/accessibility: arrows, Home/End, Page keys, toggle/range/all selection,
   visible focus, file names and mode checked states.
9. Media states: representative still, RAW, video, loading, unavailable and
   unsupported Preview; no implied support for unsupported files in the grid.
10. Regression: Locations, Catalog, Browser shell and previously accepted video
    thumbnail performance (including DJI/NAS media available to the owner).

## Outstanding gates and history

Owner Mac hands-on acceptance and original Windows reference comparison; then
required independent exact-source DARKMATTER qualification; final registry/history,
one implementation PR and full exact-head CI; explicit merge authorization and
post-merge CI. No qualifier is allocated prematurely. No PR, merge, closure or
release now; #483–#485 unstarted.

Historical maintenance: Chronicle and Source Index **Updated** for the owner-directed
local-first, one-PR workflow and bounded Grid implementation checkpoint. Development
History **Not warranted** yet: implementation is unaccepted and the broader narrative
can be assessed with the final milestone. Historical accepted evidence is preserved.
