# ADR 0002: Shared product and incremental migration

- **Status:** Accepted architectural direction; implementation scope remains separately gated
- **Date:** 2026-10-08
- **Authority:** Epic #366, accepted M0/G1/G2/G3 records and owner architecture-publication instruction; [evidence and status](../architecture/convergence-evidence.md).
- **Publication gate:** This documentation Draft PR requires explicit owner acceptance before merge. Accepted principles do not authorize implementation.

## Context

Lightflow must maintain a functional Windows product while adding initial Apple Silicon macOS support. Accepted M0 and bounded G1/G2/G3 findings support a shared architecture; proofs do not establish release readiness. The owner selected the convergence direction and authorized its documentation on 2026-10-08. [Evidence/acceptance index](../architecture/convergence-evidence.md) distinguishes that instruction from the still-open Epic gate.

## Decision

One shared neutral C# domain/application implementation, feature workflow and action architecture feeds one shared Avalonia UI. Platform code implements narrow capabilities, not duplicate product behavior. Initial Mac scope is arm64 only. Prefer free Avalonia; mandatory commercial dependencies require a separate decision.

Migrate incrementally. WPF remains shipping until explicit Windows functional, visual, performance, package and hands-on acceptance. Permanent WPF Windows plus Avalonia Mac is a fallback requiring owner review, not the selected end state. The preferred shared Details direction is a custom retained-cell control; no silent stock TableView substitution.

Existing [Browser](../BROWSER_ACTIONS.md), [Player](../PLAYER_ACTIONS.md), [keyboard](../KEYBOARD_SHORTCUTS.md), [design](../DESIGN-SYSTEM.md), Catalog and Jobs contracts continue to govern behavior. Shared assembly names, R1–R7/WQ execution, budgets and toolchain are not approved here.

## Alternatives considered

Permanent separate views reduce initial Windows replacement work but sustain duplication. A big-bang rewrite simultaneously discards proven behavior and data/lifecycle boundaries. Neither is selected.

## Consequences

Each slice has one behavior owner, a compatibility adapter and explicit retirement gate. Functional/workflow/visual-interaction/data/development parity is required; capability differences must be deliberate and accepted. Proof acceptance does not complete Mac support.

## Follow-up work

Publish/accept this package before separately authorizing implementation. Follow [migration gates](../architecture/migration-and-qualification.md).
