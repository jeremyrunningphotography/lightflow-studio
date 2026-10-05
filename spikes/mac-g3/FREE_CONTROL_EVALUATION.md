# Free control evaluation

Verified primary sources 2026-10-05; tested Avalonia 12.1.3 core, package license MIT.

| Strategy | Status | Evidence / recommendation |
|---|---|---|
| Core TableView 12.1+ | Free maintained upstream candidate | Real 18-column/100k-row headless test; bounded recycled rows; selection API inherited from ListBox; choose for further proof |
| Avalonia DataGrid | Deprecated | Official documentation; avoid a new dependency on a deprecated path |
| Current TreeDataGrid | Pro or higher | Official docs require license key; excluded from proof dependencies |
| Historical MIT TreeDataGrid fork | Free source, maintenance burden | Upstream says original MIT source remains available; no maintained fork was selected or tested |
| Custom Details renderer | Possible, untested | Only justified if TableView's missing behavior cannot be added through a bounded adapter |

[TableView](https://docs.avaloniaui.net/controls/data-display/structured-data/tableview)
is core since 12.1, read-only, has resizable/template columns and row/cell recycling,
but no column virtualization. This fits a read-only Browser projection better than a
generic editing data grid. 18 columns were realized per visible row to expose that cost.
Sorting and retained column intent remain application work. The selected experiment
does not establish column reordering, persistence or native accessibility acceptance.

[DataGrid deprecation](https://docs.avaloniaui.net/controls/data-display/structured-data/datagrid/),
[current TreeDataGrid licensing](https://docs.avaloniaui.net/controls/data-display/structured-data/treedatagrid/),
[vendor explanation of historical MIT fork](https://avaloniaui.net/blog/building-a-sustainable-future-for-avalonia).
No commercial UI/runtime/control package or product was installed or adopted.

## Preliminary engineering estimate, not an approved budget

These are uncertain person-day planning ranges for future qualified implementation,
excluding extraction of production services, native Mac acceptance and unrelated screens:

- TableView adapter: 5–10 days for sort/visibility/order/persistence, stable ID/current
  selection, anchors, templates/style and keyboard/accessibility regression coverage.
- Grid realization/cache/input adapter: 10–20 days to replace toy row/template rebuilding
  with stable recycled content, bounded decode/cancellation/cache and accepted selection.
- If core TableView cannot satisfy accepted behavior and a custom Details control is
  needed: 20–40 days initial implementation/qualification, plus ongoing maintenance.
- Complete representative Settings/shortcut slice: 5–10 days reusing neutral contracts;
  no replacement of the existing semantic catalog or conflict engine is proposed.

These estimates identify cost rather than conceal it. They are not a schedule promise,
owner-approved engineering allowance or permission to begin migration. Re-estimate after
Mac fixture results and exact missing TableView behavior. The present evidence supports
testing the free route further; it does not meet the maintainability G3 gate yet.
