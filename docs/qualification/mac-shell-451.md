# LF-MAC-DEV-024 — bounded Mac application shell

Issue [#451](https://github.com/jeremyrunningphotography/lightflow-studio/issues/451),
native parent #366; related R4 #395. Owner authorized this early shell without
completing or waiving R2 #393 / R3 #394. No merge or release authorization.

## Ownership and supported boundary

- `Lightflow.Application/ShellCatalog.cs` defines a transient projection of existing
  startup status/recovery reasons and an application-facing request port. It owns
  no persistence, lifecycle, schema, session, recovery, actions or authored state.
- `Lightflow.UI` contains one shared Avalonia window/view model. It has no reference
  to Catalog/SQLite or Mac filesystem APIs. Folder selection uses public Avalonia
  StorageProvider; views request operations, never create folders or databases.
- `MacShellCatalogSession` composes the accepted `CatalogLifecycle` and
  `MacCatalogLifecycleEffects`. It serializes presentation requests/disposal, holds
  only the returned lifecycle, and delegates preflight, create/open, activation,
  configuration, readiness checks and shutdown to that owner. It is not another
  Catalog manager or writer. Failed selection retires the existing writer and
  retains prior configuration; Retry reopens the prior Catalog.
- `Lightflow.Desktop.MacOS` is the native application composition root. Avalonia's
  classic desktop lifetime owns the process. Native Quit, window close and the
  Quit button converge on one awaited shutdown task, with no second disposal path.
- Existing Windows WPF startup, selection, Catalog Settings, paired completion,
  semantic Actions and shipping presentation remain unchanged. No WPF implementation
  is copied or retired. Browser/Player/Inspector/Settings/Jobs remain upcoming text,
  without fake data or inactive navigation controls.

Supported: explicit isolated launch; prepared-empty-folder creation; shared
existing-Catalog identity preflight and safe activation; readiness gate; Catalog
context; normal quit/close and deep reopen. The native host restricts selected
Catalogs to the explicit data root on admitted local case-insensitive APFS. This is
an intentional bounded capability, not a general normal-profile storage policy.

Unavailable: missing-folder creation transitions, Move/publication, transfer/import,
Agent 023 experimental descriptor routing, Preview peer/clean-profile completion,
media browsing/playback and the later surfaces. No schema/provider/VFS/WAL/durability
change. No active ExFAT/SMB/NAS, Windows return/round trip, physical fault, signed or
notarized distribution, full Dock/VoiceOver/menu/sleep/multiple-display qualification.
Darwin advisory locking and point-in-time identity retain the accepted #425 limits.

The pinned shell toolchain is .NET 8 / Avalonia 11.3.8, using code-based views.
The local .NET SDK 8.0.425 builds with zero warnings/errors, without the research
12.1.3 analyzer/XAML-generator pairing concern. This choice does not establish the
future full-product support matrix or requalify G3/WQ's GPU paths. Dependency locks,
including separate arm64 publish locks, are committed. Brand images are linked
unchanged from the accepted Windows assets; surfaces reuse the design-system colors.

## Early integration checkpoint and local evidence

Before substantial UI styling, a real arm64 Avalonia window composed the native
host, displayed authoritative `FirstRunChoiceRequired`, and normal window close
drained/disposed the lifecycle and exited 0. Captured task-owned evidence:
`artifacts/lf024-orientation/checkpoint-bundle.log`. The earlier loose executable
was not discoverable by the desktop inspection tool; it was stopped and the bundle
checkpoint completed. No failure is silently counted as successful shutdown.

Focused Release validation on Apple Silicon macOS 26.6:

| Evidence | Result |
| --- | --- |
| Native storage/lifecycle plus eight shell-adapter integration tests | 32 passed |
| Existing shared Catalog suite | 44 passed |
| Existing neutral Application suite | 136 passed |
| Shared presentation/headless window suite | 6 passed |
| Existing independent-process Catalog host qualification | 23 checks passed |
| Self-contained arm64 development bundle | built; real GUI create/open/reopen/quit verified |

The native cases cover prepared creation, stable Catalog identity, authored
Collection persistence, conservative deep reopening, missing/invalid refusal,
occupied/linked/outside-root refusal, recovery choices blocked (including missing
creation targets), writer exclusion, repeated disposal and active readiness loss.
Existing native tests retain rich authored-table comparison, restore/rollback,
future-schema refusal, unqualified publication refusal and no fabricated Preview.
Existing storage-policy tests supply unsupported-filesystem refusal evidence; no
unsupported media was mounted or activated. Headless tests exercise the actual
workspace's visibility before/after readiness and loss, busy/closing admission,
reason-specific plain language and rejection of stale readiness completion after a
newer Catalog selection. Mock presentation tests are not native integration proof.

The unchanged CLI process harness retains its historical LF015 labels. It ran from
this clone/branch with new task-owned fixtures under `artifacts/lf024-process-fixtures`;
it tests independent writers, process interruption, logical reopening and refused
unqualified backup transport. It does not prove physical fault or full authored-field
coverage; the richer native suite covers those authored logical fixtures separately.

Real packaged interaction used only `artifacts/lf024-owner-acceptance`: initial
gate, public native folder picker, exact prepared folder confirmation, Catalog
creation, Ready workspace, Command-Q, relaunch with the same ID, Catalog navigation,
Open Existing of the same Catalog without a redundant confirmation, and Quit button.
The CLI authored `LF024 hands-on fixture` between GUI launches using existing stores.
Subsequent inspection verifies it remains present. Catalog ID:
`d9900b2e-999a-403a-a9ca-ebf93734e4c6`.

Original logs/TRX, screenshots and process evidence stay task-owned under
`artifacts/lf024-orientation`, project TestResults and `artifacts/lf024-process-fixtures`.
The checkpoint, failures and successful results are retained separately.

## Build and hands-on handoff

Independent full clone:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-024-MacShell`.
Branch: `codex/024-mac-application-shell`.
Fetched base: `71fd8708d65d5925940961d7be97fc888328a37f`.
The Draft PR records the exact review head; the freshly rebuilt bundle's
`artifacts/mac-shell/provenance.json` independently records head, branch, dirty flag
and all executable/native/package hashes. The final handoff supplies the exact SHA.

Exact build command (task-owned SDK):

```sh
cd /Users/jeremyrunning/Git/agents/LF-MAC-DEV-024-MacShell
DOTNET_COMMAND="$PWD/work/toolchain/dotnet/dotnet" scripts/Build-MacShell.sh
```

Self-contained app, no installed .NET runtime required:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-024-MacShell/artifacts/mac-shell/Lightflow Studio.app`.
Ready-to-copy startup command:

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-024-MacShell/artifacts/mac-shell/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-024-MacShell/artifacts/lf024-owner-acceptance"
```

Expected: the existing task-owned Catalog opens, displaying its location and the
same ID above, then Home. This fixture lives at
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-024-MacShell/artifacts/lf024-owner-acceptance/Catalog`.
Native unpaired profile remains Dirty, Catalog evidence Validated; no Preview
certificate or database is manufactured. Every reopen deeply validates.

For fresh first-run acceptance use this separate prepared fixture and command:

```sh
mkdir -p "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-024-MacShell/artifacts/lf024-fresh-acceptance/Catalog"
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-024-MacShell/artifacts/mac-shell/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-024-MacShell/artifacts/lf024-fresh-acceptance"
```

Manual scenarios:

1. Fresh command: see Welcome with no workspace. Choose Create New Catalog,
   select that prepared Catalog folder, review exact location, then Create Catalog.
2. Quit using Command-Q or the Quit button, relaunch the same command, verify the
   same Catalog ID/location and Ready Home.
3. Choose Catalog → Open Existing Catalog, select the Catalog folder itself.
   Confirm it returns to Ready without changing identity. Picker Cancel preserves
   the current session; failed selection retains configuration and gates Home.
4. Verify visual consistency, keyboard focus and resizing. Upcoming capabilities
   are labels, not working Browser/Player/Inspector/Settings/Jobs features.
5. For failure review use separate disposable fixtures/tests; never rename/remove
   a Catalog while an active app owns it. No physical drive faults are requested.

Normal double-click without explicit data-root arguments is intentionally refused;
use the complete command above. Signing/notarization and full Dock relaunch semantics
are later work. The bundle is a development artifact, not a release package.

## CI and acceptance distinctions

CI now builds/archives the Mac development bundle and tests shared presentation on
Windows/macOS alongside existing native/neutral checks. Existing Windows unit tests
and installer/portable packaging jobs are unchanged. Their exact-head results must
be recorded on the Draft PR; local passing tests do not attest CI.

The historical #450 post-merge Windows `next.json` locked-report failure remains
under #446. No ownership-process test/fixture, assertion, branch protection or retry
policy was changed. Preserve any failure, identify its exact run and distinguish it
from new regressions; no rerun solely to obtain green.

Implementation and focused local validation can be reviewed independently of CI.
Owner hands-on/technical acceptance, required CI and applicable Windows package
acceptance remain pending. This Mac cannot run the repository's local Windows
private-desktop packaging script; do not claim a refreshed local Windows executable
or skipped packaging as a pass. No full R4/R2/R3/Epic completion, merge or release.

Tracking: #451 native parent #366; Project #4 In Progress / Application Framework /
Priority unset at implementation. Wider milestone dependencies remain intact.
Potential nonblocking Chronicle candidate: first production-directed shared Avalonia
shell composes the accepted native Catalog lifecycle and has a real development
bundle, subject to owner acceptance. No historical document is rewritten here.
