# Agent assignments

Assignments use `LF-[PLATFORM]-[ACTIVITY]-[NUMBER]`: Lightflow Studio, required execution environment, primary responsibility and unique global assignment number. See [the naming policy](../AGENTS.md#agent-assignment-naming) for codes, session titles, continuation and workspace rules.

This lightweight registry records identity and number allocation. GitHub remains authoritative for product issue scope, native dependencies, priority and Project status; this is not a replacement backlog.

## Assigned IDs

015 is owner-reserved for LF-MAC-DEV-015 — Native Catalog Lifecycle (#425),
2026-10-12. Independent full clone:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-015-CatalogLifecycle`;
branch `codex/425-native-catalog-lifecycle`, baseline main
`38bb1ed8fe65a4bb6232a542e79a5d2288fea90f`. Tracking reconciliation precedes
production implementation. Owner separately allocates 016 to Windows CI review;
this assignment does not maintain that workspace or its records.
**019 reservation (2026-10-12):** Jeremy allocated LF-WIN-DEV-019 for owner-authorized bounded production Catalog lifecycle extraction [#432](https://github.com/jeremyrunningphotography/lightflow-studio/issues/432), prerequisite for paused LF-MAC-DEV-015 / #425. Independent full clone: `C:\Git\Agents\LF-WIN-DEV-019-CatalogExtraction`; branch `codex/432-shared-catalog-lifecycle`; independently fetched main `38bb1ed8fe65a4bb6232a542e79a5d2288fea90f`. Number 019 is unused on this main; owner allocations 015, 017 and 018 in the kickoff remain reserved to their owners even though their registry changes are not yet merged. Status: orientation and dependency inventory; no production implementation or acceptance claimed. Native parent #366, native #432 blocks #425 and Project #4 Catalog/Backlog/unset Priority verified. #425 retains native parent #366 and remains paused; #393 incomplete. Draft PR, architecture review and packaged owner acceptance precede any merge; no release/tag or other-agent workspace changes authorized.

| Number | Agent ID | Short description | GitHub references | Assignment status |
| --- | --- | --- | --- | --- |
| 036 | LF-MAC-DEV-036 | Browser Thumbnail Foundation | [Issue #481](https://github.com/jeremyrunningphotography/lightflow-studio/issues/481), parent #480 / #395 / #366 | Mac functionality owner-accepted at 666505ef; persistent acceptance workflow in review; LF-WIN-TST-037 qualification, exact-head CI and merge pending |
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
| 014 | LF-ANY-RES-014 | R2 Qualification and Acceptance Planning | [R2 #393](https://github.com/jeremyrunningphotography/lightflow-studio/issues/393), [acceptance plan](qualification/R2-CATALOG-ACCEPTANCE.md) | Done; owner accepted bounded plan at `6553a8052624da80ef6aa628c6c4b4581b854295`; normal documentation merge authorized; R2 incomplete |
| 017 | LF-ANY-RES-017 | Lightflow Origins Research | [Issue #428](https://github.com/jeremyrunningphotography/lightflow-studio/issues/428), [History Epic #427](https://github.com/jeremyrunningphotography/lightflow-studio/issues/427), [PR #434](https://github.com/jeremyrunningphotography/lightflow-studio/pull/434) | Done; owner accepted evidence package at 70d126508970572fabfe3d34db0e22ca741daa62; normal merge ecd810a211e308354f50cb8d5c52a12b2bcf60cc on 2026-10-12; transcript IDs, ZIP execution and Kyno cause remain unverified |
| 018 | LF-WIN-DEV-018 | Jobs Checkpoint Ordering and Catalog CI Diagnostics | [Issue #431](https://github.com/jeremyrunningphotography/lightflow-studio/issues/431), [PR #433](https://github.com/jeremyrunningphotography/lightflow-studio/pull/433), [PR #423](https://github.com/jeremyrunningphotography/lightflow-studio/pull/423) | Done; owner accepted bounded Jobs correction; normal merge 2026-10-12; Catalog uncertainty retained |
| 020 | LF-ANY-DOC-020 | Lightflow Development Chronicle | [Issue #429](https://github.com/jeremyrunningphotography/lightflow-studio/issues/429), [History Epic #427](https://github.com/jeremyrunningphotography/lightflow-studio/issues/427), [PR #439](https://github.com/jeremyrunningphotography/lightflow-studio/pull/439), [registry PR #441](https://github.com/jeremyrunningphotography/lightflow-studio/pull/441) | Done; owner accepted first edition unchanged; normal PR #439 merge 2026-10-12; provenance gaps retained |

000 completed the accepted naming publication (#382). 001 completed the owner-accepted bounded R1 shared classification extraction (#383 / #385); completion does not authorize later migration. 002 completed owner-accepted bounded WQ (#384 / #386); acceptance does not authorize production Player integration, WPF retirement, or release qualification. 003 completed owner-accepted roadmap/issue/Project preparation and registry reconciliation (#399, normal merge `ae6abe898e053bbf82df8846a4d73335d3b64282`); it does not authorize or start R2–R7 implementation or allocate implementation agents. Reservations alone do not authorize execution.

004 completed the owner-accepted R2-A shared storage contracts slice (#388 / #401), accepted head `ee77d774f0d4372afcfb28a0278d559f19c3cb50`, normal merge `997079a946bd701b4ae80aa6232ff5dc069888b3`. No additional manual application testing was required for this non-visible slice. Its independent full clone and validation/package evidence are retained at `C:\Git\Agents\LF-WIN-DEV-004-StorageContracts`. Concrete adapters, Windows enforcement, identity safety and closed transfer (#389–#392) remain unstarted and separately gated; R2 is not complete.

005 completed owner-accepted R2-B #389 / #403 at accepted head `b73d72d9bfdac26051236106ca9441bac3c75c42`, normal merge `0d7405af8402615b0f03a2df48df82c0fd8ca316`. Independent architecture, accepted #408 safety-design compliance, native regression, required exact-head CI [37996711104](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/37996711104), installer/package and refreshed packaged owner startup/Settings/Collection persistence/exit/reopen acceptance PASS. #389 is Completed / Project Done; R2 #393 remains incomplete with #391/#392/#406 and remaining #271 UX gates. ExFAT active production Catalog admission remains unsupported; physical power-loss/surprise-removal and full cross-platform product integration remain unqualified. Exact original CI root causes are not claimed proven. Workspace, failed/successful validation evidence and accepted package provenance remain at `C:\Git\Agents\LF-WIN-DEV-005-CatalogAdmission`. Assignment 006 retains its separately accepted history.

006 completed owner-accepted bounded R2-C #390 / #404 at accepted head `3e8e55518ee82e07722ceb7d7fa4f04b675d7829`, normal merge `e2618f17d317de519892cc239671143a3b0949da`. Architecture, native technical validation within documented limits, shared-contract compliance, required CI and owner technical acceptance PASS. Additional hands-on testing was waived for this slice with no Mac UI/workflow. Physical external-drive durability/locking, Finder aliases, surprise removal, power loss, full Mac integration and physical cross-platform handoff remain unqualified; R2 is incomplete. Independent task evidence is retained at `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-006-StorageAdapter`.

012 completed owner-accepted #412 / #422 at accepted head `45fdf031386b886d955e22172e58dd0b95325018`, normal merge `38bb1ed8fe65a4bb6232a542e79a5d2288fea90f`. Independent technical review, Jeremy's packaged visual/functional acceptance, required exact-head CI [38184834302](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38184834302) and installer/package qualification PASS. Focused Release 109/109 and full Release 2877 passed / 0 failed / 2 existing skips; real independent Windows writer refusal and packaged authored-state/identity preservation qualified. Accepted startup, recovery, Create New, Open Existing and atomic no-replace Move use the existing authoritative ownership lease and lifecycle. #412 is Completed / Project Done; #271 remains Open/Backlog, #289/#366 Open/In Progress, #393 Open/Backlog and incomplete, and #406 Open/In Progress. Native Mac product and physical qualification remain separate gates; ExFAT active admission remains unsupported. Task clone, accepted package provenance, owner fixtures, historical unsafe two-writer evidence and failed CI logs remain preserved at `C:\Git\Agents\LF-WIN-DEV-012-CatalogSettings`. No release, tag, new assignment or additional implementation. Earlier pending-acceptance checkpoints below remain historical and are superseded by this completion record.
012 registry closeout resumed with owner authorization on 2026-10-13 after accepted test-fixture correction #446/#447 reached main (`1af0bc49e29aa70a9c43d362032791316f35cbcd`). Historical registry CI 38192587053 and 38280894459 remain failed evidence; no historical run is relabeled as passing. Current-main reconciliation preserves every concurrent assignment record and all production/test inputs. Fresh exact-head CI and installer/portable checks gate this administrative merge; unresolved SQLite, Browser and playback reliability work remains separately owned under open #446. Accepted #412 implementation/package provenance and original unsafe two-writer diagnostics remain preserved.

**012 remains allocated to this same assignment.** Check current owner allocations before assigning a later number; the 012 kickoff separately identifies assignments 013 and 014 without authorizing this agent to maintain their workspaces or execution records. Assignment 010's published PR #413 status is preserved above.

Current 012 continuation: Jeremy approved the bounded ownership correction on #412, retaining the original gate evidence below. The existing startup lease now covers startup, Move, Open Existing and recovery, with safe rollback or blocked authoring. The same assignment/branch implements the owner-approved Catalog startup gate: no workspace until verified Catalog readiness, explicit safe Create/Open/Retry, and controlled restart for healthy Settings selection or lost readiness. Earlier Settings-based recovery presentation is superseded. See [implementation and qualification](architecture/catalog-settings-412-implementation.md). #412 is Open / In Progress during validation and will be Open / Review at Draft PR handoff. Parent #271, Catalog Area, unset Priority, completed #391/#392 blockers and #393 dependency remain; no Epic completion, new assignment, release or merge is claimed.

012 received Jeremy's bounded production kickoff for #412 on 2026-10-10. Independent full clone: `C:\Git\Agents\LF-WIN-DEV-012-CatalogSettings`; branch `codex/412-catalog-settings-startup`; verified main `9e9f3509f63f821fcaa91367eaa7cb8797935613`. A task-owned private-desktop diagnostic demonstrated two independent profile instance owners and two live coordinators committing Collections to the same Catalog after the accepted relocation path releases its startup ownership lease without acquiring one at the destination. Ordinary startup did refuse the second owner. See [the safety decision gate](research/catalog-settings-412/OWNERSHIP_GATE.md). Production implementation is stopped under the kickoff's explicit single-writer/source-conflict stop condition. No UI, schema, provider, support policy or accepted ADR change; no implementation PR/package readiness or R2 completion is claimed. #412 remains Open/Backlog pending the decision because production implementation has not begun; native parent #271, Catalog Area, unset Priority, completed #391/#392 dependencies and blocker #393 remain intact.
**Next available number: 019, subject to reconciling concurrent owner allocations.** LF-ANY-RES-017 is owner-assigned to #428 in its independent clone. Owner-designated 015 (native Mac Catalog), 016 (Windows registry CI review), and 018 (Jobs checkpoint investigation) are concurrent assignments maintained by their own agents; this reservation does not change their status or scope. Historical next-number statements below remain checkpoints.

**Historical design acceptance checkpoint (superseded for #389/#403 status by 005 completion above):** 008 completed Jeremy's authorized independent bounded architecture/safety review supporting #389 / #403. Jeremy accepted the design at `20d35defc1f2e88edb87d6f8ec961df1fdcd1025`; content-free check commit `64d871a988732f8098670ebdc7ebc0fdfc9547a2` preserves the identical accepted tree. Required PR-associated CI run 37991197201 PASS; documentation PR #408 merged normally as `f7daf15ced17d22ef243f11e3e2965bb41fbc7ef`. Independent full clone/evidence: `C:\Git\Agents\LF-WIN-REV-008-CatalogReview`. The strategy is accepted, not a proven correction of either historical CI failure. #389 remains Open / Review; #403 remains Draft/unmerged/blocked. Agent 005 may consume the accepted design under separate bounded implementation authorization; no production correction, policy/support expansion, release or tag change occurred.

007 completed Jeremy's accepted bounded physical research at reviewed head `8f624bbe9dfb828a5ab53d29e0fab74a7f4cf6bd`, with registry-only main reconciliation `033f3cead5cbe5f33cf2131e03bbf652b056bfcf`. Required accepted-head CI 37987542762 and reconciled-head CI 37994199983 PASS; PR #407 merged normally as `dfab688757939a58c3e884c6938e97e17a0104de`. Mac physical 26/26 and Windows physical 30/30 are CONDITIONAL, Windows neutral 68/68 PASS, immutable Mac return 343/343 PASS; all 24 Mac and 14 Windows file hashes, Catalog/Root/32 AssetIds and the single intended Windows note delta preserved. LF-MAC-RES-007 and LF-WIN-RES-007 are execution sessions of historical LF-BOTH-RES-007, one assignment 007. Independent clone, private inventories, logs and artifact ZIP remain in `/Users/jeremyrunning/Git/agents/LF-BOTH-RES-007-ExternalCatalog`; the 37 MiB physical fixture remains intact. Issue #406 stays Open/In Progress/Catalog/Priority unset, native parent #77 and blocks #393. ExFAT active production Catalog support remains unapproved; production schema/lifecycle, app/Settings/writer integration, #391/#392 integration, atomic publication and unexpected-removal/power-loss recovery remain UNQUALIFIED. #289/#366 stay Open/In Progress; #393 Open/Backlog and incomplete. No new assignment or production implementation. Historical phase checkpoints below remain preserved and are superseded only in acceptance/status by this completion record.

009 completed owner-accepted bounded R2-E #391 / #414 at accepted implementation head `8fbd05804ca192bd1ee8f271e4261814ff4724be`, registry-only main reconciliation `a264558618f35c5ee061ea19e023f701fc41d5d2`, normal merge `51d49ae021c40d1078d2c89e364bedd4ad8006f7`. Independent architecture, required automated/native qualification within documented limits, installer/package and Jeremy's packaged functional acceptance PASS. Accepted CI [38013974271](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38013974271) and reconciled CI [38074254721](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38074254721) PASS. The shared validator preserves CatalogId/RootId/AssetId and authored state without a key/schema migration or storage-policy expansion. The earlier concurrent-test cleanup-lock failure passed on an unchanged retry; its root cause is not claimed corrected. Child-path observations are point-in-time; large root-corpus write preflight, physical external-drive and full Mac application behavior remain separately gated. Original failed/successful evidence, owner acceptance data and package provenance are retained at `C:\Git\Agents\LF-WIN-DEV-009-PathIdentity`, including the exact accepted package under `artifacts\accepted-391-8fbd058`. #391 is Completed / Project Done; #76 remains Open/Backlog, #289/#366 Open/In Progress, #393 Open/Backlog and incomplete, #392 research remains independently owned by 010 and #406 production qualification remains Open/In Progress. No transfer/UI implementation, ExFAT admission, release/tag or new assignment.

## Assignment 014 owner-accepted completion

Jeremy allocated LF-ANY-RES-014 on 2026-10-10 for research/documentation-only R2 qualification and acceptance planning (#393). Number 014 is reserved; no previous 014 entry exists on fetched main `9e9f3509f63f821fcaa91367eaa7cb8797935613`. Independent full clone: `C:\Git\Agents\LF-ANY-RES-014-R2Qualification`; branch `codex/014-r2-qualification-plan`. Status: Done for the owner-accepted bounded qualification plan in [PR #420](https://github.com/jeremyrunningphotography/lightflow-studio/pull/420). Jeremy accepted head `6553a8052624da80ef6aa628c6c4b4581b854295` and authorized normal merge, registry reconciliation and task-only cleanup. Accepted-head CI [38094592395](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38094592395) PASS; required final reconciled-head CI gates merge. The accepted qualification matrix remains byte-for-byte unchanged; concurrent 013 records and 012 ownership are preserved. See [R2 acceptance plan](qualification/R2-CATALOG-ACCEPTANCE.md).

The owner kickoff identifies 012 as #412 implementation owner and 013 as #406 Mac physical-research owner. Their current separately maintained records govern their delivery; this completion does not allocate, rename or update their assignments. 013's authorization is recorded on #406. Main's historical "next available 012" statements predate these owner allocations; no further number is allocated here and 014 must never be reused. Reconcile subsequent allocation against the current registry and owner allocations before reserving another assignment.

Historical kickoff authorization: evidence/roadmap review, matrices, bounded follow-up recommendations, documentation Draft PR and factual #393/directly affected roadmap updates. The original kickoff excluded merge; Jeremy subsequently authorized normal documentation merge and cleanup at acceptance. Production code, new filesystem support/active ExFAT, Mac app implementation, destructive physical experiments, new implementation issues/agents, dependency changes without authorization, issue closure and release/tag remain excluded. R2 remains Open/Backlog and incomplete; #412/#406 and their ownership are preserved.

Jeremy accepts the minimum R2 Mac acceptance boundary as a production-schema native Catalog lifecycle host without requiring the full later Mac UI, subject to separate implementation authorization and owner acceptance. No such host is claimed implemented or qualified. R2 #393 stays Open/Backlog/incomplete; #412 and #406 remain outstanding native gates; #289/#366 dispositions, historical research/failed evidence, and later shared UI/package/release gates remain unchanged. No new issue, agent, production implementation, filesystem support, release or tag.

## Assignment 020 reservation

Jeremy allocated LF-ANY-DOC-020 on 2026-10-12 for #429, the evidence-linked Development Chronicle under #427. Independent full clone: `C:\Git\Agents\LF-ANY-DOC-020-Chronicle`; branch `codex/429-development-chronicle`; fetched main `d1531a2f3d2caf282e1215b53615978a6ace5cd1`. 020 was absent from current main; owner-designated 019 shared Catalog lifecycle extraction and pending 012 registry closeout retain separate ownership. Documentation-only research, Chronicle/source index and Draft PR are authorized; no production changes, application tests/package, #430 implementation, merge, release or tag. The historical cutoff is October 12, 2026 inclusive in America/Los_Angeles. Next unused number is 021, subject to reconciling concurrent owner allocations.

020 delivered Draft PR #439: 46 selective dated entries, explicitly AI-authored Chronicle and provenance/source index. Documentation checks passed for chronological headings, source identities and immutable commits/paths, release dates/remote tag targets, local links and privacy. #429 remains Open / Review; #427 Open / In Progress with Chronicle acceptance and #430 still required; #430 remains Open / Backlog and unstarted. Native history hierarchy/dependencies, Other Area and unset Priority are preserved. Original transcripts/ZIP execution and other documented provenance gaps remain unresolved. No application/package validation, production change, merge or release is claimed.

## Number allocation and maintenance

### Assignment 021 reservation

Jeremy allocated LF-ANY-DOC-021 for #430 on 2026-10-13. Independent full clone: `C:\Git\Agents\LF-ANY-DOC-021-DevelopmentHistory`; branch `codex/430-development-history`; fetched main `a52473466cf40a33580ef80c425233cd49e9ee0d`. Number 021 was absent from current main and the open PR inventory. The assignment derives the first Development History from the accepted Chronicle and sources, preserving the morning research checkpoint on October 12, 2026, and adds the owner-approved nonblocking historical-maintenance policy. Native parent #427 and completed prerequisite #429 are verified; Project #4 uses Other Area, unset Priority and In Progress during drafting. Concurrent 015 Mac work, 012 registry PR #423 and historical registry records remain separately owned. Documentation-only Draft PR and review are authorized; no production changes, application tests/package, merge, release or tag. Next unused number is 022, subject to checking concurrent owner allocations.

021 delivered Draft PR #442: roughly 4,150 words of explicitly AI-authored third-person history, with software phases, human-directed AI development, source/provenance limits and maintenance guidance. AGENTS.md adds the owner-approved selective, nonblocking milestone policy without changing unrelated workflow or release rules. Documentation validation passed for 82 local links/anchors, 118 source identifiers, 189 immutable source objects, accepted Chronicle/Source Index blob identities, release metadata/remote tag targets, privacy patterns and whitespace. No historical runtime rerun, application tests/package or production changes. #430 remains Open / Project Review; #427 Open / In Progress with final narrative/policy acceptance and merge outstanding; native hierarchy/dependency, Other Area and unset Priority remain preserved. Original transcript/ZIP execution, Kyno cause, early model attribution and other accepted uncertainties remain unresolved. Automated PR CI is reported separately in the GitHub handoff; no merge/release is claimed.

- Check the registry before assigning a number; reserve the next unused global number and record planned assignments before execution.
- Use one monotonically increasing sequence across platforms and activities with at least three digits. Reserve `000` for the foundational naming assignment; regular assignments start at `001`. After `999`, continue with `1000`.
- Never recycle historical numbers, including cancelled, abandoned or superseded assignments. Numbers identify assignments, not permanent agent identities.
- Keep continuations under their original ID, number, primary session title and responsibility unless explicitly revised. Routine implementation or documentation corrections, CI troubleshooting, PR reconciliation, acceptance feedback and merge/cleanup retain the ID. Allocate a new number only for a materially separate assignment.
- Update status as assignments progress. Link actual issues and PRs when available; do not invent missing GitHub references or relationships.
- Preserve historical names, including completed XR, XC and XM agents.
- Keep the registry concise and allocation a lightweight repository documentation operation. Do not introduce a database, coordinator service, automation platform or custom numbering tool.

**Historical initial/Mac phase checkpoint:** 007 is authorized by Jeremy’s 2026-10-09 kickoff for read-only JRPhoto4T inventory/native assessment and disposable internal Mac fixtures. Independent full clone: `LF-BOTH-RES-007-ExternalCatalog` on the authorized Mac. Jeremy subsequently authorized the bounded physical Mac phase in exactly one new JRPhoto4T task directory (maximum 256 MiB). Physical tests completed CONDITIONAL; verified copy/locking/cleanup, unsupported exclusive rename disclosed. Windows execution, physical handoff, production source and filesystem support-matrix changes remain gated. Stop at the next owner decision gate; documentation remains subject to Draft PR review and explicit acceptance.

**Historical Windows phase checkpoint:** Assignment 007 continues in owner-designated sessions LF-WIN-RES-007 (Windows/DARKMATTER) and LF-MAC-RES-007 (Mac). Historical LF-BOTH-RES-007 evidence and task-root/workspace names retain their provenance. Owner authorized bounded Windows qualification in the existing root: 30/30 fixture checks, 68/68 neutral checks, original Mac hashes preserved, 37 MiB allocated, ordinary Windows safe removal passed. Return-to-Mac verification and independent/owner acceptance remain pending. No new number, issue, production implementation or active ExFAT support is allocated/approved. See docs/research/physical-catalog-406/WINDOWS_PHYSICAL_PHASE.md and MAC_RETURN_HANDOFF.md.

**Historical Mac return pre-acceptance checkpoint:** LF-MAC-RES-007 completed the authorized read-only return verification: all 24 original Mac and 14 Windows files match, 343 verifier checks pass, expected single note delta and all IDs preserved, no task holders, native anchors 0 → 0. Synthetic closed-fixture cross-platform return is demonstrated; active ExFAT/product support remains unqualified. See docs/research/physical-catalog-406/MAC_RETURN_PHASE.md. Issue #406 remains Open/In Progress and PR #407 Draft/unmerged pending owner technical acceptance.

**Historical initial research authorization checkpoint:** 010 was explicitly authorized by Jeremy on 2026-10-09. Independent full clone: `C:\Git\Agents\LF-ANY-RES-010-CatalogTransfer`. Baseline fetched main `0d7405af8402615b0f03a2df48df82c0fd8ca316` includes accepted merged #403/#389. One documentation Draft PR and bounded R2 integration issue/roadmap reconciliation are authorized; production transfer/UI implementation, Mac experiments, another agent, support expansion and merge remain excluded. Preserve Agent 009 ownership and concurrent Agent 005 completion reconciliation; older registry acceptance checkpoints remain historical.

010 completed Jeremy's owner-accepted closed-Catalog architecture and bounded R2 Settings/startup contract (#392 / #412), reviewed head `b49331a6263f58da33d1bfcd7aa45148e24631fd`, final main-reconciled head `5344ec19733f92a76f6af533300dcc288c40f7bf` and normal PR #413 merge `685dd7c0f1da8b93c4be323d94b2e6fe73c18565`. Required final-head CI [38075347646](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38075347646) and refreshed task-local package PASS. Architecture is authoritative on main; #392/#412 remain Open/Backlog and separately implementation-gated, #406 Open/In Progress with production qualification outstanding, R2 #393 incomplete. No ExFAT active admission, support expansion, release/tag or new assignment. Independent clone, failed/successful CI logs and package provenance remain at `C:\Git\Agents\LF-ANY-RES-010-CatalogTransfer`. Agent 009's accepted implementation and separately maintained registry status are preserved. Next available number remains 011, unallocated.

011 completed Jeremy's owner-accepted bounded R2-D #392 implementation at accepted head `3c5ac9e33f614f980034b0b7fdbb06dbba04abea`, normal PR #417 merge `a0a04da07dde9c8c7609f61ab3e21656c8c8b4dd`. Independent architecture/design compliance, required [exact-head CI 38088541922](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38088541922), native Windows within documented limits, neutral Windows/macOS, native Mac adapter/host, installer/package and owner packaged functional relocation acceptance PASS. No additional routine hands-on acceptance was required. #392 is Completed / Project Done. #77 remains Open/Backlog, #289/#366 Open/In Progress, R2 #393 Open/Backlog and incomplete; #412 Open/Backlog and #406 Open/In Progress remain outstanding gates. Actual network disconnection/remount, physical removal/power loss and full Mac production integration remain unqualified; ExFAT active Catalog use remains unsupported. Existing external LUT references and authored assignments are preserved, external bytes excluded. The original clone, earlier failed diagnostics, accepted package/provenance and architecture/report remain retained at `C:\Git\Agents\LF-WIN-DEV-011-ClosedTransfer`; the accepted package is preserved under `artifacts\accepted-392-3c5ac9e`. Assignments 005–010 retain their authoritative Done statuses and historical checkpoints. Next available number is 012; no new assignment, production change, release or tag is allocated by this administrative completion.

013 completed Jeremy's owner-accepted bounded Mac filesystem research at accepted head `b4a56fe85970a79a1de5401906aaf3f4b5a33fb4`, normal PR #419 merge `6b7b664e93b789d751b5c3a22286c802d6c02ec1`. Required exact-head CI [38094436260](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38094436260) passes all validation and Windows package jobs. The accepted research and documented limitations are preserved. AppleDouble companions are a bounded implementation consideration for the future Mac transfer adapter under the existing exact-artifact and lifecycle contracts; no metadata exception, new format or additional research assignment is authorized. #406 remains Open/In Progress, native parent #77 and its #393 blocker remain intact; R2 #393 remains incomplete. ExFAT active-Catalog admission remains unsupported; closed ExFAT transport remains a candidate, not a qualified production capability. Agent 007 and 013 physical fixtures, private evidence, failed diagnostics and owner files are retained; no further drive experiment or fixture deletion occurred. Independent task clone/evidence remains at `/Users/jeremyrunning/Git/agents/LF-MAC-RES-013-ExternalCatalog`. No new assignment, implementation, release, tag or support-policy change.

## Assignment 018 reservation

Jeremy allocated LF-WIN-DEV-018 on 2026-10-12 for standalone #431 and separately bounded test-only Catalog journal diagnostics. Independent full clone: `C:\Git\Agents\LF-WIN-DEV-018-CheckpointOrdering`; branch `codex/431-jobs-checkpoint-ordering`; fetched main `38bb1ed8fe65a4bb6232a542e79a5d2288fea90f`. 018 was absent on current main and fetched remote branches. Owner allocations 015 (Mac implementation), 016 (independent review, separately recorded on its branch), and 017 (historical research) remain reserved and untouched. 012 closeout remains pending in PR #423; historical records remain intact. Next unused number is 019, subject to checking current owner allocations. No merge, release, tag or Catalog production behavior change is authorized.

## Assignment 018 owner-accepted completion

Jeremy accepted the bounded Jobs checkpoint ordering correction at `352f028249f98b3f1be4343403853397d30fa92f` and authorized normal merge and administrative closeout without another hands-on test. Documentation-only main reconciliation `6c36c5a7d555def70a965b29615c6333194fd39b` preserved concurrent 017 and 018 records and all accepted implementation/test/build inputs. PR #433 merged normally as `6456c0efa88a47d6e38af0df97b2fd5e3a548a73`, with an identical tree to the reconciled validated head. #431 is completed; standalone Project Export & Jobs/Priority unset/Done, no invented Epic or dependency.

Deterministic old-order baseline failed by overwriting Completed with Running; corrected focused validation passed 25/25. Full local Release passed 2878 with 2 existing skips; accepted-head CI 38262467824 and reconciled-head CI 38270307608 passed native Mac, neutral Windows/macOS, Windows full tests, installer and staged portable checks. Both local PullRequest/SkipInstaller package refreshes passed startup, Jobs activation, graceful shutdown, Catalog backup/restore protection and dependency validation. Historical CI interleaving remains unobserved. Original baseline TRX, accepted package provenance, journal traces and private Browser evidence remain preserved in `C:\Git\Agents\LF-WIN-DEV-018-CheckpointOrdering`.

Browser #436 remains separate Open/Backlog, Browser Area, Priority unset and no invented parent or blocker. Repeated owner stalls remain real evidence; later successful candidate/main loads do not resolve the root cause. Earlier ingestion/timing is a hypothesis, and Settings-before-success is an observation, not a fix. Historical Catalog journal holder remains unidentified; original interruption assertions are preserved and no production recovery/share-mode/lease/storage behavior changed.

012's pending registry closeout remains in unchanged/unmerged PR #423; 015 paused work, 016 independent review, 017 research and owner-designated 019 shared Catalog lifecycle extraction retain their separate ownership and records. Prior next-number statements are historical; 019 is already owner-designated and no number is allocated here. No other assignment row/history, canonical checkout or other task workspace was changed. No release/tag or new production behavior. This completion is published through the registry-only PR workflow with normal required CI.


## Assignment 019 owner-accepted completion

Jeremy accepted the bounded shared Catalog lifecycle extraction at `d1fa61cf69593f47c6450d3a059f32a602fe1119` after Windows hands-on testing. The final Move diagnostic condition passed: explicit --data-root selects an isolated profile, containment/refusal is isolated-only, normal profiles retain ordinary relocation, and no safeguards or wording changed. Existing focused private-desktop tests passed 59/59 without accessing the owner's Catalog or normal profile. Owner acceptance and the condition are recorded in [#432](https://github.com/jeremyrunningphotography/lightflow-studio/issues/432#issuecomment-6129128330).

Strict branch protection required documentation-only current-main reconciliation `b5816d4ad13d6b030c8af4941fd80692ea1d6987`; only the existing 017 registry record changed relative to the accepted implementation. Production, tests, build/workflow and dependency inputs remain byte-for-byte identical. Original exact-head CI [38277625394](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38277625394) and reconciled-head CI [38283287608](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38283287608) passed required Windows/neutral/macOS/adapter/companion/installer/staging checks. PR #435 merged normally as `89ee65a2a092e8024fbd5f85dbb73c9fcca3358a`, with a tree identical to the validated reconciled head. [Post-merge CI](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38284745302) is independently verified before administrative closeout completes.

The accepted local full Release result remains 2,877 passed / 2 failed / 2 skipped. Interrupted-transfer journal diagnostic and Browser recursive-root restoration failures reproduced on accepted production source with applicable inputs and are explicit owner baseline exceptions, not passing tests or resolved defects. No assertion weakening, retries or production correction was added. Exact accepted package/hash, fresh reconciled package smoke/dependency evidence, original logs/baseline comparisons, performance samples and architecture/port reports remain in `C:\Git\Agents\LF-WIN-DEV-019-CatalogExtraction` and its task-owned baseline clone; the exact owner-tested package is preserved under `artifacts/accepted-432-d1fa61c/LightflowStudio`.

This completes only #432 extraction. #425 remains open, its extraction prerequisite satisfied after merge; Agent 015 resumes only under separate owner direction. #366/#289 remain Open/In Progress; R2 #393 remains Open/Backlog/incomplete with native Mac, physical and other qualification gates. No release/tag or filesystem support change. Historical 019 reservation/implementation checkpoints are preserved and superseded only in current completion status. Concurrent 012 closeout and owner-designated 020 work remain separately owned; no other assignment row/history or workspace was changed, and no new agent number was allocated. Historical next-number statements are not current allocation authority; 019 cannot be reused.

## Assignment 015 continuation

Jeremy resumed the existing #425 assignment after accepted #432/#435. Main `18f2e0970307098a760257104aa36fc1ef9fe29c` was fetched; the original reservation was committed and merged with current main, preserving both registry notes. The initial reservation patch and native diagnostics remain task-owned under artifacts/lf015-orientation. Native effects and a CLI compose the extracted CatalogLifecycle; no shared lifecycle contract, schema/provider/WAL policy or Windows consumer is changed. Missing-directory creation transitions and handle-bound no-replace Move publication remain refused pending qualification/review. R2 #393 and #406 remain incomplete/open.

015 native qualification checkpoint (2026-10-13): published arm64 CLI uses the sole shared CatalogLifecycle; schema 19 and rich logical authored state persist, independent writers are refused, unpaired profile remains Dirty with Catalog Validated provenance. Local Release: 24 native, 44 neutral Catalog and 136 Application tests passed; original Mac storage host and 23 process checks passed. Current main a524734 registry closeout reconciled before draft handoff. Missing-directory transitions, unverified closed-transfer capacity and handle-bound publication remain refused; recommendation REVISE. See docs/qualification/native-catalog-425.md for exact executable/owner commands and retained diagnostics. #425 Open/Review, #366 In Progress, #393/#406 incomplete/open; no merge/release/tag.

## Assignment 020 owner-accepted completion

Jeremy accepted the first Development Chronicle edition without editorial revisions at `db643466f313888e35f949861d86c07ff46ae1d9`. PR #439 merged normally as `18f2e0970307098a760257104aa36fc1ef9fe29c` from current-main-reconciled head `0c6e0f2a1715d17fb616949f73ac6011c26b0dce` after required CI [38287220646](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38287220646) passed. Earlier reconciled-head CI 38285447722 also passed; concurrent Agent 019 registry closeout required a fresh head and fresh checks. No failed CI or protection bypass is claimed.

The accepted Chronicle and Source Index are byte-for-byte preserved: Git blobs `93ff858d5059e88601143d8e1e7af505ff63cf30` and `3f7f57196b4fa5af44fbf4fcdf1a58f673013777`. The October 12, 2026, 10:16 a.m. PDT research checkpoint remains unchanged. Acceptance does not promote original conversation summaries, uninspected assistant-reported ZIPs or the Kyno hypothesis into independently verified facts. Historical Draft/pending wording is retained as part of the accepted snapshot and superseded only by this closeout record.

#429 is Completed / Project Done. Parent #427 remains open; #430 remains Open / Backlog, unstarted, with native #429 dependency intact. Other Area and unset Priority are preserved. No production change, release or tag. This registry-only closeout changes no other assignment, accepted historical document or production input. The independent task clone retains private raw research/CI evidence; no private transcript is published.

## Assignment 015 owner-accepted completion

Jeremy successfully completed hands-on acceptance of the published Apple Silicon Catalog-only CLI create/inspect/Collection-add/reopen flow and accepted implementation head `3635fff9cbb3d7c0bbe1572b8019476f14a1ab38`, recorded in [#425](https://github.com/jeremyrunningphotography/lightflow-studio/issues/425). [PR #443](https://github.com/jeremyrunningphotography/lightflow-studio/pull/443) was marked ready and merged normally with merge commit `27d4e9524c1686e1c7355450acedaa0632fa50c6`. Current-main documentation reconciliation `56764c8094aa09486de4d953d3275b3d9761f972` preserved both assignment 015 and 021 records and changed no accepted production, test, workflow, dependency or validation input. Accepted-head [CI 38402728120](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38402728120) and reconciled-head [CI 38409171669](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38409171669) passed required Windows unit tests, installer/portable staging, neutral Windows/macOS and native macOS lifecycle checks. Implementation post-merge verification is tracked in [CI 38410802145](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38410802145) and the issue closeout.

This completes only the accepted bounded #425 native milestone: the single shared CatalogLifecycle creates/opens/authors/closes/reopens schema 19 with native effects, stable identities/logical authored state, independent-process writer refusal and conservative Dirty/unpaired profile / Validated Catalog evidence. No Preview database or fabricated paired completion exists. Local 24 native, 44 Catalog and 136 Application tests, original storage host and 23 process checks passed; strengthened rollback source-byte comparison passed. This current completion supersedes only earlier 015 paused/pending/REVISE checkpoints, preserving their historical evidence.

Automatic missing-directory creation, qualified closed-transfer capacity, safe native no-replace publication/Move, Smart Collection query operations, explicit Media Root remapping, Windows return/round trip, physical power-loss/external-drive qualification, full Mac UI/Preview/signing/distribution and active ExFAT/SMB/NAS Catalog support remain outside acceptance and unqualified/refused. #425 is Completed / Project Done; #366 remains Open / In Progress, #393 incomplete and #406 open; #289 and its remaining requirements are preserved. No release/tag or new production implementation.

Task-owned clone `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-015-CatalogLifecycle` retains accepted executable hashes/provenance, native fixtures, original failures, TRX/process/CI logs and performance observations under artifacts/lf015-orientation, artifacts/lf015-fixtures and artifacts/lf015-native-host. A verified Git bundle preserves original local validation/reconciliation commits before completed branches are deleted. Registry closeout changes only this assignment's current row and this appended record; concurrent assignments and accepted historical documents remain untouched. Final registry merge, branch cleanup and synchronized-main provenance are recorded in #425.

## Assignment 022 reservation

Jeremy allocated LF-WIN-DEV-022 on 2026-10-13 for bounded Windows CI reliability stabilization under #446. Number 022 was available in independently fetched main `de2bdb68c0f963cd18296d50aecaa183b3a9504e` and the inspected open PR inventory. Independent full clone: `C:\Git\Agents\LF-WIN-DEV-022-CIReliability`; branch `codex/446-windows-ci-reliability`. Project #4 membership added with existing Other Area, initially Backlog then In Progress when the evidence-backed test correction began; Priority remains unset. Native parent and dependency lists were empty and remain unchanged. #423/#444 and other assignments retain their ownership. No merge, release/tag, canonical checkout, owner data/process or other workspace change is authorized.

022 continuation (2026-10-13): Jeremy authorized bounded playback CI attribution and journal readiness after the original `62cf2ba` handoff. Accepted-main `de2bdb68` was independently compared in task-owned full clone `C:\Git\Agents\LF-WIN-DEV-022-CIReliability-Baseline`; paired exact cadence and 31-case playback-family comparisons passed without reproducing 0.117s. Native inputs match and temporary probes were restored; historical attribution remains inconclusive, with existing asynchronous timestamp observation risk documented. The valid rebuilt focused run retained 89 passes and both unchanged Browser restoration failures, including early-startup readiness evidence; no Browser/SQLite/Player correction or weakened assertion is introduced. Final focused/full/CI/package results and the separate acceptance recommendation are recorded in #446/#447 and the reliability report. #446 stays Open/Review, Other Area, Priority unset, with empty native relationships preserved; #423/#444 unchanged. No new assignment, merge/release/tag or other workspace change.

## Assignment 021 owner-accepted completion

Jeremy accepted the Development History and AGENTS.md maintenance policy without further editorial revisions at `d87892f92c4172f4314f8022ee4959b1ba859041` and authorized normal merge, issue/Epic completion, registry reconciliation and task-only cleanup after required checks. Exact-head CI [38405033947](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38405033947) passed Unit tests, Windows installer/portable packaging, neutral Windows/macOS and native Mac storage checks. PR #442 merged normally as `12b85ed77a419ea282432075b139a9bada734d33`; its tree is identical to the accepted head. Post-merge and registry-only final-head checks remain mandatory before administrative closeout is reported complete; their final outcomes are recorded in #430/#427 and the registry PR.

The History uses dates and relative sequence rather than exact clock times or timezone explanations while preserving the accepted October 12 morning research scope. The accepted Chronicle and Source Index retain Git blobs `93ff858d5059e88601143d8e1e7af505ff63cf30` and `3f7f57196b4fa5af44fbf4fcdf1a58f673013777`. The nonblocking policy asks for meaningful milestone candidates and periodic evidence-derived narrative revisions; it introduces no production merge/release gate. Earlier CI [38398498153](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38398498153) failed Catalog locking and Browser restoration tests; passing later CI does not diagnose or resolve those failures. Original failure logs and documentation checks remain task-local in `C:\Git\Agents\LF-ANY-DOC-021-DevelopmentHistory`.

All history deliverables are accepted and present on main; #430 and #427 completion follows verification of required checks and the native #428 → #429 → #430 sequence. Historical reservation/Draft checkpoints above remain preserved. This registry-only reconciliation changes no accepted historical content, AGENTS.md policy, production/test/build inputs, other assignment or concurrent 012/015 ownership. No local application test/package, production fix, release, tag or new assignment.
Owner-authorized resumption follows accepted journal-fixture correction [#447](https://github.com/jeremyrunningphotography/lightflow-studio/pull/447), merged as `1af0bc49e29aa70a9c43d362032791316f35cbcd`. Historical #442 post-merge [38407644935](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38407644935) and #444 [38407866482](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38407866482) failed interrupted-transfer assertions and remain failed evidence. The separate bounded correction waits for native child-process termination before journal inspection, preserving refusal and recovery assertions; it does not resolve SQLite/Browser failures or establish historical playback attribution. Registry PR #444 is reconciled with current main, preserving all 012/015/022 rows and histories. Fresh exact-head and post-merge CI gate administrative completion; final results and merge/cleanup are recorded in #430/#427 and #444. No accepted history/policy, production or test change is made by this closeout.

## Assignment 023 final bounded research closeout — 2026-10-20

Jeremy accepted **Research Complete / Production Portability Blocked** for
LF-MAC-DEV-023. The exact-open feasibility result is **CONDITIONAL**: the retained
verified descriptor supplied the actual SQLite main-file inode through `/dev/fd/N`
despite pathname substitution, and immutable read-only access returned the verified
Catalog. The write-capable route opened that inode but WAL admission and querying
failed with SQLite error 14. The unprotected ABA negative control reproduced;
`HAS_MOVED` detected the original schedule but missed the controlled timed variant.
Write-capable activation, WAL/SHM companions, pooling and recovery remain unresolved.
The original production Catalog portability assignment was not successfully delivered.

[R2 #393](https://github.com/jeremyrunningphotography/lightflow-studio/issues/393),
[physical qualification #406](https://github.com/jeremyrunningphotography/lightflow-studio/issues/406)
and [migration Epic #366](https://github.com/jeremyrunningphotography/lightflow-studio/issues/366)
remain open and incomplete, with existing Definitions of Done and native dependencies
preserved. Accepted #425 and #432 decisions remain within their original bounds.
No production behavior, publication refusal, schema/provider, transfer or recovery
semantics changed; no Move, active ExFAT/SMB/NAS, round-trip or full Mac product
qualification is claimed. This documentation-only closeout requires owner acceptance
and a separately authorized merge; no new implementation begins.

All four original reports and supporting sources, executables, fixtures, logs and
native identity evidence are retained unchanged in the independent task workspace
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-023-CatalogPortability`:

- `artifacts/lf023-publication-gate/REPORT.txt` — publication safety gate.
- `artifacts/lf023-installation-proof/REPORT.txt` — descriptor-based installation proof.
- `artifacts/lf023-safety-contract-review/REPORT.txt` — cross-platform safety contract review.
- `artifacts/lf023-exact-open-proof/REPORT.txt` — exact-open feasibility proof.

The task-local `artifacts/lf023-research-closeout/` inventory records evidence hashes
and tracking verification. These artifact paths describe retained local evidence,
not files published by this registry-only PR. Keep the workspace/evidence until the
documentation closeout is accepted and safely preserved; no cleanup is authorized.

**LF-MAC-DEV-024 handoff:** the accepted local APFS Catalog lifecycle from
[#425](https://github.com/jeremyrunningphotography/lightflow-studio/issues/425) may
support separately authorized Mac UI work without depending on Catalog publication
or Move. Preserve prepared-directory creation, existing ownership/recovery, deep
validation before authoring at every reopen and conservative Dirty/unpaired evidence;
do not fabricate a Preview peer or clean-profile certificate. Keep unsupported
publication/Move refused. Agent 024's workspace and implementation are untouched.

## Assignment 024 implementation review — 2026-10-20

Jeremy authorized the early bounded Mac application shell under native child #451 of
#366, without completing or waiving broader R2/R3/R4. Independent full clone
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-024-MacShell`, branch
`codex/024-mac-application-shell`, fetched baseline
`71fd8708d65d5925940961d7be97fc888328a37f`. Number 024 was unused on main and in
the inspected open-PR inventory. Project #4 membership, In Progress only after
implementation began, Application Framework Area, unset Priority and native parent
#366 were verified through the authenticated browser after the CLI had no available
Git credential. Existing milestone dependencies and incomplete Definitions of Done
remain unchanged. No other task workspace or normal profile was modified.

The real Avalonia/native Catalog readiness/normal-close checkpoint passed before
substantial styling. One shared presentation consumes a narrow projection over the
sole CatalogLifecycle; native startup/create/open/readiness/drain/disposal and
conservative Dirty/unpaired deep reopening remain authoritative. Prepared local
APFS folders inside the explicit data root only; missing-folder creation, Move and
publication remain refused. No Preview peer, provider/schema/VFS change or fabricated
completion. Windows shipping source remains unchanged.

Local Release: 32 native, 44 Catalog, 136 Application and 11 presentation tests passed;
23 existing independent-process checks passed. Real development bundle create,
folder selection/confirmation, Command-Q, stable-identity reopen, existing selection
and Quit button were exercised; authored Collection persisted. See
[qualification and exact handoff](qualification/mac-shell-451.md). Draft PR/exact-head
CI, fresh final-head bundle and explicit owner technical/hands-on acceptance remain
separate gates. No Windows local private-desktop package is claimed from this Mac;
no #446 ownership, retry-to-green, R2/R3/R4/Epic completion, merge or release.

## Assignment 024 accepted implementation and companion qualification — 2026-10-20

Jeremy accepted LF-MAC-DEV-024 at implementation head
`b4a8e0a3a431624e05db1c2d86aca573c71ccc5d`, tree
`0ad5d14ce3beea9dd9f6d34c2c8fe87b194c7401`, including Mac functional/visual
review and final local DARKMATTER PullRequest-mode package qualification.
[PR #452](https://github.com/jeremyrunningphotography/lightflow-studio/pull/452)
merged normally as `3801de8dc5618c63ae254bb75260b2a1433d6d9b` with exactly that
accepted tree, unchanged. Exact-head CI
[39456373929](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/39456373929)
passed Windows tests (2,884 passed / 1 skipped), native Mac, neutral Windows/Mac
and Windows installer/portable staging. Post-merge run
[39480331551](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/39480331551)
and final administrative closeout results are recorded on #451/#452; this record
does not predeclare their outcomes.

LF-WIN-DEV-024 identifies the owner-reported Windows companion qualification of
this same assignment and accepted source, not a second global allocation of 024.
No new number or independent implementation assignment is created. The Mac agent
records Jeremy's accepted DARKMATTER PASS, not a claim of executing Windows locally.
The DARKMATTER qualification workspace retains its package, isolated data root,
logs, dependency/runtime checks and provenance; the owner acceptance record is
preserved on #451/#452. Owner-supplied completed qualification report identifies the actual workspace
`C:\Git\Agents\LF-WIN-DEV-024-WindowsQualification` and retained
`artifacts/lf024-qualification/REPORT.txt` (report, logs, provenance and package hashes).
The mandatory PullRequest / SkipInstaller build exited 0; packaged startup/readiness,
graceful shutdown, Catalog backup/restore protection, SQLite/native runtime,
FFmpeg/playback checksums and staging validation passed. Executable UTC timestamp
2026-10-20 17:34:36 is newer than the source commit; executable SHA-256 is
`45c755a43eda87c1f2094d1a3415cbddf96f160e4c20338b2c4514a491f5db48`.
The actual isolated root is `artifacts/lf024-windows-owner-acceptance` in that clone.
No task-owned app remains; single-writer/interrupted-recovery evidence uses existing
exact-head CI. No full local suites, installer or release archive were repeated.
This supersedes the earlier planned LF-MAC-prefixed Windows handoff path.

Mac evidence remains in the independent full clone
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-024-MacShell`: `artifacts/mac-shell`
(bundle and exact-head provenance), `artifacts/lf024-orientation` (tests, process
checks, raw CI logs, final Ready/recovery screenshots), `artifacts/lf024-handoff`
(commands and DARKMATTER handoff), `artifacts/lf024-owner-acceptance` and
`artifacts/lf024-recovery-acceptance` (isolated fixtures). Task-only Git bundle
and evidence inventory under `artifacts/lf024-closeout` precede branch cleanup.
Do not delete either task-owned clone or acceptance fixtures prematurely.

The early shared Avalonia shell composes the sole accepted Catalog lifecycle with
prepared local APFS folders, authoritative readiness and normal shutdown/deep reopen.
Portability/publication refusals and conservative Dirty/unpaired state remain.
#451 is completed; #393, #394, #395 and Epic #366 remain open/incomplete with their
existing dependencies and wider Definitions of Done. No full media UI, Preview,
Windows replacement, signing/notarization, release/tag or next-feature work.
Earlier review/pending statements above remain historical checkpoints superseded
by this accepted completion. This registry-only closeout changes no implementation,
test, build, dependency input, historical document or other assignment allocation.

## Assignment 025 reservation — 2026-10-20

Jeremy allocated LF-MAC-DEV-025 for bounded Catalog-backed Mac Browser implementation.
Independent full clone `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-025-Browser`, branch
`codex/025-mac-browser-foundation`, fetched main `32d6c110ba771233249f19475a88a2b4d1e14aa3`.
025 was absent from the current registry and inspected open PRs. Native #454 parent #366,
accepted #451 blocked-by prerequisite and Project #4 Browser / Priority unset verified;
Backlog transitioned to In Progress when implementation began. R2/R3/R4 remain incomplete.
No Preview, publication/Move, portability expansion, WPF retirement or merge authorization.

025 local implementation checkpoint: existing Catalog readers/identity, selection, folder enumeration
and recursive rules are shared; Windows compatibility effects and discovery/reconciliation remain.
Read-only Mac Browser consumes one lifecycle session, supports real roots/folders/assets and stable
single selection without Preview or ingestion. See [bounded qualification](qualification/mac-browser-454.md).
Exact-head CI, final candidate bundle provenance, owner hands-on acceptance and Windows local package
qualification are independent pending gates; no merge or broader R2/R3/R4 completion.

## Assignment 026 reservation — 2026-10-20

Jeremy allocated LF-MAC-DEV-026 for #456. Independent full clone
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-026-Finder-Navigation`, branch
`codex/026-finder-browser-navigation`, fetched main `4e1f361ab02b1e72ed729a59b439478ca916dc7b`.
026 was absent from main and the inspected open PR inventory (#457/#448/#360).
Owner explicitly accepted folder-only uncataloged navigation with existing Catalog assets
where mapped, no uncataloged file asset list or ingestion action (recorded on #456).
Shared Browser architecture, Windows behavior and Catalog safety boundaries remain required;
R2/R3/R4 incomplete. Draft PR and explicit architecture/functional/visual acceptance precede merge.

026 implementation checkpoint: shared filesystem identities reuse existing Catalog mappings and
read-only scope/AssetId selection; native location effects use the shared contained enumerator.
Uncataloged folders have folder navigation only. Native parent #366, accepted #454 prerequisite
and Project #4 Browser / Priority unset / In Progress reconciled. See
[qualification and exact startup](qualification/mac-browser-456.md). Draft/owner/CI/Windows local
package acceptance remain separate gates; no R2/R3/R4 completion, merge or release.

## Assignment 026 accepted implementation and companion qualification — 2026-10-21

Jeremy formally accepted Mac and Windows hands-on behavior for #456 / #458 at exact
head `4fac86627f449829eba4cc1a5ea590f9591bf3f9`, tree
`cdfb6be2a5f8e95bfa6845880b95ffa14cd10c2d`, and explicitly authorized normal merge and
bounded closeout. LF-WIN-DEV-026 is the owner-reported accepted Windows companion
package qualification at this same source under global allocation 026, not a new
assignment. This Mac does not claim executing Windows locally; its actual Windows
report paths/hashes were not supplied and are not invented. Preserve the Windows
owning clone, accepted package/provenance, original logs and isolated fixtures in place.

PR #458 merged normally as `d2a600f9372fc5a6869540135dc77eaa57a308d4`; both parents
and the full accepted tree were independently verified unchanged. Exact-head
[CI 39512678496](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/39512678496)
passed Windows tests/installer/portable staging, neutral Windows/macOS and native Mac
lifecycle/process/bundle jobs; release publication skipped. Implementation post-merge
[CI 39631387819](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/39631387819)
and registry reconciliation/final-main CI outcomes are recorded on #456/#458 and the
registry PR; no result is predeclared by this record. #456 is Completed / Project #4
Done / Browser / Priority unset, native parent #366 and completed #454 prerequisite
preserved. #393, #394, #395 and #366 remain open/incomplete with wider DoD and
native dependencies unchanged. No next Browser/Preview milestone, release or tag.

The independent full Mac clone
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-026-Finder-Navigation` retains the accepted
self-contained app and clean exact-head provenance in `artifacts/mac-shell`, test data
in `artifacts/lf026-fixtures`, screenshots/verification in `artifacts/lf026-evidence`,
original failures/traces/build/launch logs under `work/lf026-*.log` and project TRX files.
All 290 app hashes were independently reverified before cleanup. Accepted executable
SHA-256 is `5adeddd7c623e628296ee42e47d6ef23e5f66c7fbd133a2ddb4e956c3cd5f56f`;
its UTC timestamp `2026-10-20 23:05:14.494900` is newer than the accepted source commit.
`artifacts/lf026-closeout` preserves a verified complete Git bundle, acceptance/CI/issue
snapshots and file-hash inventory before completed branch cleanup. No clone, executable
or fixture deletion is authorized or performed by this closeout.

This supersedes only assignment 026's earlier pending/review checkpoints. Registry
reconciliation changes no production, tests, build inputs, other assignment, accepted
qualification document or historical narrative. Chronicle assessment: this bounded
follow-up needs no separate historical entry; evidence remains available for a later
shared Browser milestone account.

## Assignment 027 reservation — 2026-10-22

Jeremy allocated LF-MAC-DEV-027 for bounded #460 automatic supported-media discovery
and Catalog reconciliation. Number 027 was absent from fetched main
`6d5aee56cb84786633d416bfce4b1d8c1e87ebbd` and the open PR inventory
(#457/#448/#360). Independent full clone:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery`; branch
`codex/027-mac-browser-discovery`. Preimplementation hygiene/architecture gate only;
no production implementation or acceptance claimed. Project #4 uses Browser Area,
unset Priority and Backlog until implementation begins; native parent #366.
Existing Catalog safety, R2/R3/R4 dependencies and incomplete DoD remain intact.
No Preview, Player, thumbnails, new Catalog writer/lifecycle, storage-policy expansion,
merge, release or next milestone is authorized.

027 preimplementation gate: hygiene completed (#29/#73 moved to Backlog;
#345/#366 retained In Progress; Jeremy explicitly retained #289 In Progress).
Windows reconciliation, asset observation/fingerprinting and recursive discovery
remain internal to the Windows WPF project; accepted Mac Browser contracts are
read-only. #460's explicit material-extraction gate therefore stops production
implementation pending owner review. See [source inventory and proposal](architecture/mac-browser-discovery-460-gate.md).
No new app, implementation PR or runtime/CI acceptance is claimed.

027 continuation: Jeremy approved bounded shared extraction within #460 on 2026-10-22
(owner comment 6346316550). Pre-edit contract inventory and Windows compatibility
plan recorded on #460. Shared asset observation/reconciliation/recursive owners
compose over the existing lifecycle; Mac visited-folder discovery requires no Preview.
Project #4 Browser / unset Priority / In Progress while implementing; native parent
#366, broader incomplete R2/R3/R4 and prior hygiene preserved. Local Release 38 native,
50 Catalog and 20 UI tests passed; real task-owned Mac app discovery/selection/recursive
scope/normal shutdown exercised. See [qualification and owner launch](qualification/mac-browser-460.md).
Final-head CI, refreshed exact-head Mac bundle, separate DARKMATTER package report
and explicit owner acceptance remain distinct gates. No merge/release/next milestone.


027 correction continuation — 2026-10-22: owner narrowed #460 to package traversal, concise truthful partial-scan status, disclosure targets and persistence/performance evidence. No Computer Use or interactive launch. #462 unstarted; broad layout/visual redesign excluded. #393/#394/#395/#366 remain open/incomplete. Current correction receipts, CI and Project-access limitations are recorded on #460/#461. Original failures remain retained.


## Assignment 027 accepted implementation and Windows companion — 2026-10-26

Jeremy accepted bounded #460 / PR #461 at exact head
35592811005d8ef6b72146785ec260eb408617f2, tree
fe237aebaf820e35f115109a9f3213e5e1b2af69, and explicitly authorized
normal merge and bounded closeout. LF-WIN-DEV-027 is the Windows qualification
companion under the existing global allocation 027, not a new allocation.
Jeremy reports DARKMATTER PASS: 156 focused Windows Release tests, packaged
startup, Catalog protection and dependency validation at the accepted source.
This Mac does not claim Windows execution or invent Windows artifact hashes/paths;
retain the owning Windows clone, accepted executable, original logs, fixtures and
provenance in place. The owner report is recorded on #460/#461.

PR #461 merged normally as d847d19e8a82b94a3ab2b94001cdb2a6544893ff.
Both parents and the entire accepted tree were independently verified unchanged.
Exact-head CI 39878804057 passed all required Windows unit/package, native Mac
and neutral Windows/macOS jobs; release publication skipped. Implementation
post-merge CI 40369025993 and registry/final-main CI outcomes are recorded on
#460/#461 and the registry PR; no result is predeclared here.

#460 is closed/Completed. Project #4 Done is required; direct Project verification
or mutation is unavailable through current tools, so no successful field change is
claimed. Native parent #366 is retained. #393/#394/#395/#366 stay open/incomplete,
with broader Definition-of-Done and native dependencies unchanged. #462 remains
the next unstarted Browser UI parity milestone, required Backlog / Browser and
parent #366; its previously missing native parent/Project reconciliation remains
an explicit tool-access gap unless later verified on the issues. No #462 work starts.

The independent full Mac clone
/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery
retains the accepted self-contained bundle and clean exact-head provenance in
artifacts/mac-shell; all 290 bundle file hashes were reverified before cleanup.
Acceptance media/Catalog fixtures and persistence/timing evidence remain in
artifacts/lf027-corrections-final, earlier artifacts/lf027-owner-acceptance and
native test fixture roots. Original failures, prior manifests/screenshots and
qualification receipts remain in artifacts/lf027-evidence (including prior207),
artifacts/lf027-status-final, project TestResults and task-owned logs.
artifacts/lf027-closeout/lf027-preserved.bundle was verified complete before branch
cleanup and preserves local development/published commits and original refs.
Accepted packages, provenance, fixtures and evidence are not removed or rebuilt.

This supersedes only 027's prior pending-acceptance checkpoints. Registry closeout
changes no production/test/build inputs or accepted qualification source. Completed
branch cleanup and final repository/CI outcomes are recorded durably on the issues.
Chronicle candidate after acceptance: the owner-approved shared discovery extraction
and native Mac automatic Catalog ingestion milestone, with explicit sole-writer,
identity/safety and separate Windows qualification evidence (#460/#461). No history
narrative is automatically rewritten. No release, tag or next milestone begins.

## Assignment 028 reservation — 2026-10-27

Jeremy allocated LF-MAC-DEV-028 for bounded #462 Browser presentation parity.
028 is absent from fetched main `eb95296c6b46422ad372652490bc437823991988`
and the inspected open PR inventory (#457/#448/#360). Independent full clone:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-028-BrowserParity`; branch
`codex/028-mac-browser-ui-parity`. No other allocation is changed.

#460 is verified Closed/Completed; #366 and #395 remain open/incomplete. Current
GitHub connector has no Project-field, native-parent or dependency access; terminal
Git has no authenticated credential and gh is unavailable. Computer Use is forbidden.
Consequently #460 Project Done, #462 Browser/existing Priority/Backlog then active
status, missing native #366 parent, and current Project priorities/active Epic audit
remain explicitly unverified/unreconciled. Historical dispositions are not asserted
as current verification; no unrelated roadmap decision is changed. Owner kickoff
allows continuation with tooling limitations reported; AGENTS.md requires recording
these honestly. Preserve all wider R2/R3/R4 blockers/DoD and sole Catalog safety.
No GUI launch, release/tag, merge or acceptance is authorized by this reservation.

028 implementation checkpoint: existing shared Avalonia panes now stretch and resize,
reuse WPF palette/brand/folder silhouette, compact controls and Selection foundation.
Mac-only optional view preferences persist proportions/right visibility outside the
Catalog. 31 headless UI and 41 native Release tests pass; no native GUI app launch.
See [comparison and exact handoff](qualification/mac-browser-462.md). Project/native
parent/current priority audit limitations remain explicit; no unrelated roadmap
decisions, shared discovery/Catalog/schema or Windows shipping input changed.
Draft PR/exact-head CI, fresh bundle provenance, separate DARKMATTER qualification
and Jeremy's functional/visual acceptance remain separate gates. No merge/release.

028 continuation — 2026-11-01: Jeremy authorized complete Phase 1 geometry after
assessment, within existing clone/branch/Draft #464. Approved 1440×900 default,
1120×720 minimum under native constraints, restored geometry and fixed preferred
280/360 side widths with right initially collapsed; header toggle/logo integration
and one compact footer. Local iteration evidence/staging is versioned P1-I01.
No visible app launch/Computer Use during Stage A. Stop for separate affirmative
desktop-capture permission and applicable skill, then finalize actual-evidence ZIP.
No phase acceptance, Phase 2–4 work, Windows shipping/default-view change or merge.
Final exact-head CI/Windows local package gates and Project/parent access gap remain.

028 continuation — 2026-11-01 I02: owner feedback accepted I01 pane/right/window
resizing, restored geometry and native zoom interaction, not Phase1 as a whole.
I02 preserves1440×900 first-run target, adds optional native zoom preference distinct
from fullscreen, and aligns footer responsibilities with WPF without deferred features.
Read-only AppKit work area1800×1054 logical points/backing2 supports the target;
I01 capture was1440×900 client while later saved1200×800 overrides defaults.
Fresh I02 profile and actual native reopen evidence remain capture-gated. Existing
Project/native-parent access gap persists; no roadmap values invented. No Phase2.

028 continuation — 2026-11-01 I03: owner I02 resize-after-zoom blocker reproduced
as queued saved geometry overwriting newer live size in two headless ordering cases.
Remove state-triggered placement; observe zoom preference only, retain native ownership
and startup/display clamps. Optional isolated geometry trace supports physical capture;
headless tests are not AppKit smooth-drag acceptance. Footer/provisional/pane behaviors
unchanged; no Phase2, merge/release/tag. Native capture requires new permission.

028 continuation — 2026-11-01 P2-I01: Jeremy formally accepted Phase1 I03 at
02a996e6c0e8c4fbc0307f95bcaa8cb7b4b4c584 / build50639853af3227e6cd1d842077e5540769244f9f02950c5a9a6a8730e5b40ad3
and authorized shared visual design system and approved Mac icon packaging. Geometry,
Browser/Catalog,Windows shipping behaviors retained. Phase2 owner acceptance and native
Finder/Dock/switcher screenshots remain capture-gated. No Phase3/4 or merge/release/tag.

028 continuation — 2026-11-01 P3-I01: Phase1 I03 and Phase2 I01 owner-accepted.
Phase3 interaction audit and bounded address/shortcut/focus/accessibility corrections,
headless/native tests and exact-source package; new desktop permission gate.
No Phase4, merge/release/tag. Project/native-parent operations still unavailable.

028 continuation — 2026-11-01 P4-I01: Jeremy accepted Phase3 I01 and authorized
final inspection/qualification. No production correction indicated by focused audit;
preserve accepted source, publish equivalent tree and qualify fresh exact-head package/CI.
Prepare separate DARKMATTER handoff without remote control or invented allocation.
StageA only until new capture permission; no Phase4 acceptance, merge/close/release/tag.

## Assignments 028/029 completed — 2026-11-01

Jeremy accepted all four Mac phases and LF-WIN-TST-029 independent DARKMATTER
Windows qualification at head `3fa23ee01a38e96afa918d26a3a9b9fe798a145c`, tree
`b41a6b72ccdc432ef818cb5f4ab3fb6c20cb4ccd`; PR #464 normally merged as
`49cb3c7a04bb8a498548aee583e28b9563065993` with that exact tree; #462 is Completed.
[Post-merge CI 41369147121](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/41369147121)
attempt 1 failed a Windows Player decoded-frame assertion and skipped packaging;
attempt 2 passed all five required jobs without source changes, with tagged release
publication intentionally skipped. Both outcomes and all phase/platform acceptance
records remain preserved on #462 and in the original task evidence. Root cause of
the initial failure is not established. 028 and 029 are Done; no new allocation.
Project #4 membership/Done/Area/Priority and native parent/dependencies remain
unverified because connector operations are unavailable. #366 and #395 remain
open/incomplete; their remaining DoD and dependencies are unchanged. This registry-only
closeout does not change production inputs or authorize further UI work or release.

## Assignment 030 reservation — 2026-11-01

Jeremy allocated **LF-MAC-DEV-030 — Mac Locations Redesign** for #465. Number
030 is absent from independently cloned main `1ea4da350371c277eb95c6208310a5d355cc5085`
and inspected open PRs (#457/#448/#360). Independent full clone:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations`; branch
`codex/030-mac-locations`. Canonical checkout untouched. Current AGENTS.md,
#465/embedded approved mockup and accepted #456/#462 qualification reviewed.
Stage A only; separate owner permission required for desktop control.
User-managed Favorites excluded. Mac hands-on, independent Windows qualification,
exact-head CI and explicit acceptance/merge remain separate gates; no release/closure.
Current connector lacks Project/native-parent/dependency operations; reconciliation
remains unverified, no Area/Priority/parentage invented.

030 Stage A implementation checkpoint: native mount metadata behind shared Browser contracts,
three session-only sections, expandable built-ins and retained complete folder controls.
Focused UI51/native50/Application137/Catalog50 passing; actual visual/package/Windows/CI
gates remain separate. See [qualification](qualification/mac-locations-465.md).
Jeremy explicitly confirmed preserving accepted #460 discovery; #465 adds no ingestion
behavior (recorded on #465). Draft PR #467 remains unmerged; 23 native process checks
also passed. Stage B desktop permission/owner acceptance remain separate.

030 continuation — 2026-11-01 I02: Jeremy passed all9I01 hands-on checks and authorized
only permanent non-interactive section headings and smaller12-DIP/1.25-stroke tree
artwork within existing32-DIP targets. Same clone/branch/Draft#467; no new allocation.
I01 native/shared behavior and #460 discovery retained. I02 UI51/native50PASS; fresh
package/staging without desktop control. Stop for new5–7minute capture permission,
then owner I02 review; Windows/exact-headCI/explicit merge remain separate. I01
CI41380067750SUCCESS carried as historical evidence only. Project access gap unchanged.

030 continuation — 2026-11-02 I03: Jeremy accepted all six I02 checks and authorized
automatic native mount/unmount/rename reconciliation for local/external and mounted
network volumes. Same assignment, clone, branch and Draft PR #467. Native observer
feeds debounced/cancellable UI-thread shared-model reconciliation without discovery
or Catalog mutation; current unavailable path/history/selection retained. Application
141 / UI 56 / native 52 tests pass; actual hardware events await owner hands-on.
Separate narrow AGENTS.md workflow commit 50cb7a3 records Markdown-first routine
handoffs, without mandatory screenshots/Computer Use/ZIP. No desktop control performed.
Fresh I03 package/report, Windows local qualification, exact-head CI, owner acceptance
and explicit merge remain gates. Project/native-parent tool gap unchanged; no release.


030 continuation — 2026-11-02 I04: owner approved bounded resident iCloud media
and unrelated unavailable mapping corrections after diagnostics. Media-only native
residency/no-materialization guards preserve Catalog storage restrictions; offline
root exclusion requires resolved absent-volume versus distinct live-mount evidence.
Release Application 141, Catalog 51, shared UI 57 and Mac native 58 pass, including
task-owned disk-image remount/authored-state coverage. Cloud metadata is simulated
for registered fixtures; actual provider behavior awaits owner hands-on testing.
No spinner timeout/progress/concurrency work, Computer Use, screenshot or ZIP.
Same assignment/branch/Draft PR; fresh I04 package, Windows, exact-head CI and
explicit merge authorization remain gates. Project/native-parent tool gap unchanged.


030 continuation — 2026-11-02 I05: owner reported Desktop/Documents partial warnings
with recursion on/off. Bounded native diagnostics distinguished actual cloud-only
exclusions from genuine rejected-link diagnostics; no evidence justified hydration
or storage-policy relaxation. Typed availability counts and Mac opt-in safe sibling
reconciliation retain real warnings, disable missing inference for refused scopes
and preserve identities/authored data. Application 141, Catalog 52, UI 59 and native
59 Release tests pass. Fresh I05 isolated package/Markdown handoff, owner hands-on,
Windows, exact-head CI and explicit merge remain gated. No spinner policy changes,
Computer Use, screenshot, ZIP, new assignment or separate branch.


030 continuation — 2026-11-02 I06: owner accepted I05 and requires recursive tree
indication and toolbar parity before merge. Verified Windows selected-OR-effective
scope filled icons, inherited root/descendant rules, labeled Subfolders toggle and
leaf capability. Mac read-only presentation metadata/cache and retained row styling
now reflect those rules immediately; Windows UI and discovery semantics unchanged.
Application 141, Catalog 52, shared UI 61 and native 60 Release tests pass. Fresh I06
isolated package/Markdown handoff, owner, independent Windows, exact-head CI and
explicit merge remain gates. Same assignment/branch/Draft PR; no spinner or #468 work.


## Assignment 031 reservation — 2026-11-02

Jeremy allocated LF-WIN-TST-031 — Mac Locations Windows Qualification for #465 / Draft #467. Registry maximum 030; 031 absent on accepted source and cloned main. Independent DARKMATTER full clone: `C:\Git\Agents\LF-WIN-TST-031`; branch `codex/031-windows-qualification`; accepted source `8643942d8cb5de111e7f1d9ea73d09e32bd84308`, verified against live PR head. Qualification only; production and accepted implementation remain unchanged. Windows regression/package work begins; owner hands-on and explicit merge authorization remain required. No merge, closure, tag or release authorized. Next number 032 subject to concurrent owner allocations.

031 qualification checkpoint: Application 141/Catalog 52/headless UI 61 PASS; Windows 2879 passed, 1 failed, 2 existing environment/external skips (2882 total). Browser 749/749 and selected Catalog safety 64/64 PASS. Fresh required PR package, dependencies, startup/backup/shutdown and executable freshness PASS. Windows disposition BLOCKED by StartupRestore_NativeVideoSurvivesTaskbarReveal(seconds:1), No presented video frame is available; #465 causality unproven, no fix. No task executable remains. See qualification/mac-locations-465-windows-031.md. Owner acceptance/explicit merge gates remain; no merge/closure/release or tag. Reservation/evidence remain local and unpublished.


031 continuation — 2026-11-03: Jeremy accepted all six Windows hands-on checks. Bounded startup-frame diagnostic: accepted 8643942 and verified pre-#465 merge base 1ea4da3 each pass 3/3 isolated rounds for seconds:1 plus 3/3 seconds:0 controls. D — Inconclusive; historical full-suite failure retained, baseline failure not reproduced, causal regression or environmental root cause unproven. No production/test changes or full-suite rerun. See qualification/mac-locations-465-startup-diagnostics-031.md. Final qualification acceptance/explicit merge remain owner decisions; PR Draft/unmerged and issue open. Same assignment; records local/unpublished.


031 owner qualification acceptance — 2026-11-03: Jeremy explicitly approved Windows qualification with the documented startup-frame exception, pending final implementation merge. This supersedes earlier blocked/pending qualification status, not historical results. Original 2879 pass/1 fail/2 skip retained; baseline and accepted isolated repeats 6/6 each; D — Inconclusive remains, cause unresolved, failure not fixed/proven pre-existing. Mac I01–I06 and Windows 6/6 hands-on, Browser749/Catalog-safety64/shared suites/package/exact-head CI accepted. Separate documentation-only publication; #467 accepted SHA/tree unchanged, Draft/unmerged; #465 open. Explicit final merge authorization still required.


031 publication checkpoint — 2026-11-03: documentation-only branch codex/031-qualification-evidence targets main; registry and two reports only. #465 Project #4 Review/Browser/Priority unset verified; native parent null/dependencies empty preserved, parentage not inferred. Qualification owner-accepted with documented exception; final implementation merge pending. Related #395 remains open/incomplete; no merge authorization.

030/031 implementation merge checkpoint — 2026-11-03: Jeremy explicitly authorized
normal merge and closeout. PR #467 merged as fc00a68eef606a7894e08d8495986a78d17c4621,
exact accepted tree ba4299d4d8ac79ea687592d65025d66c138a3f66 preserved. Accepted-head
CI41539568094 passed all five jobs; implementation post-merge CI41734277170 is pending.
Reconciled #469 preserves both assignment histories and the original Windows
2879 passed / 1 failed / 2 skipped, native startup-frame exception and owner-approved
D — Inconclusive disposition. No failure was relabeled as fixed or clean passing.
Final documentation/CI and administrative completion remain pending. Native parent
#395 is valid (its parent #366 verified), but #465 parent write and Project #4
operations are unavailable through current connector/local authentication. Native
parent remains absent by REST read-back; Project latest verified Review/Browser/
unset Priority is historical, not a successful current mutation. Dependencies are
empty and preserved; #395/#366 remain open/incomplete. No release or tag.

## Assignments 030/031 final closeout — 2026-11-03

Jeremy authorized normal merge and administrative closeout. Implementation PR #467
merged as `fc00a68eef606a7894e08d8495986a78d17c4621` and preserves accepted implementation
tree `ba4299d4d8ac79ea687592d65025d66c138a3f66` exactly. Reconciled qualification
PR #469 merged as `8ff27744c2bea55058bfcbb5f9a0ef54b421c2ac`; only the intended registry and
two reports differ from implementation main. Original Windows 2,879 passed /
1 failed / 2 skipped, native startup-frame exception and owner-approved D —
Inconclusive remain; no fixed/proven-pre-existing or clean full-suite claim.

Implementation post-merge [CI 41734277170](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/41734277170)
and documentation post-merge [CI 41736178407](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/41736178407)
passed all five required jobs; tagged publication skipped. Mac I01–I06 and
Windows 6/6 hands-on, package and accepted-head CI evidence remain preserved.
#465 is Closed/Completed; assignments LF-MAC-DEV-030 and LF-WIN-TST-031 are Done.

Native #465 parent remains absent by REST read-back. Owner-selected #395 is valid
under native parent #366, but the connector has no native-parent/Projects v2 writes
and local gh/environment/Git credentials are unavailable. Project #4 Done mutation
and current Browser/unset Priority read-back remain unresolved; latest verified
Review/Browser/unset Priority is historical qualification evidence only. Empty
blocked-by/blocking lists were preserved. #395/#366 remain Open/incomplete with
unchanged wider DoD/dependencies. No unrelated roadmap change, release or tag.

This registry-only closeout does not alter accepted production or historical
qualification reports. Owning task evidence/workspaces are retained; only completed
task branches are eligible for cleanup after required checks. Remote branch deletion
is unavailable through the connector/local authentication and automatic deletion is
disabled; any retained remote refs remain an explicit administrative access gap.


## Assignment 032 reservation — 2026-11-03

Jeremy allocated LF-ANY-ADM-032 — GitHub Administrative Reconciliation.
032 is absent from live main registry and matching open PR/issue searches; maximum allocated number is 031. The owner explicitly selected ADM for this assignment, extending the listed activity codes only for this identity. Independent full clone: C:\Git\Agents\LF-ANY-ADM-032-GitHubReconciliation; branch codex/032-github-administrative-reconciliation; baseline main bb816c6492d8310cb6aeea8c678ae5bfd998b81e. Authenticated gh access works outside the restricted sandbox with repo/project scopes. No production change, release, tag or automatic merge authorized. Historical Windows qualification and startup-frame exception remain unchanged.

## Assignment 032 administrative results — 2026-11-03

Authenticated CLI access supersedes the earlier access-gap summaries without rewriting them. Native #395 parent for #465 is established and independently read back; #395 remains under #366. Project #4 Status changed Review to Done; Browser and unset Priority preserved. Both #465 dependency lists remain empty. Verified merged/unprotected/inactive branches codex/030-mac-locations (#467), codex/031-qualification-evidence (#469), and codex/030-locations-closeout (#470) were deleted with exact-SHA leases after reachability checks against bb816c6492d8310cb6aeea8c678ae5bfd998b81e; remote refs are absent. All other branches and historical evidence are retained. #465 remains Completed; #395/#366 remain open/incomplete. See [administrative report](qualification/github-administrative-reconciliation-032.md). Separate owner approval is required to merge the documentation Draft PR; 030/031 remain Done. No Chronicle entry warranted for routine administration.


## Assignment 032 final administrative closeout — 2026-11-03

Jeremy authorized normal #471 merge conditional on successful required CI and exactly one unchanged-source retry. Original CI41746919361 attempt 1 failed NativeMovePreservesDatabaseManagedBackupsAndExternalLutReferences: 2,883 passed / 1 failed / 1 skipped; source admission during Quiescing reported ambiguous containment with db-shm LeafLinkCount links=0. No deeper native exception chain was recorded. Test, production, workflow and dependency files match baseline main; no correction was made. Attempt 2 of the same run passed all five required jobs, including Windows packaging: 2,884 passed / 0 failed / 1 skipped. Both attempts reported runner image windows-2025-vs2026 version 20261030.294.1, runner 2.338.2. Exactly one retry; original cause remains unproven.

PR #471 merged normally as f9c5529767b41e7be7ec3e603c0e0b2ad4728f05, preserving accepted head 3e2cf40d870c199cd4486a8a0a2647a25dd97b1e and tree bb105fae7c2cfa92f5493ade9eaafb14547f59c9 exactly. First-parent diff contains only this registry and qualification/github-administrative-reconciliation-032.md. Post-merge CI41751296327 passed all five required jobs; tagged publication skipped. #465 remains Closed/Completed under native #395, Project #4 Done/Browser/unset Priority; #395/#366 remain open/incomplete. Original three task branches remain absent, dependencies and wider DoD preserved. Assignment LF-ANY-ADM-032 is Done; publication of this final registry record is a separate registry-only Draft PR requiring owner merge approval. Prior pending checkpoints remain historical. Windows startup-frame exception and owner-approved D — Inconclusive are unchanged; no production/test/workflow/dependency change, release or tag. No Chronicle entry warranted for routine administration.

## Assignment 033 reservation and legacy reconciliation preparation — 2026-11-04

Jeremy allocated LF-ANY-ADM-033 — Legacy PR Reconciliation. Number 033 was absent
from current main registry and matching open assignment work (the issue-search hit
in #446 was a commit substring, not an allocation); maximum allocation was 032.
Owner-selected ADM follows the bounded activity-code exception recorded for 032.
Independent DARKMATTER full clone:
`C:\Git\Agents\LF-ANY-ADM-033-LegacyPRReconciliation`; branch
`codex/033-legacy-pr-reconciliation`; starting main
`332062562a585d8123f077d05c15fa5063366d27`, tree
`7a2d1b7014fba142964edad75a63c8caf9ade576`.
Status: documentation prepared for owner review; no merge, closure or deletion authorized.

### Assignment 025 — accepted completion reconciled

[Issue #454](https://github.com/jeremyrunningphotography/lightflow-studio/issues/454)
records Jeremy's accepted Mac hands-on and same-source LF-WIN-DEV-025 Windows
companion package PASS at `6368099a304e7242a77b87ded45cedd4f7036eaa`, tree
`006d1e305a7c7d5a50045c98a1506b06966d2a24`. The companion is the same global
allocation, not a new assignment. Normal #455 merge
`4e1f361ab02b1e72ed729a59b439478ca916dc7b` preserved that tree.
Accepted-head CI 39495587310 and implementation post-merge CI 39499032786 passed.
Assignment 025 is Done; historical pending checkpoints above are superseded only
in acceptance/status. Failed CI 39494864488 and 39495258487 remain historical failures.

The exact historical #457 patch was reviewed at
`9e9cc164b12b5c2bd26d40a90174404542003a11`. Its failed CI 39499445829 remains
failed: 2,883 passed / 1 failed / 1 skipped; NativeMovePreservesDatabaseManagedBackupsAndExternalLutReferences
refused admission during Quiescing with db-shm LeafLinkCount=0; dependent packaging
was skipped. Later #446 research demonstrated a cross-fixture global SQLite pool-clear
mechanism; it did not deliver a correction or conclusively identify the unique historical
caller. No green later run establishes this historical failure fixed.

Retain the original Mac clone `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-025-Browser`,
accepted app/provenance (290 verified file hashes), real Catalog/media/process fixtures,
original failed logs, source bundles and artifacts/lf025-closeout evidence documented
on #454. Preserve the separately owned Windows qualifier's clone, package, isolated
profile and smoke/dependency evidence; unavailable paths/hashes are not invented.
The accepted qualification snapshot docs/qualification/mac-browser-454.md is unchanged.

#456 is now Closed/Completed: assignment 026, #458 normal merge
`d2a600f9372fc5a6869540135dc77eaa57a308d4`, registry #459 and successful final-main
CI 39634319595. Its four accepted requirements (filesystem Locations including
uncataloged folders, no product-facing Media Root, leaf disclosure behavior, and
Refresh context preservation) are delivered bounded follow-up, not pending 025 work.
R2 #393, R3 #394, R4 #395 and #366 remain Open/incomplete; no broader DoD is waived.

### Assignment 022 — partial delivery reconciled

[Issue #446](https://github.com/jeremyrunningphotography/lightflow-studio/issues/446)
records accepted journal source `144178ee31aa9961fa57c08a607ac758e54458be`, normal
#447 merge `1af0bc49e29aa70a9c43d362032791316f35cbcd` with identical tree, and
successful accepted-head CI 38466354591 / post-merge CI 38472601390.
Native process termination synchronization and controlled live-journal refusal coverage
preserve recovery, retention, PendingRecovery and single-writer assertions.
The later accepted initial-startup Browser fixture correction #449 merged as
`b3e353d958db4e7d1beed8331a72aa57882517bf`; accepted-head CI 38511370068 and
post-merge CI 38734852974 passed. This corrects the demonstrated initial-startup
synchronization race, not the historical after-selection failure.

Assignment 022 remains partial/incomplete; #446 remains Open. Latest authoritative
#446 record is Project #4 Review / Other / Priority unset, with no native parent or
dependency edges. No Project mutation is made here. SQLite cleanup ownership,
historical Browser after-selection attribution, playback cadence and separate #436
remain unresolved; no investigation resumes. Later pool-clear research is not a
completed CI reliability fix. Historical #423/#444 references above are checkpoints;
both were subsequently merged by their owners.

The exact #448 patch was reviewed at `e04ef580f9aad011cbc11ce3fef2991f204e9ca3`.
Its required CI 38510355543 remains failed (cadence gaps 0.134/0.117); preceding
playback failure 38456963160 and original focused Browser failures remain evidence.
Later passes do not erase them. Retain both 022 clones, original/corrected probes,
TRX/logs, package provenance, complete-history bundles and artifacts/lf022-evidence,
artifacts/lf022-continuation, artifacts/lf022-remaining and artifacts/lf022-449-closeout
in their owning workspace. The stale instrumented incremental-build attempt remains
disclosed and is not a playback reproduction. Existing qualification reports are unchanged.

### Proposed owner disposition

Prefer this minimal current-main registry reconciliation over rebasing either stale
branch. #457 and Draft #448 remain Open, unchanged, with failed CI preserved.
After explicit owner acceptance and required checks, a replacement documentation PR
may be merged normally, then the historical PRs closed as superseded only with
separate authorization; branch deletion is a further explicit disposition.
#360 remains Open/Draft and untouched; TourBox Console layout and physical acceptance
remain separate #354/#345/#346 work. No production, tests, build scripts, dependencies,
qualification snapshots, release/tag or unrelated roadmap changes. No Chronicle entry
is warranted for routine registry administration. All newer assignment rows/history
are preserved; 033 preparation does not complete 022 or the broader milestones.

## Assignment 034 reservation — 2026-11-04

Jeremy allocated LF-WIN-DEV-034 — Documentation CI & Workflow Simplification for
[#475](https://github.com/jeremyrunningphotography/lightflow-studio/issues/475), native
parent #290. Number 034 was absent from current main and matching open work; 033
and its registry-only Draft #474 remain untouched. Independent DARKMATTER full clone:
`C:\Git\Agents\LF-WIN-DEV-034-DocumentationCI`; branch
`codex/034-documentation-ci`; baseline main
`4fb5b3f9c2cdbce08befeac8f1896f08e252b242`. Conversation title verified as
LF-WIN-DEV-034 — Documentation CI & Workflow Simplification. Implementation begun;
full CI is required for this workflow/root-policy change. No production behavior,
branch-protection change, merge, issue closure, release or tag authorized. Owner
architecture acceptance, exact-head checks and explicit merge authorization remain.


## Assignment 035 reservation — 2026-11-04

Jeremy allocated **LF-ANY-DOC-035 — Development History Update** for an evidence-backed
October 13–November 4 continuation of the three historical documents. Number 035
was absent from current main, all fetched remote assignment registries and GitHub
issue/PR search; owner-designated 034 remains separately allocated. Independent
DARKMATTER full clone: `C:\Git\Agents\LF-ANY-DOC-035-DevelopmentHistory`; branch
`codex/035-development-history`; starting main
`4fb5b3f9c2cdbce08befeac8f1896f08e252b242`.
Status: revised historical narrative owner-approved at `2991a2bb03d2efaf0c1ff0c4482865f1d4447dd1`.
Owner-authorized continuation in existing #478 adds required in-assignment historical
maintenance to AGENTS.md and current guidance under #477. Original editions and
November 4 checkpoint remain intact. Overall PR stays Draft; full CI, normal owner
acceptance of the policy change and explicit merge authorization remain required.
No separate registry-closeout PR. Related #427 was already Completed and is unchanged.
No production/test/workflow, release/tag, merge or concurrent 034/#475/#474 changes.
## Assignment 033 administrative closeout — 2026-11-04

Jeremy explicitly authorized normal #473 merge, superseded #457/#448 closure,
safe completed-branch cleanup and assignment 033 closeout. PR #473 merged normally
as `4fb5b3f9c2cdbce08befeac8f1896f08e252b242`, with parents starting main
`332062562a585d8123f077d05c15fa5063366d27` and accepted head
`1fd9df90db4a1f1d8bfe939de4a99f5b084460a1`. Complete accepted tree
`413cc94d99b4ea529f7533b619d06e3a0098bf2f` is preserved exactly.
Accepted-head [CI 41889581607](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/41889581607)
and post-merge [CI 41915468032](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/41915468032)
passed all five validation jobs, including Windows installer/portable packaging;
tagged publication was skipped. No retry or protection bypass was used.

#457 closed automatically with the merge and received an explicit supersession
comment after successful post-merge CI; #448 then closed with its supersession
comment. Both are Closed/unmerged at their original heads. Their discussion,
failed CI and qualification records remain intact; #473 is the authoritative
current-main reconciliation. Assignment 025 remains Done; assignment 022 remains
Partial delivery, not Done. Read-back confirmed #454 Closed/Completed with Project
#4 Done/Browser/unset Priority and native parent #366; #446 Open with Project #4
Review/Other/unset Priority, no native parent. No issue/Project mutation was needed.

Remote branches `codex/025-browser-closeout` (9e9cc164),
`codex/022-journal-partial-registry` (e04ef580), and completed
`codex/033-legacy-pr-reconciliation` (1fd9df90) were deleted with exact-SHA leases.
Each was unprotected, had no applicable branch rules and had no remaining open PR
using it as head or base; historical content was preserved on main and in durable
issue/PR records. Remote refs were independently read back absent. The merged
local 033 branch was deleted after ancestry verification; other task workspaces
were not changed. A verified complete-history backup of both historical PR heads,
accepted 033 head and merged main remains at
`C:\Git\Agents\LF-ANY-ADM-033-LegacyPRReconciliation\artifacts\lf033-closeout\legacy-pr-history.bundle`.
Accepted-head and post-merge CI JSON and PR metadata are retained alongside it.

#360 remains Open/Draft at `5d67d752d6670c194aab31a5a3b6492ab886310f`;
its `issue-354-tourbox-console-preset` branch and separate #354/#345/#346 work are
untouched. R2/R3/R4/#366 remain incomplete. No production, test, build, dependency,
qualification-report, release/tag or unrelated roadmap changes were made.
Assignment LF-ANY-ADM-033 is Done; publication of this final registry record uses
a separate registry-only Draft PR and remains subject to required CI and explicit
owner merge authorization. The earlier preparation/proposed-disposition paragraphs
remain historical checkpoints. Routine administration warrants no Chronicle entry.

## Assignment 036 reservation — 2026-11-05

Jeremy allocated **LF-MAC-DEV-036 — Browser Thumbnail Foundation** for #481.
036 was absent from current main, every fetched remote assignment registry, and
matching GitHub issue/PR searches. Independent full clone:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation`; branch
`codex/036-browser-thumbnail-foundation`; independently verified remote main
`7f755460e36375234281481cea3004f60344325b`. Conversation title matches exactly.
Phase A only: repository/project hygiene, Windows/Mac parity and thumbnail audit,
and an unapproved architecture proposal. No production implementation, implementation
PR, merge, issue closure, release, or additional assignment is authorized.
#482–#485 remain unallocated and unstarted. Reservation publication is task-owned;
no routine registry-closeout agent or PR.


## Assignment 036 Phase B authorization and implementation — 2026-11-05

The owner explicitly approved the Phase A architecture and authorized implementation
of #481 in the existing clone and branch. This supersedes the Phase A-only authority
boundary above; the earlier audit remains historical evidence. Shared schema-4
Preview services and narrow Windows wrappers, native Mac thumbnail adapters and
bounded Browser demand are implemented for review. See the [Phase B report](qualification/browser-thumbnail-foundation-036-phase-b.md).
#481 is In Progress, Browser, priority unset. #480 and #482–#485 remain Backlog;
no child assignment was allocated. Owner Mac hands-on acceptance, independent
DARKMATTER Windows qualification, exact-head CI and explicit merge authorization
remain outstanding. This is not Done, accepted, merged or released.


## Assignment 036 performance correction — 2026-11-05

Owner acceptance of #481 was blocked by 20–30+ second DJI video thumbnails on NAS
and external media. The same assignment/branch investigates the authorized source
read-only, preserves Phase B evidence, and corrects the Mac resource loader's
synchronous whole-range delivery. Measured cold-Preview, byte-read, memory and
same-frame comparisons are in the [performance report](qualification/browser-thumbnail-foundation-036-performance.md).
No decoder dependency, Preview schema or Windows behavior change is intended.
PR #486 remains Draft; #481 remains open/In Progress; owner acceptance,
independent DARKMATTER qualification and explicit merge authorization remain gates.
No new issue or agent was allocated; #482–#485 remain unstarted.


## Assignment 036 cached presentation continuation — 2026-11-05

Owner accepted the corrected video-generation performance and seven functional
checks at `72729b35`. Remaining warm-navigation latency is addressed in the same
Draft #486 by provisional existing-Catalog/cache publication before unchanged
authoritative reconciliation. No schema/index/Windows decoder change. The separate
last-location behavior predates #481 and needs owner-scoped persistence semantics;
it is reported rather than silently implemented. See the [cached presentation report](qualification/browser-thumbnail-foundation-036-cached-presentation.md).
Owner acceptance of the final Mac source must precede independent Windows
qualification. #481 remains open/In Progress; no merge, #482 or new agent.
Historical maintenance: **Not warranted** for this bounded correction; earlier
feature milestone records remain preserved.


## Assignment 036 final thumbnail label refinement — 2026-11-05

Owner accepted all six cached-presentation scenarios at `cf6b9145`, retaining prior
video-generation/functional PASS results. The final requested product change removes
the visible Cached label in the Mac thumbnail cell only; cached-first publication,
background verification and genuine failure/unavailable/loading states remain.
See the [final UI report](qualification/browser-thumbnail-foundation-036-final-ui.md).
Fresh exact-head Mac package acceptance precedes independent Windows qualification;
#481 remains open/In Progress, #486 Draft, and merge requires explicit authorization.
Historical maintenance: **Not warranted** for a minor presentation refinement.


## Assignment 036 audio Browser visibility parity — 2026-11-05

LF-WIN-TST-037 stopped at Gate A with the owner-confirmed Audio visibility mismatch.
The same #481 / Draft #486 continuation aligns all Mac Browser projections with
Windows StillImage/RawImage/Video categories while preserving shared Audio
classification, Catalog admission, stable/authored records and accepted Preview/UI
behavior. See the [audio visibility report](qualification/browser-thumbnail-foundation-036-audio-visibility.md).
Owner focused Mac acceptance, independent LF-WIN-TST-037 Gate A revalidation/full
qualification, exact-head CI/review and explicit merge authorization remain gates.
No new issue, agent, PR, #482 work or #460 historical-specification edit.
Historical maintenance: **Not warranted** for this bounded parity correction.


## Assignment 036 persistent Mac acceptance workflow — 2026-11-05

Owner reports all Mac functionality accepted at `666505ef`, including Audio visibility.
The same assignment/branch now establishes a retained, task-owned Lightflow Test
Catalog outside packages, supported initialization and conservative package-bound
preflight, with separate disposable tests/smoke roots. Production code and schemas
remain unchanged. See [workflow handoff](qualification/mac-acceptance-workflow-036.md).
Owner workflow acceptance, final exact-head CI, LF-WIN-TST-037 qualification and
explicit merge authorization remain gates. #486 stays Draft, #481 open/In Progress;
no new agent/issue/PR, #482 work, closure or merge. Historical maintenance: Chronicle
and Source Index **Updated** for the durable acceptance-data workflow; Development
History **Not warranted**, as broader product capability is unchanged.
