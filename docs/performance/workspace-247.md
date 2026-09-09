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

First iteration validation: application build passed without warnings; 54 targeted tests passed (WorkspaceContinuation, WorkspaceMainWindowContinuation, WorkspacePlayer, WorkspaceStateStore, WorkspaceRestorationRegression, BrowserLocationRestoration). MainWindow boundary tests use a temporary Catalog without desktop interaction. The full regression suite and PR publication are deferred until hands-on acceptance. Computer-control tooling was not used.
