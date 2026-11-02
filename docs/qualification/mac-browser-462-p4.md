# LF-MAC-DEV-028 Phase 4 I01 final inspection and qualification

## Authority and accepted provenance

Issue #462 is authoritative; Draft PR #464 remains unmerged. Jeremy accepted P1 I03,
P2 I01 and P3 I01 and authorized bounded final inspection/qualification on 2026-11-01.
Accepted source/build pairs, preserved in immutable prior handoff manifests:

| Phase | Source | Build identity |
| --- | --- | --- |
| 1 I03 | 02a996e6c0e8c4fbc0307f95bcaa8cb7b4b4c584 | 50639853af3227e6cd1d842077e5540769244f9f02950c5a9a6a8730e5b40ad3 |
| 2 I01 | dd6a50f6b0f1eaf6fdb8b965d59c8899b36f3511 | 1cd4a5436f3bbad53de2022c00792cfaa8f37b52be73aa21b0ec4eba5ab2b6df |
| 3 I01 | 2aced9f08553f4dd745cfecd75ec64a849c3961c | a9a37e8d4233d5e949941d8b8e70a88a776d6a6e0c30a07eead1ec676e1c24a6 |

Initial published PR head d6bb866 lacked five accepted local commits. Final publication
must preserve equivalent source-tree contents and these historical commits/evidence,
then rerun exact-head validation/build. Historical CI never qualifies newer revisions.
The current staging manifest and CI evidence identify the final published head/tree.

## Focused inspection findings

Inspection covers accepted P1–3 source and actual P3 captures/outcomes, WPF
Themes/LightflowShell.xaml, App.xaml, Browser navigation/toolbar/right-panel/footer
source and DESIGN-SYSTEM. No confirmed regression or narrow polish defect requiring
production changes was found. Initial Phase4 qualification documentation was followed
by a bounded test-only correction after Windows CI exposed the existing30ms folder
loading race: wait up to5seconds for observable completed leaf state, retaining the
original nonempty/hidden-disclosure/reference assertions. No production UI redesign;
accepted production source remains byte-identical to P3. No speculative redesign.

| Surface | Finding and classification | Final evidence/gate |
| --- | --- | --- |
| Header/brand/icon | WPF brand surround and shared artwork retained; native titlebar/icon treatment is deliberate Mac convention | Fresh bundle metadata, exact1024master/10icon representations; native Finder/Dock capture gate |
| Geometry/lifecycle | Accepted1440x900/min1120x720; normal dimensions independent of zoom; no resize-state restore loop | Fresh geometry suite, exact-source native zoom/unzoom/reopen/resize evidence |
| Panes |280/360 preferred side widths, flexible center, initially closed right,8pt boundaries, temporary clamps preserve preference | Headless width/selection/focus tests; native pointer/keyboard checks |
| Toolbar/type/density | Shared dark palette/semantic roles,32pt controls and quiet chrome conform to accepted P2; native font and solid orange primary are deliberate adaptations | Contrast/template tests; final default/narrow comparisons |
| Focus/selection | One shell shortcut route, exact native primary modifiers, text editing precedence, persistent single selection and panel/Catalog focus accepted | Fresh input/selection tests; native traversal/AX sampling, not full VoiceOver |
| Navigation | Draft vs committed path rule accepted; BrowserViewModel remains sole history/discovery/selection owner | Focused address/history/status regression; task-owned tree fixture for folder selection |
| Footer | Real loading/cancel, count, selection, warnings/errors preserved; global Ready means Catalog readiness, not successful scan | Headless partial/cancel/permission coverage; truthful native missing/empty states |
| Locations | Expansion wobble/deeper Finder-inspired structure explicitly deferred #465; no safety blocker shown | Preserve accepted behavior; do not qualify broad redesign here |
| Feature gaps | Text-only provisional list/read-only Selection acceptable; Details/Preview/thumbs #395 and other future features excluded | No fake controls or parity claims for unmigrated features |
| Display/AX | Teardown may report scale1 after native closure; multiple physical displays/VoiceOver not qualified | Document limitation; no speculative geometry change after owner acceptance |

## Ownership and Windows impact

Shared Lightflow.UI owns view/layout/theme/input; Mac host owns composition, profile
preferences and native bundle. Existing Application/Catalog/discovery/generation guards
are unchanged. Shipping WPF references Actions/Application/Catalog, not Lightflow.UI;
no WPF sources/resources/Windows package inputs change in #462. Neutral Windows CI
must still compile/test the shared UI. Independent DARKMATTER WPF unit/package/smoke
qualification is required; Mac tests and hosted CI do not substitute for it.
See [DARKMATTER instructions](mac-browser-462-darkmatter.md).

No new companion agent is dispatched or allocation invented. Jeremy's requested Windows
qualification handoff is continuation of028; if a separate agent assignment is later
requested, reserve the next available global number in that Windows session.

## Final gates and evidence workflow

StageA: no visible GUI/Computer Use. Fresh tests, native process lifecycle/storage
fixtures, package/icon/dependency hashes and exact-head CI; isolated P4 acceptance root.
StageB: fresh affirmative exclusive desktop permission, estimated12–15minutes.
StageC: actual1440x900/default and1120x720/narrow screenshots, native lifecycle,
Locations/folder/address, panels/splitters, focus/selection/footer/error/empty/icon
checks, written outcomes and accurate limitations. Stop desktop control before ZIP.
Staging: artifacts/handoffs/LF-MAC-DEV-028-P4-I01; final ZIP only after actual capture.

Phase4 acceptance, DARKMATTER result, final Mac owner acceptance and explicit merge
permission remain separate gates. Project/native-parent/dependency operations remain
unavailable in current connector; no reconciliation/priority/status mutation claimed.
#366/#395/#465 scope remains incomplete. No merge, close, release or tag.

Routine final qualification does not warrant a separate Chronicle entry; accepted
milestone history may be assessed after owner acceptance without changing older records.
