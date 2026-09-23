# #217 local acceptance

Workspace: `C:\Git\Agents\issue-217-smart-collections`

Branch: `codex/issue-217-smart-collections`

Persistent isolated profile: `C:\Git\Agents\issue-217-smart-collections\acceptance-data`

```powershell
& "C:\Git\Agents\issue-217-smart-collections\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\issue-217-smart-collections\acceptance-data"
```

## Hands-on checklist

1. Browse a Folder, enable Include Subfolders, and add Search/rating/media filters. Create a Smart Collection from the toolbar beside Lock Filters, then from the display-area context menu. Verify Source, recursion, and defining filters are prepopulated and editable.
2. Create from the Collections header and from a Collection Set's context menu. Verify no defining filters; the latter defaults Location to the clicked Set. Create an ordinary Collection inside that Set through New Collection, and also verify New Collection Set.
3. Put a Smart Collection in Set A while sourcing Folder B. Test direct and recursive Sources, including a large previously unvisited tree. Known matching media should appear before reconciliation completes; the spinner/status should indicate updating without blocking search, selection, scrolling, or navigation.
4. Change ratings/flags/labels/keywords and add media in the Source. Verify dynamic membership. Select a static Collection Source and add/remove its members; no recursive filesystem walk should begin merely from that Source choice.
5. Change ordinary Browser filters while viewing a Smart Collection. Reopen Edit Smart Collection and confirm its definition is unchanged. Edit defining filters and Match All/Any; File or path participates normally in either mode, and alternatives stay within their visible field row.
6. Edit Source and Include Subfolders. Verify fresh reconciliation. Edit only filters/name/Location and verify no new filesystem reconciliation. Switch scopes rapidly while a large scan runs; old results and spinners must not reappear.
7. Click a Collection Set and use its caret, keyboard navigation, and right-click menu. The active Browser media must remain unchanged. Verify icon distinction, Smart rename, hierarchy move/reparent/order, and deletion.
8. Drag media onto a Smart Collection and check its menus. No manual add/remove membership should be possible. A static Source Collection cannot be deleted while a Smart Collection references it; edit/delete that dependency first.
9. Close/reopen using the same command. Verify saved definitions, hierarchy, selected Smart scope, and further dynamic updates. Try an unavailable/remapped Source and verify truthful known-content behavior.

## Automated coverage

`SmartCollectionTests` covers versioned query separation, All/Any evaluation, pending state and dynamic classification, required/static Source validation, mutation admission, explicit direct/recursive known-state-before-reconciliation, no manual membership, static membership notifications, optimistic edits and hierarchy operations, restart, migration, backup recovery, logical-root remapping, unavailable Sources, and a 10,000-candidate projection.

`SmartCollectionDialogTests` checks editable Folder/static Source prepopulation, Location independence, shared styled resources, all four creation surfaces, Set creation, icon binding, and retained grid virtualization. It renders the editor offscreen; no visible window or computer control is used.

Existing Catalog/Browser/query/reconciliation/working-state tests remain part of focused validation. Broader local validation excludes test classes that display windows or launch external processes to honor desktop-noninteractive execution. Run full required CI and companion validation again at the post-acceptance Draft PR stage.

Acceptance is pending. This checklist does not close #217 or satisfy the remaining #74 cross-cutting DoD by itself.

## Hands-on iteration: inline filters (2026-09-23)

- Save a Folder view with Images+RAW, a classification facet and search. Verify one multi-value Media Type row, other field rows, File or path, and Match all; membership matches the Browser view.
- Change all to any: search is an ordinary alternative alongside other rows. Edit choices/operators/text directly, remove a row, and add a new filter below the rows.
- New from Collections starts with no filters. A field can only have one row; alternatives are edited inside it. Exercise multiple capture-date ranges and rating thresholds.
- Change Source/recursion and check source-specific known Camera/Keyword/etc. values. Saved absent values remain available. Choosing a Source does not navigate or scan it in the editor.
- Right-click the current Folder and another Folder: Source targets the clicked folder, filters are empty, recursion copies only for the current Folder. Verify Browser scope did not change.
- Nest inside Set A / Set B; verify full breadcrumb, including after moving/renaming the Smart Collection or its parent Sets.
- Confirm toolbar Smart action is closer to filtering than Lock Filters.
- Existing development definitions remain openable; old Any+search intentionally adopts ordinary File or path semantics. No released compatibility contract is changed.

Focused iteration validation: Browser query equivalence, same-field alternatives/date ranges, Any/search, v1 normalization, shared frame-rate value discovery, inline row edits/removal, zero-filter creation, offscreen layout, persistence, navigation and Collection behavior. No computer control or full suite.

## Filesystem Location terminology investigation

A filesystem Location is a logical Media Root (`RootId`, display name) with a machine-specific mapping to a physical directory. It is different from a Smart Collection's organizational Location in the Collection Set tree.

- Add Location chooses an existing folder and display name and registers it through `IMediaRootService.CreateAsync`; it does not create or move a filesystem folder. Root path overlap/exact mapping checks apply.
- Rename Location updates only `MediaRoots.DisplayName` and its update timestamp. It preserves RootId, mapping and files. The separate folder Rename command performs filesystem renaming.
- Reconnect Location invokes `RemapAsync`: validates a selected existing path, then upserts the mapping for the same RootId and machine. Relative paths, AssetIds and their authored state, Collection membership and Smart Source references retain their identities. It does not move/copy files; the replacement directory must correspond to the same relative structure for those identities to resolve as intended.
- Rename/Reconnect are enabled when Catalog is available and the right-clicked top-level storage entry carries a RootId. Reconnect is not limited to offline entries; known offline/unmapped entries remain eligible. Ordinary descendant Folder rows do not expose a manageable Storage root object.
- Existing surfaces include the Locations tree, these menu commands and their dialogs/status notices. Documentation includes ARCHITECTURE.md (Browser Locations, #291), validation/settings-291.md and settings-291-authority.md. Related storage/root vocabulary also appears throughout architecture documentation.

The duplicated word Location is a real UX ambiguity. Renaming the commands should be a deliberate coordinated wording decision across the tree, dialogs, status/accessibility text and documentation. Display terminology can change without changing RootId architecture or storage schema. No terminology changes were made in this iteration.
