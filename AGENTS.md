# Local Codex instructions

## AI development workflow

- GitHub issues are the durable product and implementation specification: what to build.
- Agent/Codex kickoff prompts describe how to execute the issue safely; do not duplicate full issue specifications in them.
- When review changes a product or architecture decision, update the issue first, then execute against it.
- Draft PRs stay unmerged until explicit architecture and hands-on acceptance.
- Materially separate issues normally begin in a clean agent conversation after the prior issue is merged.
- Follow the branch → implementation → validation → Draft PR → review → explicit acceptance → merge/cleanup rhythm.

## GitHub project and roadmap hygiene

- Treat the GitHub Project/roadmap as durable project state and keep it continuously synchronized using its existing field values and conventions.
- Whenever an issue is created or materially updated, reconcile its Project item, Status, Area, Priority, native parent Epic, dependencies, and any directly affected Epic status or Definition of Done. Add new issues to the Project when they belong there.
- Move an issue to the Project's established active-work status only when implementation actually begins; discussion or planning alone is not In Progress.
- When a Draft PR is opened or materially updated, verify that issue, Epic, and Project state still matches its actual scope. Make product or architecture decisions discovered during implementation durable in the issue before executing them.
- After every merge/cleanup, mark completed Project work Done, verify issue closure, update the parent Epic's completed and remaining work and Definition of Done, and verify remaining child statuses still reflect reality. Never close an Epic before all required child work and Definition-of-Done items are complete.
- Do not attach unrelated follow-up work to an Epic merely because it was discovered there. Preserve historical closed issue specifications; record later superseding decisions in current Epic/status updates or authoritative comments.
- Never invent parentage, Priority, Area, Status, or product scope to tidy the roadmap. Surface genuine product ambiguity for input.
- Project reconciliation is part of completing issue-management, PR-handoff, and merge/cleanup work. Target directly affected items routinely; if that work exposes broader mechanical drift resolvable from authoritative repository state, correct it, but surface non-mechanical ambiguity instead of guessing.

## PR preparation and functional-test artifact

- Jeremy functionally tests every change by running `artifacts\release\LightflowStudio\LightflowStudio.exe`.
- Before reporting any PR as ready, always rebuild that exact local packaged executable from the PR branch with:
  `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller`
- Treat a successful `dotnet build`, `dotnet test`, or GitHub Actions artifact as additional validation, not as a substitute for refreshing the local executable above.
- Confirm the packaged startup smoke test and dependency validation pass, verify the executable timestamp is newer than the PR commit, and confirm no packaging smoke-test process remains running.
- If the executable or packaged FFmpeg files are locked, inspect for a hidden leftover `LightflowStudio.exe`; obtain approval before terminating a user-started process, then rebuild.
- Include the refreshed local executable path in the final PR handoff so it is immediately clear which binary Jeremy should test.
