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
