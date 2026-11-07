# LF-MAC-DEV-040 — Mac Browser Details, initial local implementation

Local review checkpoint for #483, not owner acceptance, independent Windows qualification,
a PR, merge or release. Workspace `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-040-MacBrowserDetails`;
branch `codex/040-mac-browser-details`, baseline `cc64f725aefb461c1b1a798cf2dfffa04658befd`.
The portable evidence ZIP contains exact final source/tree, native read-back, test logs,
package manifest/hashes, screenshots, runnable path and both launch commands.

## Reconciliation

Refreshed authentication includes `project`. #489 was unparented and absent from
Project #4. Supported GraphQL mutations established native #480 parentage and added
the Project item, then set existing Backlog/Browser, leaving Priority unset.
Read-back confirms #366 → #395 → #480 → #481/#482/#483/#484/#485/#489;
#481/#482 Closed and Project Done, all parents open, two of six complete.
#483 transitions from Backlog to In Progress at implementation start; Browser/unset
Priority retained. No native blocking edges are created; #483/#489 have no incoming
or outgoing blocking edges. Sequential order is not treated as a dependency.
040 was absent from main/fetched remote registries and matching GitHub search.

## Windows reference and architecture

Inspected shipping BrowserGrid.cs, BrowserDetails.cs, MainWindow.Details.cs,
MainWindow.xaml, shared BrowserViewModel, BrowserPane, Catalog/Preview contracts,
existing tests, shared-product-boundaries and DESIGN-SYSTEM. The requested
`Screenshot 2026-11-05 053037.png` was not available in supplied attachments; no
visual comparison against that screenshot is claimed.

Windows uses 64-DIP rows, 54-DIP previews fitted Uniform, 12-point cells, left
alignment, 8/6 horizontal cell insets, ellipsis and value tooltips, dark shell headers
with dividers and 6-point resize grippers. Widths clamp to 40–1200. Header drag
reorders visible columns; hidden slots retain their order. Header context menus can
show/hide every column but cannot hide the last column. Layout/columns are workspace
preferences. Supported Windows sort fields reuse its BrowserQuery, never a table-only order.

| Column ID / title | Width | Default visibility |
| --- | ---: | --- |
| preview / Preview | 112 | Shown |
| name / Name | 240 | Shown |
| rating / Rating | 88 | Shown |
| flag / Flag | 86 | Shown |
| capture-date / Capture Date | 158 | Shown |
| media-type / Media Type | 95 | Shown |
| dimensions / Dimensions | 112 | Shown |
| duration / Duration | 88 | Shown |
| frame-rate / Frame Rate | 95 | Shown |
| file-size / File Size | 95 | Shown |
| modified-date / Modified Date | 158 | Hidden |
| color-label / Color Label | 100 | Hidden |
| camera / Camera | 180 | Hidden |
| lens / Lens | 180 | Hidden |
| color / Color / LUT | 180 | Hidden |
| range / Saved Range | 100 | Hidden |
| subclips / Subclips | 80 | Hidden |
| keywords / Keywords | 220 | Hidden |

Mac uses custom retained cells over the same VirtualizingStackPanel/ListBox and lazy
BrowserCardRows snapshot projection. Details projects one existing asset per row;
Grid geometry/mode controls remain accepted. No DataGrid dependency or separate
selection/query/discovery/Preview engine is introduced. Header width and row cells
share one normalized configuration; horizontal scrolling synchronizes the header.
Resizing retains cells; visibility/reordering retains cell identities. Keyboard
resizing uses arrow keys on the gripper; header pointer dragging handles reordering.

Thumbnail demand remains bounded (visible ≤64, combined ≤192), with existing
BrowserThumbnailCell ownership and cached-first generation policy. Existing local
Preview metadata and Catalog classifications are projected in bounded reads; no new
metadata extraction occurs. Mac's current native metadata probe is limited mainly
to video duration: absent capture date/dimensions/frame rate/camera/lens remain empty.
Color/LUT, saved range and subclip projections remain empty pending #485, even when
those optional columns are selected. No fabricated values. Filename, type, size and
modified date use the resident Catalog asset. Headers do not sort because this shared
Mac ViewModel has no query/sort contract; tooltip explains the #484 boundary.
Windows shipping production source remains unchanged.

## Switching and preferences

Graphical Grid/Details buttons lead the Browser location/scope row. Bottom-right
Preview/Info/Hybrid controls stay in their permanent location and are disabled in
Details while retaining their remembered Grid mode. Layout switching preserves the
asset snapshot, Catalog/folder, selection/range state, Preview services and semantic
top-asset scroll anchor; keyboard focus returns to the current asset when applicable.
No rescan, cache invalidation or new thumbnail-size controls are triggered.

Existing BrowserLayoutStore's isolated `browser-layout.json` now includes layout,
Grid card mode, normalized Details widths/order/visibility and one optional last
location bookmark. Existing window/panel preferences and legacy migration remain.
Preferences are independent of Catalog media records and do not persist thumbnail size.

## Exact last-location restoration semantics

A successful ordinary navigation remembers CatalogId, RootId, relative folder and
exact absolute mapping. Startup restores only for the same active admitted Catalog,
an extant online root, safe relative path, identical mapping and accessible contained
folder. An existing location provider performs a non-user-initiated directory check;
the restore stage is bounded to two seconds including cached projection. No natural
anchor creation, remapping, Catalog mutation, discovery scan, metadata extraction,
cloud hydration or thumbnail generation is initiated by restoration.

Successful restoration presents the existing cached Catalog scope with the truthful
message **Restored Catalog view · Refresh to check for changes.** It stays provisional;
existing cached previews are shown initially, and explicit Refresh performs normal
reconciliation. This is deliberately conservative, including online network locations.
Selection is not persisted across launches. Missing/unmapped/remapped/unavailable or
invalid locations leave no selected folder and preserve all Catalog records, with a
truthful fallback message. A bookmark for another Catalog is ignored. User navigation
or Catalog switch invalidates pending restoration publication. Existing Catalog loading
retains its preexisting 15-second bound; restoration adds at most two seconds.

## Validation and remaining gates

Release Application 141 PASS; Catalog 67 PASS; native Mac 64 PASS. Initial UI 108 PASS;
final UI 110 PASS (including actual pointer resize/reorder and right-click menu); package/native checks are recorded in the ZIP, including bounded
stalled-network fallback. Tests use disposable roots, never retained acceptance data.
Headless checks cover 10,000-item virtualization, horizontal scroll, shared selection,
keyboard ranges, column normalization/configuration, preferences/reopen, Catalog/path
mismatch and unavailable/timeout restoration. Engineering checks are not owner acceptance.

Retained issue-specific `acceptance/persistent/Lightflow Test` is initialized only by
supported Mac-Acceptance tooling, outside package directories. Earlier acceptance
Catalogs are untouched. Preserve this new root after closeout until owner-authorized
deletion. Package smoke and fresh roots are disposable and separate.

Next gates: owner Mac acceptance across all three requested phases; native visual
acceptance against the missing Windows screenshot if supplied; real still/RAW/video,
NAS/offline cases and performance regression acceptance; exact-source independent
Windows qualification after owner acceptance; one implementation PR with final
registry/history/qualification integration, required exact-head CI and explicit merge
authorization. No PR, release/tag or #489/#484/#485 implementation is authorized here.

Historical maintenance: **Updated** Chronicle and Source Index for the local retained
Details/presentation persistence milestone, explicitly unaccepted/unmerged. Development
History update is deferred to a meaningful accepted Browser milestone; this initial
review checkpoint does not establish broader product delivery.

Native package QA exposed header-release routing and missing Thumb hit-test chrome.
The same local iteration corrects both and verifies right-click visibility menus,
keyboard/pointer resizing and drag reorder in the full 110-test UI suite. Original
failed engineering runs are preserved in the ZIP; no existing-behavior regression
was confirmed. Final package verification uses the corrected committed source.
