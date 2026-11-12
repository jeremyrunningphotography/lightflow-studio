# LF-WIN-DEV-046 — final Windows qualification for #492

## Accepted source and applicability

DARKMATTER workspace: `C:\Git\Agents\LF-WIN-DEV-046-FolderContextSafety`.
Target `release/1.0`, base `2242c22ec090e8b7557e7c9ac2c34845b6d61f9c`.
Owner accepted commit `bf90cfa1f90c2ce69e80d951bcebbc49326958e2`, tree
`d612f21ecfa74409a5e29e7406e661faeb7745ca`: **17/17 PASS, zero failures,
zero observations**. This is the owner's report; no invented per-step results
or independent-agent qualification is claimed.

All accepted application, tests, dependencies and build scripts retain identical
Git blobs. Only root policy, documentation and CI command routing follow acceptance.
No main merge, forward-port or other bug-bash fix. No repeat owner cycle is required.

## Review of all accepted 14 paths

| Path | Compatibility and necessity disposition |
|---|---|
| LightflowStudio/BrowserFolderContextTarget.cs | Transient existing-node/path/root/Windows directory snapshot; disposed handle; reject changed targets. No durable identity or second selection. Retain. |
| LightflowStudio/MainWindow.xaml.cs | Six context-owned commands; correct source/destination/modal validation; existing executor/Catalog policy; queued Click cleanup; ordinary routes separate. Retain. |
| LightflowStudio/MainWindow.xaml | Only folder preview/Closed wiring; existing controls/styles. Retain. |
| LightflowStudio/MainWindow.Explorer.cs | Existing shell/context boundary with target validation. Retain. |
| LightflowStudio/MainWindow.SmartCollections.cs | Existing scope/editor with context validation. Retain. |
| LightflowStudio.Tests/BrowserFolderContextSafetyTests.cs | 13 isolated routed-WPF/MenuItem cases; cancellation/stale/keyboard/ACL/Catalog state; explicit clipboard persistence limit. Retain. |
| LightflowStudio.Tests/ExplorerHandoffTests.cs | Focus/pointer assertions strengthened; no-navigation assertions retained. Retain. |
| LightflowStudio/ValidationPresentation.cs | Same imported main helper, needed by Add-Type launcher; no ordinary startup call. Retain. |
| scripts/ValidationDesktop.cs | Same main helper; suspended process assigned to private desktop/job, no SwitchDesktop, fail-closed errors and descendant teardown. Retain. |
| scripts/NoninteractiveValidation.ps1 | Same helper; task-owned TEMP/logs, caller edition preserved, bounded teardown. Retain. |
| scripts/Test-Noninteractive.ps1 | Same helper; supported entry preserves Release selection/results/hang policy. Retain. |
| scripts/Build-Release.ps1 | Required boundary and disabled build servers; release/dependency/Catalog policy unchanged. Retain. |
| docs/AGENT_ASSIGNMENTS.md | Replace unrelated historical narrative with global-allocation snapshot/link and 046 accepted/prepared-closeout record. No unrelated status rewrite. |
| docs/noninteractive-validation.md | Preserve maintenance/clipboard limits; append acceptance and CI integration. |

No production correction is necessary and no accepted test is removed or weakened.
Root AGENTS reflects current main's local acceptance → qualification → one PR →
exact-head CI → explicit merge sequence, without unrelated Mac/history imports.
Historical maintenance: **Not warranted** for this bounded correction.

## Validation

- Accepted focused Release: **157 PASS**, no failures/skips.
- Accepted complete Windows Release: **2,503 PASS / 1 existing opt-in Premiere skip**.
- Accepted package/dependency/SQLite/Catalog smoke, Browser/splash/Jobs startup,
  backup/restore protection and graceful shutdown: **PASS**.
- Owner hands-on: **17/17 PASS, zero failures/observations**.
- Original executable SHA-256:
  `bed4de496046d3b1ec27e558ef2d91708a502ac23b63e99d013322c4c3c6b42e`.

Prior evidence remains applicable to identical accepted source/manifests. The only
extra validation input is CI's Test command: use the imported private-desktop entry
under PowerShell 7 rather than direct dotnet test. Focused qualification:
`pwsh.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -NoRestore -Filter FullyQualifiedName~BrowserFolderContextSafetyTests -ResultsName final-qualification-pwsh.trx`
passed **13/13**, with no owned processes remaining. Full exact-head CI and refreshed
required local packaging qualify final integration. Root AGENTS/mixed changes require
full CI. No failed/pending/skipped check is treated as passing. Final head/tree,
package provenance, logs and CI links are recorded in the portable ZIP and issue/PR
metadata, avoiding a status-only commit/CI loop. A separate Mac→Windows qualifier
does not apply to this Windows-only fix.

## Storage and launch

Retained acceptance data is preserved; smoke uses separate disposable roots.
No production Catalog/personal media. Fresh PR-head package command:

```powershell
& "C:\Git\Agents\LF-WIN-DEV-046-FolderContextSafety\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-DEV-046-FolderContextSafety\.cache\acceptance\persistent"
```

Separate fresh-root launch if requested:

```powershell
& "C:\Git\Agents\LF-WIN-DEV-046-FolderContextSafety\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-DEV-046-FolderContextSafety\.cache\acceptance\fresh-I01"
```

## Tracking and gates

#492 remains open for one maintenance PR. #491 remains incomplete with only the
folder finding annotated accepted/awaiting merge. Native parent/dependency edges
remain unchanged; tracking references do not invent an Epic. Project #492 uses
Review / Browser / P1; mixed tracker fields remain unset where not authoritative.
Main evaluation/forward-port is deferred, not authorized here. No separate admin PR.
Qualification: **PASS subject to final exact-head CI**. Explicit owner merge
permission remains required. No merge/post-merge cleanup, release/tag or other fix.
