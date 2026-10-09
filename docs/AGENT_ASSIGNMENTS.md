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

000 completed the accepted naming publication (#382). 001 completed the owner-accepted bounded R1 shared classification extraction (#383 / #385); completion does not authorize later migration. 002 completed owner-accepted bounded WQ (#384 / #386); acceptance does not authorize production Player integration, WPF retirement, or release qualification. 003 completed owner-accepted roadmap/issue/Project preparation and registry reconciliation (#399, normal merge `ae6abe898e053bbf82df8846a4d73335d3b64282`); it does not authorize or start R2–R7 implementation or allocate implementation agents. Reservations alone do not authorize execution.

**Next available number: 004.** It is not allocated by this assignment.

## Number allocation and maintenance

- Check the registry before assigning a number; reserve the next unused global number and record planned assignments before execution.
- Use one monotonically increasing sequence across platforms and activities with at least three digits. Reserve `000` for the foundational naming assignment; regular assignments start at `001`. After `999`, continue with `1000`.
- Never recycle historical numbers, including cancelled, abandoned or superseded assignments. Numbers identify assignments, not permanent agent identities.
- Keep continuations under their original ID, number, primary session title and responsibility unless explicitly revised. Routine implementation or documentation corrections, CI troubleshooting, PR reconciliation, acceptance feedback and merge/cleanup retain the ID. Allocate a new number only for a materially separate assignment.
- Update status as assignments progress. Link actual issues and PRs when available; do not invent missing GitHub references or relationships.
- Preserve historical names, including completed XR, XC and XM agents.
- Keep the registry concise and allocation a lightweight repository documentation operation. Do not introduce a database, coordinator service, automation platform or custom numbering tool.
