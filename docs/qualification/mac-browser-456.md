# LF-MAC-DEV-026 — Finder-Style Browser Navigation

Issue #456 is authoritative; native parent #366, accepted prerequisite #454 / PR #455.
Jeremy explicitly accepted uncataloged folder navigation only, existing Catalog assets where
mapped, and no uncataloged file asset list or ingestion action before implementation. The
owner decision is recorded on #456. R2 #393, R3 #394 and R4 #395 remain incomplete.

## Architecture and compatibility

`IBrowserLocationProvider` supplies filesystem locations and directory observations, separate
from persistent Catalog identities. `BrowserLocationMapping` selects the most-specific existing
mapping without creating an anchor. `BrowserViewModel` composes those observations with the
accepted `IBrowserFolderSession`/`BrowserCatalogReader` and AssetId selection primitive. A
filesystem path without a mapping produces an explicit not-cataloged state, never an asset scan.

`MacBrowserLocationProvider` discovers Home, Desktop, Documents, Downloads and actual startup/
visible mounted volumes, including already-mounted authorized network shares. It does not
mount shares or discover credentials. Native effects reuse `ContainedMediaFolderFileSystem`
with a directory-only option; the default Windows enumeration path remains unchanged. Link
checks remain conservative. Filesystem browsing grants no active Catalog storage eligibility.
No Root, mapping, source observation, authored record, schema/provider, lifecycle or writer is
created or changed by navigation. Existing APFS admission, recovery, Move/publication refusal
and Windows WPF discovery/reconciliation are retained.

Protected standard folders, mounted volumes and unvisited filesystem branches wait for an
explicit navigation/disclosure request before enumeration. This avoids speculative permission
requests. Successfully visited branches can subsequently refresh. Two speculative slots and
two explicit-demand slots bound native directory reads, so speculative metadata stalls do not
consume foreground capacity. A presentation timeout cannot forcibly interrupt a blocked OS
metadata call; such failures remain retryable and do not authorize permission bypass.

Refresh reuses row instances and expanded flags, current filesystem/logical location, valid
AssetId selection and practical ScrollViewer offsets. Duplicate visual appearances of a folder
have separate controls and the same location identity. Known leaves have no disclosure/filler
text; uninspected locations retain a disclosure until their first observation. Folder, selection,
Catalog epoch, refresh and row generations reject superseded results. Removed/unavailable
folders retain identity and explanatory guidance; unrelated branches remain intact. No workspace
continuation across restart is added.

## Local evidence

Focused validation preceded broader checks. Retained logs are in `work/lf026-*.log`; TRX files
are in each test project's `TestResults`. Task-owned fixtures are under `artifacts/lf026-fixtures`.

- Shared Application: 137 passed, including most-specific mapping/boundary cases.
- Shared presentation: 19 passed, including asynchronous disclosure, alias controls, permission-
  sensitive demand, valid/removed selection retention and superseded Refresh/navigation.
- Native Mac: 35 passed; focused real-Catalog/location cases were repeated after native effect
  refinements. These cover missing/denied/linked folders, empty/recursive scopes, stable IDs,
  cancellation, source absence and unchanged logical contents of all Catalog tables on reopen.
- Native visual checks: real Locations, mapped hierarchy, Empty/Day 2 leaves, current folder,
  selected forest AssetId, expanded branches and scrolled sidebar retained across Refresh;
  Home shows the explicit uncataloged state. Normal Quit and Command-Q returned exit 0.

Historical failures are retained, not relabeled: sandbox MSBuild IPC refusal; initial duplicate
visual-control alias regression; FileNotFoundException status mismatch; native speculative
Desktop/Documents slot starvation. Each prompted a bounded correction and focused validation.
Temporary scheduling trace source was removed; raw trace logs remain task-owned. Screenshots
are in `artifacts/lf026-evidence`. No external-drive fault or live network-share acceptance is
claimed. Required exact-head CI and Windows local package qualification remain separate gates.

## Exact runnable Mac handoff

Workspace: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-026-Finder-Navigation`.
Branch: `codex/026-finder-browser-navigation`.
Base: `4e1f361ab02b1e72ed729a59b439478ca916dc7b`.
The PR records candidate head; `artifacts/mac-shell/provenance.json` records rebuilt head,
branch, dirty flag and every bundle file's SHA-256. Final handoff independently confirms it.
Unsigned self-contained Apple Silicon development bundle, with packaged native dependencies
and third-party notices; no release, signing or notarization claim.

Build from this clone:

```sh
DOTNET_COMMAND="$PWD/work/toolchain/dotnet/dotnet" scripts/Build-MacShell.sh
```

Ready-to-copy macOS launch:

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-026-Finder-Navigation/artifacts/mac-shell/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-026-Finder-Navigation/artifacts/lf026-fixtures/lf025-browser-cc86183f6a0d4216a526c28c4bf087ab"
```

Equivalent PowerShell startup (on this Mac with PowerShell installed):

```powershell
& "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-026-Finder-Navigation/artifacts/mac-shell/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-026-Finder-Navigation/artifacts/lf026-fixtures/lf025-browser-cc86183f6a0d4216a526c28c4bf087ab"
```

The data root is newly created in assignment 026 by the production-lifecycle integration test;
its retained `lf025-browser-*` label identifies the reused test harness, not another workspace.
It contains Catalog `597c309c-91fe-42f2-9c66-dbcabdf0a27b`, Field Notes root
`6ba2ae26-2770-4749-b456-605991558c5d`, three real disposable PNGs, an intentionally absent
fourth asset, an offline second location, and a preserved authored Collection. Root/asset IDs
and fixture details are in its `LF025-fixture.txt`. No personal media or normal profile is used.

Owner scenarios:

1. Inspect real Home/standard folders/visible mounted volumes. Navigate Home: explicit
   not-cataloged guidance, no file asset list or ingestion action. Inspect permissions on
   a standard folder only if desired; denial/unavailability should explain retry/reconnect.
2. Expand Field Notes. Empty has no disclosure and no explanatory leaf filler. Trip expands
   to Day 2; Day 2 is also a leaf. Select Day 2 and forest.png.
3. Scroll the Locations pane, then Refresh. Field Notes/Trip remain expanded, Day 2 and
   forest.png remain selected, and sidebar scroll remains at the previous practical offset.
4. Select Trip/missing.png: retain Catalog identity and source-unavailable guidance. Select
   Disconnected media: explain unavailability without deletion/remapping. Refresh/retry.
5. Change folders rapidly while refreshing; older work must not overwrite the newer folder.
6. Quit using Command-Q or Quit; relaunch the exact command and verify the same Catalog and
   authored records. Transient navigation/expansion is not a restart-persistence feature.

Jeremy's architecture, functional and visual acceptance is pending. Mandatory Windows local
qualification must use the same PR source in its own full clone and run
`scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller` through the repository's private-
desktop workflow, supplying its actual absolute executable/data-root, provenance, smoke and
dependency evidence. This Mac does not claim executing that gate; hosted Windows packaging
is additional evidence. Draft only, no merge/release or R2/R3/R4/Epic completion.

Chronicle assessment: this bounded follow-up does not warrant a separate historical entry;
its evidence can support a later account of the shared Browser milestone after acceptance.
