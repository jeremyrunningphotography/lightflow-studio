# Agent assignments

Assignments use `LF-[PLATFORM]-[ACTIVITY]-[NUMBER]`: Lightflow Studio, required execution environment, primary responsibility and unique global assignment number. See [the naming policy](../AGENTS.md#agent-assignment-naming) for codes, session titles, continuation and workspace rules.

This lightweight registry records identity and number allocation. GitHub remains authoritative for product issue scope, native dependencies, priority and Project status; this is not a replacement backlog.

## Assigned IDs

| Number | Agent ID | Short description | GitHub references | Assignment status |
| --- | --- | --- | --- | --- |
| 000 | LF-ANY-DOC-000 | Agent Naming Convention | [PR #382](https://github.com/jeremyrunningphotography/lightflow-studio/pull/382) | Done; accepted and merged |
| 001 | LF-WIN-DEV-001 | Shared Classification Extraction | [Issue #383](https://github.com/jeremyrunningphotography/lightflow-studio/issues/383), [PR #385](https://github.com/jeremyrunningphotography/lightflow-studio/pull/385) | Done; owner accepted and merged 2026-10-08 |
| 002 | LF-WIN-RES-002 | Windows Avalonia Presentation Proof | [WQ #384](https://github.com/jeremyrunningphotography/lightflow-studio/issues/384), [PR #386](https://github.com/jeremyrunningphotography/lightflow-studio/pull/386) | Done; owner accepted CONDITIONAL technical PASS and merged 2026-10-08 |
| 003 | LF-ANY-DOC-003 | Migration Roadmap Preparation | [Epic #366](https://github.com/jeremyrunningphotography/lightflow-studio/issues/366), [Catalog Epic #289](https://github.com/jeremyrunningphotography/lightflow-studio/issues/289), [PR #399](https://github.com/jeremyrunningphotography/lightflow-studio/pull/399) | Done; owner accepted roadmap and registry reconciliation; merged 2026-10-09 |
| 004 | LF-WIN-DEV-004 | Shared Storage Contracts | [Issue #388](https://github.com/jeremyrunningphotography/lightflow-studio/issues/388), [PR #401](https://github.com/jeremyrunningphotography/lightflow-studio/pull/401) | Done; owner accepted architecture, validation and technical delivery; merged 2026-10-09 |
| 005 | LF-WIN-DEV-005 | Windows Catalog Admission | [Issue #389](https://github.com/jeremyrunningphotography/lightflow-studio/issues/389), [Draft PR #403](https://github.com/jeremyrunningphotography/lightflow-studio/pull/403) | Owner-authorized accepted safety-design correction implemented; validation and renewed technical/packaged acceptance pending |
| 006 | LF-MAC-DEV-006 | macOS Storage Adapter | [Issue #390](https://github.com/jeremyrunningphotography/lightflow-studio/issues/390), [PR #404](https://github.com/jeremyrunningphotography/lightflow-studio/pull/404) | Done; owner accepted architecture, native validation within documented limits, shared-contract compliance and technical delivery; merged 2026-10-09 |
| 008 | LF-WIN-REV-008 | Catalog Admission Safety Review | [Issue #389](https://github.com/jeremyrunningphotography/lightflow-studio/issues/389), [PR #408](https://github.com/jeremyrunningphotography/lightflow-studio/pull/408), [Draft implementation PR #403](https://github.com/jeremyrunningphotography/lightflow-studio/pull/403) | Done; owner accepted safety design; documentation merged 2026-10-09; no production implementation |

000 completed the accepted naming publication (#382). 001 completed the owner-accepted bounded R1 shared classification extraction (#383 / #385); completion does not authorize later migration. 002 completed owner-accepted bounded WQ (#384 / #386); acceptance does not authorize production Player integration, WPF retirement, or release qualification. 003 completed owner-accepted roadmap/issue/Project preparation and registry reconciliation (#399, normal merge `ae6abe898e053bbf82df8846a4d73335d3b64282`); it does not authorize or start R2–R7 implementation or allocate implementation agents. Reservations alone do not authorize execution.

004 completed the owner-accepted R2-A shared storage contracts slice (#388 / #401), accepted head `ee77d774f0d4372afcfb28a0278d559f19c3cb50`, normal merge `997079a946bd701b4ae80aa6232ff5dc069888b3`. No additional manual application testing was required for this non-visible slice. Its independent full clone and validation/package evidence are retained at `C:\Git\Agents\LF-WIN-DEV-004-StorageContracts`. Concrete adapters, Windows enforcement, identity safety and closed transfer (#389–#392) remain unstarted and separately gated; R2 is not complete.

005 is authorized by Jeremy's 2026-10-09 kickoff after accepted #388 / #401. Workspace: `C:\Git\Agents\LF-WIN-DEV-005-CatalogAdmission`. Draft review, architecture and packaged owner acceptance remain required. Assignment 006 belongs to the separately authorized Mac assignment and is not reserved here.

006 completed owner-accepted bounded R2-C #390 / #404 at accepted head `3e8e55518ee82e07722ceb7d7fa4f04b675d7829`, normal merge `e2618f17d317de519892cc239671143a3b0949da`. Architecture, native technical validation within documented limits, shared-contract compliance, required CI and owner technical acceptance PASS. Additional hands-on testing was waived for this slice with no Mac UI/workflow. Physical external-drive durability/locking, Finder aliases, surprise removal, power loss, full Mac integration and physical cross-platform handoff remain unqualified; R2 is incomplete. Independent task evidence is retained at `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-006-StorageAdapter`.

**Next available number: 009 (unallocated).** Assignments 005 and 006 were explicitly allocated by the owner kickoff on 2026-10-09. Reservation does not authorize any other assignment.

008 completed Jeremy's authorized independent bounded architecture/safety review supporting #389 / #403. Jeremy accepted the design at `20d35defc1f2e88edb87d6f8ec961df1fdcd1025`; content-free check commit `64d871a988732f8098670ebdc7ebc0fdfc9547a2` preserves the identical accepted tree. Required PR-associated CI run 37991197201 PASS; documentation PR #408 merged normally as `f7daf15ced17d22ef243f11e3e2965bb41fbc7ef`. Independent full clone/evidence: `C:\Git\Agents\LF-WIN-REV-008-CatalogReview`. The strategy is accepted, not a proven correction of either historical CI failure. #389 remains Open / Review; #403 remains Draft/unmerged/blocked. Agent 005 may consume the accepted design under separate bounded implementation authorization; no production correction, policy/support expansion, release or tag change occurred.

007 remains reserved by the separately authorized LF-BOTH-RES-007 in open Draft PR #407 (verified at `8f624bbe9dfb828a5ab53d29e0fab74a7f4cf6bd`). Its current registry status is: Mac/Windows physical phases CONDITIONAL; safe Windows removal and read-only Mac fixture return passed; independent/owner support acceptance pending. Its row, authorization and evidence remain owned by that PR; this completion does not duplicate or replace them. Reconcile its exact accepted record when #407 merges. Assignment 009 is unallocated.

## Number allocation and maintenance

- Check the registry before assigning a number; reserve the next unused global number and record planned assignments before execution.
- Use one monotonically increasing sequence across platforms and activities with at least three digits. Reserve `000` for the foundational naming assignment; regular assignments start at `001`. After `999`, continue with `1000`.
- Never recycle historical numbers, including cancelled, abandoned or superseded assignments. Numbers identify assignments, not permanent agent identities.
- Keep continuations under their original ID, number, primary session title and responsibility unless explicitly revised. Routine implementation or documentation corrections, CI troubleshooting, PR reconciliation, acceptance feedback and merge/cleanup retain the ID. Allocate a new number only for a materially separate assignment.
- Update status as assignments progress. Link actual issues and PRs when available; do not invent missing GitHub references or relationships.
- Preserve historical names, including completed XR, XC and XM agents.
- Keep the registry concise and allocation a lightweight repository documentation operation. Do not introduce a database, coordinator service, automation platform or custom numbering tool.
