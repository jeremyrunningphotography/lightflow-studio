# LF-MAC-DEV-028 DARKMATTER Windows qualification handoff

Status: PREPARED, NOT RUN ON DARKMATTER. Jeremy runs this independently; no remote control.
Final P4 staging manifest / CI record gives the exact published source SHA and tree.
Replace the explicit SOURCE_FROM_FINAL_MANIFEST token below with that SHA, never main.
Verify the exact head before testing; report any source difference and stop rather than
qualifying a moving branch. Accepted P1–3 local sources remain historical provenance.

## Workspace, source and isolation

Use a full independent clone, no worktree and no canonical checkout. Proposed exact
workspace: C:\Git\Agents\LF-MAC-DEV-028-BrowserParity-DARKMATTER.
No separate agent is assigned by these instructions; qualification continues028.
A future separate Windows agent must reserve a fresh available global allocation first.

```powershell
$qualificationSource = 'SOURCE_FROM_FINAL_MANIFEST'
$qualificationRepo = 'C:\Git\Agents\LF-MAC-DEV-028-BrowserParity-DARKMATTER'
if (!(Test-Path -LiteralPath $qualificationRepo)) {
    git clone https://github.com/jeremyrunningphotography/lightflow-studio.git $qualificationRepo
}
Set-Location -LiteralPath $qualificationRepo
if (git status --porcelain) { throw 'Preserve existing local changes before qualification.' }
git fetch origin main codex/028-mac-browser-ui-parity
if ($LASTEXITCODE -ne 0) { throw 'Fetch failed.' }
git switch --detach $qualificationSource
if ($LASTEXITCODE -ne 0) { throw 'Exact-source checkout failed.' }
if ((git rev-parse HEAD) -ne $qualificationSource) { throw 'Source mismatch.' }
git rev-parse HEAD 'HEAD^{tree}'
```

## Required validation

Shared changes are in Lightflow.UI and Mac host/packaging only; shipping WPF sources,
Application, Catalog, discovery and Windows behavior must be unchanged. Verify the PR
changed-file inventory, not just an apphost hash. No new Preview/Player/Details features.

Use scripts/Test-Noninteractive.ps1, never direct WPF tests or a visible desktop bypass.
The private desktop must not be switched into view. Keep using the machine normally.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -ResultsName lf028-p4-darkmatter.trx
if ($LASTEXITCODE -ne 0) { throw 'Windows Release tests failed.' }
dotnet test .\Lightflow.Application.Tests\Lightflow.Application.Tests.csproj -c Release --disable-build-servers -p:RestoreLockedMode=true --logger 'trx;LogFileName=lf028-p4-application.trx'
if ($LASTEXITCODE -ne 0) { throw 'Shared Application tests failed.' }
dotnet test .\Lightflow.Catalog.Tests\Lightflow.Catalog.Tests.csproj -c Release --disable-build-servers -p:RestoreLockedMode=true --logger 'trx;LogFileName=lf028-p4-catalog.trx'
if ($LASTEXITCODE -ne 0) { throw 'Shared Catalog tests failed.' }
dotnet test .\Lightflow.UI.Tests\Lightflow.UI.Tests.csproj -c Release --disable-build-servers -p:RestoreLockedMode=true --logger 'trx;LogFileName=lf028-p4-ui.trx'
if ($LASTEXITCODE -ne 0) { throw 'Shared headless UI tests failed.' }
```

Full WPF suite includes Browser navigation/selection/tree, shortcut ownership/actions,
status, native Player coexistence and private-desktop safety regressions. The UI suite
is synthetic Avalonia, not a Windows shipping shell replacement. Existing explicit
hardware/external Premiere limitations remain visible; no newly skipped tests.

## Installer, portable package and required local executable

Ensure the repository-required .NET SDK/Inno Setup and pinned FFmpeg prerequisites.
Use existing scripts; do not replace dependency hash verification or native smoke.
Full packaging validates installer and portable artifacts. The second exact policy
command refreshes the local executable Jeremy uses for hands-on acceptance.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest
if ($LASTEXITCODE -ne 0) { throw 'Installer/portable qualification failed.' }
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller
if ($LASTEXITCODE -ne 0) { throw 'Required local package qualification failed.' }
```

Record dependency/startup smoke results, source/head/tree and package manifest/hash.
Executable must be newer than source commit. Confirm owned smoke processes ended via
script checks; if locked, inspect leftover processes and obtain Jeremy's permission
before terminating any user-started process. Never run smoke on normal user storage.

## Exact hands-on paths and command

Executable:
C:\Git\Agents\LF-MAC-DEV-028-BrowserParity-DARKMATTER\artifacts\release\LightflowStudio\LightflowStudio.exe

Isolated root:
C:\Git\Agents\LF-MAC-DEV-028-BrowserParity-DARKMATTER\artifacts\lf028-p4-windows-acceptance

```powershell
& "C:\Git\Agents\LF-MAC-DEV-028-BrowserParity-DARKMATTER\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-MAC-DEV-028-BrowserParity-DARKMATTER\artifacts\lf028-p4-windows-acceptance"
```

Verify existing Windows shell/Browser/navigation/selection/focus/panes/footer/native
resize and quit; compare brand/density to Mac's foundation, not unmigrated features.
No Windows startup-size/view default changes are part of #462. Keep local owner visual
acceptance distinct from automated/private-desktop/CI results. Return exact SHA/tree,
TRX counts/skips, logs, package manifest/hash, executable timestamp/path, isolated root,
smoke/dependency/process outcomes and Jeremy's result. Do not merge/close/release/tag.
