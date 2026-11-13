# Agent assignments — release/1.0

This maintenance line predates the global registry. Global allocation and history
remain authoritative in the [main registry](https://github.com/jeremyrunningphotography/lightflow-studio/blob/main/docs/AGENT_ASSIGNMENTS.md).
The table preserves main's allocation snapshot at
`c6685baae8707fd3ef14c5bb67ee649d309de272` and adds this maintenance assignment.
Unrelated historical narratives are referenced rather than copied into this branch;
no unrelated status is reconciled by #492. Consult current main, this supplement
and owner allocations before reserving a number. IDs are global and never reused.
See [naming policy](../AGENTS.md#agent-assignment-naming).

## Assigned IDs

| Number | Agent ID | Short description | GitHub references | Assignment status |
| --- | --- | --- | --- | --- |
| 049 | LF-WIN-RES-049 | Player Frame Stepping | Issue #498, parent #491 | Owner accepted I01 8/8; focus preserved; final focused358 PASS, full2589 PASS/2 native startup presentation failures/8 opt-in SKIP; isolated release comparison2 PASS does not prove baseline failure; qualification blocked, no PR/merge; decoder research deferred |
| 048 | LF-WIN-DEV-048 | Export Active In/Out | [Issue #497](https://github.com/jeremyrunningphotography/lightflow-studio/issues/497), [PR #502](https://github.com/jeremyrunningphotography/lightflow-studio/pull/502), parent #491 | Done; owner accepted 8/8 PASS; normal release/1.0 merge 293cd4b; post-merge CI 43511787157 SUCCESS; retired; accepted changes and evidence preserved; no release/main forward-port |
| 047 | LF-WIN-DEV-047 | Export Subclips Reliability | [Issue #496](https://github.com/jeremyrunningphotography/lightflow-studio/issues/496), [PR #501](https://github.com/jeremyrunningphotography/lightflow-studio/pull/501), tracking #491 | Done; owner accepted 10/10 PASS, zero observations; normal release/1.0 merge ca929e5; post-merge CI 43496848475 SUCCESS; #496 Completed / Project Done; accepted production preserved; local implementation branch retired; evidence retained; no release/main forward-port |
| 046 | LF-WIN-DEV-046 | Windows Folder Context-Menu Safety | [Issue #492](https://github.com/jeremyrunningphotography/lightflow-studio/issues/492), tracking #491 | Owner accepted 17/17 PASS; qualification PASS; prepared closeout; exact-head CI and explicit merge authorization required; not merged/Done |
| 043 | LF-WIN-ADM-043 | Details final qualification and closeout | Issue #483, parent #480 | Prepared closeout; accepted source preserved; fresh Windows 2884/0/2, Details 14, shared 355 and Premiere 90 PASS; owner authorizes normal merge after package/exact-head CI; actual merge/post-merge provenance in #483/PR |
| 042 | LF-WIN-DEV-042 | Windows Details Presentation Parity | [Issue #483](https://github.com/jeremyrunningphotography/lightflow-studio/issues/483), parent #480 | Done through corrected source ce35e036 / c700cb3d and owner Windows 4/4 PASS; full Release 2884/0/2; final integration/CI/merge by 043 |
| 041 | LF-WIN-TST-041 | Mac Details Windows Qualification | Issue #483, parent #480 | Done; exact b5694012 / 741442a9 independently qualified: full Release 2880/0/2, focused 670, package and Catalog safeguards PASS; later corrections qualified separately |
| 040 | LF-MAC-DEV-040 | Mac Browser Details | [Issue #483](https://github.com/jeremyrunningphotography/lightflow-studio/issues/483), parent #480 | Done through accepted I04 b5694012 / 741442a9, owner Mac 4/4 PASS and independent 041 PASS; final integration/CI/merge by 043 |
| 039 | LF-WIN-TST-039 | Mac Grid Windows Qualification | [Issue #482](https://github.com/jeremyrunningphotography/lightflow-studio/issues/482), parent #480 | Done; accepted e941fabef / ef16701a independently qualified with owner-approved inconclusive Catalog exception; focused 606 PASS, full 2879 passed / 1 failed / 2 skipped; Windows owner 6/6 PASS; implementation integration/CI/merge tracked by 038 |
| 038 | LF-MAC-DEV-038 | Mac Browser Grid Cards | [Issue #482](https://github.com/jeremyrunningphotography/lightflow-studio/issues/482), parent #480 | Done through accepted implementation/qualification; Mac I02 8/8 and Windows 6/6 accepted at e941fabef / ef16701a; final single-PR CI/merge/post-merge gates remain pending at record preparation |
| 037 | LF-WIN-TST-037 | Browser Thumbnail Windows Qualification | [Issue #481](https://github.com/jeremyrunningphotography/lightflow-studio/issues/481), [PR #486](https://github.com/jeremyrunningphotography/lightflow-studio/pull/486) | Done; accepted 93ab479e / ea732bc6 qualified; Gate A/focused/package/Catalog safety PASS; full Release 2,880 passed / 2 skipped; owner Windows 6/6 PASS; original failed attempts retained; implementation merged 4d182639 |
| 036 | LF-MAC-DEV-036 | Browser Thumbnail Foundation | [Issue #481](https://github.com/jeremyrunningphotography/lightflow-studio/issues/481), parent #480 / #395 / #366 | Done; owner Mac/workflow and Windows acceptance PASS; #486 normal merge 4d182639 preserves accepted 93ab479e / ea732bc6; independent LF-WIN-TST-037 PASS; exact-head/post-merge CI PASS; #481 Completed |
| 034 | LF-WIN-DEV-034 | Documentation CI & Workflow Simplification | [Issue #475](https://github.com/jeremyrunningphotography/lightflow-studio/issues/475), parent #290 | In Progress; independent DARKMATTER implementation; owner review and explicit merge authorization required |
| 033 | LF-ANY-ADM-033 | Legacy PR Reconciliation | [PR #473](https://github.com/jeremyrunningphotography/lightflow-studio/pull/473), superseded #457/#448 | Done; #473 normal merge 4fb5b3f preserves accepted tree; exact-head/post-merge CI PASS; historical PRs closed without merging and three completed branches deleted; final registry publication pending |
| 032 | LF-ANY-ADM-032 | GitHub Administrative Reconciliation | [Issue #465](https://github.com/jeremyrunningphotography/lightflow-studio/issues/465), related #395/#366 | Done; native #395 parent and Project Done/Browser/unset Priority verified; three completed task branches deleted; #471 normal merge f9c5529 and required post-merge CI PASS; original CI failure preserved |
| 031 | LF-WIN-TST-031 | Mac Locations Windows Qualification | [Issue #465](https://github.com/jeremyrunningphotography/lightflow-studio/issues/465), [PR #467](https://github.com/jeremyrunningphotography/lightflow-studio/pull/467) | Done; Windows qualification owner-accepted with startup-frame exception; original 2879 passed / 1 failed / 2 skipped and D — Inconclusive preserved; implementation fc00a68 and docs 8ff2774 merged; post-merge CI PASS |
| 030 | LF-MAC-DEV-030 | Mac Locations Redesign | [Issue #465](https://github.com/jeremyrunningphotography/lightflow-studio/issues/465), [PR #467](https://github.com/jeremyrunningphotography/lightflow-studio/pull/467), related #395/#366 | Done; Mac I01–I06 and Windows exception qualification owner-accepted; implementation merge fc00a68, qualification docs merge 8ff2774; required post-merge CI PASS; #465 Completed; native-parent/Project access gaps documented |
| 028 | LF-MAC-DEV-028 | Mac Browser UI Parity | [Issue #462](https://github.com/jeremyrunningphotography/lightflow-studio/issues/462), [PR #464](https://github.com/jeremyrunningphotography/lightflow-studio/pull/464), parent #366 | Done; #462 Completed, normal merge 49cb3c7; all four Mac phases and independent DARKMATTER Windows qualification owner-accepted at 3fa23ee / tree b41a6b7; post-merge CI 41369147121 attempt 2 PASS without source changes; Project/native-parent access gap remains |
| 029 | LF-WIN-TST-029 | Mac Shell Windows Qualification | [Issue #462](https://github.com/jeremyrunningphotography/lightflow-studio/issues/462), [PR #464](https://github.com/jeremyrunningphotography/lightflow-studio/pull/464) | Done; independent DARKMATTER qualification at accepted 3fa23ee / tree b41a6b7; Windows tests, installer/portable staging, final executable refresh and Jeremy hands-on acceptance PASS; no production changes |
| 027 | LF-MAC-DEV-027; LF-WIN-DEV-027 companion | Mac Browser Discovery | [Issue #460](https://github.com/jeremyrunningphotography/lightflow-studio/issues/460), [PR #461](https://github.com/jeremyrunningphotography/lightflow-studio/pull/461), parent #366 | Done; Jeremy accepted head 3559281 and Windows companion PASS (156 focused tests/package); normal merge d847d19; #462 unstarted and wider milestones incomplete |
| 026 | LF-MAC-DEV-026 | Finder-Style Browser Navigation | [Issue #456](https://github.com/jeremyrunningphotography/lightflow-studio/issues/456), [PR #458](https://github.com/jeremyrunningphotography/lightflow-studio/pull/458) | Done; Jeremy accepted Mac/Windows hands-on and LF-WIN-DEV-026 companion package PASS at 4fac866; normal merge d2a600f; bounded navigation only, R2/R3/R4 incomplete |
| 025 | LF-MAC-DEV-025 | Catalog-Backed Browser | [Issue #454](https://github.com/jeremyrunningphotography/lightflow-studio/issues/454), related #395/#366 | Done; owner accepted Mac hands-on and same-source Windows companion qualification at 6368099; #455 normal merge 4e1f361 and post-merge CI passed; #456 subsequently completed under 026; broader milestones incomplete |
| 024 | LF-MAC-DEV-024 | Mac Application Shell | [Issue #451](https://github.com/jeremyrunningphotography/lightflow-studio/issues/451), [PR #452](https://github.com/jeremyrunningphotography/lightflow-studio/pull/452), related #395/#366 | Done; owner Mac functional/visual and LF-WIN-DEV-024 DARKMATTER companion package qualification accepted at b4a8e0a; normal merge 3801de8; bounded shell only, no R2/R3/R4 or release completion |
| 023 | LF-MAC-DEV-023 | Catalog Portability | [R2 #393](https://github.com/jeremyrunningphotography/lightflow-studio/issues/393), [physical qualification #406](https://github.com/jeremyrunningphotography/lightflow-studio/issues/406), accepted host [#425](https://github.com/jeremyrunningphotography/lightflow-studio/issues/425) | Research Complete / Production Portability Blocked; owner accepted final bounded CONDITIONAL disposition on 2026-10-20; write-capable SQLite activation, WAL/SHM, pooling and recovery integration unresolved; original production objective not delivered |
| 022 | LF-WIN-DEV-022 | Windows CI Reliability Stabilization | [Issue #446](https://github.com/jeremyrunningphotography/lightflow-studio/issues/446), [merged PR #447](https://github.com/jeremyrunningphotography/lightflow-studio/pull/447) | Partial delivery; accepted journal #447 and initial-startup Browser fixture #449 merged; #446 remains Open/Review; SQLite ownership, historical after-selection Browser failure and playback attribution unresolved; not Done |
| 015 | LF-MAC-DEV-015 | Native Catalog Lifecycle | [Issue #425](https://github.com/jeremyrunningphotography/lightflow-studio/issues/425), accepted prerequisite [#432](https://github.com/jeremyrunningphotography/lightflow-studio/issues/432) | Done; owner accepted native CLI at 3635fff; normal merge #443 / 27d4e95; bounded portability refusals retained |
| 021 | LF-ANY-DOC-021 | Development History and Historical Maintenance | [Issue #430](https://github.com/jeremyrunningphotography/lightflow-studio/issues/430), [History Epic #427](https://github.com/jeremyrunningphotography/lightflow-studio/issues/427), [PR #442](https://github.com/jeremyrunningphotography/lightflow-studio/pull/442) | Done; owner editorial/architecture acceptance; normal merge 12b85ed on 2026-10-13; source/provenance limitations preserved |
| 019 | LF-WIN-DEV-019 | Shared Catalog Lifecycle Extraction | [Issue #432](https://github.com/jeremyrunningphotography/lightflow-studio/issues/432), [PR #435](https://github.com/jeremyrunningphotography/lightflow-studio/pull/435), downstream [#425](https://github.com/jeremyrunningphotography/lightflow-studio/issues/425) | Done; owner technical and Windows hands-on acceptance; isolated-only condition verified; normal merge 89ee65a on 2026-10-12; two local baseline exceptions preserved; Mac host/R2 remain outstanding |
| 000 | LF-ANY-DOC-000 | Agent Naming Convention | [PR #382](https://github.com/jeremyrunningphotography/lightflow-studio/pull/382) | Done; accepted and merged |
| 001 | LF-WIN-DEV-001 | Shared Classification Extraction | [Issue #383](https://github.com/jeremyrunningphotography/lightflow-studio/issues/383), [PR #385](https://github.com/jeremyrunningphotography/lightflow-studio/pull/385) | Done; owner accepted and merged 2026-10-08 |
| 002 | LF-WIN-RES-002 | Windows Avalonia Presentation Proof | [WQ #384](https://github.com/jeremyrunningphotography/lightflow-studio/issues/384), [PR #386](https://github.com/jeremyrunningphotography/lightflow-studio/pull/386) | Done; owner accepted CONDITIONAL technical PASS and merged 2026-10-08 |
| 003 | LF-ANY-DOC-003 | Migration Roadmap Preparation | [Epic #366](https://github.com/jeremyrunningphotography/lightflow-studio/issues/366), [Catalog Epic #289](https://github.com/jeremyrunningphotography/lightflow-studio/issues/289), [PR #399](https://github.com/jeremyrunningphotography/lightflow-studio/pull/399) | Done; owner accepted roadmap and registry reconciliation; merged 2026-10-09 |
| 004 | LF-WIN-DEV-004 | Shared Storage Contracts | [Issue #388](https://github.com/jeremyrunningphotography/lightflow-studio/issues/388), [PR #401](https://github.com/jeremyrunningphotography/lightflow-studio/pull/401) | Done; owner accepted architecture, validation and technical delivery; merged 2026-10-09 |
| 005 | LF-WIN-DEV-005 | Windows Catalog Admission | [Issue #389](https://github.com/jeremyrunningphotography/lightflow-studio/issues/389), [PR #403](https://github.com/jeremyrunningphotography/lightflow-studio/pull/403) | Done; owner accepted corrected architecture, native regression, exact-head CI/package and refreshed packaged functional behavior; merged 2026-10-09 |
| 006 | LF-MAC-DEV-006 | macOS Storage Adapter | [Issue #390](https://github.com/jeremyrunningphotography/lightflow-studio/issues/390), [PR #404](https://github.com/jeremyrunningphotography/lightflow-studio/pull/404) | Done; owner accepted architecture, native validation within documented limits, shared-contract compliance and technical delivery; merged 2026-10-09 |
| 007 | LF-BOTH-RES-007 | Physical Catalog Portability Qualification | [Issue #406](https://github.com/jeremyrunningphotography/lightflow-studio/issues/406), [PR #407](https://github.com/jeremyrunningphotography/lightflow-studio/pull/407) | Done; owner accepted bounded research and normal merge 2026-10-09; Mac/Windows physical CONDITIONAL, Mac return PASS; production support unqualified |
| 008 | LF-WIN-REV-008 | Catalog Admission Safety Review | [Issue #389](https://github.com/jeremyrunningphotography/lightflow-studio/issues/389), [PR #408](https://github.com/jeremyrunningphotography/lightflow-studio/pull/408), [Draft implementation PR #403](https://github.com/jeremyrunningphotography/lightflow-studio/pull/403) | Done; owner accepted safety design; documentation merged 2026-10-09; no production implementation |
| 009 | LF-WIN-DEV-009 | Cross-Platform Path and Catalog Identity Safety | [Issue #391](https://github.com/jeremyrunningphotography/lightflow-studio/issues/391), [PR #414](https://github.com/jeremyrunningphotography/lightflow-studio/pull/414) | Done; owner accepted architecture, automated/native qualification within documented limits, installer/package and packaged functional behavior; merged 2026-10-10 |
| 010 | LF-ANY-RES-010 | Closed Catalog Transfer and Settings Architecture | [Issue #392](https://github.com/jeremyrunningphotography/lightflow-studio/issues/392), [PR #413](https://github.com/jeremyrunningphotography/lightflow-studio/pull/413), [integration #412](https://github.com/jeremyrunningphotography/lightflow-studio/issues/412) | Done; owner accepted architecture and bounded R2 integration contract; PR #413 merged 2026-10-10; no production implementation |
| 011 | LF-WIN-DEV-011 | Verified Closed-Catalog Transfer | [Issue #392](https://github.com/jeremyrunningphotography/lightflow-studio/issues/392), [PR #417](https://github.com/jeremyrunningphotography/lightflow-studio/pull/417) | Done; owner accepted architecture, automated/native qualification within documented limits, installer/package and packaged relocation behavior; merged 2026-10-10 |
| 012 | LF-WIN-DEV-012 | Catalog Settings and Startup Portability | [Issue #412](https://github.com/jeremyrunningphotography/lightflow-studio/issues/412) | Done; owner technical, visual/functional, exact-head CI and installer/package acceptance PASS; PR #422 merged normally 2026-10-11 |
| 013 | LF-MAC-RES-013 | External Catalog Filesystem Qualification | [Issue #406](https://github.com/jeremyrunningphotography/lightflow-studio/issues/406), [PR #419](https://github.com/jeremyrunningphotography/lightflow-studio/pull/419) | Done; owner accepted bounded research and merged 2026-10-10; REVISE transport / BLOCK active ExFAT; no production policy change |
| 014 | LF-ANY-RES-014 | R2 Qualification and Acceptance Planning | [R2 #393](https://github.com/jeremyrunningphotography/lightflow-studio/issues/393), [acceptance plan](https://github.com/jeremyrunningphotography/lightflow-studio/blob/c6685baae8707fd3ef14c5bb67ee649d309de272/docs/qualification/R2-CATALOG-ACCEPTANCE.md) | Done; owner accepted bounded plan at `6553a8052624da80ef6aa628c6c4b4581b854295`; normal documentation merge authorized; R2 incomplete |
| 017 | LF-ANY-RES-017 | Lightflow Origins Research | [Issue #428](https://github.com/jeremyrunningphotography/lightflow-studio/issues/428), [History Epic #427](https://github.com/jeremyrunningphotography/lightflow-studio/issues/427), [PR #434](https://github.com/jeremyrunningphotography/lightflow-studio/pull/434) | Done; owner accepted evidence package at 70d126508970572fabfe3d34db0e22ca741daa62; normal merge ecd810a211e308354f50cb8d5c52a12b2bcf60cc on 2026-10-12; transcript IDs, ZIP execution and Kyno cause remain unverified |
| 018 | LF-WIN-DEV-018 | Jobs Checkpoint Ordering and Catalog CI Diagnostics | [Issue #431](https://github.com/jeremyrunningphotography/lightflow-studio/issues/431), [PR #433](https://github.com/jeremyrunningphotography/lightflow-studio/pull/433), [PR #423](https://github.com/jeremyrunningphotography/lightflow-studio/pull/423) | Done; owner accepted bounded Jobs correction; normal merge 2026-10-12; Catalog uncertainty retained |
| 020 | LF-ANY-DOC-020 | Lightflow Development Chronicle | [Issue #429](https://github.com/jeremyrunningphotography/lightflow-studio/issues/429), [History Epic #427](https://github.com/jeremyrunningphotography/lightflow-studio/issues/427), [PR #439](https://github.com/jeremyrunningphotography/lightflow-studio/pull/439), [registry PR #441](https://github.com/jeremyrunningphotography/lightflow-studio/pull/441) | Done; owner accepted first edition unchanged; normal PR #439 merge 2026-10-12; provenance gaps retained |

## Assignment 046 — accepted implementation and prepared closeout

**LF-WIN-DEV-046 — Windows Folder Context-Menu Safety**, #492, tracking #491.
Independent full clone `C:\Git\Agents\LF-WIN-DEV-046-FolderContextSafety`;
branch `codex/492-folder-context-safety`, target `release/1.0`. Initial allocation
was absent from fetched remote registries. Verified release base / v1.0.2 source:
`2242c22ec090e8b7557e7c9ac2c34845b6d61f9c`.

Owner reports **17/17 PASS, zero failures, zero observations**, accepting commit
`bf90cfa1f90c2ce69e80d951bcebbc49326958e2`, tree
`d612f21ecfa74409a5e29e7406e661faeb7745ca`. Focused Release 157 PASS; full Release
2,503 PASS / 1 existing opt-in skip; package/dependencies/startup/shutdown PASS.
Accepted production, dependencies and build scripts remain unchanged. The existing
Release workflow contract test now verifies the supported launcher/full Release
invocation rather than the obsolete direct-command substring.
Final integration only reconciles workflow/documentation and routes release CI
through the already-qualified private-desktop launcher.

See [final qualification](qualification/folder-context-safety-492-046.md).
PowerShell 7 launcher qualification: 13/13 PASS. One implementation PR contains
code, tests, qualification and prepared closeout. Required exact-head CI and
explicit owner merge authorization remain gates; **not Done, merged or released**.
Final merge/post-merge provenance belongs in issue/PR discussion without another
administrative PR. No main forward-port, other bug-bash fix, release/tag or
production Catalog operation is authorized. Historical maintenance:
**Not warranted** for a bounded targeting correction.

## Assignment 049 — 2026-11-13

Owner allocated LF-WIN-RES-049 for #498 research only. 049 absent from freshly cloned main/release registries and remote branch inventory; owner allocations 047 and 048 remain reserved. Independent full clone: C:\Git\Agents\LF-WIN-RES-049-PlayerFrameStepping; branch codex/498-player-frame-stepping-research; release base 90f4f94d72f4397325ba6547f486966044208a3f, tree 1b65a123ad0bdf81711c512859eddebe2cd5b79b. No production edits, PR, merge, release or production Catalog changes authorized. Stop at analysis and recommendation for owner review.


## Assignment 049 — focus-only implementation authorized (2026-11-13)

The owner approved only the confirmed playback-speed/Player keyboard-focus
correction under #498. Existing identity, clone and research branch continue.
Baseline release/1.0 ca929e586b929d721656a5ca5c5c5fb8fff5f905 includes #496 / #501.
Issue scope updated before implementation; native parent #491 and Project #4
In Progress / Player / P1 reconciled from authoritative issue/owner scope.
No decoder, frame-step calculation, queue, scheduling, range or export changes.
Backward latency, native EOF/VFR/duplicate-PTS/zero-boundary and teardown findings
remain deferred; all prior ZIPs are preserved. Focused/full Windows tests,
installer/portable packaging and isolated owner acceptance precede any PR.
No PR, acceptance, merge or release is claimed. Historical maintenance:
Not warranted for a bounded focus correction.
## Assignment 048 — reserved, investigation only (2026-11-13)

LF-WIN-DEV-048 — Export Active In/Out; issue #497, bug bash #491,
dependency #496. Owner allocation verified absent from fetched main and
release registries; no conflicting 048 allocation found in local assignment
registries. Independent clone:
`C:\Git\Agents\LF-WIN-DEV-048-ExportActiveInOut`; branch
`codex/497-export-active-in-out`, target `release/1.0`.
Base `90f4f94d72f4397325ba6547f486966044208a3f`.
Status: source investigation only; production implementation and packaging
blocked until #496 is merged into release/1.0. No PR or acceptance claimed.
Reservation will be included in the single implementation PR; no administrative PR.

048 dependency clearance — 2026-11-13: verified PR #501 merge ca929e586b929d721656a5ca5c5c5fb8fff5f905 on release/1.0 and incorporated it before production edits. Implementation now underway under #497; original investigation checkpoint above is historical. Existing Player partial-mark semantics reused; no frame-stepping changes. Project #4 In Progress / Export & Jobs / P2, native parent #491 and completed dependency #496 reconciled. Owner package acceptance precedes one implementation PR. No merge/release/tag/main forward-port authorized.

## Assignment 047 — authorized registry reconciliation (2026-11-13)

Owner authorized this correction within the single #497 implementation PR.
#496 is Closed — Completed / Project Done. PR #501 normally merged into
`release/1.0` as `ca929e586b929d721656a5ca5c5c5fb8fff5f905`, preserving the
accepted production source and 10/10 owner acceptance (zero observations).
[Post-merge CI 43496848475](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/43496848475)
SUCCESS. The merged local implementation branch was retired; evidence retained.
No release or main forward-port. This supersedes the stale prepared-closeout
status previously in row 047; no separate administrative PR or new agent.

## Assignment 048 — owner acceptance and final qualification (2026-11-13)

Owner accepted I01 `b26812893314f9045b761a3d952049af696e5ff3`: **8/8 PASS,
zero observations**. Production tree `cc54c2b448849d5de93a362226320b47e3097c8d`
remains unchanged. Current release is dependency merge `ca929e5`; no intervening
release changes. Original investigation/dependency checkpoints above are historical.
Final Windows qualification, the single #497 implementation PR and exact-head CI
are authorized; merge is not. No repeat owner acceptance unless a meaningful
production change/regression requires review. See
[qualification](qualification/export-active-in-out-497-048.md).
048 final local qualification: focused 263 PASS / 1 opt-in SKIP; unfiltered Release 2568 PASS / 0 FAIL / 8 opt-in SKIP; packaged-tools export/safety 12 PASS; package startup/shutdown/Catalog/dependencies PASS. No new failures or production/test changes. Historical I01 Jobs access-denied evidence retained. Single-PR exact-head CI, explicit merge authorization, normal merge and post-merge reconciliation remain gates; not merged/Done.


## Assignment 049 — accepted focus and conditional final closeout

Owner accepted I01 b1592ddf560dc6c1ff34e68f0413a8079a221362 **8/8 PASS**,
no corrections. Conditional final qualification, one implementation PR and normal
merge are authorized only with preserved production and green required gates.
Release reconciliation includes merged #501/#502 at 293cd4b; four accepted Player
focus files remain byte-identical. No experimental decoder/cache/queue/presentation,
VFR/duplicate-PTS/zero or teardown changes survived the deferred-code audit.
Final test/package results and actual PR/merge/post-merge provenance belong in
[qualification](qualification/player-focus-498-049.md), #498 and its single PR.
This is prepared closeout, not a premature claim of merge or Project Done. If any
required gate stays red, stop without merging. Historical maintenance: Not warranted.
047 and048 are retired; routine source/private-desktop coordination uses active agents.

## Assignment 049 — final qualification stopped (2026-11-13)

Fresh source e241cc02f237ec095ed4930bfd7b1a147287c325, app subtree
687cf634924c1616ac804264e6fbc434a77cb54b: focused358/0/0, companion90/0;
full2589 PASS /2 FAIL /8 opt-in SKIP. Both StartupRestore_NativeVideoSurvivesTaskbarReveal
cases (seconds0 and1) reported no presented frame after taskbar reveal. Both passed
in focused qualification and in isolated unchanged release293cd4b comparison2/2.
The full-run cause remains unclassified and is not proven baseline. Required full
gate remains red; owner stop gate applied before PR/package/merge. No production
or test changes, weakening, exclusions or retries until green. Accepted four focus
files remain byte-identical; native slot released to051, branch and evidence retained.
These local-only findings belong in the same eventual implementation PR if resumed;
no administrative PR, release/tag, main forward-port or production Catalog change.
