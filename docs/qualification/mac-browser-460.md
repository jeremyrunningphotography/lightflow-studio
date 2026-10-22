# Mac Browser discovery — LF-MAC-DEV-027

Issue #460; bounded owner-approved extraction and Mac Browser slice. No merge or
release authorization. Owner authorization:
https://github.com/jeremyrunningphotography/lightflow-studio/issues/460#issuecomment-6346316550
Pre-edit inventory:
https://github.com/jeremyrunningphotography/lightflow-studio/issues/460#issuecomment-6351302827

## Architecture and Windows compatibility

The existing asset/fingerprint, reconciliation, recursive-root and recursive-walk
algorithms now live once in shared Catalog code, using existing repositories and
CatalogMutationLifecycle. Dependency-free service/result contracts live in
Application. Windows keeps the same names, constructor defaults, path semantics,
optional Preview scheduler and storage composition. Shared relative-path helpers
replace equivalent Windows forwarding methods; no algorithm was copied for Mac.
No schema/provider, new Catalog writer or lifecycle was introduced.

MacShellCatalogSession borrows its sole ready lifecycle session for each complete
request. Switching/close waits for that request; cancellation stops further work.
The native effects assess MediaSource paths before/after access, reject links and
case/Unicode ambiguity, and exclude owned storage. An explicit Browser-only
MediaSource assessment mode allows ancestor volume anchors to contain Catalog/cache
storage while refusing equal/inside protected objects. Default assessor behavior
and all ActiveCatalog policy remain unchanged. A dedicated native test proves
this distinction. Native volume anchors are Catalog media identity, not active
Catalog storage admission.

Only explicit folder navigation/Refresh invokes discovery. Expanding Locations
remains read-only. Direct scopes reconcile one visited folder; recursive scopes
reuse the existing per-folder pipeline, default 4 concurrent folders and 20,000
folder cap. Enumeration retains its default 2 concurrency bound. Catalog mutation
participation, stable path identity, partial commits, missing inference and optional
derived-work semantics remain those of Windows. Native host serialization also
prevents concurrent host requests from retiring or competing over its session.

| Behavior | Windows | This Mac slice |
| --- | --- | --- |
| Uncataloged accessible folders | Volume/share anchor; automatic Catalog reconciliation | Native mount anchor; same reconciliation owner |
| Supported formats | Existing extension registry, no promise of codec decoding | Same registry; unsupported files omitted |
| Same logical file path | Retain RootId/AssetId and authored state | Same service/repository; qualified with rating and Collection membership |
| External rename/move | New path identity; old record marked missing, not deleted | Same behavior; no content-based relink |
| Missing/unavailable/error | Missing only after authoritative successful scan; no destructive offline inference | Same rules and native refusal |
| Cancellation | Completed observations may persist; no unseen missing inference on interrupted scan | Same rules; latest UI generation wins; Cancel or navigate elsewhere |
| Include Subfolders | Stored roots with inheritance; bounded shared walk | Same root rules and walk; toggle supported |
| Locations/Refresh | Retain valid navigation/selection/expansion context | Existing retained tree rows/scroll plus stable selected AssetId |
| Controls | Back/Forward/Up, path/Go, Refresh, Include Subfolders | Corresponding controls, filename/type/size list, dark styling |
| Keyboard | Windows native/WPF routing | Native Mac text/focus/list behavior; Enter path, Command-L, Command-R, F5, Alt-Left/Right |
| Preview/thumbnails/Player | Separate optional derived/playback owners | Deliberately absent; no Preview DB/peer or clean certificate |
| Other controls | Advanced filter/edit/context/file operations | Deferred; single selection; no file mutation UI |

The Mac shell remains a bounded Avalonia presentation; Windows WPF is still the
shipping Windows UI. This does not claim complete Browser visual/interaction parity,
R2/R3/R4 completion, signing/notarization, external-drive fault qualification or
active ExFAT/SMB/NAS Catalog support. Already-mounted readable media locations and
eligible active Catalog locations remain separate policies.

## Local evidence

Task clone:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery`
Branch: `codex/027-mac-browser-discovery`.
Fetched base: `6d5aee56cb84786633d416bfce4b1d8c1e87ebbd`.

Release results before publication: 38 native adapter/lifecycle tests; 50 Catalog
and mutation/discovery tests; 20 headless presentation tests. All passed. Relevant
TRX files live in their test projects' TestResults as `lf027-native-regression.trx`,
`lf027-catalog-regression.trx`, and `lf027-presentation-final.trx`. Focused development
runs and original failures are preserved separately; they are not relabeled passing.
Two existing platform-analysis warnings in MacBrowserCatalogTests remain.

Coverage includes actual schema Catalog creation/discovery/commit/reopen, repeated
identity, changed sources, external rename/missing, authored rating and Collection
membership, absence of Preview storage, competing writer refusal, links/owned storage,
permission-sensitive navigation and existing recovery tests. Deterministic shared
checks exercise interruption after a committed observation, failed/offline scans,
ambiguous names, unsupported files, recursive truncation/descendant diagnostics and
cancellation. UI tests reject late discovery results and retain selection on Refresh;
existing tree expansion/permission-demand tests remain intact.

Preliminary real bundle smoke: created isolated prepared Catalog, navigated by path
to uncataloged Media, displayed only cover.png/quiet.wav, enabled recursive scope
and displayed four assets while retaining the selected cover.png and source availability;
Command-Q completed normal lifecycle drain/disposal. Final-head package provenance,
exact-head CI results and final smoke evidence are reported on the PR/issue, not
predeclared here. Windows private-desktop tests/package were not run on this Mac.

## Mac owner launch and scenarios

The self-contained unsigned development bundle is rebuilt with:

```sh
DOTNET_COMMAND="$PWD/work/toolchain/dotnet/dotnet" scripts/Build-MacShell.sh
```

Executable:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/mac-shell/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac`

Copyable terminal launch (self-contained; no SDK needed):

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/mac-shell/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/lf027-owner-acceptance"
```

PowerShell equivalent:

```powershell
& "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/mac-shell/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/lf027-owner-acceptance"
```

Use Command-L and enter:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/lf027-owner-acceptance/Media`

1. Inspect control sizes, spacing, focus, selection and dark styling against Windows.
   Navigate by disclosure rows and path/Enter; use Back/Forward/Up and keyboard commands.
   Protected Desktop/Documents locations should wait for explicit demand.
2. With Include Subfolders off, expect cover.png and quiet.wav. notes.txt never appears.
   Turn it on and expect sunrise.png and forest.png as well. Disable from a descendant
   to verify inherited-root semantics match Windows.
3. Select a file, Refresh repeatedly, expand/collapse branches, resize the window,
   and check valid selection, expanded rows and practical scroll context are retained.
4. Add a supported media fixture inside this task-owned Media tree; Refresh and verify
   automatic ingestion. Rename or remove it, Refresh, and inspect truthful missing-source
   behavior. Authored Catalog records must remain. Do not use owner files for mutations.
5. Navigate elsewhere/Cancel during a sufficiently large disposable scan. Later results
   must not replace the current folder. Denied, unavailable or linked paths must not
   silently delete records. Do not force-remove or fault-inject an owner drive.
6. Quit and relaunch this exact command, revisit Media, and verify Catalog/Root/Asset
   identities persist. No Preview/thumbnails/Player behavior is expected.

Bundle provenance is `artifacts/mac-shell/provenance.json`, including exact head,
clean/dirty state and file hashes. The final handoff must verify a clean exact-head
build newer than its source commit and normal smoke-process exit.

## Separate DARKMATTER qualification gate

Use an independent full Windows clone at:
`C:\Git\Agents\LF-WIN-DEV-027-WindowsQualification`.
Fetch this PR's exact head; preserve its SHA and tree. Under AGENTS.md run focused
Release Windows tests through Test-Noninteractive.ps1 covering MediaDiscovery,
CatalogReconciliation, MediaAsset, BrowserNavigation, BrowserRecursiveScope,
BrowserRecursiveRoot, DerivedWorkScheduling and mutation lifecycle. Do not weaken
existing failures, retry-to-green, or take ownership of #446.

Then run the required task-local package command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller
```

Record startup/dependency validation, normal shutdown and zero lingering smoke
processes; executable timestamp/hash and exact-head provenance. Owner interactive
launch from that clone:

```powershell
& "C:\Git\Agents\LF-WIN-DEV-027-WindowsQualification\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-DEV-027-WindowsQualification\artifacts\lf027-owner-acceptance"
```

No Windows package PASS is claimed until the actual report is supplied. Draft PR,
exact-head CI, independent Windows package and Jeremy's architecture/functional/visual
acceptance remain distinct gates. No merge, release or next milestone.

Chronicle assessment: this shared Browser milestone is a candidate after acceptance;
no historical narrative is rewritten during implementation.
