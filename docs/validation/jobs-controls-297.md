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
5. Idle queue: Pause Queue is disabled in both views. Running/queued Export: enabled. While queue-paused, running
   exports finish and new/waiting exports remain held. Resume remains available even if the queue became empty.
6. Check compact/full selection, hover and keyboard focus at narrow and wide panel widths: restrained row treatment,
   distinct status indicators, no layout shift or stock teal rectangle. All header controls remain usable.
7. Complete Visual Index while full Jobs is hidden and Player is open. Return to full Jobs: state/actions are current.
   Foreground Player Visual Index demand must still take priority over background preparation.
8. Premiere full-view clearing explains its session-only scope and keeps reconciliation provenance; retained rows
   may return after restart. Visual Index results are also session-only, as before #297.

No push, Draft PR, issue closure, or merge is authorized until Jeremy accepts the local implementation for PR review.
