# Agent assignments

Assignments use `LF-[PLATFORM]-[ACTIVITY]-[NUMBER]`: Lightflow Studio, required execution environment, primary responsibility and unique global assignment number. See [the naming policy](../AGENTS.md#agent-assignment-naming) for codes, session titles, continuation and workspace rules.

This lightweight registry records identity and number allocation. GitHub remains authoritative for product issue scope, native dependencies, priority and Project status; this is not a replacement backlog.

## Assigned IDs

| Number | Agent ID | Short description | GitHub references | Assignment status |
| --- | --- | --- | --- | --- |
| 000 | LF-ANY-DOC-000 | Agent Naming Convention | Pending documentation PR | In Progress |
| 001 | LF-WIN-DEV-001 | Shared Classification Extraction | Not yet linked | Planned |
| 002 | LF-WIN-RES-002 | Windows Avalonia Presentation Proof | Not yet linked | Planned |

000 transitions to Done after successful merge and reconciliation. 001 is the proposed first shared-code extraction milestone for Mac compatibility. 002 is the proposed early Windows GPU/Avalonia presentation qualification. These reservations do not authorize execution.

**Next available number: 003.** It is not allocated by this assignment.

## Number allocation and maintenance

- Check the registry before assigning a number; reserve the next unused global number and record planned assignments before execution.
- Use one monotonically increasing sequence across platforms and activities with at least three digits. Reserve `000` for the foundational naming assignment; regular assignments start at `001`. After `999`, continue with `1000`.
- Never recycle historical numbers, including cancelled, abandoned or superseded assignments. Numbers identify assignments, not permanent agent identities.
- Keep continuations under their original ID, number, primary session title and responsibility unless explicitly revised. Routine implementation or documentation corrections, CI troubleshooting, PR reconciliation, acceptance feedback and merge/cleanup retain the ID. Allocate a new number only for a materially separate assignment.
- Update status as assignments progress. Link actual issues and PRs when available; do not invent missing GitHub references or relationships.
- Preserve historical names, including completed XR, XC and XM agents.
- Keep the registry concise and allocation a lightweight repository documentation operation. Do not introduce a database, coordinator service, automation platform or custom numbering tool.

