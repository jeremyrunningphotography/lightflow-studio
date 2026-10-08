# ADR 0006: Image, Preview and Color boundaries

- **Status:** Accepted shared boundary direction; exact production pixel API and display qualification Proposed/Deferred
- **Date:** 2026-10-08
- **Authority:** Epic #366, accepted M0/G1/G2/G3 records and owner architecture-publication instruction; [evidence and status](../architecture/convergence-evidence.md).
- **Publication gate:** This documentation Draft PR requires explicit owner acceptance before merge. Accepted principles do not authorize implementation.

## Context

Current WIC/BitmapSource paths are Windows implementations. G3 supplies bounded EXIF/ICC/alpha/lifetime and native format-gap evidence, not arbitrary-format or calibrated-display certification.

## Decision

Keep one shared image/metadata/identity contract and common Skia path with narrow native format-gap decoders. Mac ImageIO fills tested TIFF/HEIC gaps; common JPEG stays on Skia because decoder pixels differ. Native fallback returns the same owned-pixel boundary, not a separate feature/view.

Normalize EXIF orientation exactly once; authored rotation remains separate. Preserve source/profile provenance, explicit alpha/channel/stride/destination-space intent and resource ownership. Source file handles must be released after detached decode; native memory cannot escape unowned into shared views.

Catalog owns Camera then Creative Color/LUT intent; live Player, derived Preview and immutable Export snapshots consume it through their own adapters. Preview demand/retry/source/work identity and conditional publication remain shared and rebuildable. No new per-view cache/probe/decoder owner.

Initial SDR boundary explicitly identifies source/destination color space for OS-profile presentation. G3 supports source-profile → sRGB pixels → sRGB-tagged presentation → OS display conversion; it is not proof of physical parity. Avoid double monitor transforms. Exact production pixel signatures, upload/copy optimization and display-notification implementation remain **PROPOSED — PENDING OWNER ACCEPTANCE**.

## Alternatives considered

Direct framework image loading failed orientation cases; arbitrary decoder substitution changes JPEG pixels. Native whole-view forks and untagged presentation hide divergence. None is selected.

## Consequences

Windows image goldens, broader variants, calibrated display/per-monitor transitions and HDR/wide gamut remain qualification/deferred scope. No zero-copy guarantee or new package version is approved.

## Follow-up work

Define owned-pixel API and corpus/tolerances before implementation. Follow current [Color/Preview architecture](../ARCHITECTURE.md) and [G3 evidence](../architecture/convergence-evidence.md); qualify supported versions and licenses.
