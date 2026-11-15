# Export preferences #503 — accepted I01 and final qualification

LF-WIN-DEV-052, 2026-11-15. This record supersedes the earlier qualification
stop reports; those reports remain historical evidence, including their failures.

## Acceptance and source preservation

Jeremy accepted I01 as a complete feature and explicitly authorized final
qualification, one implementation PR to `release/1.0`, green exact-head CI,
normal merge and post-merge closeout. This is feature-level acceptance, **not**
a claim that all 18 acceptance checklist scenarios were individually executed.

Accepted source: `d735dfc670717f0a87d6def843172bc1dc700841`; tree
`62feaba02bed54c8c9ddeae6e2ff4d3d6f8398ac`; production subtree
`8dfa4fd8fe70fec9bc40240cd657c8abf3d8fa44` (unchanged from `43941ec`).
Finalization changes only documentation. Release base `4cf5e61` is an ancestor;
PRs #505, #506 and #507 are merged. No unrelated Player/Browser/Jobs change,
dependency, version change or main forward-port is included.

The [current contract](../EXPORT_PREFERENCES.md) documents sparse schema-1
overrides, automatic persistence/recovery, contextual Same as Source, explicit
Specific Folder/MRU, independent subfolder settings, Reset and async reanalysis.
OriginalName-only and empty Custom text remain defaults. Ranges, assets, jobs,
progress and overwrite authorization are not reusable preferences.

## Qualification and explicit limits

* Focused Export: 204 PASS / 0 FAIL / 6 existing opt-in SKIP.
* Full Release: 2,699 PASS / 2 FAIL / 8 existing opt-in SKIP. The unchanged
  Player cadence case observed two 117-ms intervals outside 32–101 ms; Browser
  Cut failed Clipboard.Clear with `0x800401D0`. Causes remain unclassified;
  neither is proven harmless, baseline or environmental. #504 tracks the separate
  clipboard category. Jeremy authorizes these **local** exceptions only; required
  exact-head CI must pass. No full rerun merely to obtain green.
* Approved launcher guard: 4/4 PASS; independent unchanged release plus guard:
  destination tests 6/6 PASS. Original historical recursion trigger remains
  unconfirmed. The child marker is a recursion tripwire, not a desktop bypass.
* Corrected GPU group: 12/12 PASS, no skips, one authorized rerun at `d735dfc`.
  Fixture now uses an existing parent and an absent output subfolder; negative
  EOF refusal, button/error and no-output/source-safety assertions are retained.
  Tests used pinned task FFmpeg tools, not a packaged executable launch.
* Local Release package: installer compilation/metadata/branding/icon, portable,
  startup/shutdown, dependency and isolated Catalog/backup checks PASS. All 25
  extracted portable files match staged hashes. No installer was installed and
  no release was published.
* Packaged Export smoke: defaults, pending analysis circle/message/disabled
  Export, explicit destination and custom subfolder persistence over reopen and
  full restart, then Reset clearing overrides while retaining MRU PASS. Initial
  desktop-access failure was resolved after owner unlock; original failure is
  retained. No packaged media export was queued; native GPU tests cover output.

Evidence remains valid because production, tests, dependencies and launcher inputs
are unchanged. The mandated final `Build-Release.ps1 -Mode PullRequest
-SkipInstaller` refresh and exact-head CI results are recorded in PR metadata.
No required gate is waived. Merge and post-merge results must be actual observed
results, recorded in issue/PR discussion without another administrative PR.

## Artifacts and isolated acceptance convention

Workspace: `C:\Git\Agents\LF-WIN-RES-052-ExportSettingsPersistence`.
Packaged executable: `artifacts\release\LightflowStudio\LightflowStudio.exe`
under that workspace. Accepted I01 distributions are preserved in
`implementation\accepted-i01-artifacts` before the mandatory refresh.

Persistent acceptance root: `implementation\data-persistent`; its Catalog and
generated assets remain intact. Fresh independent root: `implementation\data-fresh`.
The persistent profile already browses generated `implementation\acceptance-media`
assets. Future acceptance builds should similarly open a task-owned prepopulated
Catalog browsing relevant test media. Never reset this Catalog or use production
storage; exclude live databases and caches from evidence archives.

```powershell
& "C:\Git\Agents\LF-WIN-RES-052-ExportSettingsPersistence\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-RES-052-ExportSettingsPersistence\implementation\data-persistent"
& "C:\Git\Agents\LF-WIN-RES-052-ExportSettingsPersistence\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-RES-052-ExportSettingsPersistence\implementation\data-fresh"
```

Accepted evidence ZIP: `C:\Git\Agents\evidence\LF-WIN-DEV-052-I01-Acceptance.zip`,
SHA-256 `9F6B291AFE4C07AD5CE8BA7CDC8855E1514A8EDA77FEF4CC2DB0B1C1E26C4CC5`.
It preserves raw qualification history, source and 18-item owner checklist.
The final evidence ZIP adds PR/merge/CI and roadmap disposition with manifests.

Historical chronicle maintenance: **Not warranted** for bounded Export
preferences using existing settings infrastructure. The contract and qualification
records capture the consequential details without a separate historical PR.

## Prepared closeout and next assignment

Registry 052 records acceptance and conditional closeout, not a premature merge.
After green CI and normal merge, close #503 Completed / Project Done; keep #504
open and #491 incomplete for its remaining findings. Record actual provenance in
issue/PR metadata. Release Engineering is next, by separate owner kickoff only:
handoff release head, #496–#500 completion, #503, #504/#491, CI/package state,
local exceptions, defaults and evidence. No publication/tag/version bump or main
forward-port is authorized by this assignment.
