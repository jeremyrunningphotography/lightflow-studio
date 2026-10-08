# ADR 0007: Cross-platform qualification and release principles

- **Status:** Accepted parity/release principles; exact CI, isolation, support matrix and signing design Deferred
- **Date:** 2026-10-08
- **Authority:** Epic #366, accepted M0/G1/G2/G3 records and owner architecture-publication instruction; [evidence and status](../architecture/convergence-evidence.md).
- **Publication gate:** This documentation Draft PR requires explicit owner acceptance before merge. Accepted principles do not authorize implementation.

## Context

Proof runtime passes and Windows CI preserve bounded evidence. Current CI is Windows-only and production remains WPF. Neither establishes a shipping Mac package.

## Decision

Require common behavioral contracts and shared tests, platform adapter qualification and packaged owner acceptance on Windows/Mac. Preserve Windows support during every migration slice. WPF retirement requires explicit functional, visual, performance, packaging and hands-on acceptance.

Initial Mac distribution is signed/notarized direct download for Apple Silicon arm64; Intel/universal and Store distribution are not initial release requirements. Native assets, pinned dependency provenance/licenses, lifecycle and data safety require qualification. Proof versions/API floors do not establish product minimum OS or toolchain selection.

Follow [AGENTS](../../AGENTS.md), [noninteractive validation](../noninteractive-validation.md) and [release plan](../RELEASE_PLAN.md). Windows private-desktop/package requirements stay unchanged. Mac native tests need qualified isolation and task-owned roots before they can affect a user's desktop/data. No autonomous merge, signing policy or governance change is activated here.

## Alternatives considered

Managed build-only acceptance misses native/package risks. Shipping a Mac build while losing Windows behavior violates parity. A proof matrix cannot replace broader package/physical qualification.

## Consequences

VoiceOver, display/audio/device/sleep/Dock/quit/reopen/launch, upgrade/restore and resource soak remain release gates. RenderTimer -6661 remains unexplained and requires diagnosis/packaged launch qualification. Estimates are unapproved.

## Follow-up work

Approve supported OS/hardware, compiler/runtime, numeric limits, native CI isolation and signing topology separately. Proposed [migration qualification](../architecture/migration-and-qualification.md) adds no workflows or release changes now.
