# Workspace continuation inventory (#247)

Starting main: `c632dab3650c8b8257e44eee6077009e10c6428a`.

Inventory before persistence changes:

| State | Existing owner / persistence | Continuation treatment |
| --- | --- | --- |
| Restored window bounds/maximized | WorkspaceState v1; monitor-aware placement | Retain |
| Locations, Right Panel, Jobs splitter widths | Workspace layout | Retain |
| Right Panel visibility/preferred surface | Workspace layout / shared Right Panel | Retain, including deferred Subclips availability |
| Thumbnail size; Preview/Info/Hybrid | Workspace layout / BrowserGrid | Retain |
| Collection scope, expanded Sets, sidebar sections | Workspace layout / Collection hierarchy | Retain |
| Folder location | RootId + relative folder / normal navigation | Retain |
| Include Subfolders | Final #124 Catalog recursive-root configuration | Derive through normal navigation; obsolete workspace toggle stays inert |
| Folder branches and shared sidebar scroll | Transient BrowserTree / ScrollViewer | Logical folder identities, lazy branch materialization, saved offsets |
| Search, media toggles, predicates, sort, query lock | Transient typed BrowserQuery | Persist same typed query, hydrate before first scope projection |
| Grid scroll | Transient pixel virtualized viewport | Stable top AssetId + within-row offset, pixel fallback |
| Multi-selection / Shift anchor | Transient BrowserGridSelection | AssetIds intersected with restored results, anchor remapped to current order |
| Player source, normalized timestamp | PlayerViewerHost / playback coordinator | AssetId resolution; existing open/seek, paused before surface attachment |
| Fit/zoom, pan, Loop, speed, cadence | Final #199 host / playback review options | Source-aware restore, clamp viewport and duration |
| Audio mute/volume | Player playback service | Capture intentional controls through existing service |
| Selected Subclip / range focus | Player + durable Subclip/range stores | Restore only surviving source-owned IDs; no copied Catalog intent |
| Hover, pointer, drag, focus rings, fullscreen, overlays, animation, playback, modal dialogs | Ephemeral or unsafe | Never restore |

No new Catalog/Preview session data or second query/playback authority is introduced.

## #111 integration

The Player filmstrip adds `WorkspaceLayoutState.PlayerFilmstripVisible` (default
true). It merges with the existing layout and restores before Player opens.
Review membership is reconstructed from the restored Browser results/selection
and current AssetId, rather than persisted as another query or media history.
The interrupted current source retains #247's paused position restoration;
traversal afterward starts at each destination's own In. Folder and Collection,
single-result-set and selected-subset restoration are covered through actual
MainWindow continuation tests. See `player-111.md` for the later acceptance record.

Jeremy accepted the functionality hands-on before final acceptance validation. Final review preserved the accepted UX and closed two cancellation gaps: new Browser input discards a deferred scroll restore even after startup finishes, and image decode checks cancellation before publishing its surface.

Final Release validation:

- 33 issue-specific continuation tests passed (`WorkspaceContinuationTests`, `WorkspaceMainWindowContinuationTests`, `WorkspacePlayer_`).
- 792 affected workspace, Browser, Player host, and Player review tests passed.
- All 1,841 tests in the complete test suite passed, with no failures or skips.

Coverage includes schema/legacy/partial-corruption recovery and atomic persistence; repeated save/load lifecycles; independent, missing, offline, and cancelled tree branches; actual MainWindow startup over a temporary Catalog; recursive query, result-pruned multi-selection/anchor, layout and view restoration; current AssetId relocation/root remapping; deleted/offline media fallback; current-row grid scroll and pixel fallback; paused normalized seek with duration clamping; source-aware cadence and bounded zoom/pan; Subclip selection without moving the saved playhead or writing Catalog intent; and source-load cancellation/lease reuse. Dispatcher milestones and controlled backends exercise the asynchronous boundaries without primarily relying on wall-clock timing. Computer-control tooling was not used.

The final PR handoff additionally requires rebuilding `artifacts/release/LightflowStudio/LightflowStudio.exe` from the final commit with `scripts/Build-Release.ps1 -Mode PullRequest -SkipInstaller`, checking packaged dependency/content/startup validation and freshness, and waiting for GitHub CI. The Draft PR remains unmerged pending explicit approval.

## #227 integration

Grid/Details mode and ordered column IDs/visibility/widths now merge into `WorkspaceLayoutState` through `WorkspaceStateService.SetBrowserDetails`. The existing `WorkspaceGridState` remains the sole owner of persisted Browser selection/anchor and top-asset scroll intent; it additionally records the shared keyboard current AssetId and horizontal offset. Layout mode restores before Browser scope hydration. Column IDs normalize tolerantly, and a presentation switch restores the top asset in the new row grouping without querying or navigating. Queued layout restoration checks both Browser generation and layout/input revision. The actual folder/recursive and folder/Collection Player continuation regressions now exercise both Grid and Details. Historical #247 validation counts above remain unchanged; #227 validation is recorded in `browser-227.md`.
