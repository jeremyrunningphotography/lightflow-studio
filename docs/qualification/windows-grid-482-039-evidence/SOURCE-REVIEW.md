# Accepted-source review — LF-WIN-TST-039
Baseline: 1b37d62fe1bb153e6988661ce65453648d937551.
Accepted: e941fabefde207878d88690000a624bab8853cae; tree ef16701a7522a215962657716d047251128a6f9b.
Baseline is the exact merge-base and remains remote main at verification.

Sixteen changed files: 43 lines of root policy reconciliation; new Avalonia card geometry, card control and virtualized-row presentation; BrowserPane integration, viewport demand projection, selection/refresh projection, scoped Avalonia styles; two UI test files; assignment/history/qualification documentation. See accepted-source.diff and changed-files.txt for the complete diff.

The shipping Windows WPF project references Catalog, Actions and Application; it does not reference Lightflow.UI. No Windows source/test, Application, Domain, Actions, Catalog, dependency manifest/lock, project/build configuration or validation script differs from baseline. Mac presentation dependencies therefore do not leak into the Windows application graph. No Catalog or Preview schema/migration is introduced.

BrowserCardRows supplies O(1) virtualized row projection over existing assets. The new cards consume the existing thumbnail cell and shared selection owner; no decoder, ingestion engine or cache is added. Viewport enumeration changes from list-item MediaAsset data contexts to realized BrowserGridCard AssetIds while retaining bounded demand. Mode changes update realized presentation without cache disposal/discovery. BrowserViewModel adds selection queries, toggle/range/select-all and refresh retention using the existing selection model. These affect consumers of the Avalonia UI project; the Windows WPF consumer remains unchanged. Control/Meta handling exists in the Avalonia presentation, while Windows retains its original keyboard implementation. Scoped card and mode styles do not alter WPF resources.

Functional/visual Windows acceptance must still be performed on the fresh package. Automated WPF Browser and native tests supplement that gate; static non-reachability is not a claim of owner GUI acceptance. Exact cross-platform image/frame comparison is outside this presentation-only delta and was not performed in this assignment.

Read-only Project/native verification: #482 Open, In Progress, Browser, priority unset, native parent #480; #481 Closed, Done, Browser, priority unset; #480 Open, Backlog, Browser, priority unset, children #481–#485 with only #481 closed. The Epic's historical unstarted #482 prose is superseded by #482's active implementation record; no scope/status correction was invented or written. No PR exists for this local transfer and none is created here.
