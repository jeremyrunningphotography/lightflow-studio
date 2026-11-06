# LF-MAC-DEV-036 — Cached Browser presentation latency

## A. Assignment and provenance

Continuation of **LF-MAC-DEV-036 — Browser Thumbnail Foundation**, same independent
full clone `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation`,
branch `codex/036-browser-thumbnail-foundation`, [Draft #486](https://github.com/jeremyrunningphotography/lightflow-studio/pull/486),
implementation [#481](https://github.com/jeremyrunningphotography/lightflow-studio/issues/481), parent #480/#395/#366.
Starting HEAD `72729b35e98006c4394a14bdf6577db98cc1e29a`, tree
`7c40edcc479ccbf71011953a0a9ce50bc68ec2b0`; independently verified main remains
`7f755460e36375234281481cea3004f60344325b`. Corrected immutable source/tree and build
receipts are appended to the generated handoff after publication. Tracked source
is isolated here; unrelated Finder `.DS_Store` files are left untouched.

Current root AGENTS.md, Phase A audit, Phase B implementation/performance evidence,
prior Mac navigation/discovery acceptance and actual Windows source were reviewed.
Earlier artifacts are preserved unchanged. New outputs use only
`artifacts/lf036-cached-presentation`; no new issue, agent or later milestone.

## B. Executive summary

Owner accepts the corrected video-generation experience (discovery, first/subsequent
MP4 previews, scrolling and cache reuse, including Wi-Fi) and seven functional checks:
still, RAW, video, navigation/selection, unavailable/unsupported media, mounted NAS,
and overall Browser integrity. Those results are preserved; the entire acceptance
cycle is not repeated.

The remaining warm-navigation delay is **publication ordering**, not slow JPEG cache
reads. Mac clears the visible collection, checks folder access, awaits complete
source enumeration/fingerprinting/reconciliation, then queries/publishes assets;
viewport demand is gated by IsLoading. Windows already has a provisional known-media
publication before reconciliation. On the same warm 35-asset DJI scope, Mac waited
743–816 ms before asset publication; the earlier packaged trace waited 5.9 seconds
for discovery before cache demand. For 1,204 local assets the original waits ~19 s.

The bounded correction adds an optional source-I/O-free existing Catalog-scope read,
publishes known records with an explicit cached/checking state, hydrates at most 192
local Preview records without scheduling generation, then runs the unchanged
reconciliation path. Final publication rechecks existing source/visual cache reuse.
Failed/canceled verification remains unverified, never verified current. There is no
persistent folder index, schema migration, new discovery owner or Windows change.

The separate restart-location observation is a missing existing Mac capability,
not a #481 regression. It is deferred under the request's persistence scope limit;
see G. Owner acceptance of this new correction remains pending.

## C. Baseline execution trace

`BrowserViewModel.NavigateAsync` clears thumbnail demand/collection, sets Checking
folder, awaits `IBrowserLocationProvider.GetFoldersAsync`, then
`DiscoverBrowserFolderAsync`. The Mac session borrows its lifecycle under `_requests`
through mapping, direct/recursive discovery, source fingerprinting, reconciliation
and final scope projection. Only afterward does UI publish assets, clear loading and
allow `BrowserPane` viewport demand. Preview records were not late because of cache
I/O; demand itself was late. `_requests` ownership must remain intact to prevent
Catalog retirement during source operations.

The representative warm DJI trace has two native enumerations totaling 116.1 ms,
35 fingerprints totaling 376.9 ms (69.8 ms actual sampled reads, the rest admission,
path checks and rechecks), 623.5 ms source observation, 677.3 ms reconciliation,
743.0 ms discovery, and only 0.36 ms final Catalog projection. Timings are nested;
do not sum them as independent work. Ordinary warm visits publish first assets at
786.2/743.1/815.7 ms; first cached projection at 834.3/808.9/865.2 ms. Reopen/manual
navigation is 1081.1/1130.8 ms respectively. The 1-file local baseline publishes at
25.8–37.0 ms and projects cache at 32.5–45.7 ms.

The harness uses monotonic Stopwatch/Activity timestamps, path-free stage names,
asset IDs and per-navigation intervals. It separates first model asset/cached
projection from compositor presentation; packaged UI traces supply next-frame
callbacks. The first exploratory baseline incorrectly counted an old folder's
notification and did not initialize Preview reads on reopen; it is preserved but
superseded by `baseline-repeat.json`, not used as a performance claim.

## D. Windows comparison

Shipping `LightflowStudio/BrowserNavigation.cs`, `LoadAndCommitAsync`, determines
root/recursive scope and calls existing `assets.ListScopeAsync`; it commits
`IsRevalidating=true` known content through `KnownContentAvailable` before invoking
normal direct/recursive discovery. Generations and `CanPresentKnownContent` reject
obsolete reports. `BrowserCatalogScope.Entries` excludes known missing/nonpresentable
rows. `MainWindow.BrowserNavigation_KnownContentAvailable` publishes on the UI thread;
`ApplyBrowserState` and `LoadBrowserAssetStatesAsync` hydrate persisted state.

Windows still revalidates sources on navigation. It does not infer unchanged files
from an unchecked persistent folder cache. Scope, selection, scroll and authored
identity are retained through existing navigation/grid ownership. Status says known
media/checking changes until final validation, and unavailable results remain honest.

Mac reuses the existing Catalog repository, recursive-root rules, Preview reuse and
source reconciliation owners; only publication orchestration is adapted through an
optional Application port. WPF navigation/grid/restoration is not extracted wholesale
and no Windows behavior changes. Mac retains native descriptor/no-hydration/source
admission and mounted-volume safeguards.

## E. Correction delivered

Production files: `Lightflow.Application/BrowserCatalog.cs`, Mac
`MacShellCatalogSession.cs` / `.Previews.cs`, UI `BrowserViewModel.cs` /
`.Thumbnails.cs` and `BrowserThumbnailCell.cs`. Tests and the isolated
`tools/BrowserPresentationPerformance` harness accompany them.

`IBrowserCachedFolderSession` provides an optional read-only Catalog scope. Mac
validates relative paths, derives current stored recursion, queries the existing
repository and excludes known Missing/Unknown/nonmedia records. It performs no
source enumeration/fingerprinting or new persistent indexing. Preview reads can
initialize the existing store on reopen without scheduling generation.

UI publishes known records as provisional and bounded cached pixels as Cached.
It then executes existing folder access/discovery. Cached selection reports that
source availability is unverified. Final reconciliation replaces additions/removals,
re-reads source/preferred-frame/Color/LUT reuse checks, retains surviving selection,
and clears the status only when appropriate. Failed/canceled verification labels
retained pixels Unverified and disables generation/scope changes until a successful
retry. Epoch/generation/token checks protect every awaited publication; the existing
Catalog borrowing and native resource-drain boundaries are unchanged.

No mutation, missing inference or AssetId reassignment occurs in the provisional
phase. Existing invalidation includes same-size/same-time source changes; it is not
replaced with stat-only reuse. New folders still use ordinary discovery. The bounded
initial cache page may leave off-page rows without pixels until reconciliation
finishes; no claim of immediate hydration of every row in a large folder is made.

## F. Before/after measurements

Same immutable sources, independently seeded equivalent local Catalog/Preview states,
three warm repetitions plus reopen/manual navigation. Physical OS/device caches were
not flushed. The tests measure warm Preview presentation, not cold-device speed.

| Folder / metric | Original warm median (max) ms | Corrected warm median (max) ms |
| --- | ---: | ---: |
| 1-file local: first asset | 32.4 (37.0) | 0.3 (3.6) |
| 1-file local: first cached projection | 43.1 (45.7) | 1.1 (8.6) |
| 35-asset external DJI: first asset | 786.2 (815.7) | 0.4 (0.5) |
| 35-asset external DJI: cached projection | 834.3 (865.2) | 5.8 (8.4) |
| 35-asset external DJI: reconciliation complete | 786.2 (815.7) | 765.1 (790.1) |

DJI reopen/manual navigation: first asset 1081.1→0.3 ms; cached projection
1130.8→5.7 ms; complete reconciliation 1081.1→754.1 ms. These are model/cache
projection times, not hardware scanout. The 1,204-file local scope originally publishes first assets at median **19,197.3 ms**
(max 19,232.7), cached projection **19,247.1 ms** (max 19,282.1). Corrected first
assets are median **3.13 ms** (max 3.35), cached projection **30.15 ms** (max 30.64).
Complete reconciliation remains **19,112.2 ms** (max 19,204.8), versus original
19,197.3 ms. Reopen/manual navigation first asset/cache changes from
19,054.9/19,103.3 ms to **4.10/29.43 ms**, while verification still takes 19,056.4 ms.

Sampled process RSS peaks are **169.2→162.2 MB** for the primary local/external
comparison and **153.4→151.1 MB** for large scope; these are 50 ms samples, not exact
GPU/system peaks. Three real-media navigation replacements after active fingerprinting
finish in **30.9/31.3/34.2 ms** and all retain the new scope. Final native readers are
zero. A fast initial publication does not eliminate the large-folder verification
cost or promise that every off-page preview is immediately populated. Packaged UI
and resource receipts are appended after final exact-head smoke.

A concurrently running owner app is left untouched; measurements are not an isolated
whole-machine benchmark. Mounted NAS functional behavior is owner-accepted at the
starting source. No additional NAS media location was authorized, so this correction
has fresh external/local tests and no invented NAS timing result. Small sample maxima
are reported instead of universal percentile or latency guarantees.

## G. Last-location investigation

Pre-#481 main already calls `BrowserViewModel.OpenAsync`→`Suspend`, clears Location,
and presents Choose a location. `ShellViewModel.RunAsync` opens that model without
restoration. Desktop `Program.cs` loads/saves `BrowserLayoutStore`, whose documented
contract explicitly excludes navigation, selection and Catalog state. The #456
acceptance report explicitly states transient navigation is not restart persistence.
Thus this is a missing/deferred Mac foundation capability, not a thumbnail regression.

Windows `WorkspaceStateService.SetBrowserLocation` stores logical RootId/relative
folder and diagnostic last-resolved path. `BrowserLocationRestoration.RestoreAsync`
uses ordinary navigation, checks the current root mapping, retains honest unavailable
context, and tries nearest valid ancestors for a missing folder. It does not silently
remap Catalog identity or mount a NAS share.

**Deferred:** Mac has no equivalent persisted navigation contract. Adding it would
introduce new persistence/restore semantics, including Catalog switching, unavailable
mounts and fallback policy. The request excludes such an unapproved expansion. A
small future Catalog-keyed continuation should use existing navigation/admission,
restore only when valid, preserve an unavailable context without mounting, and be
covered for Catalog switches/corrupt preferences/cancellation. Owner may explicitly
extend #481 or assign that preference work to existing #483; no issue was created
and #483 was not started. No restart-location restoration is claimed in this package.

## H. Regression validation

Focused UI tests cover provisional publication, no generation before validation,
selection retention, add/change/remove identity replacement, delayed cache and
reconciliation stale-result rejection, cancellation/failure honesty, recursion and
192-record cache bounds. The native integration test proves cached projection makes
zero source-I/O stage calls, while unchanged discovery still detects same-size/
same-timestamp content changes, added/removed files, recursion, unavailable scopes
and reopen identity. Existing AVFoundation read-amplification/cancellation tests pass.

Final broader Release suites passed **352 tests, zero failed/skipped**: Application 141,
Catalog 67, UI 81, Mac 63. Windows application/test cross-build completed with zero
errors (existing analyzer warnings retained). Exact commands/results are in
`final-commands.json` and the corresponding logs. Earlier failed test setup trials are preserved: an Activity
listener's static-initialization recursion and an unseeded recursive Catalog child;
corrected focused tests pass. They are not hidden as product successes.

Entire JRPhoto4T is read-only. Only the specifically authorized owner folder is
enumerated/read; existing task-local immutable media and disposable test fixtures
supply other cases. No source-management command, source write, metadata/xattr/time
change, remount, repair or owner Catalog operation is used. All Catalogs, Preview
caches, traces and packages are in this task's local storage. Final integrity receipts
compare original selected metadata/xattrs, listing and sampled identities; no whole-
volume hash or filesystem-managed access-time claim. Private maps and owner-derived
images remain outside portable/public evidence. Windows cross-build/CI are supporting
checks, not independent packaged Windows qualification, which must wait for final Mac
source acceptance as instructed.

## I. Mac package and owner acceptance

Fresh exact-head package after publication:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-cached-presentation/package/Lightflow Studio.app`.
Prepared unused owner root:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-cached-presentation/owner-acceptance-data`.

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-cached-presentation/package/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-cached-presentation/owner-acceptance-data"
```

PowerShell equivalent:

```powershell
& "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-cached-presentation/package/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-cached-presentation/owner-acceptance-data"
```

Create a disposable Catalog locally; visit the authorized DJI folder once to populate
it, switch to a task-local folder and back. Known assets/cached pixels should appear
while the concise checking-changes status remains; it clears after reconciliation.
Scroll/select during validation, switch rapidly, Refresh and check surviving selection.
Use disposable local fixtures for added/removed/modified files, never owner media.
Quit/reopen and manually revisit to confirm cache reuse. Last-location auto-restoration
remains deferred. Previously accepted format/video/NAS behavior needs affected-area
spot checks, not an unnecessary repeat of all acceptance checks.

## J. GitHub and history

#481 remains open/In Progress/Browser/unset priority; #480 open/Backlog/Browser with
incomplete DoD; #482–#485 Backlog/unstarted. Project #4 and native hierarchy were read
back. Issue/Epic record the owner's prior acceptance and remaining observations.
Draft #486 receives the measured correction and new package/evidence. No merge,
closure, new agent/issue or independent Windows qualification begins.

Registry updated in this implementation PR. **Historical maintenance: Not warranted**:
a bounded publication-order correction using existing Catalog/Preview owners, not a
new product/architecture milestone. Earlier Phase B history is preserved.

## K. Artifact manifest

New artifacts only under `artifacts/lf036-cached-presentation`:
`REPORT.md`, `MANIFEST.json`, baseline/corrected navigation JSON and stage traces,
large-scope comparisons, source-integrity, cancellation, tests/build logs, package
provenance/dependency/GUI receipts and optional synthetic-fixture screenshots.
Separate archives:
`artifacts/LF-MAC-DEV-036-Cached-Presentation-evidence.zip` and
`artifacts/LF-MAC-DEV-036-Cached-Presentation-Mac-app.zip`.
SHA-256 receipts accompany archives; internal manifest hashes each evidence file.
No owner MP4/image, private filename map, personal Catalog, credentials or large cache
is included. Final immutable hashes are appended after exact-head build.

## L. Outstanding gates

Owner acceptance of corrected cached presentation, explicit last-location scope
decision, independent Windows qualification **after final Mac source acceptance**,
exact-head full CI/review and explicit merge authorization remain gates. Previous
video/functional owner PASS results stay accepted. No claim of full #481 acceptance,
last-location restoration, independent Windows qualification or merge authorization.

**STOP FOR OWNER REVIEW AND ACCEPTANCE.** No merge, closure, #482 or new agent.
