# Backlog and GitHub Workflow

## Source of truth

GitHub issues are the durable specification for current product work and acceptance.
Accepted `main` establishes delivered behavior; native issue relationships and the
[Roadmap Project](https://github.com/users/jeremyrunningphotography/projects/4) establish
current hierarchy, planning fields and disposition. Documentation supplies reusable
contracts and context. Issues may link to those documents while recording current
decisions explicitly. Follow [AGENTS.md](../AGENTS.md) for reconciliation and review.

## Recommended labels

The label lists below record the initial backlog scaffold. The live Project's Area
and Priority fields use the current planning vocabulary. Legacy label/field conflicts
require an owner taxonomy decision; do not synchronize or remove labels automatically.

### Type

- `type:epic`
- `type:feature`
- `type:enhancement`
- `type:bug`
- `type:research`
- `type:documentation`
- `type:architecture`

### Product area

- `area:video`
- `area:images`
- `area:files`
- `area:metadata`
- `area:integrity`
- `area:workflow`
- `area:publishing`
- `area:ai`
- `area:platform`
- `area:ux`

### Priority

- `priority:p0`
- `priority:p1`
- `priority:p2`
- `priority:p3`

### Effort

- `effort:small`
- `effort:medium`
- `effort:large`
- `effort:epic`

### Release

- `release:0.9`
- `release:1.0`
- `release:1.1`
- `release:1.2`
- `release:1.3`
- `release:future`

## Current Project fields

Use the live Project's existing values rather than creating fields from this document.
At the October 5, 2026 reconciliation:

- Status: Backlog, Next, In Progress, Review, Done
- Priority: P0 — Critical, P1 — High, P2 — Normal, P3 — Later
- Area: the Project's existing capability/ownership areas

Unset Area/Priority is not permission to guess. Done includes documented completed
disposition such as Not Planned, without changing the issue's closure reason.

## Issue structure

Each feature issue should contain:

- Summary
- User value
- Scope
- Out of scope
- Acceptance criteria
- Dependencies
- Specification link

## Local issue creation

The `scripts/Initialize-GitHubBacklog.ps1` script creates labels and initial issues with
the GitHub CLI. It is designed to be safe to rerun: existing labels are updated and
issues with matching titles are skipped.
