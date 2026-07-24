# Backlog and GitHub Workflow

## Source of truth

Durable product intent lives in `docs/`.

GitHub issues represent actionable work. An issue should link to its relevant specification
instead of duplicating the entire document.

## Recommended labels

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

## Recommended project fields

- Status: Idea, Discovery, Ready, In Progress, Blocked, Done
- Priority: P0, P1, P2, P3
- Product Area
- Effort
- Target Release
- Confidence: Low, Medium, High

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
