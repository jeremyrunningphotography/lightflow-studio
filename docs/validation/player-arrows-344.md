# Player Arrow restoration — issue 344

## Reproduction and RCA

Starting main: `65f9ce508f23269238cfce7ca48f2e6b04d3e318`.

Jeremy reproduced the failure in the unchanged packaged app: open video in integrated Player, click its already-selected displayed folder in the Locations tree, then press Left/Right. Player remains open, but frame stepping stops.

A loaded MainWindow routed-input diagnostic independently confirmed the route. Player-origin input reaches RequestStep; folder-tree-origin plain Arrows are rejected by IsArrowKeyOwnedByFocusedControl before any step is requested. The first Selector ancestor of BrowserFolderTree is MainTabs. TabControl derives from Selector. The earlier stop-at-Player guard cannot apply because the folder tree is outside Player ancestry. The introducing commit is not established; blame does not prove introduction.

The final folder-tree regression fails against the unchanged production source at its first Arrow-handled assertion. With the correction it verifies both directions, alternating input, exact directional-request counts, Ctrl review traversal, Alt marker traversal, and Browser-only tree expansion/collapse.

## Ownership correction

Before: every Selector ancestor claims plain Left/Right unless the walk first reaches Player or Filmstrip.

After: a TabControl is a content boundary only when its actual selected content is in the input origin's visual ancestry. Input from selected content therefore does not become tab-navigation input merely because it reaches a containing TabControl. Focus on the TabControl itself or its tab/header route continues to yield to the Selector. Local TextBox/editable ComboBox, Slider, Thumb, ListBox/ComboBox and other Selectors encountered before this boundary still own their keys. Menu/MenuItem ownership is explicit. Existing Player and Filmstrip boundaries remain.

This is not a special case for MainTabs or one folder tree. Ordinary separate tab content is covered with the same rule, while nested list-item/template editor origins retain local ownership. MainWindow's active Player presentation and Right Panel routing gates are unchanged. Browser grid presentation does not dispatch Player keys; the tree retains native expansion/collapse. Inspector and tab/splitter interactions retain their established shell gates.

Only PlayerViewerHost.xaml.cs changes in production. RequestStep, FrameStepQueue, presented-frame handoff, MediaPlaybackService, Flyleaf backend, coordinator/lease and timestamp policies are unchanged. No focus transfer, alternate stepping path, synthesized transport clicks, #345 architecture, configurable shortcuts or controller work.

## Coverage and isolation

PlayerArrowInputTests: real MainWindow tree route with explicit WPF tree focus; host, transport, media container and Filmstrip routed origins; fullscreen WPF route and Escape; repeated/alternating requests; playing-to-paused behavior; retained-frame handoff; local text, editable ComboBox template editor, Slider, Thumb, list item, dropdown, tab header and menu ownership. A blocked fake step deterministically proves the existing 20-request pending bound and opposite-direction net coalescing through routed requests.

PlayerArrowDecodedFrameTests: routed real-decoder beginning-boundary request, exact consecutive decoded forward PTS, repeated end-boundary requests, silent paused stepping, and playing-source Arrow stopping audio/leaving paused. Existing Flyleaf integration coverage additionally exercises VFR decoded PTS, direct backward stepping, native-operation serialization, alternating input and source lifecycle. Existing Player retention tests remain intact.

Fixtures do not send physical keyboard/mouse input. Keyboard modifier state is saved/set/restored on the test STA thread only around synchronous RaiseEvent, before any pumping/await. They do not depend on desktop held keys. MainWindow test storage stays under this task's artifacts/344 directory and is disposed/deleted after the fixture. No pointer/capture gesture is used. The fake backend gains only a test hook for blocking a step.

Repeat filtering is deliberately unchanged: window/host WPF dispatch accepts routed repeats; PlayerSurfaceInput ignores auto-repeat except handling Space. Both continue to use the existing bounded/coalesced FrameStepQueue. No evidence links this pre-existing route difference to the folder-tree defect. Physical native-surface hold/repeat and visual fullscreen behavior remain hands-on acceptance items.

## Independent finding outside the ownership fix

An exploratory real Player presentation probe opens a 3-second 10-fps audio fixture, sends Left at source start, Right to 0.1 seconds, then Left. Its service timestamp remains 0.1 seconds rather than returning to 0.0. The exact same failure reproduces against unchanged production source and with the authoritative Previous Frame transport button replacing that last Left. This is distinct from Arrow ownership. No backend/presentation change was made to resolve it. The failure alone does not establish whether decoder movement, frame capture/presentation or another existing mechanism is causal.

Original Arrow/transport probes and TRX evidence are retained locally under artifacts/344 and LightflowStudio.Tests/TestResults. These were exploratory diagnostics, not an existing regression assertion removed from the suite. The durable real-decoder test checks forward PTS/boundary/silence/pause; the durable fake-backed presentation test checks dispatch and retained-frame handoff. This PR does not claim a fix for the independent near-start backward-presentation finding. Required hands-on review should compare Arrow and transport behavior on Jeremy's footage; product disposition remains outstanding.

## Validation

- Final focused Release group: 128 passed, 0 failed, 0 skipped (344-focused-final.trx).
- New Arrow cases: all 17 passed in the complete Release run.
- Complete Release run: 2520 passed, 1 failed, 1 skipped, 2522 total. The skip is the opt-in installed Premiere acceptance test.
- Failure: unchanged CatalogCollectionsTests.BackupRestore_PreservesOrganizationAndLeavesPreviewStorageIndependent, NullReferenceException at line 248. It failed before any new Arrow test began. Its single isolated follow-up passed (1/1). Cause remains unverified; neither that pass nor focused coverage makes the full suite green. No full-suite retry or Catalog repair was performed.
- Premiere companion: 90 passed, 0 failed.
- Pinned processing and playback FFmpeg dependency preparation passed.
- Required Build-Release.ps1 -Mode PullRequest -SkipInstaller runs after the final commit; exact package/startup/dependency/freshness/process results are reported in the Draft PR and handoff.

## Hands-on acceptance

Workspace: `C:\Git\Agents\agent-m-arrow-restoration`.
Acceptance data root is preserved from the diagnostic handoff: `C:\Git\Agents\agent-m-arrow-restoration\artifacts\344\acceptance-data`.

```powershell
& "C:\Git\Agents\agent-m-arrow-restoration\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\agent-m-arrow-restoration\artifacts\344\acceptance-data"
```

1. Open video, click its already-selected folder in Locations, and use Left/Right. Compare with transport Previous/Next Frame.
2. Check Player surface/chrome/Filmstrip, repeated and alternating presses, playing-to-paused and start/end boundaries.
3. Check Ctrl+Left/Right review assets and Alt+Left/Right markers where markers exist.
4. Check text editing, sliders/thumbs/dropdowns/list navigation and tab headers keep local keys.
5. Return to Browser grid and confirm tree Left/Right expansion/collapse remains normal.
6. Check fullscreen stepping, Escape restoration and native media-surface hold/repeat behavior.

Draft PR must remain unmerged until explicit architecture and hands-on acceptance. Do not clean up the workspace, package or acceptance data.
