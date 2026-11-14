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
Neutral outer panel padding releases focus only while Inspector is the active
surface; the other surfaces and interactive tab chrome remain owners.
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

## I03 — superseding Inspector ownership policy

Owner I02: **7 PASS / 1 FAIL**. The failed neutral Inspector-to-Browser check is
superseded. Neutral Inspector interaction now retains Inspector ownership; the
other seven accepted behaviors and original I01/I02 evidence remain preserved.
Release `293cd4b` (#502) is reconciled by local merge `d0d8bea`, preserving prior
source ancestry. #501 is verified merged at `ca929e5`. Only assignment rows
conflicted; authoritative release rows and all 050 history were retained.

The explicit Apply handler uses InspectorDescriptionEditor.ApplyAsync's existing
authoritative result. A stable non-Tab-stop Inspector focus anchor owns pending
saves while the Apply button is disabled. Save-scoped focus-loss, pointer and
deactivation handlers cancel a return after later user interaction. Media/editor
revision and visibility checks prevent stale restoration. All handlers detach at
completion; no timer infers success and persistence code is unchanged.

Only successful, still-current completion requests Browser focus. MainWindow
rechecks ready Home Grid/Details and popup ownership through the existing guard,
then focuses BrowserGridRows without changing selection, filters, sorting or
metadata. Failed validation/save, cancellation and superseded focus/context do
not return focus. The old neutral Inspector Browser-shortcut exception is removed.
Local neutral focus remains to establish Inspector ownership when clicking back
from Browser and to preserve accepted marker focus-loss saves; it never selects
Browser media. No generic focus manager or Player change was introduced.

Focused qualification: **266 PASS / 0 FAIL** with real routed clicks/keys, actual
styled bulk confirmation, controlled pending/failure/cancellation boundaries
around the real Catalog store, saved values/revisions, stable selection until
Ctrl+A, focus changes away/back, menus/workspaces, superseded editors and repeated
Apply. Initial fixture failures and their corrections remain in the I03 evidence.
Full Release and installer/portable/smoke results are recorded in the handoff ZIP.

Stop at the eight-point I03 owner acceptance gate. No PR, merge, tag/release or
main forward-port is authorized before acceptance and subsequent required gates.

## Owner I03 acceptance and final qualification — 2026-11-13

Owner accepted all **8/8 checks, no observations**, at I03
`48316bdfa0b923553ece346d7733efaa34793315` (tree
`46715082103a55e3ebe32c74e008a5ffb11dedde`). Accepted production subtree:
`79d5730fdeb59d61f9c5e4b2d9b7df6546bbb619`; accepted test subtree:
`8e45234de554c0b56641358be1c10941794187c7`.
The eight checks cover native Inspector editing, neutral Inspector ownership,
Apply persistence/confirmation, successful Browser handoff, unchanged selection,
no premature/stale focus return, Search/filtered results, and other selection
contexts in both Grid and Details. No repeat acceptance is required while this
production behavior remains unchanged. Original I01-I03 source, ZIPs, runtime
and isolated acceptance data are retained.

Release `293cd4b` remains an ancestor of the accepted source; no production
reconciliation is needed at this checkpoint. The complete PR production delta
consists of MainWindow.xaml(.cs), MainWindow.Inspector.cs and
MediaInspectorView.xaml(.cs); the regression file is BrowserSelectAllRoutingTests.cs.
No Player frame-stepping, Export filename, metadata persistence, Browser selection
model, dependency or application-wide focus-manager changes are included.

Final focused/full Windows Release, companion, installer/portable and isolated
smoke qualification precede a single implementation PR. The owner conditionally
authorizes normal merge only on green exact-head required CI and unchanged
accepted production behavior. Auto-merge stays disabled. Post-merge verification
and direct issue/Project closeout remain gates. Historical documentation work is
**Not warranted** for this bounded bug fix. No release/tag/main forward-port.

## Final qualification stopped — required red gate

Docs-only checkpoint `a92e40177c2679644da7237bb99bcf6ff079da73` preserves the
accepted application/test/companion/validation/CI inputs exactly. Focused expanded
regressions: **357 PASS /0 FAIL**. Companion: **90 PASS /0 FAIL**. Full Release:
**2613 PASS /13 FAIL /8 existing opt-in SKIP**, total2634, duration5m53s.

NeutralInspectorClickKeepsInspectorOwnershipAndDraftAndExplicitSave failed in
its common RunAsync startup assertion at line769, before the behavior case body.
Task-owned WPF log records UnauthorizedAccessException in ExportQueueStore.Save
(IndependentExportJobs.cs:98) during startup scheduler configuration at
MainWindow.xaml.cs:320. Twelve BrowserFolderContextSafetyTests cases additionally
failed Clipboard.Clear with CLIPBRD_E_CANT_OPEN. These are diagnostic locations;
root cause and causality are not established. Native overlap with049 was reported
after050 started against an idle process inventory; coordination messages were
rejected by automatic approval review and not bypassed. Possible contention is
recorded without dismissing failures or claiming baseline reproduction.

All logs/TRX and the final WPF activity log are preserved. Per the owner's red-gate
instruction: no retry/baseline native batch, no further packaging, no PR, no CI/merge,
no assertion weakening or unrelated production correction. Existing I03 package
is unchanged and explicitly historical, not a fresh final-qualified build.
Owner I03 8/8 acceptance remains valid for unchanged behavior; the required new
final suite is red. Issue499 stays open/Review, parent491 incomplete. Unmerged
branch/evidence/acceptance data retained pending owner review of this blocker.
