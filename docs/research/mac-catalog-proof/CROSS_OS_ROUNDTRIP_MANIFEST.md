# Same-fixture Windows→Mac→Windows protocol

Status: **pending Windows origin and owner identity-policy gate**. The local Mac control is not an origin leg or completed round trip. Harness phase methods compile; Windows script and real transfer phases have not run. A transfer checkpoint will supply the exact harness commit and bundle SHA256. No raw live database copy or WAL discard is permitted.

## Windows origin handoff to Jeremy

Copy the task-local `work/evidence/x2-proof-source.bundle` from Mac to `C:\Git\Agents\Agent-X2-Mac-Catalog-Proof\work\x2-proof-source.bundle`, verify the supplied SHA256, then in the existing Windows X2 task-owned clone:

```powershell
Set-Location 'C:\Git\Agents\Agent-X2-Mac-Catalog-Proof'
git status --short
git fetch '.\work\x2-proof-source.bundle' codex/x2-mac-catalog-proof-20261005
git switch -c codex/x2-mac-catalog-proof-return FETCH_HEAD
git rev-parse HEAD
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\tools\X2CatalogProof\Windows-Leg.ps1' -Phase Seed
```

Stop for any existing work/branch, unexpected source hash or production diff. Do not reset another workspace or branch. This console/database harness does not launch WPF; subsequent WPF tests/packaging retain scripts/Test-Noninteractive.ps1/private-desktop boundary. No PR or merge is requested.

Expected output paths:

- `C:\Git\Agents\Agent-X2-Mac-Catalog-Proof\work\x2-windows-origin.zip` and its SHA256.
- `...\work\x2-windows-origin\catalog.db`, `snapshot.json`, `manifest.json`, `X2.cube`.
- `...\work\x2-windows-seed-data` is the isolated seed root; all fixtures/caches/temp are task-local.

The32-asset origin is created with all19 production migrations and the services/seeding limits disclosed in the main report. Stable CatalogId, RootId and32 AssetIds, every-table rows, revisions, timestamps and migration history are recorded. The protected production backup is finalized as a standalone database before it is copied to the archive. The cube resource hash must match its Catalog row. No NLE dispatch occurs.

Windows X2 must additionally record commands/logs, actual native selected win-x64 asset/hash/source, runtime/schema/policies, table counts and archive SHA256, then give Jeremy that exact archive for transfer. Verify #369 Project In Progress/Area Catalog/Priority unset, native parent#366 and completed prerequisite#367, and Epic open/incomplete; Mac tools cannot independently read Project-v2 fields.

## Mac dependent leg (do not execute before real origin arrives)

Transfer/extract the exact verified origin archive into:

`/Users/jeremyrunning/Git/agents/Agent-X2-Mac-Catalog-Proof-20261005-01a10e68/work/data/windows-origin`

Verify both externally reported ZIP SHA256 and the manifest's DB SHA256. From the Mac clone:

```sh
python3 tools/X2CatalogProof/run.py tools/X2CatalogProof/bin/Release/net8.0/X2CatalogProof.dll mac-leg work/data/mac-roundtrip work/data/windows-origin work/data/mac-return
```

The harness rejects a Mac-local-control manifest as an origin. It installs through production SQLite-aware restore into a new root, checks every table before mutation, adds a machine-specific Mac mapping to the existing RootId, verifies32 stable IDs resolve deterministic source bytes, and writes one first-asset Notes edit through the optimistic description store. It creates a new consistent protected snapshot and outgoing full-table manifest. It does not Observe/reconcile and rewrite imported source observations.

Proposed protocol differences, requiring Jeremy review before treating the leg as accepted:

- One Mac row added in MediaRootMappings; every Windows mapping row unchanged. Its MachineId/PhysicalPath/MappingId/timestamps are local operational facts.
- First asset's Notes=`X2 Mac return-leg authored note`, Revision increments exactly1, UpdatedUtc changes. All other description columns/rows remain identical.
- All remaining table rows, IDs, query JSON, migration history and authored state must remain identical. SQLite physical pages/journal state and database/archive hashes may differ; logical snapshots are the equality contract.

Raw final expected Mac outputs: `work/data/mac-return/{catalog.db,snapshot.json,manifest.json,runtime.json,checks.json,X2.cube}`. Compute SHA256 for the closed standalone DB, JSON files and ZIP; record actual commit. Return this exact directory/archive to Jeremy, not the unrelated local-control export.

## Windows return

After verifying the Mac archive SHA256, extract into `C:\Git\Agents\Agent-X2-Mac-Catalog-Proof\work\x2-mac-return` and run:

```powershell
Set-Location 'C:\Git\Agents\Agent-X2-Mac-Catalog-Proof'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\tools\X2CatalogProof\Windows-Leg.ps1' -Phase Return
```

The return reader checks incoming hash/full-table snapshot, restores a task-owned copy, reopens production Catalog19 and requires all tables to equal the exact Mac snapshot before and after read/backup. Preserve original Windows origin and Mac return bytes. Record native evidence, checks and final manifest in `work/x2-windows-verified`. Then run the applicable Windows focused Release suite through the established private-desktop script. Fresh Windows package/hands-on evidence is required before any PR-ready claim, not replaced by this console proof.

Any unexpected identity/path/row difference stops acceptance. G2, production lifecycle/storage and combined G1/G2/G3 gates remain open for Jeremy review even if the bounded same-fixture transfer succeeds.
