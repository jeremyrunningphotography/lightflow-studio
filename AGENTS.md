# Local Codex instructions

## AI development workflow

- GitHub issues are the durable product and implementation specification: what to build.
- Agent/Codex kickoff prompts describe how to execute the issue safely; do not duplicate full issue specifications in them.
- When review changes a product or architecture decision, update the issue first, then execute against it.
- Draft PRs stay unmerged until explicit architecture and hands-on acceptance.
- Materially separate issues normally begin in a clean agent conversation after the prior issue is merged.
- Follow the branch → implementation → validation → Draft PR → review → explicit acceptance → merge/cleanup rhythm.

## Assignment closeout and documentation CI

- The global [assignment registry](docs/AGENT_ASSIGNMENTS.md) remains mandatory and authoritative for identities and number allocation. Record completion in the original implementation/documentation PR whenever practical; avoid chains of PRs solely to mark agents Done, and do not allocate new agents for routine closeout administration.
- When final merge provenance cannot be included in an already-accepted source tree, record it in the issue or PR discussion. If registry reconciliation is necessary, use one registry-only reconciliation through documentation CI.
- Only changes exclusively within `docs/**` qualify for lightweight CI: changed-file discovery, whitespace/Markdown checks, assignment table/unique-number validation and practical local-reference checks. Root `AGENTS.md`, application, tests, dependencies, build scripts, workflows, configuration, mixed changes and uncertain discovery require full CI. Tags and manual runs retain full CI.
- Documentation-only post-merge CI is monitoring: preserve failures and pending results without automatically starting another acceptance cycle. Production changes retain explicit owner acceptance and merge authorization; identity, independent workspace, Catalog safety, qualification and evidence requirements remain in force.

## Implementation handoff convention

- Owner handoffs are ZIP-first: place the complete self-contained Markdown report, provenance, validation, launch instructions, manifest/hashes and outstanding gates in one portable evidence ZIP that can be reviewed independently. Keep Codex chat output minimal and link the ZIP; do not duplicate the full report in chat. Runnable applications may remain outside the ZIP when their exact paths and provenance are identified. Screenshots and Computer Use are optional unless visual uncertainty warrants them.
- Significant visual redesigns or material visual uncertainty warrant screenshots and an evidence package. Always obtain explicit owner permission before controlling the visible desktop.
- Reports must include exact source/build provenance, a runnable isolated launch command, focused owner acceptance instructions, validation results and outstanding gates.
- Owner hands-on acceptance, appropriate automated tests, independent Windows qualification where applicable, exact-head CI and explicit merge authorization remain required. This convention changes handoff format only; all storage isolation, packaging and other safety requirements below still apply.

## Mac owner acceptance data

- Scope retained acceptance Catalogs to one implementation issue on each platform. Reuse them across compatible packages within that issue only; after closeout retain them for troubleshooting/qualification. Never carry them into the next issue: initialize a separate acceptance environment.
- Reuse a stable task-owned `acceptance/persistent` data root and supported-created
  **Lightflow Test** Catalog across compatible Mac packages. This retained owner
  data is independent of package directories; never delete, overwrite, reset,
  silently migrate or repair it without explicit owner authorization.
- Keep automated tests, migration/recovery experiments, package dependency/smoke
  checks and fresh-start acceptance in separate disposable roots. They must never
  target retained acceptance data or normal user storage.
- Before a new package uses retained data, verify exact package/source provenance,
  data-root configuration, Catalog identity/schema, Preview compatibility, local
  admission, recovery evidence and single-writer availability. Refuse uncertain or
  incompatible state; investigate a disposable clone instead. Production admission
  remains authoritative after advisory preflight.
- Give both a primary persistent-root Terminal launch command and a separate
  fresh-root command, always with `--data-root`. Record package timestamp/hashes,
  source SHA/tree, Catalog ID/location and disposable roots. Never include live
  Catalogs, retained databases, private media or caches in portable evidence ZIPs.
  See [Mac acceptance workflow](docs/qualification/mac-acceptance-workflow-036.md).

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

- Run automated Release tests through `scripts\Test-Noninteractive.ps1` (use `-Filter` for focused validation). WPF/native tests and `Build-Release.ps1` require the shared private-desktop boundary; do not bypass it or switch its desktop into view. Explicit hands-on executable launches remain interactive. See `docs/noninteractive-validation.md`.

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

- The implementing agent must update [the Chronicle](docs/history/CHRONICLE.md) within its existing PR when the assignment delivers a major feature or product milestone, a consequential architecture or integration decision, a significant change in product direction or capability, an important development-workflow change, or a major release or distribution milestone. Include qualifying historical updates in the original PR whenever practical; do not defer them to a dedicated history-maintenance agent or create a history-only PR for each milestone.
- Entries must be selective, dated, factual and evidence-backed. Exclude routine bug fixes, minor UI adjustments, CI failures, retries, qualification exceptions and registry administration unless they represent a significant product/architecture integration conflict. Keep detailed validation evidence in qualification reports, issues and PRs.
- Update [the Development History](docs/history/DEVELOPMENT_HISTORY.md) when the milestone meaningfully advances the broader narrative of Lightflow's development; not every Chronicle entry requires a History update. Keep it readable and focused on product evolution rather than implementation minutiae. Update [the Source Index](docs/history/SOURCES.md) whenever a Chronicle entry needs additional evidence references.
- Every agent's final completion report must include a brief historical-maintenance disposition: **Updated** (identify the historical documents changed and milestone represented) or **Not warranted** (briefly explain why the work does not meet the threshold). Do not silently omit this disposition.
- Historical maintenance is part of normal assignment validation, like documentation and tests. Routine qualifying entries require no separate editorial review or approval gate. Normal owner acceptance and explicit merge authorization for the overall PR remain unchanged. Do not reopen completed history issues or Epics.
- Preserve source identifiers, immutable provenance and accepted historical boundaries. Distinguish proposals, implementation, owner acceptance, merge and release. Use accepted source and PR references when final merge provenance is unavailable before merge; record final merge details in the PR or issue discussion rather than creating a follow-up documentation PR solely for that purpose.
- Keep both historical documents explicitly AI-authored. Do not impersonate Jeremy or invent personal motivations or private conversations. Do not publish private transcripts or sensitive source material without explicit owner review. See the [current maintenance guidance](docs/history/DEVELOPMENT_HISTORY.md#editorial-refinement-for-future-maintenance); earlier edition guidance remains preserved as historical provenance.

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
