# Proposed Mac Compatibility Epic and decision gates

**Proposal only. No Epic/issues/PRs/branches created.** Research baseline main 1119a907b02ef5cc97c9c62b4f2fddb196e540df, 2026-10-05. Existing #322 and TourBox #345/#346/#354/PR #360 remain separate. No speculative Project Area/Priority/parentage is assigned.

## Proposed Epic

Working title: Mac compatibility and shared desktop architecture.

Goal: one Lightflow product and authoring model on Windows/macOS, with shared product rules and eventually shared Avalonia views, narrow native services, explicit capability differences and nondestructive Catalog portability. Windows WPF remains the accepted shipping shell until replacement acceptance.

Non-goals: new grading/codec/RAW features, Resolve implementation, TourBox transport discovery, a generic plugin API, App Store-first release, simultaneous Intel promise, broad feature freeze, or changing historical closed specifications. New product decisions must be recorded in future authoritative issues before implementation; use normal branch→validation→Draft PR→architecture/hands-on acceptance→merge and roadmap reconciliation.

## Ranked recommended spikes (not performed)

| Rank / gate | Narrow question | Fixture/result required | Failure/decision |
|---|---|---|---|
| 1 / G1 | Can a Mac backend deliver exact Player semantics with a compositable surface? | Actual arm64 app surface; VFR/B frames/nonzero-start/long-GOP corpus; forward/reverse PTS, retained pause/seek, silent step, audio drift/speed/loop, LUT+capture+fullscreen | Reject generic clock-based approximation. Compare libmpv before committing to custom FFmpeg clock/render work; if neither meets budget stop architecture commitment |
| 2 / G2 | Can the same Catalog preserve identity safely across OSes? | Native SQLite package reports expected runtime/schema/options; case/Unicode/link fixtures; consistent backup Windows→Mac→Windows IDs/intent equality; crash/replace integrity | Agree initial unsupported-root rejection or portable key migration; no silent duplicate/merge |
| 3 / G3 | Can free Avalonia controls carry Lightflow's Browser/Settings and image identity? | Representative large grid/Details/tree, scroll anchors, selection/local focus, dark styled Settings/shortcuts; EXIF8/ICC/formats; native Retina performance | Select pinned free table realization; costed custom path if needed; no unapproved premium-control dependency |
| 4 / G4 | Can the full native bundle be signed/notarized and installed cleanly? | .NET/media/SQLite/helpers, JIT entitlements/rpaths, .app/DMG, quarantined clean-machine launch, upgrade and preserved data | Fix actual loader/signature evidence before public release; direct download remains first path |
| 5 / G5 | Does Mac Premiere preserve current projections/retries? | Real supported 26.5+ host, UPIA/CCX, pairing/folder grants, source/Subclip/marker readback, project change and save/reopen | Record genuine API/version limitations, no duplicate retries or silent timing loss |
| 6 / G6 | Do Mac filesystem and lifecycle contracts stay nondestructive? | Local/removable/SMB Trash, copy/move/rename, watch overflow/remount, permissions, singleton/Dock/Quit/sleep | Reject destructive fallback; choose native IPC/ownership and supported roots |
| 7 / G7 | Can native tests remain isolated without the owner's GUI? | Dedicated host/session or qualified VM; task roots/process cleanup, native surface tests | No current-session global input; physical GPU/controller/NLE acceptance still required |
| 8 / G8 | Is Intel a supportable business requirement? | Audience/OS/toolchain demand, actual x64 native assets plus physical Player/export acceptance | Keep out of first release absent justified support budget |

TourBox Mac proof belongs to a separately authorized #354/#346 follow-up; it may consume actions/profile changes accepted by the Mac program but must not become an undocumented transport subtask. Resolve proofs remain #322. These dependencies are product acceptance coordination, not duplicate Epic children.

## Proposed children, ordered by dependency

| Proposed slice | Kind | Depends on | Outcome / review gate |
|---|---|---|---|
| M0. Architecture/support/parity ADR and corpus | Decision | This research review | Choose B or A fallback, arm64 scope/OS floor, distribution, case policy, budget and numeric performance/color tolerances |
| M1. Player capability/native-surface proof | Spike | M0 | G1 report, pinned backend/build/license evidence, pass/fail recommendation |
| M2. Catalog/runtime/path portability proof | Spike | M0 | G2 report and identity/recovery decision, no implementation by assumption |
| M3. Free desktop UI/image proof | Spike | M0 | G3 report and pinned control/image dependencies |
| M4. Neutral domain/application extraction | Shared architecture | Accepted M1/M2 contracts; independent low-risk extraction may start after M0 | Domain/query/authoring/jobs/Export/metadata/settings ports build without WPF; Windows behavior preserved |
| M5. Playback/presentation contract split | Shared architecture | M1, M4 boundary agreement | Remove FrameworkElement from neutral backend; same Windows Flyleaf correctness/Color retained |
| M6. Platform filesystem/storage/lifecycle ports | Shared architecture + Mac | M2, M4 | G6 adapters, stable instance/file identity/grants, recovery and nondestructive delete |
| M7. Mac FFmpeg/render/audio backend | Mac implementation | M1, M5; M6 paths/process services | Exact Player contract and corpus passed on real arm64 hardware |
| M8. Shared image/metadata/Preview pipeline | Shared architecture + Mac | M3, M4, M6 | Native pixel/metadata adapters and format/ICC/cache resource acceptance |
| M9. Shared desktop Browser/shell/Settings vertical slice | Shared UI + Mac | M3, M4, M6, M8 | Shared views/tokens/actions; native menu/focus/Dock; no new domain authority |
| M10. Player/Inspector/Subclips/VisualIndex UI | Shared UI + Mac | M7–M9 | G1 native composition and all authoring/capture/dirty-edit parity |
| M11. Export/Jobs/recovery UI and Mac encoding | Mac + shared UI | M4, M6, M9; M7 frame-boundary policy | Probed VideoToolbox, immutable materialization, history/retry/cancel/output evidence |
| M12. Mac Premiere adapter qualification | Integration parity | M6, M9–M11 enough working product | G5 real host acceptance; #255 product scope retained; Resolve stays separate |
| M13. Dual-platform automated/native isolation | Qualification | Incremental from M4, native G7 | Shared/headless + Windows private desktop + Mac native/package corpus; versioned evidence |
| M14. Mac signing/distribution/release qualification | Distribution | M7–M13; G4 may prototype earlier | Signed/notarized .app/DMG, clean install/upgrade, source/license hashes and release gates |
| M15. Windows shared-UI migration | Windows migration | M9–M11 proven; explicit owner gate | WPF replacement only after functional/visual/performance packaged acceptance; retire obsolete views deliberately |
| M16. First-public-Mac acceptance and support docs | Release | Required capability rows, M14 and owner scope | Published parity exceptions/OS/device support, troubleshooting, final source acceptance |

M15 can follow the first Mac release if owner prioritizes availability; it remains required for primary strategy's development-parity destination. If explicitly selecting fallback A, close the migration proposal through a documented architecture decision and budget enduring two-view acceptance. No silent indefinite “temporary” dual-shell state.

Parallelizable after boundary decisions: Catalog/core extraction, image/UI realization, Mac platform adapters, packaging/isolation proofs. Playback/Color/capture need one agreed semantic owner; do not fork competing authorities. Future authorized parallel implementation uses independent clones in C:\Git\Agents and task-owned outputs/data, never canonical checkout or shared build outputs. Dependency order and acceptance matter more than parallel throughput.

## First public Mac release definition of done

- Exact accepted source SHA, pinned .NET/UI/native/FFmpeg/SQLite/Companion versions and minimum supported OS/hardware documented.
- Every required CAPABILITY_PARITY_MATRIX row passed on arm64 hardware or carries an explicit owner-approved exception visible in release documentation; no generic “Mac support” claim for unknown capabilities.
- Same Catalog preserves logical IDs/intent through consistent Windows↔Mac round trips; root remapping and unsupported-name/case behavior are explicit; no raw-open-database transfer workflow.
- Player forward/reverse PTS, paused retained frame, audio, speed/loop, ranges/Subclips/markers, Color/rotation/viewport/fullscreen/capture match accepted semantics within declared tolerances.
- Browser/Details/selection/query/Collections/Inspector/Jobs/Export/Settings/shortcuts and all dirty/modal/local-input ownership workflows pass functional and visual acceptance.
- Recoverable Trash, copy/move/rename/watch/permission/remount/singleton/Quit/sleep behavior is truthful and nondestructive.
- Actual packaged SQLite reports expected runtime/compile options/schema/integrity and single-runtime selection; native media/image dependencies load without user-installed runtimes.
- Shared core/headless suites and Windows regression/private-desktop checks pass; Mac native/package tests use isolated task state/session/process ownership; no smoke process remains.
- Supported Premiere workflow passes real-host acceptance; Resolve/TourBox capability is described according to separately accepted work, not advertised from assumptions.
- Signed/notarized/stapled package passes clean quarantined install and upgrade with preserved data, minimal entitlements, native dependency/license/source manifests and diagnostics.
- Owner architecture and packaged hands-on acceptance occurs before merges/releases; Project/issue/Epic state reconciled then. Maintenance branches/tags follow existing RELEASE_PLAN.md, exact accepted source and no published-tag movement.

## Future-feature policy and three worked examples

| Feature example | Once in shared code/tests | Presentation work | Native qualification |
|---|---|---|---|
| Browser adds a metadata filter | Query intent/descriptor/predicate, Catalog/provider projection, Smart persistence, selection invariants | One shared filter editor/Details column; during transition paired WPF editor also required | Mac/Windows IME/focus and large-grid fixtures; no OS-specific query fork |
| Player adds review navigation mode | Typed action, target/eligibility/range policy, semantic tests and expected PTS sequences | One shared toolbar/status; only surface commands through port | Both backend contract suites and native fullscreen/audio checks |
| Settings adds a shortcut-capable preference | Schema/defaults/migration/profile conflict and staged-save tests | One shared category/editor/conflict row | Primary Cmd/Ctrl, reserved menu gestures, local recording and hold cancellation |

Do not require identical UI chrome to preserve identical product semantics. Each feature issue identifies shared intent and native exceptions, expands the parity matrix/tests, and cannot complete with a platform silently missing. OS capability differences are runtime data behind services; business decisions and durable authoring live in shared owners. This prevents two applications emerging behind a nominal shared repository.
