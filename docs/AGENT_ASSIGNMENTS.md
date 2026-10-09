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
| 005 | LF-WIN-DEV-005 | Windows Catalog Admission | [Issue #389](https://github.com/jeremyrunningphotography/lightflow-studio/issues/389), [Draft PR #403](https://github.com/jeremyrunningphotography/lightflow-studio/pull/403) | Implemented; validation and owner architecture/packaged acceptance pending |
| 006 | LF-MAC-DEV-006 | macOS Storage Adapter | [Issue #390](https://github.com/jeremyrunningphotography/lightflow-studio/issues/390), [Draft PR #404](https://github.com/jeremyrunningphotography/lightflow-studio/pull/404) | Implemented and published for review; required validation recorded in PR #404; owner architecture/technical acceptance pending |

000 completed the accepted naming publication (#382). 001 completed the owner-accepted bounded R1 shared classification extraction (#383 / #385); completion does not authorize later migration. 002 completed owner-accepted bounded WQ (#384 / #386); acceptance does not authorize production Player integration, WPF retirement, or release qualification. 003 completed owner-accepted roadmap/issue/Project preparation and registry reconciliation (#399, normal merge `ae6abe898e053bbf82df8846a4d73335d3b64282`); it does not authorize or start R2–R7 implementation or allocate implementation agents. Reservations alone do not authorize execution.

004 completed the owner-accepted R2-A shared storage contracts slice (#388 / #401), accepted head `ee77d774f0d4372afcfb28a0278d559f19c3cb50`, normal merge `997079a946bd701b4ae80aa6232ff5dc069888b3`. No additional manual application testing was required for this non-visible slice. Its independent full clone and validation/package evidence are retained at `C:\Git\Agents\LF-WIN-DEV-004-StorageContracts`. Concrete adapters, Windows enforcement, identity safety and closed transfer (#389–#392) remain unstarted and separately gated; R2 is not complete.

005 is authorized by Jeremy's 2026-10-09 kickoff after accepted #388 / #401. Workspace: `C:\Git\Agents\LF-WIN-DEV-005-CatalogAdmission`. Draft review, architecture and packaged owner acceptance remain required. Assignment 006 belongs to the separately authorized Mac assignment and is not reserved here.

006 is authorized for the bounded macOS assessor, fixture and tests. Independent owner acceptance remains pending.

**Next available number: 007.** Assignments 005 and 006 were explicitly allocated by the owner kickoff on 2026-10-09. Reservation does not authorize any other assignment.

## Number allocation and maintenance

- Check the registry before assigning a number; reserve the next unused global number and record planned assignments before execution.
- Use one monotonically increasing sequence across platforms and activities with at least three digits. Reserve `000` for the foundational naming assignment; regular assignments start at `001`. After `999`, continue with `1000`.
- Never recycle historical numbers, including cancelled, abandoned or superseded assignments. Numbers identify assignments, not permanent agent identities.
- Keep continuations under their original ID, number, primary session title and responsibility unless explicitly revised. Routine implementation or documentation corrections, CI troubleshooting, PR reconciliation, acceptance feedback and merge/cleanup retain the ID. Allocate a new number only for a materially separate assignment.
- Update status as assignments progress. Link actual issues and PRs when available; do not invent missing GitHub references or relationships.
- Preserve historical names, including completed XR, XC and XM agents.
- Keep the registry concise and allocation a lightweight repository documentation operation. Do not introduce a database, coordinator service, automation platform or custom numbering tool.
