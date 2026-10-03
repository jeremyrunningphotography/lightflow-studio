# Collection Delete routing (#343)

Verified starting main: `1d452fd1a073503e6bbec8a2f45ff159209110d8`.

## RCA and correction

`MainWindow_PreviewKeyDown` consumed Delete and called source-file deletion before
`BrowserGridRows_KeyDown` could reach the accepted static-Collection membership
removal path. Details uses the same Browser presentation, rows control, and
selection authority, so the preview interception affected both layouts.

The preview route now resolves intent using the existing Browser scope coordinator,
active static/Smart Collection state, presentation, and eligible focused folder
selection before calling a mutation workflow. Static Collections use the existing
confirmed, revision-checked batch membership removal. Smart/ineligible Collection
removal returns without a filesystem fallback. The now-redundant bubbling Delete
branch is removed. No command registry, shortcut configuration, controller layer,
new membership service, Catalog identity change, or #345 architecture is introduced.

The membership confirmation uses the existing dialog and identical singular/plural
wording. A supplied confirmation response and workflow delegates let the routing
boundary be exercised without native dialogs or physical keyboard/mouse state.

## Behavior

- Folder media: existing recycle/preflight/permanent-delete safeguards.
- Eligible focused folder-tree target: existing folder deletion workflow and Shift flag.
- Static Collection: confirm membership removal only, including multi-selection.
- Empty/ineligible static selection: no mutation and no destructive fallback.
- Smart Collection: no static membership, Source, or defining-rule mutation; no file deletion.
- TextBox, RichTextBox, editable ComboBox: editor owns Delete.
- Player opened from a Collection: retained Browser context cannot invoke hidden Browser deletion.
- Explicit media context-menu Delete: unchanged source-file deletion capability.

## Regression coverage

`BrowserDeleteKeyTests` runs offscreen MainWindow fixtures on the existing serialized
STA dispatcher with task-owned storage. It covers Grid/Details single and multiple
membership removal, confirmation labels/count grammar, Cancel, other Collection
membership preservation, unchanged source bytes/AssetIds, Catalog reopen durability,
folder media routing and Shift flag, focused folder deletion, Smart Collection with
selected media, real Collection-to-Player opening, editor ownership, empty selection,
and explicit menu availability plus the existing file workflow.

The obsolete source-text assertion requiring Delete in the bubbling Grid handler
is replaced by these behavioral checks. Existing Collection, Catalog, Details,
Smart Collection and file-operation/preflight tests remain in the focused group.

## Validation and handoff

- Final focused Release group: 155 passed, 0 failed, 0 skipped (15 new Delete cases).
- Full Release suite: 2,504 passed, 0 failed, 1 intentional live-Premiere skip;
  5m 9s. This run used the final production code. The subsequent focused run
  includes strengthened selected-Smart/actual-Player and source-byte assertions.
- Premiere companion: 90 passed, 0 failed.
- `git diff --check`: passed.
- Required post-commit PullRequest package/startup/dependency checks are reported
  in the Draft PR handoff after the package is rebuilt from this commit.
- Two pre-existing xUnit2031 analyzer warnings in ApplicationIdentityTests remain.

The initial offscreen fixture omitted normal Collection-tree hydration and opened
an existing error dialog during scope loading. Its task-owned testhost was stopped;
initializing the hierarchy through the normal refresh corrected the fixture.
No production error-dialog behavior was changed. Final fixtures do not open windows
or dialogs and do not depend on ambient desktop input. Full-suite generated
`.task-notes` diagnostic artifacts are preserved outside the commit.

Workspace: `C:\Git\Agents\agent-l-343`
Branch: `codex/issue-343-collection-delete`
Packaged executable: `C:\Git\Agents\agent-l-343\artifacts\release\LightflowStudio\LightflowStudio.exe`
Persistent isolated acceptance data root: `C:\Git\Agents\agent-l-343\acceptance-data`

```powershell
& "C:\Git\Agents\agent-l-343\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\agent-l-343\acceptance-data"
```

Hands-on acceptance remains Jeremy's: add disposable media to a static Collection,
exercise one/multiple/Delete/Cancel in Grid and Details, verify source files and
other Collection membership, restart, then check Folder recycle and explicit
Collection context-menu deletion using disposable files. Verify Smart Collection,
Player-from-Collection, and text editing retain their specified boundaries.
The Draft PR remains unmerged, with clone and acceptance storage preserved.
