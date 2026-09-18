# Local Codex instructions

## AI development workflow

- GitHub issues are the durable product and implementation specification: what to build.
- Agent/Codex kickoff prompts describe how to execute the issue safely; do not duplicate full issue specifications in them.
- When review changes a product or architecture decision, update the issue first, then execute against it.
- Draft PRs stay unmerged until explicit architecture and hands-on acceptance.
- Materially separate issues normally begin in a clean agent conversation after the prior issue is merged.
- Follow the branch → implementation → validation → Draft PR → review → explicit acceptance → merge/cleanup rhythm.

## Release and maintenance branches

- Normal issues and features start from and target `main`, the development trunk.
- Each released minor family has a maintenance branch named `release/<major>.<minor>` (for example, `release/0.40`, never `release/0.40.0`). Create it from the exact accepted release source; do not reset or rebase `main` to it.
- Patch/hotfix work for a released line starts from its release branch and targets that branch for review. Reconcile every release-line fix into `main` through the normal review/acceptance process; record the corresponding PR or why the fix is already present/not applicable.
- Do not backport ordinary new features. Only the current released minor line is actively maintained unless Jeremy explicitly decides otherwise. Do not introduce a permanent `develop` branch or full GitFlow workflow.
- Release tags and artifacts must identify the exact accepted commit on the applicable release line, not the current development tip. Preserve validation only while its source and inputs remain applicable. Follow [release planning](docs/RELEASE_PLAN.md) for source verification, publication gates, and deliberate unpublished-tag correction; never move a published release tag.

## GitHub project and roadmap hygiene

- The GitHub Project/roadmap is durable project state and must remain continuously synchronized with issues, Epics, PRs, and implementation status.
- Whenever an issue is created or materially updated, reconcile its Project membership, Status, Area, Priority, native parent Epic, dependencies, and any directly affected Epic status or Definition of Done. Add new issues to the Project when they belong there.
- Move an issue to the Project's established active-work status only when implementation actually begins; discussed or planned work is not In Progress.
- When a Draft PR is opened or materially updated, verify that its issue, parent Epic, and Project item still reflect the implemented scope. Make product or architecture decisions discovered during implementation durable in the issue before treating the PR description as authoritative.
- After every merge/cleanup, mark completed Project work Done, verify issue closure, update the parent Epic's completed and remaining work and Definition of Done, and verify remaining child relationships and statuses. Do not close an Epic until all required child work and Definition-of-Done items are complete.
- Keep unrelated follow-up work outside an Epic even when discovered during that Epic. Preserve historical closed issue specifications; record later superseding decisions in current Epic/status summaries or authoritative comments.
- Use the Project's existing field values and conventions. Never invent parentage, priority, Area, Status, or product scope merely to tidy the roadmap; surface genuine ambiguity for product input.
- Project reconciliation is part of completing issue-management, PR-handoff, and merge/cleanup work. Normally inspect directly affected items, not the entire Project. Correct broader mechanical drift when authoritative repository state resolves it, and report non-mechanical ambiguity instead of guessing.

## User-facing UI conventions

- Before adding or modifying WPF UI, inspect comparable Lightflow surfaces and reuse their controls/resources, spacing, typography, button hierarchy, dark-theme behavior and dialog interaction patterns.
- Use Lightflow-styled dialogs and explicit action labels; do not introduce raw/default WPF presentation (such as `MessageBox`, stock Yes/No prompts, unstyled controls or default modal chrome) where a shared Lightflow pattern exists or should be created.
- Functional correctness alone is insufficient for new UI: packaged hands-on acceptance must include visual consistency.

## PR preparation and functional-test artifact

- For parallel agent work, use full independent clones under `C:\Git\Agents` (not Git worktrees). Keep edits, build outputs, and data roots within the owning task workspace; never reuse another task's outputs or Jeremy's canonical checkout.
- Launch any agent/test build with `--data-root "<absolute task-owned directory>"`, including packaged hands-on tests. Never launch an experimental build against normal user storage. Packaging supplies its own disposable isolated smoke root.
- Report the exact workspace, packaged executable, and isolated data-root paths in the handoff. Isolation does not authorize merging; normal review and explicit acceptance still apply.

- Jeremy functionally tests every change by running `artifacts\release\LightflowStudio\LightflowStudio.exe`.
- Before reporting any PR as ready, always rebuild that exact local packaged executable from the PR branch with:
  `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller`
- Treat a successful `dotnet build`, `dotnet test`, or GitHub Actions artifact as additional validation, not as a substitute for refreshing the local executable above.
- Confirm the packaged startup smoke test and dependency validation pass, verify the executable timestamp is newer than the PR commit, and confirm no packaging smoke-test process remains running.
- If the executable or packaged FFmpeg files are locked, inspect for a hidden leftover `LightflowStudio.exe`; obtain approval before terminating a user-started process, then rebuild.
- Include the refreshed local executable path in the final PR handoff so it is immediately clear which binary Jeremy should test.
