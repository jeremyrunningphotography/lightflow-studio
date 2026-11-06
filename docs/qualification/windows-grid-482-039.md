# LF-WIN-TST-039 — Mac Grid Windows Qualification

2026-11-06, DARKMATTER, America/Los_Angeles. Conversation name retained exactly.

## Disposition

**Engineering qualification: INCONCLUSIVE.** Source transfer/integrity PASS; Windows Browser/Grid and Preview regression coverage PASS; local Windows package and its isolated Catalog/dependency/startup/shutdown checks PASS. The provisioned full Release suite has **2,879 passed, 1 failed, 2 skipped**. The single Catalog restore admission failure did not reproduce in accepted-source focused validation or either bounded baseline comparison. Its cause remains unresolved. No production regression is confirmed, no correction is claimed, and the original failure is not waived by later passes. Owner disposition or further explicitly scoped investigation is required before treating Windows qualification as complete. Owner hands-on acceptance and final implementation PR/CI/merge gates remain outstanding.

No PR, merge, issue closure, release/tag, #483 work, other agent, production edit, rebase, cherry-pick or amendment occurred. No Mac acceptance cycle was repeated.

## Exact source and transfer

- Accepted commit: `e941fabefde207878d88690000a624bab8853cae`.
- Accepted tree: `ef16701a7522a215962657716d047251128a6f9b`.
- Parent: `8b3cb49df858daa0966b333e46e0c337f43a5e1f`.
- Baseline/merge-base and independently read remote main: `1b37d62fe1bb153e6988661ce65453648d937551`.
- Task clone: `C:\Git\Agents\LF-WIN-TST-039-MacGridWindowsQualification`.
- Task branch: `codex/039-mac-grid-windows-qualification`, pointing at the accepted commit throughout tests and packaging.
- Owner transfer located at `C:\Users\jerem\Downloads\LF-MAC-DEV-038-Accepted-Source-Windows-Transfer.zip`; actual file existence inspected before extraction. ZIP hash is in transfer-zip-hash.txt.
- Bundle SHA256: `e9caddd10e8778c9a6381a364c56d3a9515a995190d601ada67b8b860fcf28b8`.
- PROVENANCE.md SHA256: `1a291f5cf15736b7f79e5c0d38b653b86c35206586b410a50c817a283f37ce89`.
- Both manifest entries verified; Git bundle verify PASS, complete history/zero prerequisites. Imported originating ref without rewriting history into `refs/remotes/transfer/lf038-accepted`; task branch then checked out that exact commit. Git commit/tree/parent independently match the owner-supplied identifiers. Full object integrity (`git fsck --full --no-reflogs`) and diff whitespace check PASS.
- Reconciled AGENTS.md blob: `bebb9b207d9031aa80fa68dcc6358efdf5673a10`, exactly matching transfer provenance; root instructions read completely.
- Originating Mac workspace/branch and clean tracked/staged state are recorded in the included PROVENANCE.md. Mac's pre-existing untracked .DS_Store is excluded, not modified.
- Windows tracked source was clean during validation/package. Tests generated untracked `.task-notes/` synthetic fixtures/screenshots; retained locally and excluded from this evidence ZIP. Qualification-only documentation/registry changes were prepared after packaging and remain uncommitted; they do not alter accepted production source or its identity.

The original unavailable-source gate report is included as historical evidence, superseded by this verified transfer. Complete accepted-source bundle is included for portable re-verification.

## Assignment and repository orientation

039 retains the original allocation: fetched global registries reached 037; owner kickoff and #482 reserved 038; remote/local reservation checks found no 039 allocation. This is a full independent clone, never a worktree or the canonical checkout. Baseline diagnostics use a separate full task-owned clone at `C:\Git\Agents\LF-WIN-TST-039-Baseline`, branch `codex/039-baseline-diagnostics`, exact pre-#482 baseline. Baseline outputs were not used to qualify/package accepted source.

Read-only GitHub evidence confirms #482 Open / In Progress / Browser / priority unset, native parent #480; #481 Closed / Done / Browser / priority unset; #480 Open / Backlog / Browser / priority unset, native children #481–#485, only #481 closed. Epic DoD remains incomplete. Historical Epic prose calling #482 unstarted predates its current active issue record; no speculative corrections or Project mutations were made. Issue/Project/native snapshots are included. The original requested Windows reference screenshot remains unavailable; no pixel comparison or independent owner GUI PASS is invented.

## Source-diff review

See SOURCE-REVIEW.md, accepted-source.diff and changed-files.txt. All 16 changed files were reviewed against the baseline. Application changes are confined to the Avalonia UI project: card geometry/control, virtualized row projection, BrowserPane composition/viewport mapping, selection/refresh projection and scoped styles, plus UI tests. The remaining changes are policy/registry/history/qualification documentation.

The shipping Windows WPF project does not reference Lightflow.UI. Windows application/tests, shared Application/Domain/Actions/Catalog, dependencies/locks, project/build configuration and validation scripts are unchanged. No Preview cache schema, Catalog schema, migration, ingestion/decoder engine or Windows resources/selection behavior changed. Mac cards consume existing shared identity, selection and thumbnail-demand contracts. Existing bounds on visible-first demand remain. No platform dependency leaks into the Windows executable graph. Static non-reachability supplements runtime validation; it does not replace owner hands-on checks.

## Automated validation

All WPF/native runs used the repository's real private-desktop boundary. It was never switched into view or bypassed. Neutral UI uses Avalonia headless tests per CI. Native tests use real processes/rendering, not substituted mocks. All TRX and validation logs are included.

| Run | Passed | Failed | Skipped | Disposition |
| --- | ---: | ---: | ---: | --- |
| Initial full Release | 2764 | 116 | 2 | Invalid prerequisites/desktop environment; retained |
| Provisioned retry before desktop restoration | 0 | 0 | 0 | Runner recursively relaunched before VSTest; aborted/retained |
| Provisioned full Release after desktop restoration | 2879 | 1 | 2 | One unresolved Catalog restore failure |
| Accepted-source focused Windows | 606 | 0 | 0 | PASS, includes failed restore test |
| Shared Application Release | 141 | 0 | 0 | PASS |
| Shared Catalog Release | 67 | 0 | 0 | PASS |
| Shared Avalonia UI Release | 102 | 0 | 0 | PASS, including Grid/I02 tests |
| Premiere companion Node tests | 90 | 0 | 0 | PASS, not installed Premiere acceptance |
| Baseline ClosedCatalogTransferIntegrationTests | 14 | 0 | 0 | Failure not reproduced |
| Baseline Catalog filter | 406 | 0 | 1 | Failure not reproduced |

Counts represent separate runs and overlap; do not sum them as unique coverage. Full-run test-name scope extraction: Browser 864 passed/0 failed; Preview/Thumbnail/Derived 274 passed/0 failed; Catalog 421 passed/1 failed/1 skipped; native/playback/process name group 85 passed/0 failed. Groups overlap. Detailed counters and actual test names are in test-summary.json/TRX.

Full-suite skips: `CatalogClosedAdmissionTests.NativeFileSymlinkLeaves_RefuseWithoutTargetWrites` (environment capability) and `PremiereLiveAcceptanceTests.InstalledCompanion_ProductionBridgeAcceptance` (external acceptance opt-in). No new skips, assertion relaxation or filtering of the full suite.

### Commands

Run from the accepted-source task clone unless stated otherwise:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -ResultsName lf039-full-release.trx
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Get-Ffmpeg.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Get-PlaybackDependencies.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -NoBuild -NoRestore -ResultsName lf039-full-provisioned.trx
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -NoBuild -NoRestore -ResultsName lf039-full-unlocked.trx
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -NoBuild -NoRestore -Filter 'FullyQualifiedName~BrowserGrid|FullyQualifiedName~BrowserDetails|FullyQualifiedName~BrowserNavigation|FullyQualifiedName~Preview|FullyQualifiedName~Thumbnail|FullyQualifiedName~Derived|FullyQualifiedName~Selection|FullyQualifiedName~ClosedCatalogTransferIntegrationTests|FullyQualifiedName~NoninteractiveValidation' -ResultsName lf039-focused.trx
dotnet test Lightflow.Application.Tests/Lightflow.Application.Tests.csproj -c Release --disable-build-servers -p:RestoreLockedMode=true --logger 'trx;LogFileName=lf039-neutral.trx'
dotnet test Lightflow.Catalog.Tests/Lightflow.Catalog.Tests.csproj -c Release --disable-build-servers -p:RestoreLockedMode=true --logger 'trx;LogFileName=lf039-neutral.trx'
dotnet test Lightflow.UI.Tests/Lightflow.UI.Tests.csproj -c Release --disable-build-servers -p:RestoreLockedMode=true --logger 'trx;LogFileName=lf039-neutral.trx'
node --test PremiereCompanion/*.test.cjs
```

From the separate baseline clone:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -Filter 'FullyQualifiedName~ClosedCatalogTransferIntegrationTests' -ResultsName lf039-baseline-restore.trx
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -NoBuild -NoRestore -Filter 'FullyQualifiedName~Catalog' -ResultsName lf039-baseline-catalog.trx
```

### Failure and retry record

The first sandbox attempt could not create a private desktop. Authorized sandbox escalation enabled the prescribed runner; no alternate desktop mechanism was substituted. The initial full run was mistakenly started before fresh-clone FFmpeg provisioning. Its 116 failures comprise 60 input-desktop access denials, 10 null playback-path failures, and 46 missing-FFmpeg/dependency failures. All messages/stacks are preserved in initial-failures.json and original TRX/log. Both pinned native archives were then independently downloaded, SHA256 verified and extracted by repository scripts into task-owned directories.

A provisioned retry recursively relaunched the private-desktop wrapper before tests. The specifically identified task-owned launcher PID 75288 was terminated; closing its kill-on-close job cleaned descendants. No user-started process was terminated. Its 86 nonempty recursive-launch logs are retained. A direct read-only OpenInputDesktop probe independently returned error 5. Owner subsequently reported returning DARKMATTER to the normal desktop; the same probe returned a valid handle/error 0. The next runner worked without any source/security/check changes. No lock screen was bypassed. These observations support desktop-state infrastructure attribution, not a production fix.

The valid full run's only failure is `ClosedCatalogTransferIntegrationTests.BackupCopybackRestoresThroughExistingRecoveryOwner`: restore activation refused with “Storage locality cannot be established”; CreateFileW/MetadataNoFollow returned Win32 5 on a task-owned Catalog leaf. The exact test later passed in accepted-source focused validation; baseline transfer 14/14 and broader Catalog 406/0/1 also passed. Windows storage/fixture/runner source is identical to baseline. This does **not** establish the historical failure reproduced on baseline, identify a unique cause, or prove a correction. Classification: unresolved intermittent qualification failure, no confirmed #482 regression. No full-suite clean PASS is claimed and no further green-seeking retries were performed.

## Windows package, dependency and Catalog safety

Required command executed successfully (49.7 seconds) on a private desktop:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller
```

Exact executable: `C:\Git\Agents\LF-WIN-TST-039-MacGridWindowsQualification\artifacts\release\LightflowStudio\LightflowStudio.exe`.

- SHA256: `8e725c89d9707089fb77fa3a0b601357b6e6ff42bacb9472a9ef6e9a60865ee5`.
- Executable timestamp UTC: `2026-11-06T20:21:06.8792691Z`, newer than accepted commit (`2026-11-06T07:34:59-08:00`).
- 28 staged files, 558,942,066 bytes; every file hashed in package-manifest.json. Package/source record is in package-provenance.json.
- Approved executable icon: byte-for-byte PASS.
- Packaged SQLite 3.53.3 / coherent SQLitePCLRaw 2.1.13 win-x64 payload verified against publish lock and runtime hash evidence. Native SQLite SHA256 `b7385d722c83fb52142a00477a726723745916d22a555711ee89834c1111fb2e`.
- Pinned encoding FFmpeg archive SHA256 `089e4169e93b2b3f3acbfced3c0704d24276a225641bdda04d796d28b07a2a38`; playback shared archive SHA256 `138fe84aa6e4836920f615704115d7ecf4cfbfc20f9a3d5c60f27c1e604fc12b`. Repository scripts verified hashes and staged fresh contents; package content/native dependency checks PASS. Premiere companion, licenses and notices included.
- Real packaged Browser startup, splash-to-workspace readiness and Jobs workspace activation PASS; exact readiness receipt `presentation-ready; splash-closed`. Empty isolated profile initialized Catalog, Preview DB, settings and activity log without inheriting media/LUT preferences.
- Graceful window close/shutdown PASS. Packaged backup, restore protection and long-path/shutdown boundary checks PASS; detailed JSONL retained. This separate smoke PASS does not erase the full-suite restore failure.
- Disposable smoke roots were created inside task release staging and removed by the prescribed build. No task-owned LightflowStudio process remained on independent post-build inspection; launcher also confirmed no owned descendants. See process-cleanup.json.
- PullRequest mode validated portable staging. Release portable archive intentionally skipped; installer intentionally skipped by the required command. No installer binary, signing, release publication or installer execution is claimed. Final implementation CI must run its prescribed full installer job.

Catalog/Preview assessment: no schema/migration source change; shared Catalog suite, focused Preview/cache/identity/lifecycle tests and packaged isolated initialization/runtime checks pass. No normal user Catalog/profile was launched, copied, repaired or migrated. No owner media was modified. Safe refusal in the failed disposable restore test is retained as an unresolved qualification exception. Therefore package-safety checks PASS, while broad Catalog engineering qualification remains INCONCLUSIVE pending failure disposition.

## Windows regression and owner acceptance

Automated/static Browser/Grid regression: PASS within tested coverage. Existing WPF Grid Preview/Info/Hybrid, Details/sizing/persistence, selection/focus/navigation, filters/sort, metadata/status, caching and Preview source behavior are unchanged and covered by full/focused Browser tests. Native rendering/process integration and source-integrity tests passed. This does not assert every user GUI scenario has been observed independently.

Owner GUI acceptance: **NOT RUN / OUTSTANDING**. See WINDOWS-ACCEPTANCE.md for six numbered checks and both ready-to-copy persistent/fresh launch commands. No visible desktop automation or independent Mac GUI testing was performed.

Primary command:

```powershell
& "C:\Git\Agents\LF-WIN-TST-039-MacGridWindowsQualification\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-TST-039-MacGridWindowsQualification\acceptance\persistent"
```

Independent fresh-start command:

```powershell
& "C:\Git\Agents\LF-WIN-TST-039-MacGridWindowsQualification\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-TST-039-MacGridWindowsQualification\acceptance\fresh"
```

Both roots are task-owned, newly scoped to #482, initially empty, and separate from test/migration/package smoke roots. Use supported initialization, never select the normal owner Catalog. Expected app-created Catalog path is `<data-root>\Catalog\LightflowCatalog.db`; no acceptance Catalog ID exists yet. Retain acceptance data after closeout. Private media, live Catalogs and Preview caches are excluded from this ZIP.

## Handoff / outstanding gates

1. Owner disposition or further scoped investigation of the unresolved Catalog restore full-suite failure. Engineering qualification remains INCONCLUSIVE until addressed; no #482 regression or fix is asserted.
2. Owner performs and records the six Windows hands-on checks on this exact fresh package.
3. LF-MAC-DEV-038 integrates qualification report/evidence references and 039 registry record into the single #482 implementation PR when all acceptance/qualification gates are satisfied. Included qualification.patch contains documentation/registry changes only; accepted production source must remain intact. No separate qualification PR.
4. Final exact-head full CI (including Windows installer staging), review, explicit merge authorization and required post-merge/Project closeout remain implementation-owner responsibilities. #482 and #480 are not Done/closed here.

Historical maintenance: **Not warranted** for bounded qualification, environment failures or registry administration. Accepted Mac milestone/history entries are preserved without modification. No release-line backport is warranted for this ordinary trunk presentation feature.
