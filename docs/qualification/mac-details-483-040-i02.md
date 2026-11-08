# LF-MAC-DEV-040 — Details and restored Locations, Iteration 02

Local I02 correction checkpoint for #483, not a new assignment, owner acceptance,
Windows qualification, PR, merge or release. Existing full clone and branch
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-040-MacBrowserDetails`,
`codex/040-mac-browser-details`. I01 baseline `4a9e5473c20a10485172e0d44997e003de3f647d`,
tree `e0307d1f08c0339906768e6b9f084b91fb4bee61` verified before edits; tracked tree was
clean, with an existing untracked `.DS_Store` preserved. No repeated Project mutations.

## Owner acceptance retained

The owner reported eleven I01 PASS results: Details appearance/density; thumbnails,
filenames and available metadata; mouse/keyboard/multi-selection; responsiveness;
three Grid modes; switching continuity; window/right-panel layout; navigation and
Refresh; layout/card-mode persistence; column persistence; last-location identity.
Check 3 (column configuration) needs work. Checks 13 (unavailable volume/NAS) and
14 (real RAW/DJI/NAS performance) remain **NOT TESTED**, not PASS. I02 does not
revoke the eleven results or claim final #483 acceptance.

## Column diagnosis and correction

I01's keyed metadata text was insufficient to guarantee visual association. Headers
were rebuilt while only attached rows received imperative column updates; a detached
retained row had no current-column subscription or reattach refresh. In addition,
physical-coordinate tests demonstrated a fixed-width header could be centered when
narrower than the viewport while rows remained left-aligned. Header focus could move
its scroll offset independently, making otherwise correct fields appear under the
wrong title. Tests of preference order or Grid.Column indices alone missed these
visual failure modes.

One `BrowserDetailsColumnState` now publishes normalized immutable ID/width/visibility/
order records. Every attached row subscribes; every reattached row applies the current
snapshot. Headers and rows use the same snapshot and stable field IDs. Cells retain
identity, formatting, alignment and accessibility ID (`BrowserDetailsCell-<id>`).
Reordering moves existing controls and their Grid columns, preserving Preview bitmap
instances and row focus; it does not remap data to arbitrary positions. Header/resize
accessibility IDs use the same field ID. Hidden slots preserve their remembered
positions; a visible field moves with its own width/visibility record.

Headers are explicitly left-aligned. Their viewport reserves the body scrollbar gutter,
and body scrolling synchronizes the header, including the rightmost offset. Pointer
focus cannot independently scroll the header. Keyboard focus brings a column into view
through the body scroll owner, maintaining header/data alignment. Existing selection,
asset order, card geometry, Preview demand and Catalog behavior are unchanged.

Shipping Windows uses `GridViewHeaderRowPresenter` with `AllowsColumnReorder=True`
and stock header dragging; there is no application-owned insertion-indicator template
to reuse. Following the owner's permitted Lightflow adaptation, I02 shows a restrained
2-point warm focus-colored line at the proposed content boundary. It updates during
pointer movement, accounts for hidden columns and horizontal scroll, and uses a muted
line plus No cursor for invalid/no-op destinations. Release, Escape, capture loss,
presentation switching and teardown clear it. No large animation or redesign.

Actual presentation tests cover adjacent and long-distance moves, first/last columns,
hidden columns, widths, missing and partially available metadata, visual header/cell
coordinates, accessible cell order, detached/recycled rows, retained bitmap identity,
focus/selection, persistence/reopen, Grid/Details, Refresh and navigation.

## Restored Locations presentation

The accepted I01 last-location admission, identity, persistence, two-second source
availability check and cached/provisional content semantics remain unchanged. A new
transient successful-restoration signal drives only the existing Locations tree.

The view chooses the most specific existing favorite, volume or mounted-share node,
reveals only the needed ancestor chain through existing `ExpandAsync`/directory-only
provider calls, reuses loaded or pending child listings, and brings the normally
highlighted selected folder into view. Organizational server groups are revealed only
for that share. It neither selects another folder nor invokes Navigate, Discover,
Catalog mutation, Preview generation, mount or hydration. New siblings materialized
for automatic reveal do not gain prefetch permission; explicit user interaction retains
its existing behavior. Recursive scope and normal folder artwork remain model-owned.

The whole presentation reveal has a five-second deadline and a 128-step safety cap,
is asynchronous to Catalog opening, and rejects stale completion after navigation,
Catalog suspension or user disclosure actions. A timeout or incomplete chain preserves
the restored content/identity and shows a truthful Locations-path retry status; it does
not change the accepted unavailable-location fallback or repair storage. Explicit
user disclosure cancels automatic reveal so a user collapse is not undone.

Tests cover direct/deep paths, an already-expanded retained branch, asynchronous
children, mounted shares, scrolling/highlighting, unavailable and removed Catalog roots,
late completion, a genuinely stalled ancestor deadline, user cancellation, no unrelated
expansion, no discovery and no Catalog mutation. These are engineering fixtures;
physical disconnected-volume/NAS acceptance remains outstanding.

## Deferred sorting

No sort state, engine, active-sort glyph or click-to-sort promise is introduced.
#484 can map the existing stable `BrowserDetailsColumn.Id` to its future shared
Browser query command. Grid and Details continue to consume the same ordered asset
snapshot. The column-state fix does not require a later field-binding redesign.
#489 sizing and #485 full metadata/status remain unstarted. Bottom-right Grid mode
controls and shipping Windows production source remain unchanged.

## Validation, package and data

The I02 evidence ZIP supplies final test counts/logs, exact commit/tree, package hashes,
manifest/dependencies, screenshots when available and both launch commands. Final
native smoke uses a separate disposable root. The existing retained #483
`acceptance/persistent/Lightflow Test` is preserved without reset, migration or repair;
its package-bound preflight must pass before I02 uses it. An owner-started I01 process
was detected and preserved; any unresolved single-writer refusal is recorded rather
than bypassed. I01 package/evidence remain retained. No new clone or agent.

Focused owner recheck: drag feedback and correct header/value association (including
hidden columns and horizontal scrolling); configuration after restart/Grid switching/
Refresh; restored ancestor visibility/highlight/scroll; cancellation/unavailable cases.
Preserve the eleven accepted checks and complete the two previously untested media/
unavailable-location gates before final Mac acceptance. Independent Windows
qualification remains after that acceptance and before the single implementation PR.

Historical-maintenance disposition: **Not warranted** for these bounded correctness
and usability corrections. I01's historical milestone entries remain unchanged.
