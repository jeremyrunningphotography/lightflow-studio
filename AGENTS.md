# Local Codex instructions

## AI development workflow

- GitHub issues are the durable product and implementation specification: what to build.
- Agent/Codex kickoff prompts describe how to execute the issue safely; do not duplicate full issue specifications in them.
- When review changes a product or architecture decision, update the issue first, then execute against it.
- Allocate the global assignment and create a task-owned branch in an independent full clone before implementation. Implement locally, validate locally, package a runnable application and provide one complete portable evidence ZIP for owner hands-on acceptance. Iterate locally until accepted; do not open a Draft PR at the start or create/update a PR for each acceptance iteration.
- Materially separate issues normally begin in a clean agent conversation after the prior issue is merged.
- After owner acceptance, complete independent Windows qualification where applicable against the exact accepted source. Source may move between platforms through a verifiable commit, bundle or other source-preserving transfer without a PR. Finalize implementation, tests, qualification evidence, registry and historical documentation, then open one implementation PR for the issue. Run required exact-head CI, obtain explicit owner merge authorization, merge normally, verify required post-merge CI, reconcile issue/Project state and safely clean completed branches. Do not bypass checks or release gates.
- One issue has one implementation PR containing production changes, tests, qualification, registry, historical and required workflow documentation. Separate routine administrative, registry-closeout, qualification-only or history-maintenance PRs require explicit owner approval. Record final merge provenance and post-merge CI in issue/PR metadata or comments, without another PR.

## Assignment closeout and documentation CI

- The global [assignment registry](docs/AGENT_ASSIGNMENTS.md) remains mandatory and authoritative for identities and number allocation. Finalize assignment completion records in the single implementation/documentation PR before opening it, distinguishing prepared closeout from actual acceptance/merge. Do not allocate new agents for routine closeout administration.
- When final merge provenance cannot be included in an already-accepted source tree, record it in the issue or PR discussion. Routine registry reconciliation belongs in the same implementation PR; exceptions require explicit owner approval.
- Only changes exclusively within `docs/**` qualify for lightweight CI: changed-file discovery, whitespace/Markdown checks, assignment table/unique-number validation and practical local-reference checks. Root `AGENTS.md`, application, tests, dependencies, build scripts, workflows, configuration, mixed changes and uncertain discovery require full CI. Tags and manual runs retain full CI.
- Documentation-only changes that do not alter accepted production source or relevant validation inputs do not automatically invalidate functional acceptance. Documentation-only post-merge CI is monitoring: preserve failures and pending results without automatically starting another acceptance cycle. Production changes retain explicit owner acceptance and merge authorization; identity, independent workspace, Catalog safety, qualification and evidence requirements remain in force.

## Implementation handoff convention

- Owner handoffs are ZIP-first: place the complete self-contained Markdown report, provenance, validation, launch instructions, manifest/hashes and outstanding gates in one portable evidence ZIP that can be reviewed independently. Keep Codex chat output minimal and link the ZIP; do not duplicate the full report in chat. Runnable applications may remain outside the ZIP when their exact paths and provenance are identified. Screenshots and Computer Use are optional unless visual uncertainty warrants them.
- Significant visual redesigns or material visual uncertainty warrant screenshots and an evidence package. Task-owned GUI verification and screenshots may proceed when authorized by the assignment; otherwise obtain owner permission before controlling the visible desktop. Never bypass a lock screen, change security settings, terminate unrelated processes or access unnecessary personal files.
- Every implementation handoff ZIP must contain a complete Markdown report: agent identity/assignment, exact source commit/tree, build/package provenance, architecture decisions, tests/validation, numbered owner acceptance checklist, exact application/Catalog paths, ready-to-copy persistent-root and independent fresh-root launch commands, screenshots when useful, artifact manifest/SHA-256 hashes, limitations and outstanding gates. Exclude private media, live Catalog databases and retained Preview caches.
- Owner hands-on acceptance, appropriate automated tests, independent Windows qualification where applicable, exact-head CI and explicit merge authorization remain required. This convention changes handoff format only; all storage isolation, packaging and other safety requirements below still apply.

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
- When the single implementation PR is opened or materially updated, verify that its issue, parent Epic, and Project item still reflect the implemented scope. Make product or architecture decisions discovered during implementation durable in the issue before treating the PR description as authoritative.
- After every merge/cleanup, mark completed Project work Done, verify issue closure, update the parent Epic's completed and remaining work and Definition of Done, and verify remaining child relationships and statuses. Do not close an Epic until all required child work and Definition-of-Done items are complete.
- Keep unrelated follow-up work outside an Epic even when discovered during that Epic. Preserve historical closed issue specifications; record later superseding decisions in current Epic/status summaries or authoritative comments.
- Use the Project's existing field values and conventions. Never invent parentage, priority, Area, Status, or product scope merely to tidy the roadmap; surface genuine ambiguity for product input.
- Project reconciliation is part of completing issue-management, PR-handoff, and merge/cleanup work. Normally inspect directly affected items, not the entire Project. Correct broader mechanical drift when authoritative repository state resolves it, and report non-mechanical ambiguity instead of guessing.

## User-facing UI conventions

- Before adding or modifying WPF UI, inspect comparable Lightflow surfaces and reuse their controls/resources, spacing, typography, button hierarchy, dark-theme behavior and dialog interaction patterns.
- Use Lightflow-styled dialogs and explicit action labels; do not introduce raw/default WPF presentation (such as `MessageBox`, stock Yes/No prompts, unstyled controls or default modal chrome) where a shared Lightflow pattern exists or should be created.
- Functional correctness alone is insufficient for new UI: packaged hands-on acceptance must include visual consistency.

## Platform validation and functional-test artifact

- Run Windows Release tests through `scripts\Test-Noninteractive.ps1`; WPF/native tests and `Build-Release.ps1` require the shared private-desktop boundary. Never bypass it or switch its desktop into view. See [noninteractive validation](docs/noninteractive-validation.md).
- Preserve applicable acceptance when only documentation changes and production/validation inputs remain unchanged. Root `AGENTS.md` and mixed implementation changes still require full exact-head CI. A CI command-routing change must receive relevant qualification; it does not authorize changes to accepted production behavior.

- For parallel agent work, use full independent clones under `C:\Git\Agents` (not Git worktrees). Keep edits, build outputs, and data roots within the owning task workspace; never reuse another task's outputs or Jeremy's canonical checkout.
- Launch any agent/test build with `--data-root "<absolute task-owned directory>"`, including packaged hands-on tests. Never launch an experimental build against normal user storage. Packaging supplies its own disposable isolated smoke root.
- Report the exact workspace, packaged executable, and isolated data-root paths in the handoff. Isolation does not authorize merging; normal review and explicit acceptance still apply.
- Every hands-on handoff must also include a complete, ready-to-copy PowerShell startup command: `& "<absolute packaged executable>" --data-root "<exact task-owned acceptance data root>"`. It must launch the freshly packaged branch build; include any genuinely required scenario arguments without omitting `--data-root`.

- Jeremy functionally tests every change by running `artifacts\release\LightflowStudio\LightflowStudio.exe`.
- Before reporting any PR as ready, always rebuild that exact local packaged executable from the PR branch with:
  `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller`
- Treat a successful `dotnet build`, `dotnet test`, or GitHub Actions artifact as additional validation, not as a substitute for refreshing the local executable above.
- Confirm the packaged startup smoke test and dependency validation pass, verify the executable timestamp is newer than the PR commit, and confirm no packaging smoke-test process remains running.
- If the executable or packaged FFmpeg files are locked, inspect for a hidden leftover `LightflowStudio.exe`; obtain approval before terminating a user-started process, then rebuild.
- Include the refreshed local executable path in the final PR handoff so it is immediately clear which binary Jeremy should test.

## Historical documentation maintenance

- Include historical maintenance in the single implementation PR only for a major feature, milestone, consequential architecture/integration decision, product-direction change or important workflow/distribution milestone. Exclude routine bug fixes, minor adjustments, retries and registry administration. Report **Not warranted** with a brief reason when below this threshold. Do not create a separate routine historical PR.
- Preserve accepted source and historical evidence. Record final merge provenance and post-merge CI in issue/PR discussion when they cannot be included in the accepted tree; do not claim acceptance or merge prematurely.

## Agent assignment naming

- Future assignments use `LF-[PLATFORM]-[ACTIVITY]-[NUMBER]`; `LF` identifies Lightflow Studio.
- Platform codes describe required execution, not feature support: `WIN` (Windows), `MAC` (macOS), `ANY` (either), `BOTH` (both). Windows work on shared code is still `WIN`; do not select `BOTH` merely because a feature supports both platforms.
- Choose one primary activity, without compound codes: `DEV` (development, implementation, refactoring, fixes), `RES` (research, investigation, architecture analysis, technical proofs), `DOC` (documentation, ADRs, maintenance), `TST` (testing, QA, regression validation, verification), `OPS` (build, CI, packaging, releases, infrastructure), `REV` (independent code/architecture review, implementation assessment).
- Use one global monotonically increasing sequence across platforms and activities, with at least three digits. Reserve `000` for the foundational naming assignment; regular assignments begin at `001`. Never reuse numbers, including cancelled, abandoned or superseded assignments; after `999`, continue with `1000`.
- Session titles use `[AGENT-ID] — [SHORT DESCRIPTION]`. Keep descriptions short, specific and human-readable. Roadmap identifiers such as R1 or WQ, issue/PR numbers and Epic references belong in specifications and GitHub records, not session titles.
- Continuations retain their original ID, number, primary session title and responsibility unless explicitly revised. Routine corrections, CI troubleshooting, acceptance feedback, documentation corrections, PR reconciliation and merge/cleanup retain the ID; materially separate assignments receive new numbers.
- Prefix task-owned workspaces with the agent ID, for example `C:\Git\Agents\LF-WIN-DEV-001-Classification` or `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-004-Example`. Use independent full clones; do not share mutable workspaces or use Git worktrees unless future owner-approved repository policy explicitly changes that rule.
- Check and maintain [the assignment registry](docs/AGENT_ASSIGNMENTS.md) for number allocation and status. Include the agent ID in initial handoffs, meaningful progress reports, final reports and task-owned evidence/workspace documentation, and PR descriptions where useful.
- Historical XR, XC, XM and other agents retain existing names; do not retroactively rename completed agents. IDs identify work assignments, not permanent agent identities.
