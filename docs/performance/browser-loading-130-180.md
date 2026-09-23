# Browser loading: #130 and #180

Investigation source: freshly fetched main `bbcd09d457b49666b704b010cdd2a5357f62c529`.
This includes #131, #185, #287, and #293. The two issues remain independently tracked.

## #180 findings and disposition

No reproduction of the historical 10–20 second idle interval and no current queue wake-up defect was found.
The Catalog state that formerly reproduced it is unavailable after Jeremy's rebuild. This work does not
claim to identify or repair that historical cause, and makes **no production Preview scheduling change**.
The investigation is complete for the available evidence; the historical symptom remains evidence-waiting
under #180's existing semantics. Keep the issue open pending review and renewed reproduction. Do not use
this PR to automatically close it or claim the Epic's final completion gate has been met.

Current path:

1. Navigation resolves the root/path and effective recursive scope. Known Catalog assets can publish immediately.
2. Cold scopes await authoritative discovery. Enumeration identifies candidates; reconciliation observes source
   identity and commits the folder result, including missing-file reconciliation. This existing per-folder
   correctness boundary is not bypassed just because an individual source observation has completed.
3. `MediaDiscoveryRefreshService` calls `TrySchedule` immediately after successful folder reconciliation.
   `Enqueue` releases the worker semaphore directly; there is no Preview-start debounce or timed polling.
4. Fixed workers resolve Catalog identity, inspect Preview/cache state, probe missing metadata, then request
   thumbnails. Required video metadata supplies the representative frame position. Generation consults cached
   metadata rather than unconditionally probing again. Explicit regeneration uses the same thumbnail service;
   it does not have the initial folder reconciliation cost and ordinarily already has metadata.
5. Recursive discovery submits each folder independently. Root/earlier-folder thumbnails can start while
   descendants remain blocked. Cold Browser candidate publication still awaits scope discovery, as documented
   by #131; artifact generation and first on-screen publication are distinct boundaries.
6. Browser attaches admitted batches and hydrates committed artifacts through its existing generation-guarded
   projection. `Generating previews` represents an admitted nonterminal derived batch, including prerequisites
   and queued work; it is not proof that FFmpeg is currently decoding. No status is created from a future intent.

There is no new scheduler, priority policy, probe, decoder, identity or cancellation path. #293's shared
foreground/background Visual Index demand boundary, Color identity and source-oriented rotation remain intact.

## Measurements

One isolated run on this Windows machine, Release .NET 8, local storage, real Catalog/Preview databases,
filesystem reconciliation, metadata service, scheduler, thumbnail service and WIC renderer. Each scope has
30 valid synthetic 32×32 JPEGs; recursive media spans three subfolders. These are small synthetic images,
not a claim about large RAW/video libraries, remote disks, or the user's previous Catalog. Initial WIC and
metadata warm-up are included. There are no universal millisecond assertions.

Times below are milliseconds from navigation, except explicit regeneration which starts its own clock.
“Presentable” is the navigation's candidate publication, not an assertion of a painted screen. Worker entry
is measured at the metadata service after source/cache inspection. Artifact commit includes validation and
the source recheck. Eligible/admission timestamps coincide because submission is synchronous at that boundary.

| Scenario | First presentable | First nonempty admission | Worker metadata starts | Thumbnail service starts | Renderer starts | First committed artifact | Reconciliation/navigation ends |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Direct cold | 59.8 | 57.4 | 64.5 | 114.7 | 128.5 | 158.3 | 59.7 |
| Direct cached | 1.3 | 46.5 | reused | reused | none | reused | 47.0 |
| Recursive cold | 115.1 | 17.1 | 18.5 | 39.7 | 46.2 | 100.5 | 115.1 |
| Recursive cached | 0.8 | 16.7 | reused | reused | none | reused | 49.8 |
| Direct explicit | — | — | cached | 0.0 | 6.7 | 21.4 | — |
| Recursive explicit | — | — | cached | 0.0 | 6.7 | 22.6 | — |

Admission-to-worker gaps were 7.1 ms direct and 1.4 ms recursive in this run. There was no full-tree
generation gate: the recursive artifact committed before the cold candidate set published. Cached revisits
invoked no renderer. Since scheduling was not changed, these are measurements of the retained pipeline,
not before/after speedup claims. Raw phase output is in `browser-loading-130-180-timings.txt`.

`BrowserPreviewStartupEvidenceTests` is opt-in; run it alone so the process-wide ActivityListener observes
only this fixture. Set `LIGHTFLOW_BROWSER_STARTUP_REPORT` to a task-owned output file, then run:

```powershell
dotnet test LightflowStudio.Tests -c Release --no-build --filter FullyQualifiedName~BrowserPreviewStartupEvidenceTests
```

If the symptom returns, retain the affected Catalog/Preview state before rebuilding; capture source identity,
reconciliation, cache versions/failures, worker occupancy, metadata/probe latency, renderer entry, artifact
commit and UI hydration. This fixture cannot recreate vanished Catalog history or explain it retroactively.

## #130 behavior and evidence

The status-bar ring means “discovering media and checking this Browser folder scope for changes.” It is a
16-DIP instance of the existing Jobs radial control, with a 1.2-second rotation while visible, matching its
accent and stroke. No modal, blocking overlay, extra row or determinate percentage remains. The
150 ms presentation debounce only suppresses trivial work; it neither schedules work nor prolongs activity.

One navigation generation owns the working state from request start through discovery/reconciliation.
Cancellation retires it even if a provider is still unwinding. Completion/failure uses `finally` and can only
retire its own generation. Dispatcher callbacks and delayed display recheck the current authority. Recursive
per-folder reports do not restart the indicator. Cached media remains usable during revalidation; Preview
generation/failure never extends discovery activity. Restoration uses the same navigation lifecycle.

Deterministic tests cover direct/recursive/restored transitions, A → B → C with providers ignoring cancellation,
immediate cancellation/disposal, cached presentation, failed/offline/empty completion, and an unfinished then
failed Preview batch after discovery ends. Scheduler tests hold a recursive descendant and a metadata probe
at explicit gates, proving worker wake-up and thumbnail entry before tree completion. Existing scheduler,
cache, Color/rotation, Preview failure, Player/Visual Index and Browser regression suites remain applicable.

All local UI validation runs on a task-owned private desktop without switching desktops or computer control.
Automated status renders at 1120/1440 widths are supplementary; Jeremy's packaged visual acceptance remains pending.

## Hands-on acceptance

Use the retained profile and final packaged build:

```powershell
& "C:\Git\Agents\browser-loading-130-180\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\browser-loading-130-180\artifacts\acceptance-data"
```

Check a large direct folder and an Include Subfolders tree, cached revisits, fast small scopes, A → B → C,
empty/offline folders and restart restoration. The status ring must stop after discovery while thumbnails can
continue; failed Previews retain their established badges. Cached media remains usable and the ring must not
flash per descendant. Confirm the restrained status placement at minimum width and normal DPI. For #180,
compare an uncached folder with Regenerate; if the historical pause returns, preserve state before rebuilding.

No merge, issue closure, Epic completion or hands-on acceptance is implied by automated validation.

## Validation checkpoint

- Focused Browser/scheduling/layout coverage: 774 passed.
- Complete Release suite: 2,270 passed, zero failed; one expected opt-in installed Premiere live-acceptance skip.
- Premiere companion: 90 passed, zero failed.
- Isolated evidence fixture: passed, with real WIC artifacts and zero cached-revisit renderer calls.
- Real WPF status renders at 1120 and 1440 pixels inspected: ring and label fit the existing status bar;
  known Catalog media remains usable during the deterministic reconciliation gate.
- Every local run used a private non-visible desktop and task-owned TEMP/state. Initial sandbox-only shell
  preflight denial was resolved by running that required test with Windows shell permissions. Early fixture
  failures exposed obsolete visual-completion assumptions; tests now wait on navigation state. No flaky
  failure was accepted as normal.

Final package, commit identity and GitHub CI results are recorded in the Draft PR handoff.
