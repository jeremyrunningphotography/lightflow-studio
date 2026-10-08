# Convergence evidence and acceptance index

## Baseline and publication authority

Verified source baseline on 2026-10-08: `f2c258b502df377c8eb2d1bed05f0e1658e05137`, GitHub main and fetched origin/main. Production remains the Windows WPF application plus neutral Actions. No target project, production Mac UI/backend or migration milestone is implemented by this publication.

The owner accepted the overall shared-product direction and authorized this documentation branch/commit/push/Draft PR in the XR1 publication instruction on 2026-10-08. Exact documentation still requires owner acceptance before merge. This record makes that instruction durable; it does not claim GitHub's combined gate was already reconciled or authorize R1/WQ. The research/refinement reports informed the proposed roadmap, not budgets or feature-specific decisions.

At inspection Epic #366 remains Open with its combined architecture checkbox unchecked. M0 #367, G1 #368, G2 #369 and G3 #370 are closed/completed. Current closeout records report children Project Done and Epic In Progress; direct Project field inspection was unavailable through the connector. Do not infer a Project update from this documentation PR.

## Exact accepted snapshots

| Gate | Owner-accepted head | Preserving merge | Disposition |
| --- | --- | --- | --- |
| M0 planning / #367, PR #371 | `c9a5ae99bc1615a422f74bf70e51e6bb2f75b7ce` final qualified preservation head | `e302a20d888161d6face6e311610d67eab9c1512` | Completed planning; earlier reviewed head `ebd337f546ea8807c819184917bf9ab2fde880c9` recorded separately |
| G1 / #368, PR #379 | `bbe3528b2aa63264ce7d70c9cd70f7564f9b8b53` | `1777a4bbb7921ed481f49b204262bb3521ea2922` | Owner-accepted bounded Player PASS |
| G2 / #369, PR #375 | `e33f08e95430a72f419260e84d0a69d608cad821` | `7b7f1c2cac5426f7df145921d8890adde6f9c490` | Owner-accepted selected P2 PASS |
| G3 / #370, PR #380 | `e9c2975b1c7ea4a795d93042039637175a23124c` | `f2c258b502df377c8eb2d1bed05f0e1658e05137` | Owner-accepted bounded UI/image PASS |

Acceptance/status: [Epic #366](https://github.com/jeremyrunningphotography/lightflow-studio/issues/366), [M0 #367](https://github.com/jeremyrunningphotography/lightflow-studio/issues/367), [G1 #368](https://github.com/jeremyrunningphotography/lightflow-studio/issues/368), [G2 #369](https://github.com/jeremyrunningphotography/lightflow-studio/issues/369), [G3 #370](https://github.com/jeremyrunningphotography/lightflow-studio/issues/370); merged [#375](https://github.com/jeremyrunningphotography/lightflow-studio/pull/375), [#379](https://github.com/jeremyrunningphotography/lightflow-studio/pull/379), [#380](https://github.com/jeremyrunningphotography/lightflow-studio/pull/380). PR descriptions and frozen reports may still say pending; later owner acceptance records supersede disposition, not measurements.

## Immutable research and contracts

- [M0 subsequent owner decisions](https://github.com/jeremyrunningphotography/lightflow-studio/blob/e302a20d888161d6face6e311610d67eab9c1512/docs/research/mac-compatibility/PLANNING_DECISIONS.md) — Shared architecture, arm64, parity, WPF continuity and direct download; later G2 narrows active storage.
- [G1 final native audio qualification](https://github.com/jeremyrunningphotography/lightflow-studio/blob/bbe3528b2aa63264ce7d70c9cd70f7564f9b8b53/docs/research/mac-compatibility/x1-player-audio-completion/output-qualification/FINAL_G1.md) — Protected causal clock, streaming feed, drain/exclusive ranges; offsets are RenderReady, not physical AV.
- [G1 operation acknowledgement](https://github.com/jeremyrunningphotography/lightflow-studio/blob/7dec8f7372ba0af06bc15969ec0a976de7984900/docs/research/mac-compatibility/x1-player-completion/ACKNOWLEDGEMENT_SEMANTICS.md) — Operation-specific Decoded/RenderReady/UIAccepted; historical pending callback statement predates final G3.
- [G1 operation matrix](https://github.com/jeremyrunningphotography/lightflow-studio/blob/7dec8f7372ba0af06bc15969ec0a976de7984900/docs/research/mac-compatibility/x1-player-completion/OPERATION_STATE_MATRIX.json) — Reference exact operation semantics rather than replacing them with one generic Presented stage.
- [G1b frame offer](https://github.com/jeremyrunningphotography/lightflow-studio/blob/0991cedd319c59d310f8028b2abe6c738f0e69ea/docs/research/mac-compatibility/x1-player-g1b/PRESENTED_FRAME_CONTRACT.md) — Immutable source/frame/render identity and leases.
- [G1b interop contract](https://github.com/jeremyrunningphotography/lightflow-studio/blob/0991cedd319c59d310f8028b2abe6c738f0e69ea/docs/research/mac-compatibility/x1-player-g1b/PRESENTATION_INTEROP_CONTRACT.md) — IOSurface/event pointers, timelines and composition ownership; its later obligations are qualified only to G3 limits.
- [G2 P2 final recommendation](https://github.com/jeremyrunningphotography/lightflow-studio/blob/e33f08e95430a72f419260e84d0a69d608cad821/docs/research/mac-catalog-proof/local-portability/P2_FINAL_RECOMMENDATION.md) — 36/106/34 round-trip checks and provenance/line-ending limits; 32 synthetic assets, 23 tables.
- [G2 selected local portability policy](https://github.com/jeremyrunningphotography/lightflow-studio/blob/e33f08e95430a72f419260e84d0a69d608cad821/docs/research/mac-catalog-proof/LOCAL_CATALOG_PORTABILITY_PROOF.md) — Local active storage, fail-closed paths and finalized closed NAS transport; guards not production implementation.
- [G3 final report](https://github.com/jeremyrunningphotography/lightflow-studio/blob/e9c2975b1c7ea4a795d93042039637175a23124c/docs/research/mac-compatibility/x3-g3-completion/G3_COMPLETION_PROOF.md) — Actual UIAccepted, retained Details, image/AX/lifecycle findings with startup/package limits.
- [G3 UIAccepted records](https://github.com/jeremyrunningphotography/lightflow-studio/blob/e9c2975b1c7ea4a795d93042039637175a23124c/docs/research/mac-compatibility/x3-g3-completion/UIACCEPTED_RESULTS.json) — Exact token/capture pairs and stale rejections; synthetic producer, not rerun of G1 decode.
- [G3 image/Color records](https://github.com/jeremyrunningphotography/lightflow-studio/blob/e9c2975b1c7ea4a795d93042039637175a23124c/docs/research/mac-compatibility/x3-g3-completion/COLOR_MANAGEMENT_RESULTS.json) — Explicit SDR/ICC/alpha evidence, not calibrated physical parity.
- [G3 shared-view projection](https://github.com/jeremyrunningphotography/lightflow-studio/blob/e9c2975b1c7ea4a795d93042039637175a23124c/docs/research/mac-compatibility/x3-g3-completion/SHARED_VIEW_ARCHITECTURE.md) — Whole shared UI direction; not every named surface was tested.
- [G3 convergence draft](https://github.com/jeremyrunningphotography/lightflow-studio/blob/e9c2975b1c7ea4a795d93042039637175a23124c/docs/research/mac-compatibility/x3-g3-completion/ARCHITECTURE_CONVERGENCE_DRAFT.md) — Historical draft, not the production implementation specification.

## Current production authorities

These immutable baseline references describe delivered Windows behavior. Future implemented slices update the current map while retaining this baseline.

- [Architecture](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/ARCHITECTURE.md)
- [Production project](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/LightflowStudio/LightflowStudio.csproj)
- [Browser actions](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/BROWSER_ACTIONS.md)
- [Player actions](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/PLAYER_ACTIONS.md)
- [Keyboard contracts](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/KEYBOARD_SHORTCUTS.md)
- [Catalog ADR 0001](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/decisions/0001-lightflow-catalog-persistence.md)
- [Current backup and drain](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/catalog-backup-272.md)
- [Color intent](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/LightflowStudio/ColorManagement.cs)
- [Derived Preview Color](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/LightflowStudio/DerivedFrameColor.cs)
- [Windows live Color adapter](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/LightflowStudio/LightflowColorPostProcessor.cs)
- [Design system](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/DESIGN-SYSTEM.md)
- [Noninteractive validation](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/noninteractive-validation.md)
- [Release gates](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/RELEASE_PLAN.md)
- [CI](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/.github/workflows/ci-release.yml)

## Historical evidence remains historical

Keep original REVISE/FAILED/pending records, raw archives and reports unchanged. A later PASS supersedes a gate disposition only within its accepted scope. Component tests, simulations, native runtime, logical composition and physical/packaged acceptance are distinct evidence classes.

- [Initial Player failure/alternative findings](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/research/mac-compatibility/x1-player/PLAYER_MAC_PROOF.md)
- [G1b incomplete integration](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/research/mac-compatibility/x1-player-g1b/G1B_INTEGRATED_PLAYER_PROOF.md)
- [Preserved native audio startup failure](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/research/mac-compatibility/x1-player-audio-completion/AUDIOQUEUE_START_RCA.md)
- [Earlier IOSurface proof and limits](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/research/mac-compatibility/x3-iosurface/AVALONIA_IOSURFACE_PROOF.md)
- [Network Catalog negative checkpoint](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/research/mac-catalog-proof/LIVE_NAS_QUALIFICATION.md)
- [Path hazards](https://github.com/jeremyrunningphotography/lightflow-studio/blob/f2c258b502df377c8eb2d1bed05f0e1658e05137/docs/research/mac-catalog-proof/PATH_IDENTITY_MATRIX.md)

Discrepancies retained: M0/older PR descriptions still describe unmerged preservation or unstarted proofs; latest Epic/acceptance records resolve chronology. The G2 report identifies its historical manifest source field versus executed proof SHA and CRLF/LF accommodation. G1b used Avalonia 11.3.8 source; final G3 used 12.1.3 runtime. Neither pins the production toolchain. The canonical checkout was previously older than main; this publication uses a fresh independent clone and fetched baseline. None of these historical records is silently rewritten.

Bounded PASS does not approve the 16–28 engineer-week Player estimate, 20–40 person-day Details estimate or maintenance, wider filesystem support, calibrated displays, signing, packaged lifecycle or Windows Avalonia GPU seam. See [deferred decisions and gates](migration-and-qualification.md).
