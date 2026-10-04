# Browser action validation (#351)

Starting and reverified remote main: `390544cdc7e198d899d0f1aeb44fce7491cae779` (accepted #350 / PR #356).
Independent clone: `C:\Git\Agents\agent-o-browser-actions-351`; branch `agent-o/351-browser-actions`.
Architecture and policies: [Browser actions](../BROWSER_ACTIONS.md).

## Deterministic evidence

The neutral dispatcher tests invoke Controller actions directly without keyboard events. Production-port integration
uses MainWindow, its shared selection model and a real isolated Catalog in all six Folder/static Collection/Smart
Collection × Grid/Details combinations. Coverage includes navigation, boundaries/current projection/Shift,
0–5 ratings, direct and clamped relative flags, every Color label and null, captured multi-selection, Catalog reopen,
Smart defining-query changes, concurrent field/keyword mutation, quiescence and replacement scope, revision guards,
dirty Inspector Cancel/Apply, selected/clicked Open review sets and pending Browser classification after Player Open.
Presentation-generation and invocation ownership tests prove an obsolete Open cannot block or release a fresh Open.
Existing keyboard ownership, Player actions, Details, Inspector, Smart Collections, #343 Delete and #344 Arrow
regressions remain gates. Tests do not depend on physical keyboard/mouse input.

## Release qualification

- Final expanded focused Release: **196 passed, zero failed/skipped** (`351-focused-qualified.trx`).
- Final full Release: **2,625 passed, zero failed, one expected installed-Companion opt-in skip**, 2,626 total (`351-full-release-qualified.trx`).
- Companion: **90 passed, zero failed/skipped** (`artifacts/351/companion-final.log`).
- Independent `Lightflow.Actions` net8.0 Release build: **zero warnings/errors**.
- The two startup rechecks passed in the expanded focus and the final full suite; no startup/native/tree implementation changes were needed.

Commands use `dotnet test LightflowStudio.Tests/LightflowStudio.Tests.csproj -c Release --no-restore` with focused
filters for BrowserAction/BrowserSemantic/BrowserPlayerViewerLiveInteraction/BrowserDeleteKey/PlayerAction/
PlayerSemantic/PlayerReview/InspectorDescription/SmartCollection/PlayerKeyboard/PlayerFocus/Arrow/
PlayerClassificationRow/BrowserDetails/CatalogClassification and the two startup rechecks. Full Release uses
`--no-build --no-restore`, TRX and `--blame-hang-timeout 5m --blame-hang-dump-type mini`.
Companion: `node --test PremiereCompanion/*.test.cjs`.
Neutral: `dotnet build Lightflow.Actions/Lightflow.Actions.csproj -c Release --no-restore --nologo -o artifacts/351/neutral`.
TRX evidence and diagnostics remain under `artifacts/351/tests` in this clone.

Earlier qualification exposed an obsolete source-string assertion, corrected to the fresh-value lambda, and an
Open/Back race. Open admission now belongs to the captured presentation target and invocation, and completion
checks its expected Player destination. A focused decoded-frame Arrow test initially observed a timestamp failure;
its isolated and later focused rechecks passed without changing decoding. A subsequent full run observed native
startup frame unavailability and a recursive-folder tree node not materialized; results of rechecks are included above.
No startup/native playback or tree policy changes are included. Existing xUnit analyzer warnings in
ApplicationIdentityTests are unrelated; the neutral build has no warnings.

## Packaged owner acceptance

After committing this source, run the repository-required command in this clone:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller
```

The Draft PR handoff records actual package/dependency/startup/shutdown results and executable freshness against
its commit. Package validation exercises the real isolated Browser/presentation/Jobs startup, Catalog runtime,
backup/restore/shutdown and pinned dependency checks. Installer generation is intentionally skipped in PullRequest mode.

Preserve package `C:\Git\Agents\agent-o-browser-actions-351\artifacts\release\LightflowStudio` and acceptance root
`C:\Git\Agents\agent-o-browser-actions-351\artifacts\351\acceptance-data` for owner testing:

```powershell
& "C:\Git\Agents\agent-o-browser-actions-351\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\agent-o-browser-actions-351\artifacts\351\acceptance-data"
```

Add test media in this isolated profile. Check previous/next and Grid/Details parity; ratings/flags/labels and
multi-selection in Folder/static/Smart scopes; derived Smart membership after classification; Inspector edit/Apply
and dirty navigation; search/text and tree/list/dropdown/menu ownership; #343 Delete intent (cancel destructive
confirmations when checking routing); Player shortcuts after Open. Architecture and owner hands-on acceptance
remain outstanding. Do not merge or clean up before explicit owner acceptance.
