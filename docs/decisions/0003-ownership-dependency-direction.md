# ADR 0003: Ownership and dependency direction

- **Status:** Accepted boundary principles; exact project map and ports Proposed
- **Date:** 2026-10-08
- **Authority:** Epic #366, accepted M0/G1/G2/G3 records and owner architecture-publication instruction; [evidence and status](../architecture/convergence-evidence.md).
- **Publication gate:** This documentation Draft PR requires explicit owner acceptance before merge. Accepted principles do not authorize implementation.

## Context

Production consists of a Windows application plus neutral Actions. Conceptual service ownership exists, but `MediaPlayback.cs` exposes WPF surfaces and Browser files mix models with converters. A TFM edit cannot produce a shared product.

## Decision

Domain/application behavior and semantic contracts remain neutral. Shared application code cannot depend on WPF, WinForms, native presentation handles or graphics/audio objects. Concrete Catalog/media/platform implementations consume neutral contracts; composition roots wire implementations. Shared UI owns presentation, not SQL/engines or duplicate feature policy. A framework/native presenter adapter confines interop without exposing it into feature views.

Existing `Lightflow.Actions` retains command/admission/shortcut authority. Do not create another registry, selection model or Jobs runtime. A shell cannot become a dependency of shared code.

**PROPOSED — PENDING OWNER ACCEPTANCE:** names, project count, grouping, visibility and port signatures in [boundary map](../architecture/shared-product-boundaries.md). Create assemblies only as authorized slices require them.

## Alternatives considered

An unowned Common project hides dependency closure. One project per feature over-fragments ownership. Shared behavior referencing the Windows executable defeats portability. These are rejected boundary patterns.

## Consequences

Neutral builds and reference inspection are required evidence. Current production is not claimed to satisfy every target boundary. Extracted behavior cannot retain independent per-OS implementations.

## Follow-up work

R1 is the bounded classification proposal, not general assembly restructuring. Review dependency exceptions through an ADR and approved issue before implementation; proposed enforcement checks require later work.
