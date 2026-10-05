# Accepted Mac planning decisions — 2026-10-05

This is the subsequent owner decision record, separate from the unchanged research.
GitHub issues are the durable execution specifications. This record does not authorize
spike execution, production migration, package/TFM changes or Catalog/Preview schema changes.

## Architecture and support

- Initial Mac support and the first public Mac release are **Apple Silicon / arm64 only**.
  Intel/x64 support and universal binaries are not required; Intel is not a release blocker.
  Reconsideration requires an explicit owner decision supported by demand, hardware,
  toolchain availability and maintenance budget.
- The accepted direction is shared Avalonia UI, neutral C# domain/application logic and
  narrow native adapters. Permanent WPF Windows plus Avalonia Mac is a fallback proposal,
  not the selected architecture.
- WPF remains shipping during incremental migration. Retirement requires explicit Windows
  functional, visual, performance, packaging and hands-on acceptance; no flag-day replacement.
- Initial distribution is signed and notarized direct download. App Store distribution is
  optional later. Prefer free Avalonia; mandatory commercial UI/control/runtime dependencies
  require a separate owner decision.

## Parity and future development

Parity covers functional behavior, workflows, visual/interaction identity, durable authored
data and ongoing development. Native chrome, OS dialogs, encoder availability/bitstreams
and performance may differ only through explicit capability contracts and accepted limits.
No silently missing feature counts as parity. Preserve one Catalog identity and authored
state model; derived Previews remain rebuildable.

Future feature work should define shared semantics, add shared contract tests, implement
shared views and thin adapters, then qualify both platforms. Platform-specific behavior
must be deliberate and observable. This planning does not freeze unrelated Windows work.

## Subsequent owner review: Catalog portability — 2026-10-05

The owner accepted the research/planning direction and authorized qualification and merge
of PR #371, reviewed at `ebd337f546ea8807c819184917bf9ab2fde880c9`.
This additional product direction is separate from the historical investigation:

User-authored Lightflow metadata remains Catalog-owned. Lightflow will not adopt a
Kyno-style sidecar metadata architecture or scatter Lightflow metadata sidecars through
media folders. The long-term target is one logical Catalog usable by either supported
platform, including from a supported central location such as a NAS. Machine/platform
media-root resolution may differ; one logical root may resolve through different Windows
paths and macOS mounts.

Cross-platform Catalog portability/access, root/path remapping and simultaneous/concurrent
Catalog access are distinct. The first is the product goal; this decision neither claims
nor authorizes concurrent multi-writer NAS Catalog support. It prescribes no implementation,
locking model or schema change. M2 / #369 remains responsible for proving safe identity,
root/path policy and supported storage constraints.

## Decisions still open

- Minimum supported macOS version and exact supported hardware/resource requirements.
- Case-sensitive path identity/key policy, Unicode normalization and safe unsupported-root rejection.
- Root remapping UX and machine-specific storage/operational state portability.
- Numeric source/display PTS, frame/seek and audio synchronization tolerances.
- Color, ICC/HDR and pixel comparison tolerances and display-dependent qualification.
- Browser scrolling/virtualization performance, memory and allocation thresholds.
- Actual media backend: libmpv viability versus controlled FFmpeg/Metal/CoreAudio engineering.
- Shared image pipeline dependencies, native format/metadata gaps and pinned licenses.
- Safe Mac native/UI test isolation and physical hardware qualification coverage.
- Signing topology, hardened runtime entitlements and notarization details.
- Proof engineering budgets and corpus sizes; future Intel reconsideration criteria.
- Remaining unresolved questions in the historical reports, including NLE capability and
  packaging/CI contracts, are not silently resolved by accepting the direction.

Proof owners must agree measurable tolerances, representative corpus/counts and bounded
budgets with the owner before treating results as pass/fail. No invented benchmark
threshold or undocumented semantic reduction establishes success.

## Initial proof contracts

**M1 / G1 — Player and native surface.** Run on actual Apple Silicon with a representative
UI/native surface. Bound a libmpv evaluation first; if it fails, record the exact capability
failure and evaluate a controlled FFmpeg/Metal/CoreAudio proof within the approved budget.
Cover source/display PTS, VFR, B-frames, nonzero starts, long GOPs, forward/reverse stepping,
retained pause/seek frames, silent stepping, play/speed/loop, audio drift/mute/volume/device
restart, orientation, Camera/Creative LUT and Compare Original, capture, zoom/pan/fullscreen,
overlays, In/Out, markers, Subclip timing and review. Use representative timing, Color and
audio fixtures with expected results. Distinguish shared semantic, backend and presentation
ownership. Pass only with a credible backend preserving these semantics within approved
tolerances and budget. If neither route is credible, stop and return to the owner.

**M2 / G2 — Catalog, SQLite and paths.** Execute the pinned SQLite provider on actual arm64:
version/options, current schemas, open/read/write/reopen, WAL/checkpoint, backup/restore,
crash/replacement and Windows→Mac→Windows. Preserve AssetId/RootId and all authored data.
Exercise drive letters versus mounts, root remapping, case-sensitive and insensitive volumes,
case-only names, Unicode NFC/NFD, symlinks, legal character differences, removable media,
SMB, offline/remount and file identity. Explicitly test the current uppercase/case-folding
key hazard. Pass only with a documented safe identity/key/remapping policy or explicit safe
rejection of unsupported roots; no silent merges, duplicates, corruption or destructive
repair. Identify any future migration requirement; schema changes need later explicit approval.

**M3 / G3 — Free Avalonia UI and images.** Prove a representative Lightflow-density slice
on arm64 using a maintainable free/open-source path, not a production migration. Pin versions
and licenses. Test realistic Grid/Details/Tree counts, virtualization, scrolling anchors,
selection/multiselect, focus/local keyboard, context menus, drag/drop, dark design tokens,
Settings/shortcut capture, native menus, Retina, accessibility, memory/allocations and native
surface hosting. Exercise images/formats, EXIF orientation, ICC, pixels and resource lifetime.
If DataGrid is deprecated, prove a free alternative or disclose custom-control engineering
cost. Paid controls may explain a gap but cannot become a requirement without owner decision.
Pass requires credible visual/interaction identity, performance, image correctness and
maintainability within agreed thresholds and budget.

Each proof must publish reproducible source/dependency versions, hardware/OS, isolated
workspace/data root, corpus and measurements, failures/limitations and a capability verdict.
Windows WPF/native validation retains the private-desktop boundary. Mac isolation must be
qualified before tests can interfere with an owner's active desktop or data.

## Combined gate and ownership

M1–M3 have M0 as their prerequisite and no dependencies on one another. They may be
executed independently or in parallel only after separate owner authorization, in independent
task workspaces and isolated data roots. Reserved names are Agent X (coordinator), X1 (M1),
X2 (M2), X3 (M3), X4+ (later); this planning assigns or starts no agents.

After all three proofs, the owner reviews G1/G2/G3 together. Only that review may authorize
M4 neutral domain/application extraction, M5 playback/presentation split, M6 platform
filesystem/storage/lifecycle, M7 Mac media backend, M8 shared image/Preview pipeline or
M9+ UI migration. One successful spike never authorizes later production work.

The historical M0–M16 sequence remains a proposal. No M4–M16 issues are created now.
Resolve #322 and TourBox #345/#346/#354/PR #360 remain independent and retain their current
parentage and gates. Non-goals include unrelated grading/RAW/codec features, Resolve
implementation, TourBox transport, generic plugin architecture, Store-first distribution,
initial Intel support and silent WPF replacement.
