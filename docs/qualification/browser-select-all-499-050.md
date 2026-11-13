# Browser Select All — LF-WIN-RES-050

Issue #499, parent bug-bash #491; Windows DARKMATTER, release/1.0.
Owner approved implementation after research on 2026-11-13. Same independent
clone and assignment; research ZIP retained. No PR until owner hands-on acceptance.

## Behavior and boundary

The window now handles unhandled **exact Ctrl+A** after native child handling.
Ready Grid/Details results can be selected from Browser navigation trees,
toolbars and neutral Home/window focus without transferring focus to the grid.
The existing grid shortcut uses the same ownership guard for exact Ctrl+A;
its existing extra-modifier behavior remains local and unchanged.

The ownership guard excludes text/rename controls, independent selectors,
right-panel content (Inspector, Subclips, compact Jobs), menus, open dropdowns,
popups, foreign windows and Player presentation. The shell TabControl is a
container, not a competing selection owner. Closed noneditable Browser toolbar
ComboBoxes remain eligible. Other destinations and hidden/loading results cannot
select stale Browser content.

Selection still uses BrowserGridModel.SelectAll and UpdateBrowserStatusText:
current visible mixed-media query results, prior hidden selection replacement,
existing anchor and Inspector veto. No second selection implementation, focus
transfer, dependency, Player shortcut or playback change.

## Validation and acceptance

BrowserSelectAllRoutingTests exercises real WPF routes with restored thread-local
keyboard state, isolated task-owned Catalog fixtures and populated mixed-media
identities. It covers positive contexts, filtered/repeated selection and anchor,
native text/rename/RichTextBox/editable ComboBox, Subclips and Jobs multi-selection,
compact Jobs/Inspector/Filmstrip/Player ownership, open menus/dropdowns/dialog
ownership, modifiers, empty/loading states and the selection veto.

All native tests and packaging use the required Test-Noninteractive.ps1 and
Build-Release.ps1 private-desktop entry points. Detailed results, original failed
development iterations, package provenance, manifest/hashes and owner checklist
are delivered in the I01 evidence ZIP. Routed-input tests are not physical
keyboard or packaged owner acceptance. No PR, merge, release or main forward-port
is authorized at this handoff.

The owner must verify physical Ctrl+A after fresh/restored startup and folder
navigation, both Browser layouts, mixed/filter results, text editing and other
selection owners. Package startup and dependency gates must pass before handoff.
The evidence report supplies exact executable/data roots and full PowerShell
launch commands. Stop at explicit owner hands-on acceptance.

## Coordination

049 confirmed no conflict: its Player investigation remains independent and has
no Ctrl+A changes. MainWindow.xaml.cs is the shared source boundary; this change
does not modify PlayerViewerHost or Player arrow/speed handling. No other task's
workspace, data or process is used. #491 stays open with remaining findings.

Historical documentation maintenance: **Not warranted** for a bounded shortcut fix.

## I02 — owner focus observations

Owner I01 hands-on result: **12 PASS, 2 NEEDS WORK, 0 FAIL**. Original source
`d133db2`, I01 evidence ZIP and packaged runtime are preserved. Current fetched
release head is still `ca929e5`; no rebase or integration difference is needed.
The I01 behavior remains accepted except for Search Enter and neutral Inspector
focus; no PR is authorized during I02.

Search now handles unmodified Enter locally after child input handling, flushes
the current debounce into the same live query, and synchronously focuses the
Browser grid/Details selection surface. Text, filters and ordering remain intact.
Active popups/menus, modified Enter and not-ready results retain local ownership.
There is no deferred focus callback.

The Inspector has a pointer-only neutral focus sink, outside keyboard Tab order.
Neutral content/background clicks focus the Inspector container without marking
the pointer event handled. Only this exact focused container is eligible for the
existing Browser Select All resolver; its interactive descendants remain excluded.
Controls, selectors, links, marker cards and pointer capture retain ownership.
Player and not-ready Browser guards still apply before the neutral exception.
Transparent hit testing adds no visual layout or styling change.

Focus does not apply or discard description drafts: PropertyChanged binding,
explicit Apply and the existing selection-transition confirmation/veto remain
authoritative. Marker rename keeps its existing LostKeyboardFocus save. No new
save path, selection model, generic focus manager or Player focus change is added.

Expanded focused qualification: **172 PASS / 0 FAIL**, including real WPF routed
keyboard/pointer events and real task-owned Catalog description/marker saves.
Native mouse capture cannot be acquired on the private non-input desktop; its
owner exclusion is tested at the local classifier boundary, with real routed
scrollbar/control pointer cases. Physical drag/scroll is an owner check. Full
Release, installer/portable and packaged gates are recorded in the I02 evidence.
Original compile/fixture failures remain evidence; no production behavior or
assertion was weakened to obtain a pass.

Stop at the eight-point I02 owner acceptance checklist. Do not create a PR,
merge, release or main forward-port; separate final qualification/PR authorization
will follow explicit acceptance.
