# Browser revisit investigation (#131)

Starting `main` / fetched `origin/main`: `cd92edbcc52fb7890b059d4cdc1d37dc38b3efa4`.

## Method

`BrowserPerformanceTests.MeasureNavigationStages` is an opt-in command-line evidence harness over real isolated
Catalog and Preview databases, filesystem enumeration/reconciliation, navigation, grid models, query projection,
cached artifact access, and WIC decode. It creates its own temporary sources; no photography library is touched.
Normal = 200 files; large = 1,000 files; recursive = 1,000 files across 10 child folders. Sources are synthetic
128 KiB JPEG-labelled files; valid tiny thumbnails and Current metadata records are seeded after the cold visit.
This measures cache access, not image quality or first-time generation. Debug .NET 8, Windows, local C: storage.

“Cold” means first Catalog visit, not a flushed OS disk cache. “Restart” disposes and reopens storage and creates
a new navigation session; it excludes application startup/WPF initialization. Each visit uses a new navigation
session, so the benefit cannot come from a session-local Browser cache. There are no CI wall-clock thresholds.
The baseline was collected before optimization, with only opt-in stage instrumentation added. An additional
blocking control disables initial Catalog presentation in the final harness; its scoped Catalog query is already
optimized, so it is a conservative control, not a substitute for the original baseline.

Physical removable/network media were not used. The slow-root control injects 150 ms at each authoritative
enumeration boundary. It does not simulate SMB failure modes or an OS call blocked on root availability.

## Measurements

Milliseconds; one representative run, not statistical latency percentiles. Original values are in
[before](browser-131-before.txt), final two-stage values in [final run](browser-131-final.txt) (with an earlier [repeat](browser-131-after.txt)), and the additional
[blocking control](browser-131-blocking-control.txt). Concurrent recursive stage durations overlap and must not
be added as wall time.

| Scenario | Before: grid candidates available | After: Catalog candidates available | After: reconciliation complete |
| --- | ---: | ---: | ---: |
| Normal, first visit | 333.2 | 312.9 | 312.9 |
| Normal, same-session revisit, all Previews cached | 286.8 | 6.4 | 302.8 |
| Normal, reopened storage | 297.4 | 0.8 | 286.1 |
| Large, first visit | 1765.5 | 1364.0 | 1364.0 |
| Large, same-session revisit | 1540.9 | 2.6 | 1420.9 |
| Large, reopened storage | 1602.6 | 3.9 | 1363.9 |
| Recursive, first visit | 1597.4 | 1910.7 | 1910.7 |
| Recursive, same-session revisit | 1473.2 | 3.6 | 1502.9 |
| Recursive, reopened storage | 1867.5 | 2.5 | 1510.1 |
| Simulated slow enumeration, normal revisit | 586.5 (blocking control) | 0.8 | 586.6 |

The primary bottleneck was per-file source observation and durable reconciliation, not candidate creation:
normal revisit 280.8 ms source observation versus 0.9 ms enumeration and 0.8 ms Catalog listing; large revisit
1529.9 ms source observation. Source observation includes root/path resolution, sampled fingerprint reads,
and Catalog observation writes. This preserves detection of changes with unchanged size/timestamp.
The old recursive walk also read the entire Catalog per folder (82.8 ms summed across a revisit);
the shared repository now uses a root/path-bounded query for both initial presentation and reconciliation.

After presentation, cached Preview batch lookup, artifact existence checks, and metadata/model hydration took
9.9–21.3 ms for normal revisits and 36.2–40.7 ms for large/recursive revisits. Decoding 32 tiny cached artifacts
took 2.2–3.2 ms after WIC warm-up (21.8 ms on first decode). These numbers exclude WPF binding/layout and are
not claims of an equally fast visible screen. Query projection and candidate construction were normally below
1 ms after warm-up. First visits still await discovery and do not have fabricated Catalog completeness.
Total reconciliation is not claimed to become faster; the optimization removes it from initial presentation.

`BrowserCatalogPresentationLiveTests` separately exercises a real offscreen MainWindow, actual Image bindings,
WIC decoding and virtualization, with reconciliation held at a deterministic gate. It verifies that a cached
viewport is visible while the task is still incomplete and that a queued obsolete presentation cannot replace
a newer folder. It also measures a first WPF presentation and a warmed same-window revisit. Jeremy's packaged
hands-on review remains the authority for perceived UX; no computer-control tools are used.

## Chosen behavior and correctness

- `BrowserNavigationSession` queries known assets by RootId and relative path, projects through the registered
  media types, and emits provisional `BrowserFolderState` through `KnownContentAvailable`. The existing
  `ApplyBrowserState`, BrowserQuery, grid, selection, state projection and Preview hydration consume it.
- A status suffix says **Known media • Checking for changes…**. The Catalog snapshot is explicitly unverified;
  it does not set new source observations or scan completeness. Source actions still use existing revalidation.
- Initial history is committed once. The same request continues into existing direct discovery or the bounded
  recursive discovery service. The final state replaces the provisional scope as a refresh. No second recursive
  walk, Preview scheduler, Catalog model, or query/filter engine is created.
- Unchanged tiles and surviving selection are retained. Changed observations invalidate derived presentation,
  including fingerprint-only changes, before the shared grid is repopulated. Removed files disappear. New files
  enter the same query/sort/filter projection. Catalog identities remain available even without a Preview scheduler.
- Missing/old-version/stale/source-mismatched cached Previews remain placeholders. The background scheduler
  now checks the physical artifact as well as its Current record and repairs missing thumbnail files through
  the existing thumbnail service. Current existing artifacts do not require regeneration.
- Failed validation leaves known content explicitly unverified with a retry message. Empty or entirely missing
  Catalog scopes use the existing authoritative load, because there is no durable folder-completeness record.
  Partially Cataloged folders can present their known subset, then receive newly discovered files.
- Root and folder availability checks remain before presentation. Offline/unmapped locations keep the existing
  unavailable behavior, preserve Catalog/Preview data, and do not infer missing assets. Network OS resolution
  delays remain possible; no offline browsing redesign is included.
- Generation, cancellation, disposal, presentation retirement and UI state identity are checked before queued
  initial presentation and after asynchronous Preview reads. Cancellation tokens are captured before their
  source can be disposed. Final reconciliation never overwrites a newer navigation. Progress-driven Preview
  hydration coalesces overlapping requests and awaits final results before detaching.
- Watcher hints arriving during a load are retained and coalesced into an authoritative follow-up, rather than
  canceling the in-flight reconciliation or disappearing because the loading overlay is already hidden.

## Retained diagnostics and reproduction

The opt-in `LightflowStudio.Browser` ActivitySource emits stage durations without source paths or asset labels:
`location.resolve`, `navigation.load`, `catalog.scope`, `catalog.read`, `filesystem.enumeration`,
`reconciliation`, `source.observation`, `grid.populate`, `query.project`, `ui.apply`, `ui.navigation`, `preview.hydration`.
With no listener it does not collect timings or write logs. MainWindow creation/global watcher startup remain
outside navigation; the offscreen test covers real presentation, while primary hands-on acceptance covers
the packaged app. No ad hoc production stopwatch logging is retained.

Separate global watcher setup measured 0.2–1.4 ms in the final harness run. It is not performed per navigation.
Root/location resolution measured about 0.2 ms locally after warm-up. These local values do not establish an
upper bound for unavailable network or removable roots.

The [real WPF evidence](browser-131-ui.txt) measured 220.9 ms on first cached display, **169.1 ms on a same-window
revisit versus 633.1 ms with early UI presentation suppressed** (3.7× faster). The control keeps the same warmed
window and final discovery/scheduler/binding path, so it is conservative relative to the pre-change pipeline.
This meets the investigation target of less than 100 ms for normal-folder data/hydration and less than 250 ms
for a warmed local 200-item cached viewport on this machine. These are review targets, not CI assertions or
hardware-independent guarantees. Offscreen testing does not replace Jeremy's hands-on UX acceptance.

Validation: full suite **1,770 passed, zero failed/skipped**, plus a focused real-WPF rerun confirming that
overlapping watcher hints wait for reconciliation and coalesce into one follow-up refresh. Original source-text
test anchors were updated to the shared hydration helper; unchanged-file fixtures now use stable timestamps.

```powershell
$env:LIGHTFLOW_BROWSER_BENCHMARK = "$PWD\artifacts\browser-131-after.txt"
dotnet test LightflowStudio.Tests --filter FullyQualifiedName~BrowserPerformanceTests
# Set LIGHTFLOW_BROWSER_BASELINE=1 for the final harness's blocking control.
# Remove benchmark environment variables before running the ordinary full suite.
$env:LIGHTFLOW_BROWSER_UI_REPORT = "$PWD\artifacts\browser-131-ui.txt"
dotnet test LightflowStudio.Tests --filter FullyQualifiedName~BrowserCatalogPresentationLiveTests
```

Deterministic coverage includes gated direct/recursive presentation, cancellation/disposal and a provider that
ignores cancellation, partial/missing/empty/offline scopes, failed validation, stable selection, fingerprint-only
changes, Unicode/wildcard/path-boundary queries and missing-artifact repair. Performance tests assert behavior,
not machine-specific latency. This issue remains incomplete until architecture and hands-on acceptance.
