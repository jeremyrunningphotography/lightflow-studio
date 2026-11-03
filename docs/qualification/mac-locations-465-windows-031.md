# LF-WIN-TST-031 — Mac Locations Windows Qualification

Qualification date: 2026-11-02, DARKMATTER. Issue #465; Draft PR #467.

## Current owner disposition — 2026-11-03

**Owner-accepted with a documented exception, pending final implementation merge.** Jeremy explicitly approved this bounded Windows qualification disposition after reviewing the native startup-frame diagnostic. This is not merge authorization.

Accepted source: `8643942d8cb5de111e7f1d9ea73d09e32bd84308`; tree `ba4299d4d8ac79ea687592d65025d66c138a3f66`. Mac I01–I06 and Windows 6/6 hands-on are accepted. Browser 749, selected Catalog/identity safety 64, shared Application 141/Catalog 52/UI 61, packaged startup/dependencies and all five exact-head CI jobs passed.

The original Windows full-suite result remains **2,879 passed / 1 failed / 2 skipped**, not clean. The exception is `StartupRestore_NativeVideoSurvivesTaskbarReveal(seconds: 1)` / `No presented video frame is available`. File-symlink privilege/Developer Mode and external Premiere acceptance remain the two skipped-test limitations. Three isolated rounds on accepted source and verified pre-#465 baseline `1ea4da350371c277eb95c6208310a5d355cc5085` / tree `d88f735be83d176227fea0a5332bf50ebb39de03` passed both seconds:1 and seconds:0 (6/6 per source). Diagnostic disposition remains **D — Inconclusive**: no root cause established, failure not proven pre-existing, no baseline failure reproduced, and no fix claimed. See [detailed diagnostic evidence](mac-locations-465-startup-diagnostics-031.md).

This section supersedes only the earlier pending/blocked acceptance status below. Historical results remain intact. Reports/registry are proposed for publication in a separate documentation-only Draft PR from main; accepted implementation PR #467 is not modified. Raw logs/TRX, package and task artifacts remain local at the documented paths. Final implementation review and explicit owner merge authorization remain required; #465 stays open and #467 Draft/unmerged. No new test or package run was performed for this documentation update.

---


## Source and assignment

Owner allocated LF-WIN-TST-031. The accepted-source and cloned-main assignment registries have maximum allocation 030 and no assignment 031. Reservation is recorded locally in `docs/AGENT_ASSIGNMENTS.md`; it has not been published to the accepted PR branch. Next number 032 remains subject to concurrent owner allocations.

Independent full clone: `C:\Git\Agents\LF-WIN-TST-031`.
Branch: `codex/031-windows-qualification`.
Accepted and checked-out commit: `8643942d8cb5de111e7f1d9ea73d09e32bd84308`.
Accepted Git tree: `ba4299d4d8ac79ea687592d65025d66c138a3f66`.
Live PR head matched before qualification. Only qualification documentation and the assignment reservation are edited locally; production code, tests, scripts and dependencies remain identical to accepted source. Jeremy's canonical checkout is unchanged.

Reviewed repository-root AGENTS.md, assignment registry, authoritative issue and comments, PR metadata, I06 qualification evidence and noninteractive-validation guidance. Mac I01–I06 owner acceptance is supplied by the kickoff; this Windows assignment does not independently repeat Mac hands-on testing.

## Automated validation

- Shared Application Release: 141 passed, 0 failed, 0 skipped.
- Shared Catalog Release: 52 passed, 0 failed, 0 skipped.
- Shared headless UI Release: 61 passed, 0 failed, 0 skipped.
- Full Windows Release: 2,879 passed, 1 failed, 2 skipped; 2,882 total, duration 7m 7s. BLOCKED by the native startup-frame assertion. All 749 Browser tests and 64 tests selected from PathIdentityPersistenceTests, WindowsCatalogAdmissionTests, MediaRootTests and CatalogReconciliationTests passed.

Windows entry point: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -NoRestore -ResultsName LF-WIN-TST-031-qualified.trx`.
Neutral suites use the CI-prescribed `dotnet test <project> -c Release --disable-build-servers -p:RestoreLockedMode=true` commands. These do not host WPF windows.

Evidence: each project retains its TRX under `TestResults`; shared logs are in `artifacts\qualification-031`. Final Windows log: `artifacts\validation\14862b290a9e4208b506f8e714476aae\output.log`.

Initial setup history: the sandbox denied desktop creation; approved execution used the prescribed private desktop without bypass. I initially started Windows tests before preparing FFmpeg. That run reported missing dependencies and a native startup-frame failure; it was interrupted by stopping only the verified agent-owned launcher, letting its kill-on-close job clean descendants. Its log remains at `artifacts\validation\20dd8df584e84bb68b97da508a81edba\output.log`; no complete counters or passing claim exist for that interrupted run. Both pinned FFmpeg preparation scripts subsequently passed checksum verification before the final run began.

Warnings observed: three existing Windows analyzer warnings (two xUnit2031 at ApplicationIdentityTests.cs:61/62 and one xUnit2020 at CatalogExitBackupTests.cs:553); two existing obsolete CS0618 headless-key warnings at BrowserTests.cs:440. Shared Application/Catalog and packaging emitted no warning lines. Final incremental Windows build emitted zero warning lines. No warning is treated as a test failure.

Exact-head CI [41539568094](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/41539568094) independently verified successful for the accepted SHA: Windows unit tests, Windows installer/portable package, neutral Windows, neutral macOS, and macOS storage/Catalog lifecycle. Tagged-release publication was skipped as expected.

## Scope and safety evidence

Shipping Windows UI/source and Windows storage assessor are unchanged by the PR. Windows composition constructs CatalogReconciliationService with default `allowPartialSources=false`; only the Mac composition opts in. Strict linked-entry refusal remains before Catalog mutation/missing-state inference. Shared tests cover unavailable sources not opened or marked missing, canceled/failed enumeration, default link refusal and opt-in safe sibling reconciliation.

Windows full-suite coverage includes Browser recursive scope/tree/startup restoration, inherited-leaf toggle capability, navigation/Refresh, native admission/junction/hard-link refusal, offline mapping, and PathIdentityPersistenceTests. The latter seeds ratings, notes, keywords, collections, markers and subclips and compares identity/authored state across safe remap/reopen and refused operations. All 749 Browser and 64 selected Catalog safety tests passed in the final TRX.

Two final skips: installed Premiere acceptance requires an explicitly selected disposable external project; native file-symlink leaf coverage lacks Windows privilege/Developer Mode (Win32 1314). Actual cloud-provider hydration, physical removable/network faults, and skipped file-symlink coverage are not inferred from synthetic tests or source inspection. Ordinary Windows navigation retains accepted discovery behavior; this report does not claim that explicit discovery never creates Catalog observations.

## Package and startup

Required command completed successfully on an isolated private desktop:

`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller`

PASS: approved icon byte identity; packaged SQLite 3.53.3 / coherent 2.1.13 win-x64 payload; Browser startup; workspace/splash handoff; Jobs activation; graceful shutdown; Catalog backup, restore protection and shutdown boundary; pinned encoding/playback FFmpeg; staged dependency validation. Packaging reported zero owned processes remaining. After final Windows teardown, an independent process inventory found no executable running from the qualification workspace. No visible desktop control was used.

Package log: `artifacts\validation\d8fe715c19e34b78a692eff687053d2b\output.log`.
Executable UTC timestamp: `2026-11-03T03:01:52.1680331Z`, newer than accepted commit `2026-11-02T22:42:42Z`.
Executable SHA-256: `DD1A1281C335E4409CA8954D887A4662C7BB392570F8C061466EDC629E313076`.
Machine-readable provenance: `artifacts\qualification-031\package-provenance.json`.
Local installer compilation and portable ZIP creation were intentionally skipped by the required PR command; exact-head CI verified both. No release artifact was published.

## Owner launch and focused acceptance

Executable: `C:\Git\Agents\LF-WIN-TST-031\artifacts\release\LightflowStudio\LightflowStudio.exe`.
Isolated acceptance data root: `C:\Git\Agents\LF-WIN-TST-031\.cache\acceptance-031` (created and unused).

```powershell
& "C:\Git\Agents\LF-WIN-TST-031\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-TST-031\.cache\acceptance-031"
```

Task-owned fixture base: `C:\Git\Agents\LF-WIN-TST-031\artifacts\qualification-031\fixtures`. It contains Scope\parent.jpg, Scope\Child\Leaf\sample.jpg and Unrelated\unrelated.jpg.

Pending Jeremy's Windows hands-on checks:

1. Start using the exact command; inspect Windows Locations, dark styling, disclosure spacing and expansion/selection stability against the accepted Windows baseline.
2. Navigate to fixture Scope; enable Subfolders. Verify the label/checked state, selected filled-folder icon, and inherited root/descendant indicators. Ancestors outside scope and Unrelated stay outside recursive coverage; selected rows remain filled independently.
3. Navigate to Child\Leaf. Inherited recursive state keeps the toggle enabled so scope can be removed. A direct known leaf with recursion off disables it with No subfolders. Restore scope and verify persistence across restart.
4. Exercise Back/Forward, address navigation, Refresh, collapse/re-expand and sibling selection; verify context/history and row geometry remain stable.
5. Rate sample.jpg, add notes and a collection, exit/reopen with the same data root, then revisit/Refresh; verify authored state remains. Optional task-only unavailable-folder handling should preserve records and report unavailability; do not disrupt user mounts or cloud files.
6. Exit normally. Confirm only this isolated build/profile was used.

## Gates and limits

Windows automated disposition is BLOCKED by StartupRestore_NativeVideoSurvivesTaskbarReveal(seconds: 1): No presented video frame is available at StartupVideoPresentationTests.cs:55 / FlyleafPlaybackBackend.cs:457. This reproduced with dependencies present; causality to #465 is unproven and no fix or weakened assertion was attempted. Owner Windows visual/functional acceptance and explicit final merge authorization are still required. PR #467 remains Draft/unmerged; issue #465 stays open. No merge, closure, release, tag, production fix or unrelated roadmap mutation was performed. Local qualification records are not yet published. No new Chronicle entry is warranted for this bounded qualification.



Focused reproduction command (provided, not additionally run):

```powershell
Set-Location "C:\Git\Agents\LF-WIN-TST-031"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -NoBuild -NoRestore -Filter "FullyQualifiedName~StartupRestore_NativeVideoSurvivesTaskbarReveal" -ResultsName LF-WIN-TST-031-startup-repro.trx
```

The dependency-complete full run is the reproducible failed evidence; the same seconds:1 failure also occurred in the interrupted initial run. Attribution to the Mac Locations change versus baseline/environment is unresolved. Stop here for owner disposition; no additional investigation, production fix, PR readiness claim or merge authorization is implied.

## Diagnostic continuation and owner acceptance — 2026-11-03

Jeremy explicitly accepted all six Windows hands-on checks listed above. The earlier pending checklist and unused-profile statement describe the original handoff, not current owner acceptance. Preserve the original failed full-suite evidence.

Three isolated startup-test rounds on accepted 8643942 and three on verified pre-#465 merge base 1ea4da3 all passed (both seconds:1 and seconds:0, 6/6 per source). Diagnostic disposition D — Inconclusive: historical full-suite failure remains; no baseline failure or accepted-source regression was established, and no clean qualification follows solely from reruns. See [diagnostic report](mac-locations-465-startup-diagnostics-031.md). Windows hands-on is accepted; final qualification disposition and explicit merge authorization remain for owner review. No source/test changes, full-suite rerun, new package or merge.


## Documentation publication and roadmap reconciliation — 2026-11-03

Separate full documentation clone: `C:\Git\Agents\LF-WIN-TST-031-Documentation`; branch `codex/031-qualification-evidence`, based on main `1ea4da350371c277eb95c6208310a5d355cc5085`. Only this registry and the two qualification reports are proposed; no implementation commits or test outputs are included. Earlier statements about local-only records describe historical checkpoints; raw evidence remains local after report publication.

GitHub CLI access verified #465 was absent from Roadmap Project #4; it was added with existing Status **Review**, Area **Browser**, Priority unset. Issue stays open; implementation is not Done. Native parent is null and blocked-by/blocking lists are empty, now verified accessible rather than assumed inaccessible. These relationships are preserved: #395 is the related R4 Epic and #366 the broader migration Epic, but this assignment does not infer a native parent or new dependency. Parentage remains an owner clarification item before final tracking acceptance. Related #395 remains open/incomplete with its existing DoD unchanged. PR #467 remains Draft/open/unmerged at accepted SHA/tree. Final implementation review and explicit merge authorization remain separate gates. Documentation PR itself also stays unmerged without separate authorization.
