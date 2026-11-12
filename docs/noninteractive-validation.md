# Noninteractive Windows validation on release/1.0

## Required commands

Run Release tests and packaging from this task checkout:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -Filter "FullyQualifiedName~BrowserFolderContextSafetyTests" -ResultsName context.trx
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller
```

These scripts create a unique `LightflowValidation-<GUID>` Windows desktop and
assign the child process before CLR/STA/WPF initialization. They never switch to
that desktop. Descendant WPF/native/smoke processes inherit it. Failure to create
the desktop/job fails closed; there is no visible-desktop fallback. Creating the
boundary requires desktop/job permissions outside Codex's restricted sandbox.

The wrapper verifies separation from the input desktop, uses a kill-on-close job,
retains logs under `artifacts/validation/<run>/output.log` and fails on a nonzero
exit, timeout or surviving owned descendants. Build servers are disabled to avoid
persistent descendants. It never terminates unrelated/user-started processes.
TRX evidence is under `LightflowStudio.Tests/TestResults`.

Task-local TEMP/TMP is assigned to the sibling
`LF-WIN-DEV-046-FolderContextSafety.validation/temp/<run>` directory so fixtures
requiring a repository-less root keep their existing semantics. All test data
remains assignment-owned. Package smoke passes its own disposable `--data-root`
under this clone's `artifacts/release`, and removes it after shutdown.

## Maintenance-line integration

The v1.0.2 source predates this boundary. Assignment 046 imports the unchanged
`ValidationDesktop.cs`, `NoninteractiveValidation.ps1`, `Test-Noninteractive.ps1`
and `ValidationPresentation.cs` from current main
`c6685baae8707fd3ef14c5bb67ee649d309de272`, and adds the wrapper entry to the
maintenance Build-Release script. This is a narrow validation-tooling integration,
not a merge of main into release/1.0. Existing maintenance application and test
hosts are not broadly refactored to add main's later host-level guards. Always
use the required entry points above; do not launch desktop-sensitive tests directly.

The imported ValidationPresentation helper is compiled by both Add-Type and the
application, but this correction adds no ordinary application startup behavior.
Explicit owner acceptance launches remain interactive and require task-owned
`--data-root` storage.

## Clipboard test boundary

Private-desktop OleFlushClipboard persistence failed with CLIPBRD_E_CANT_OPEN.
The new live context tests inject only the folder clipboard writer, retain real
WPF file-drop data and process-lifetime clipboard ownership (`copy: false`), and
verify the custom operation payload directly before OLE serialization. The test
host disables BinaryFormatter whereas the packaged WPF app enables it by default;
no runtime switches are changed by these tests. Production retains its original
persistent SetDataObject(data, true), formats and Preferred DropEffect semantics.
Cross-process clipboard persistence/Explorer interoperability remains an explicit
owner packaged acceptance check, not an automated PASS claim.

## Final acceptance and CI integration — assignment 046

Owner accepted the packaged correction 17/17 PASS, zero failures/observations,
at `bf90cfa1f90c2ce69e80d951bcebbc49326958e2`. This supersedes the earlier
pending-owner clipboard gate above; it does not relabel private-desktop OLE limits
as an automated pass. Accepted focused/full Release evidence remains applicable
for unchanged production and context regression inputs. A source-contract test
for CI routing is updated as described below.

The maintenance CI Test step calls `scripts/Test-Noninteractive.ps1 -NoRestore`
under PowerShell 7 instead of direct `dotnet test`. Restore, FFmpeg preparation,
test selection, results upload, installer/package and tag-only release gates remain
unchanged. Focused PowerShell 7 qualification passed 13/13 with no owned processes
remaining. Exact-head CI runs the full suite and package gate. No unrelated main
or platform workflow is imported.

The first exact-head CI at `0393b273` reported 2,502 PASS / 1 FAIL / 1 existing
skip: ReleaseWorkflow_GatesPackagingOnTests expected the literal `dotnet test`
in YAML. The gate assertion now follows the supported launcher, verifies private
desktop enforcement and the underlying full Release project/dotnet invocation,
while retaining packaging dependency and tag/checksum assertions. This is a
validation-contract adaptation, not a production correction or relaxed assertion.
The failed attempt remains evidence and is not bypassed; full exact-head CI must
pass on the corrected final head before qualification can complete.

Corrected workflow-contract plus context qualification under PowerShell 7: **26/26 PASS**,
zero failed/skipped tests and no owned processes remaining. Final full exact-head CI
is still required; the original failed attempt is retained.
