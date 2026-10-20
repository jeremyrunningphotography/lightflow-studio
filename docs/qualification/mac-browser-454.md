# LF-MAC-DEV-025 — Catalog-backed Browser foundation

Issue #454, native parent #366, completed prerequisite #451; bounded early contribution
related to R4 #395. R2 #393, R3 #394 and R4 remain open/incomplete with their existing
native dependencies. No merge, parity, release, signing or notarization claim.

## Shared owners and bounded behavior

`CatalogMediaRootReader` extracts the existing List/Get machine-local mapping projection;
`CatalogMediaRootService` inherits it, retaining its existing mutation/admission implementation.
The existing `MachineIdentityProvider`, `BrowserGridSelection`, recursive membership rules,
media type registry and folder enumerator now live in Application. Windows compatibility
wrappers retain `WindowsMediaRootPathEffects`, constructor defaults, profile exclusions and
shipping discovery/reconciliation behavior. No Windows UI is replaced.

`BrowserCatalogReader` composes the existing folder enumeration, indexed asset scope repository
and stored recursive-root rules. It reads already-cataloged assets and filesystem child folders;
it does not ingest new files, reconcile observations, fingerprint/probe sources, filter/query
Smart Collections or schedule Preview work. This is deliberately a Catalog reader, not a
replacement for the complete Windows Browser pipeline. Empty scopes say **no Catalog assets**,
without claiming the filesystem contains no media. Asset order is the repository's logical path
order; broader query/filter/sort and Grid/Details migration remain deferred.

`MacShellCatalogSession` implements the application-facing Browser ports while borrowing its
sole existing lifecycle/session under the existing request gate. Complete reads finish before
Catalog selection/disposal can retire that session. Readiness is checked before and after each
borrow. There is no second writer, lifecycle, database, schema or SQLite provider change.

`MacBrowserMediaEffects` supplies MediaSource/Read assessment through the accepted native
assessor/shared policy, contained path effects and existence observations. Active Catalog
APFS admission remains distinct from media-location suitability. Existing conservative alias,
link, protected-root and unavailable-location refusals remain; no remapping or policy expansion.
Child observations are point-in-time, not hostile-namespace leases or physical fault qualification.

`Lightflow.UI` owns one transient view/view model. Single selection uses the extracted selection
primitive with AssetId keys; controls have no SQL/filesystem dependencies. Source availability
is checked on selection without changing stored observations or removing records. Each folder,
Catalog and selection request carries cancellation and generation/epoch checks. A 15-second
presentation demand timeout offers retry; it cannot forcibly interrupt a blocked OS metadata call.
Such an admitted read retains the lifecycle borrow until it actually returns, preserving safe
shutdown/selection ordering. No new background ingestion or derived-work queue exists.

Root/folder/asset selection, expansion/collapse, parent navigation, Refresh, empty and unavailable
states are supported. Refresh reloads mappings and current scope. Catalog changes/readiness loss
retire Browser state. Navigation/selection/expanded branches are transient across application
restart; broader workspace continuation is inspected and deferred. No fake production roots,
assets, thumbnails or unavailable controls are supplied.

## Integration and validation

Before full presentation, the real shell opened disposable Catalog
`dc164a89-71fb-4f2e-b42e-52dfb38351d3`, received Root
`4c243718-0720-4436-b868-763c97caefc6` and Asset
`4b39b36b-d86c-4e96-9c0f-e23a718bf54a` through the shared boundary, and closed normally.
The native checkpoint also proved competing-writer refusal, authored Collection preservation,
no Preview database, conservative Dirty/unpaired evidence and deep reopen. Sandbox GUI launch
exited 134 without logging; the native launch passed. Both records are retained separately.

Focused Release evidence in this task clone:

| Check | Result |
| --- | --- |
| Native Mac storage/lifecycle/shell and two real Browser Catalog tests | 34 passed |
| Shared Application tests | 136 passed |
| Shared Catalog tests | 44 passed |
| Avalonia presentation, stale folder/selection/readiness cases | 15 passed |
| Existing independent-process lifecycle qualification | 23 checks passed |
| Apple Silicon self-contained development bundle | built; native interaction verified |

The richer real fixture covers empty Catalog before seeding, two roots, nested/empty folders,
stored recursive scope, stable IDs, missing/disconnected sources, disappeared folder/asset,
invalid traversal, and unchanged logical contents of **all production Catalog tables** after
read-only browsing and reopen. Existing native tests retain recovery/Move/publication refusal,
readiness loss, rich authored state and ownership assertions. Existing test harnesses retain
historical LF015/LF024 labels and their own temporary-fixture conventions; new retained Browser
fixtures and all build/evidence outputs are inside this task workspace.

Native GUI checks: real root selection, compact hierarchy expansion/collapse, Empty folder,
Trip/Day 2 navigation, stored recursive inheritance, AssetId selection/details, unavailable
source with preserved Catalog entry, disconnected root guidance, Parent and Command-Q.
Real accessibility rows expose readable names rather than record implementation dumps. Visual
inspection prompted replacement of default bulky expanders and application of the orange accent.
Screenshots/logs/TRX and original sandbox failure remain under `artifacts/lf025-orientation`,
project TestResults and `artifacts/lf025-process-fixtures`. No owner acceptance is inferred.

CI run 39495258487 exposed a defect in the two new tests' default fixture location:
`Path.GetTempPath()` returned the `/var` alias, correctly refused by the production unlinked-path
guard. The remaining 32 native cases passed. The new tests now default to their own build-output
`BrowserFixtures` directory (or the explicit task-owned override). They are requalified without
that override; no production guard, ownership assertion or existing test is weakened. Raw failed
job evidence is retained as `artifacts/lf025-orientation/failed-ci-39495258487-native.log`.
A new source head, rather than a retry of failed inputs, requires fresh CI/bundle provenance.

## Runnable candidate and hands-on commands

Workspace: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-025-Browser`.
Branch: `codex/025-mac-browser-foundation`.
Fetched base: `32d6c110ba771233249f19475a88a2b4d1e14aa3`.
The Draft PR records exact head. `artifacts/mac-shell/provenance.json` records exact rebuilt
head/branch/dirty flag and package hashes; the final handoff supplies that head independently.

Build:

```sh
cd /Users/jeremyrunning/Git/agents/LF-MAC-DEV-025-Browser
DOTNET_COMMAND="$PWD/work/toolchain/dotnet/dotnet" scripts/Build-MacShell.sh
```

Bundle: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-025-Browser/artifacts/mac-shell/Lightflow Studio.app`.
Launch (self-contained, no installed .NET runtime required):

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-025-Browser/artifacts/mac-shell/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-025-Browser/artifacts/lf025-fixtures/lf025-browser-73dcb263e37e4ad1b9a65628a67447d4"
```

Catalog fixture is `Catalog` beneath that exact isolated root. Its `LF025-fixture.txt` records
all IDs. Catalog ID `48163e72-7ecd-4d0f-b97d-cb7a783377dd`; Field Notes RootId
`29d9e156-18f0-4141-9908-a0552f65aff1`; disconnected RootId
`745f8b9c-bce8-4c11-9660-8f879f8c893c`.

1. Select **Field Notes**: one `cover.png` Catalog entry. Expand its disclosure: Empty and Trip.
2. Select **Empty**: truthful no-Catalog-assets state. Parent returns to Field Notes.
3. Select **Trip**: three Catalog entries, including nested `forest.png` and intentionally absent
   `missing.png`; stored Include Subfolders applies without adding a new toggle.
4. Expand Trip, select **Day 2**, then `forest.png`: visible selection, source availability,
   recorded size/time/path and AssetId. Parent returns to Trip with inherited recursion.
5. Select `missing.png`: keep its entry/identity and show source-unavailable guidance.
6. Select **Disconnected media**: explanatory unavailable state, without deletion or remapping.
7. Refresh, resize and keyboard-navigate the single-selection list; inspect dark styling/focus.
8. Quit with Command-Q or Quit button, relaunch the same command: same Catalog/roots/assets.
   The authored Collection `LF025 preserved field notes` remains in Catalog (not an editing UI).

Fixture media are three disposable real 1×1 PNGs; the fourth record intentionally has no file.
A post-build fixture-format audit found an incorrect IDAT CRC in the original tiny PNG data.
The test fixture constant and retained acceptance media were corrected without changing their
length, recorded timestamps, Catalog identities or authored records. Original bytes/checks remain
in `artifacts/lf025-orientation/png-fixture-correction.json`. CRC and decompression verification,
the two real Browser tests and a fresh exact-head bundle/CI qualify the correction; no production
Browser decoding or probing was added.
No personal media is used. No thumbnail, Preview peer, decoder/probe, Player, rating/flag,
collection editing, Inspector editing, Jobs/Export, Move or transfer implementation is included.
No external-drive destructive testing or normal-profile launch is requested.

## Independent acceptance gates

Implementation/focused/native interaction evidence is separate from exact-head hosted CI,
owner technical/visual/hands-on acceptance, mandatory Windows local package qualification and
merge. The Draft PR reports actual CI results. Do not retry failed CI merely for green; retain
failures and keep unrelated #446 ownership separate.

This Mac cannot execute the mandatory Windows private-desktop package gate. For a separately
prepared Windows full clone of this exact candidate, the applicable command remains:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller
& "$PWD\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "$PWD\artifacts\lf025-windows-owner-acceptance"
```

The Windows qualifier must supply its actual absolute workspace/executable/data-root paths,
commit, timestamp, smoke/dependency results and process cleanup. No such local package PASS
is claimed here. Hosted Windows tests/installer/staging are additional evidence.

No R2/R3/R4/Epic completion, owner acceptance, merge, release/tag or Windows retirement.
Potential nonblocking Chronicle candidate: first useful Catalog-backed shared Mac Browser,
only after owner acceptance, with #454/PR/accepted-source evidence. No history rewrite here.
