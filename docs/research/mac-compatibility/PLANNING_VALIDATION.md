# Planning preservation validation — 2026-10-05

The planning branch starts from fetched current main
`1119a907b02ef5cc97c9c62b4f2fddb196e540df`; the research baseline has not advanced.
The independent task checkout is `C:\Git\Agents\Agent-X-Mac-Research`.

- SHA-256 comparisons verified all nine historical Markdown reports and three compact
  evidence files against the original research outputs before commit. Git's normal
  text line-ending normalization does not change historical prose.
- Relative Markdown file links in the preservation directory and documentation index
  were checked for existing targets. Historical external citations are preserved;
  this planning does not claim their continuing prices/versions or native runtime proof.
- `git diff --check` and a docs-only changed-path check passed. No product code,
  project files, packages, TFMs, schemas, TourBox or Resolve implementation changed.
- Epic #366 and M0–M3 (#367–#370) use native child relationships. M1–M3 each have
  M0 as their native prerequisite and no dependency on one another. M0 is a completed
  decision record; all three proofs and the Epic remain Backlog. Priority is unset
  pending owner sequencing. No agent is assigned or started and no M4+ issue is created.
- The preservation PR remains Draft and unmerged pending owner review. M0 completion
  does not assert that any unresolved technical question or Mac release requirement is solved.

No broad application test suite is needed for this documentation-only change.
Repository AGENTS.md independently requires refreshing the local packaged executable
before PR handoff; its private-desktop package smoke/dependency results are recorded
in the PR after execution. That Windows package validation supplies no Mac evidence.
