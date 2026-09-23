# #217 local acceptance

Workspace: `C:\Git\Agents\issue-217-smart-collections`

Branch: `codex/issue-217-smart-collections`

Persistent isolated profile: `C:\Git\Agents\issue-217-smart-collections\acceptance-data`

```powershell
& "C:\Git\Agents\issue-217-smart-collections\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\issue-217-smart-collections\acceptance-data"
```

## Hands-on checklist

1. Browse a Folder, enable Include Subfolders, and add Search/rating/media filters. Create a Smart Collection from the toolbar beside Lock Filters, then from the display-area context menu. Verify Source, recursion, and defining rules are prepopulated and editable.
2. Create from the Collections header and from a Collection Set's context menu. Verify blank defining rules; the latter defaults Location to the clicked Set. Create an ordinary Collection inside that Set through New Collection, and also verify New Collection Set.
3. Put a Smart Collection in Set A while sourcing Folder B. Test direct and recursive Sources, including a large previously unvisited tree. Known matching media should appear before reconciliation completes; the spinner/status should indicate updating without blocking search, selection, scrolling, or navigation.
4. Change ratings/flags/labels/keywords and add media in the Source. Verify dynamic membership. Select a static Collection Source and add/remove its members; no recursive filesystem walk should begin merely from that Source choice.
5. Change ordinary Browser filters while viewing a Smart Collection. Reopen Edit Smart Collection and confirm its definition is unchanged. Edit defining rules and Match All/Any; Search narrows either mode, and same-field alternatives remain OR in Match All.
6. Edit Source and Include Subfolders. Verify fresh reconciliation. Edit only rules/name/Location and verify no new filesystem reconciliation. Switch scopes rapidly while a large scan runs; old results and spinners must not reappear.
7. Click a Collection Set and use its caret, keyboard navigation, and right-click menu. The active Browser media must remain unchanged. Verify icon distinction, Smart rename, hierarchy move/reparent/order, and deletion.
8. Drag media onto a Smart Collection and check its menus. No manual add/remove membership should be possible. A static Source Collection cannot be deleted while a Smart Collection references it; edit/delete that dependency first.
9. Close/reopen using the same command. Verify saved definitions, hierarchy, selected Smart scope, and further dynamic updates. Try an unavailable/remapped Source and verify truthful known-content behavior.

## Automated coverage

`SmartCollectionTests` covers versioned query separation, All/Any evaluation, pending state and dynamic classification, required/static Source validation, mutation admission, explicit direct/recursive known-state-before-reconciliation, no manual membership, static membership notifications, optimistic edits and hierarchy operations, restart, migration, backup recovery, logical-root remapping, unavailable Sources, and a 10,000-candidate projection.

`SmartCollectionDialogTests` checks editable Folder/static Source prepopulation, Location independence, shared styled resources, all four creation surfaces, Set creation, icon binding, and retained grid virtualization. It renders the editor offscreen; no visible window or computer control is used.

Existing Catalog/Browser/query/reconciliation/working-state tests remain part of focused validation. Broader local validation excludes test classes that display windows or launch external processes to honor desktop-noninteractive execution. Run full required CI and companion validation again at the post-acceptance Draft PR stage.

Acceptance is pending. This checklist does not close #217 or satisfy the remaining #74 cross-cutting DoD by itself.
