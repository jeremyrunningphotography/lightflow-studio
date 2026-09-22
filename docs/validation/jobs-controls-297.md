# Jobs controls #297 local acceptance

Workspace: `C:\Git\Agents\issue-297-jobs-controls`  
Branch: `codex/297-jobs-controls`  
Baseline: `af303cc3cd89c3933ae3c85d61790b906569d2d1`

Build with `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller`.
Run `artifacts\release\LightflowStudio\LightflowStudio.exe` only with
`--data-root "C:\Git\Agents\issue-297-jobs-controls\artifacts\acceptance-data"`.
The task-local acceptance launcher supplies this argument. No normal Catalog/profile is used.

Automated validation runs on a private Windows desktop that is never switched to the input desktop, using task-owned
temporary data and dependencies. Focused coverage exercises lifecycle and mixed-selection eligibility, filesystem
retention/reload without media or checkpoint deletion, Export History concurrent completion/removal, scheduler
pause/resume/cancel/recovery, Visual Index retry/current identity and foreground priority, hidden-view completion,
real WPF context-menu targeting, multi-selection, typed mixed cleanup, and stable focus/selection layout.
The normal packaged smoke and dependency checks remain required; no CI or remote PR update is used for local iteration.

Hands-on checks:

1. Complete Export and filesystem Jobs, then create failed and cancelled Jobs. In compact Jobs, use individual
   Clear and Clear all. Mix terminal Jobs with active work: only terminal cards disappear; active work continues.
   Full Jobs still contains saved records after compact clearing.
2. In full Jobs, select mixed terminal capabilities/outcomes and delete them. Include an active row to verify
   selected removal disables. Clear all terminal Jobs removes only eligible rows in the current search/filter.
   Reopen the workspace, refresh, and restart: deleted Export/filesystem records remain absent, media stays intact.
3. Right-click a different row from the previous selection. The menu acts on that row. Right-click within an existing
   multi-selection and confirm selection remains intact; the menu's singular actions target only its context row.
4. Failed Visual Index offers Retry and uses its original video even if Browser selection changed. Failed Export
   offers Review & Rerun when History exists. Filesystem/Premiere do not offer unsupported Retry. Waiting Export
   offers Pause; paused Export offers Resume; running Export offers Cancel without an unsupported process Pause.
5. Idle queue: Pause Queue is disabled in both views. Running/queued Jobs: enabled. While queue-paused, running
   jobs finish and new/waiting jobs of every capability remain held. Resume remains available even if the queue became empty.
6. Check compact/full selection, hover and keyboard focus at narrow and wide panel widths: restrained row treatment,
   distinct status indicators, no layout shift or stock teal rectangle. All header controls remain usable.
7. Complete Visual Index while full Jobs is hidden and Player is open. Return to full Jobs: state/actions are current.
   Foreground Player Visual Index demand must still take priority over background preparation.
8. Premiere full-view clearing explains its session-only scope and keeps reconciliation provenance; retained rows
   may return after restart. Visual Index results are also session-only, as before #297.

No push, Draft PR, issue closure, or merge is authorized until Jeremy accepts the local implementation for PR review.

## Hands-on revision after 14bb955

- Queue mixed capabilities and multiple Export submissions. New Jobs appear at the bottom in both surfaces;
  finishing or starting work does not move rows. Explicit waiting-Export reorder remains authoritative.
- Expand terminal cards: only Clear remains in the inline action row. Failed Visual Index can still Retry through
  its context menu. Waiting Export exposes Pause; held Export exposes Resume; running Export exposes Cancel.
- Click an Export output path in either surface. Explorer selects that output. Missing outputs produce a styled
  explanation and never close the application.
- Right-click a completed Export and choose Review & Rerun with no LUT folder configured. It opens the established
  review flow without crashing, preserves original source/configuration validation, and does not immediately execute.

Additional automated coverage exercises chronological mixed-capability ordering across lifecycle changes, retained
scheduler reorder, inline action visibility, single-card Clear, output hyperlink routing (using an injected shell
launcher so tests never open Explorer), and the real no-LUT context-menu Review & Rerun regression.

### Shared Active jobs acceptance revision

Both surfaces now use one admission limit for Export, Visual Index, promoted filesystem operations, and Premiere.
Set Active jobs to 1, start a long Export and submit Visual Index: it must show Waiting without elapsed execution
time. Increase to 2: Visual Index can start alongside Export. Lower to 1: running jobs finish normally and no new
Job starts until capacity is available. Pause Queue, enqueue mixed capabilities, and verify every new Job waits;
cancel a waiting job and confirm no executor-side mutation. Resume and verify shared FIFO admission. Premiere
handoffs remain serial and do not reserve unused slots while waiting for another Premiere handoff. Check both
compact and full controls, persistence across restart, cancellation/failure releasing capacity, and foreground
Player responsiveness. Direct operations and foreground Player frame demands are not queued Jobs.

Automated coverage includes actual Export plus Visual Index concurrency, waiting VI timestamps/cancellation,
filesystem waiting cancellation with no mutations, Premiere waiting cancellation before journal dispatch,
exclusive-lane capacity, and queue pause/live limit behavior. Existing Export recovery and foreground frame-priority
regressions remain part of the full suite. Saved settings keys retain their legacy names for compatibility.
