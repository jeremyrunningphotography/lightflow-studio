# Browser Details acceptance (#227)

Starting main: `c6a5477b23f84bb527945e96c87ca5704d8a21ed`.
Branch: `codex/227-browser-details`.

Jeremy accepted functionality hands-on after the row/header alignment, graphical toolbar controls, larger Preview/row density, dark header fill, and visible-column checkmark refinements. Final acceptance preserves that UX. No computer-control tools were used.

## Ownership and data availability

Details is a presentation of `BrowserGridModel.Rows`, grouped one original `BrowserGridTile` per row. Both presentations share scope (folder, recursive folder, or Collection), typed query, selection/anchor, Preview paths, Catalog publication, drag and Open handlers. The two WPF GridView presenters share a column collection; they do not use a DataGrid selection authority or local collection-view sorting. `MainWindow.Details.cs` performs no filesystem, Catalog, discovery, or Preview-store reads.

| Columns | Authoritative resident data |
| --- | --- |
| Preview, Name, Media Type, File Size, Modified Date | Existing Browser entry/thumbnail projection |
| Rating, Flag, Color Label, Color/LUT, Saved Range, Subclips, Keywords | Existing batched Catalog asset-state/classification publication |
| Capture Date, Dimensions, Duration, Frame Rate, Camera, Lens | Existing indexed Preview technical projection |
| Codec | Omitted; not in current Browser technical projection |
| Markers / raw metadata | Omitted; outside #227 |

The accepted ten default columns are Preview, Name, Rating, Flag, Capture Date, Media Type, Dimensions, Duration, Frame Rate, File Size. All other delivered columns are optional. Sorting supports the defaults except Preview, plus Modified Date, using `BrowserQueryEngine`. Optional text/state summaries do not add sorting modes. New typed modes are appended to preserve persisted enum values. Missing values remain last, with existing deterministic name/key ties.

## Performance boundary

The same recycling vertical ItemsControl/VirtualizingStackPanel realizes Details rows. Accepted rows are 64 DIPs, Preview height 54 DIPs. Visual row/cell creation stays bounded by the viewport and visible columns; metadata conversion reads in-memory values. Changing layout does regroup the resident result set in O(asset count), and shared query sorting retains its normal cost. No claim of constant-time switching or virtualized database results is made.

A local Release WPF run over 10,000 synthetic resident assets at a 1440×900 offscreen window measured 184.4 ms Grid → Details and 421.6 ms Details → Grid, with 10 Details rows initially realized. These are diagnostic observations including dispatcher/layout settling, not performance thresholds or cold-folder benchmarks. The test scrolls deep into the result set and asserts fewer than 100 realized rows. #131's existing Catalog-first revisit tests remain in affected/full validation.

## Regression coverage

- `BrowserDetailsTests`: tolerant persistence (unknown/duplicate IDs, bad widths, visibility fallback), shared sort and missing-value behavior, deterministic ties, stable Shift anchor during live re-sorts, resident text/live-state projection, Preview-object retention.
- `BrowserDetailsWpfTests`: actual 10,000-item recycling view, both layout switches, shared query/selection/anchor, header sort reflected in toolbar, header/row origins after narrow/wide resize, column reorder/show/hide/checkmarks, dark padding header, live metadata cells, content-based vertical/horizontal scroll restoration, user-input cancellation of queued restoration, keyboard current-item capture.
- `WorkspaceMainWindowContinuationTests`: Grid and Details variants for real Catalog folder/Include Subfolders/search/filter/multi-selection/anchor/current/layout restoration; folder and Collection, single and subset Player continuation/order and Back behavior.
- `BrowserPlayerViewerLiveInteractionTests.MultiSelection_OpenEntryPoints_BackRetainsSubset`: both layouts, descending Browser order, Enter/double-click/context Open, traversal and return-selection preservation.
- `UiLayoutTests`: original Grid media/admission/chrome/capability checks now follow the shared templates and context-menu resource rather than assuming inline-only resources. Both row templates reference the same asset context menu.
- Existing Browser/Catalog/Preview/Player/workspace tests cover discovery, generation cancellation, shared Preview reuse and publication, Collection scope, recursive scope, source-load cancellation, and Player ownership.

The issue-specific Release run passed 38 tests. Final affected/full-suite results are recorded below after completion. The first affected run exposed three source-structure assertions tied to the former inline Grid template; these were updated to the shared resource placement without changing their intended checks or application UX.

Final local Release validation: 38 focused tests, 1,030 affected Browser/Catalog/Preview/Player/workspace tests, and all 1,886 tests in the complete suite passed with zero failures or skips. The full suite ran with hang diagnostics enabled. No unrelated application behavior was changed to obtain these results.

The final handoff additionally verifies the required packaged startup, FFmpeg/playback dependencies and package contents, executable timestamp newer than the final commit, and absence of leftover smoke-test processes. CI and package results are recorded on the linked Draft PR. The Draft PR remains unmerged; hands-on acceptance is complete, while merge/cleanup and Project Done are intentionally pending.
