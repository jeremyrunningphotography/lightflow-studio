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

## Value/operator editor acceptance iteration

The complete field inventory and operator/cardinality/value-source decisions are in [smart-filter-editor-inventory-217.md](smart-filter-editor-inventory-217.md). There are 17 current descriptors. No predicate/evaluator or composition semantics were added.

- Resolution is authored as exact positive integer width × height in pixels, with known size suggestions where available. This supports 3840 × 2160 before any matching Source asset exists; no invented canonical aspect-ratio list.
- Frame Rate is authored as positive numeric fps, optionally selected from the existing canonical rational rate table plus observed rates. Browser normalization is used by both input and evaluation.
- Duration is authored in seconds, mm:ss or hh:mm:ss and means **is at least** (inclusive >=). The existing query cannot express shorter-than or between; those operators are deliberately absent. Multiple thresholds remain explicit alternatives when capturing existing Browser intent.
- In/Out Range uses **is set / is not set**, consistent with the Player/Browser state indicator. It queries the saved primary range, not transient playback marks. Boolean rows place state directly in the operator control and omit a redundant third value input. An explicit either-state option preserves saved same-field alternatives while still requiring hydrated authored state.
- Camera, Lens and Keywords are conditionally absent from new-row authoring when no selectable vocabulary is available. Saved selections remain editable even if no longer observed. Other authoring fields remain useful with an empty Source.
- Shared descriptors now declare structured input conversion, state operators, authoring availability and suggestions separately from the unchanged Browser-context value-discovery function. No #299 work or unrelated Browser filter behavior was added.
- Values use a dark shared ToggleButton template; checked/focused/hover states change the border rather than the fill. Checklists use the same shared dark brushes and rounded border, natural selected summaries, focusable checkboxes, Down/F4 open and Escape return.
- Filter removal uses a transparent compact 24px action derived from the ordinary Lightflow Button. Alternative removal appears only for multiple alternatives. One restrained container surrounds the filter rows and + Add filter.
- File/path now uses the ordinary TextBox directly. Its optional placeholder lives in the shared TextBox template using the same padding and content alignment, eliminating the independently margined overlay that altered the dynamic input's layout.

Hands-on: test an empty Source with 3840 × 2160, standard/custom fps and a 00:30 inclusive minimum; add a matching asset later. Exercise saved In/Out set/unset, multi-select popup keyboard use and collapsed summaries, multiple numeric/date alternatives, remove actions, blank filter state, and File/path caret/placeholder alignment. Recheck Save current view and All/Any after editing.

## Latest preset/custom iteration

Resolution and Frame Rate default to selectable numeric presets; Custom switches the same value area to structured entry and Presets returns to the selector. No simultaneous unexplained free-entry/rates controls. Binary menus have only their two true states; captured dual-state alternatives remain explicit rather than being silently flattened. Color Label Not set is a semantic unset flag, not a string sentinel. Duration supports inclusive minimum and maximum with per-alternative operators; no exactly/between option. Query documents now write version 3 and read existing development versions.

Aspect Ratio is intentionally **not implemented** under the requested stop condition. Persisted video metadata lacks authoritative source rotation and sample/display aspect ratio; #287 stores an adjustment and playback composes source rotation separately. The current Browser projection cannot truthfully classify displayed geometry for all supported media. See the inventory document for the required normalization/hydration follow-up.

Focused hands-on checks: select common Resolution/Frame Rate presets, switch to Custom and back, save/reopen custom values; choose Not set and Red together; test Duration is at most and is at least at the boundary; verify binary menus contain no either-state choice; recheck captured multi-value filters, Source behavior, All/Any and transient filtering. The remaining 17-field inventory was reviewed; no other editor defect requiring a new query feature was identified.

## Latest local iteration: Aspect Ratio and folder-menu simplification

The ordinary folder context menu no longer exposes Add Location, Rename Location or Reconnect Location. Normal browsing already resolves/creates Browser anchors; none of these menu entries is required for navigation. Underlying handlers/services, RootId, persistence, reconnect and Smart Folder Source identities remain unchanged. Contextual recovery UI is deferred.

Aspect Ratio adds the existing inline checklist with 16:9, 9:16, 4:3, 3:2, 1:1 and 21:9. Verify native portrait, source-rotated landscape and #287 user-rotated video; reopen the saved definition, rotate again and confirm membership updates. Verify EXIF-rotated stills. Resolution continues to match encoded dimensions. Non-square pixels/conflicting display-aspect metadata are unknown pending clarification; see the editor inventory.

Focused validation includes rational persistence, source/authored quarter turns, stale rotation reads and restore, all eight still orientations, legacy raw metadata hydration, retained editor/composition/Sources, folder navigation/menu contracts, and real FFmpeg/FFprobe landscape/portrait fixtures at four source rotations. No full-suite run or publication is authorized for this iteration.
