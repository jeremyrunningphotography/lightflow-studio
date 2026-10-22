# LF-MAC-DEV-027 correction acceptance

The superseding #460 scope and PR #461 owner correction keep this a functional
discovery/reconciliation slice. #462 owns resizing/layout/toolbar/right-panel/icon
parity and remains unstarted. No current-candidate interactive GUI check was run:
Jeremy prohibited agent desktop control. Earlier head207 GUI results are historical.

## Corrected behavior

Mac effects omit nonlinked application-package directories (case-insensitive .app
suffix), exclude their contents from recursion, and refuse explicit paths inside
packages. No exceptions are supported for .app contents; other Finder package types
are not inferred. Links masquerading as .app still trigger existing link refusal;
internal package links are never encountered. Protected storage, mount isolation,
native admission, portable identity and missing inference remain unchanged. Skipping
packages does not mark historical descendants missing.

Recursive descendant failures still preserve Catalog rows and prevent unsafe missing
inference. The UI shows one concise “Scan incomplete” status, with supported result
count and preserved-record guidance. Full technical diagnostics go to stderr (the
launch below retains stderr in browser.log). Base-folder refusal remains a truthful
load failure. Partial scans never become complete-success claims.

Folder disclosure has a 32×32 minimum hit target with the compact caret retained.
No shell/layout restructuring or default-window change is included.

## Noninteractive persistence and latency evidence

Workspace: /Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery

Branch: codex/027-mac-browser-discovery

Base: 6d5aee56cb84786633d416bfce4b1d8c1e87ebbd

Release validation: 41 native adapter/lifecycle, 50 Catalog/mutation/discovery,
21 headless Avalonia tests pass. 83 focused Application identity/storage/mapping tests pass. Exact-head CI
are recorded in the issue/PR handoff. TRX names begin lf027-corrections in each
project's TestResults. Original 39/50/20 results, automation crash and first-pass
correction timings remain preserved separately. Existing two CA1416 analysis
warnings in MacBrowserCatalogTests remain.

Existing BrowserPerformance ActivitySource now distinguishes native mapping,
enumeration, fingerprinting, Catalog-only reads and projection from shared
reconciliation/source-observation stages. Without a listener no additional timing
work is done. Stage timings are nested/inclusive: do not add them together.

Raw task-local receipts:

- artifacts/lf027-corrections/persistence-timing.json: before redundant enumeration removal.
- artifacts/lf027-corrections-final/persistence-timing.json: final correction implementation.

The fixture has 24 tiny local files plus one new file. It proves stable Catalog,
Root and Asset identities, rating 4 and Collection membership across navigation,
close and deep reopen, without Preview storage or duplicate ingestion. New-file
identity survives repeat discovery and read-only reopen.

Catalog-only reads after reopen perform zero discovery/enumeration/fingerprinting.
Explicit navigation/Refresh still revalidates the requested scope, as on Windows:
offline filesystem changes cannot safely be inferred from a persisted cache. No
whole-volume scan, persistent cache, schema or additional discovery owner is added.
The old Mac projection enumerated a direct folder three times; reusing its final
listing removes the unnecessary third pass, retaining authoritative reconciliation.


| Phase | Elapsed ms | Native enumerations | Fingerprints |
| --- | ---: | ---: | ---: |
| existing-first-discovery | 344.59 | 2 | 24 |
| reopen | 21.77 | 0 | 0 |
| existing-catalog-read | 1.04 | 0 | 0 |
| existing-navigation-revalidation | 319.22 | 2 | 24 |
| new-first-discovery | 22.76 | 2 | 1 |
| new-repeat-discovery | 22.59 | 2 | 1 |
| new-reopened-catalog-read | 1.00 | 0 | 0 |

These single-run tiny-fixture measurements are not a realistic Downloads benchmark,
rendering measurement or performance guarantee. They distinguish the cost of source
observation/reconciliation from Catalog reads; graphical responsiveness is pending.

## Fresh Mac owner launch

Self-contained unsigned/unnotarized bundle, built without launching. Exact candidate
head and all file hashes are recorded in artifacts/mac-shell/provenance.json.

Executable:
/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/mac-shell/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac

Isolated persisted acceptance data:
/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/lf027-corrections-final/Acceptance

Ready-to-copy launch for Jeremy:

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/mac-shell/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/lf027-corrections-final/Acceptance" 2>> "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/lf027-corrections-final/browser.log"
```

PowerShell equivalent:

```powershell
& "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/mac-shell/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/lf027-corrections-final/Acceptance" 2>> "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-027-BrowserDiscovery/artifacts/lf027-corrections-final/browser.log"
```

Under that data root, Command-L/Enter navigates to Media subfolders:

1. Existing: 24 cataloged JPG fixtures. Select photo-00.jpg, Refresh, quit and reopen.
   Confirm stable selected identity on Refresh and persisted AssetId on revisit.
   Rating/Collection preservation is established by the native receipt; this slice
   has no classification or Collection editor.
2. New: new.jpg is already persisted by the discovery fixture. Revisit/reopen must
   retain its recorded AssetId. Pending contains uncataloged added.jpg for fresh
   hands-on automatic discovery; Refresh/reopen must retain its newly assigned ID.
3. Packages: visible.jpg appears; Example.app and its internal resource/link tree
   must not appear as expandable folders or media. Include Subfolders must not
   descend into it. Explicit Example.app/Contents navigation is refused.
4. Partial: Include Subfolders shows safe.jpg and one concise incomplete-scan status;
   a linked descendant refuses reconciliation. Full paths/reasons are in browser.log.
   No long diagnostic paragraph or complete-success claim.
5. Click edges of disclosure targets, separately from folder selection. Exercise
   Up/Back/Forward, path entry, Refresh and recursive scope; retain valid selection
   and expansion context. Broader Windows visual parity belongs to #462.
6. Quit normally and relaunch this command. Verify persistence and usable navigation.
   GUI-dependent checks and renewed functional acceptance remain pending.

Exact IDs and timing stages are in persistence-timing.json. No current-candidate
graphical launch/shutdown PASS is claimed. The original automation exception remains
recorded; it was not treated as a successful smoke or erased.

## Separate Windows and owner gates

Follow the DARKMATTER independent-clone, private-desktop focused tests and mandatory
PullRequest-mode local Build-Release.ps1 instructions in mac-browser-460.md at this
candidate's exact head. CI package success is additional evidence, not a substitute
for that local Windows package gate. Preserve startup/dependency checks, timestamp,
hashes, normal drain and zero leftover processes. Jeremy's renewed functional
hands-on acceptance and final merge authorization are separate. PR stays Draft;
no merge, release, tag or #462 implementation.

Project access limitation: #460 native parent #366 verified by API. #462 had no native
parent on reorientation despite its specification naming #366. Available connector
tools cannot mutate parent/Project fields and Computer Use is prohibited. Project #4
verification and #462 Backlog/Browser/parent reconciliation remain explicitly pending;
no completion is claimed. #393/#394/#395/#366 remain open/incomplete.

Chronicle assessment: no new entry for these corrections; assess the shared Browser
milestone after acceptance. No historical narrative is rewritten.
