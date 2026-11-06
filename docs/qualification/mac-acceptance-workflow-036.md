# LF-MAC-DEV-036 — Persistent Mac acceptance workflow

## A. Identity and provenance

Assignment/chat: **LF-MAC-DEV-036 — Browser Thumbnail Foundation**. Existing independent
workspace `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation`, branch `codex/036-browser-thumbnail-foundation`.
Starting source `666505ef81310a62dffd13b514cf562084487c13`, tree
`bef6552d360606ae7670f4d675b2d5ade312a900`; independently verified main
`7f755460e36375234281481cea3004f60344325b`.
[Issue #481](https://github.com/jeremyrunningphotography/lightflow-studio/issues/481),
[Draft #486](https://github.com/jeremyrunningphotography/lightflow-studio/pull/486).
Final SHA/tree, build timestamp and hashes are stamped into the generated
`artifacts/lf036-acceptance-workflow/REPORT.md` and manifest after commit/build.

## B. Purpose and production boundary

Owner reports all Mac functionality accepted at the starting source, including Audio
visibility, cached presentation and DJI performance. This continuation retains a
single isolated owner acceptance Catalog across compatible packages. Only task tools,
scripts and documentation change; production application code and storage schemas
are unchanged. No merge authorization is implied.

## C. Retained environment

- Data root: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/acceptance/persistent`.
- Catalog: **Lightflow Test**, folder `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/acceptance/persistent/Lightflow Test`.
- Identity: `180251a8-5eb9-46bf-8e27-7de4189273c9` (recorded at supported initialization).
- Preview storage remains `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/acceptance/persistent/Previews`, outside package directories.

`MacShellCatalogSession.CreateAsync` performs existing supported creation, admission,
identity allocation and selected-Catalog configuration. The helper never writes SQL.
The folder name supplies the test Catalog name; no new Catalog naming field exists.
`initialize` only creates when the entire data root is absent. Existing valid roots
are checked read-only; partial/missing configuration is refused, never recreated.
After initialization this is retained owner data. Future agents must not reset,
replace, delete or repair it, or discard cache/authored state, without explicit owner
authorization. `/acceptance/` is ignored by Git.

## D. Disposable environments and isolation

Automated workflow fixtures and package smoke roots live below
`artifacts/lf036-acceptance-workflow/disposable/`, separately from retained data.
The failure matrix creates a unique new root on each run. Ordinary Release suites
keep their existing private temporary fixture roots. No automated test or package
smoke test may target `acceptance/persistent`. Initial supported setup and practical
owner-workflow reopen validation are distinct from disposable smoke tests.
Retain evidence for review; disposable data can be removed after all owning processes
close. Never apply that cleanup rule to retained data. Existing earlier acceptance
and Phase B/performance artifacts remain untouched. No normal user Catalog or
JRPhoto4T media is needed; JRPhoto4T remains strictly read-only.

## E. Tools and compatibility

- `tools/MacAcceptance`: task-only bootstrap/read-only preflight, not referenced by app.
- `scripts/Mac-Acceptance.sh`: `initialize`, `check`, or guarded `launch` for the stable root.
- `scripts/Test-MacAcceptance.py`: disposable refusal/idempotency/isolation cases.
- `scripts/Build-MacShell.sh`: adds source tree, UTC build time and tracked-dirty provenance.
- Root AGENTS, this handoff, assignment registry and selective historical evidence.

Preflight requires exact helper/package hashes for Catalog, Application, Domain,
Mac adapter and native storage library. It checks the existing profile configuration,
Catalog ID, current Catalog schema (19), Preview schema (4 if present), application
identities, SQLite integrity, existing native local-storage policy, read-only exclusive
profile/Catalog leases, ownership-envelope checksum and pending transfer recovery.
Nonempty WAL/journal, malformed evidence, unknown packages, partial roots and schema
mismatches refuse with no migration/reset/repair. Expected zero-length WAL and idle
SHM files are permitted. SQLite inspection uses immutable read-only connections only
after writer exclusion and nonempty-WAL refusal; it does not recover a database.

The Mac Catalog-only lifecycle deliberately leaves Validated/Dirty completion evidence;
preflight neither fabricates Clean nor grants the fast path. The unchanged application
performs its authoritative deep validation and admission on every launch. Advisory
preflight leases end before exec; a competing process can cause application refusal.
An absent Preview DB is compatible with a new empty Catalog and is created only by
normal app demand. Schema uncertainty is investigated using a disposable clone,
never by experimenting on retained data. Do not bypass refusal by using a direct
launch command. Data-root changes or new helper/package mismatches require review.

## F. Validation

The generated report records focused checks, genuine packaged reopen/clean shutdown,
same Catalog identity and cache pixel/mtime retention across the newly packaged build,
disposable package smoke, and broader Release/native tests. The refusal matrix never
targets retained data; it hashes fixture files/mtimes before and after each preflight.
Full exact-head CI is required for root AGENTS/scripts/tool changes. Earlier-head CI
is historical evidence only. Local Windows cross-compilation is not independent
Windows execution; LF-WIN-TST-037 owns that qualification.

## G. Owner launch instructions

Fresh package: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-acceptance-workflow/package/Lightflow Studio.app`.
Before each new package's first retained launch, close other instances using the root
and run (from any directory, with this clone's existing SDK):

```sh
DOTNET_COMMAND="/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/.cache/dotnet/dotnet" sh "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/scripts/Mac-Acceptance.sh" check "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-acceptance-workflow/package/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac"
```

Primary copyable Terminal launch, after successful preflight:

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-acceptance-workflow/package/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/acceptance/persistent"
```

Alternatively replace `check` with `launch` above for preflight followed by exec.
First-ever setup in a new authorized task clone uses `initialize` once, then `launch`.
Never delete an existing root to make initialization succeed.
The workspace should open directly in the same Catalog without a setup dialog.
Last browsed location restoration remains outside this assignment: manually revisit
a previously scanned folder; its cache and Catalog records remain available.

Separate clean-start command (choose a NEW suffix if this root already exists):

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-acceptance-workflow/package/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-acceptance-workflow/disposable/owner-clean-start-01"
```

This deliberately shows supported first-run Catalog setup. It does not replace or
reset the persistent environment. Equivalent PowerShell primary command on Mac:

```powershell
& "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-acceptance-workflow/package/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/acceptance/persistent"
```

## H. GitHub and history

Existing #486 remains Draft; #481 open/In Progress/Browser with priority unset.
#480 remains open/Backlog with incomplete DoD; #482–#485 unstarted. Native hierarchy
and assignment numbers are preserved. Registry records owner Mac functional acceptance
and this workflow continuation. No new issue/agent/PR or independent Windows work.

Historical maintenance: **Updated** — Chronicle and Source Index record the important
retained-versus-disposable acceptance workflow convention. Development History:
**Not warranted** — no broader product capability or architecture narrative changes.
Earlier historical and qualification provenance remains intact.

## I. Artifacts

Generated complete report, evidence manifest and logs:
`artifacts/lf036-acceptance-workflow/`.
Portable ZIP: `artifacts/LF-MAC-DEV-036-Acceptance-Workflow-evidence.zip`.
Separate app ZIP: `artifacts/LF-MAC-DEV-036-Acceptance-Workflow-Mac-app.zip`.
Manifest hashes enumerate included files. Never ZIP the retained/live Catalog,
settings/private databases, media, credentials, Preview cache or large disposable
fixture roots. Evidence contains metadata/receipts only. Earlier artifacts stay intact.

## J. Remaining gates

Owner acceptance of this workflow; final exact-head CI/review; LF-WIN-TST-037
qualification against final accepted source; explicit merge authorization. Owner Mac
functional acceptance remains established for starting production code, not automatic
acceptance of the new workflow. Stop for owner review; no merge, closure or #482.
