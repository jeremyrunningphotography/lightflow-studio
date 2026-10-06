# Windows → Mac → Windows closed-Catalog handoff — completed

**Completed on proof commit `7d7adc1a6eb43292275aad3013e599ab1c8d407a`: 36 origin / 106 Mac / 34 return checks.** See [final evidence and JSON line-ending reproduction accommodation](P2_FINAL_RECOMMENDATION.md). Commands below are the retained procedure, not authorization to repeat it. P1 is deferred. No authority-service endpoint, owner token or central generation is part of this handoff.

Use a fresh independent task clone on Windows NTFS. Preserve any existing X2 clone/fixtures. Record the exact published commit from this Draft PR/#369 before running. No Lightflow GUI is launched by these commands; WPF/private-desktop tests and package/hands-on acceptance remain separate gates.

## 1. Windows origin

The following names are new task-local outputs; stop if the destination already exists. `$ExpectedCommit` is the exact 40-character published head supplied in the PR handoff, not a branch-tip guess.

```powershell
$ExpectedCommit = Read-Host 'Paste the exact published proof commit from #369'
if ($ExpectedCommit -notmatch '^[0-9a-f]{40}$') { throw 'Exact commit required' }
$Proof = Read-Host 'Absolute path for a fresh task-owned clone on local NTFS'
if ($Proof -notmatch '^[A-Za-z]:[\\/]' ) { throw 'Local absolute path required' }
if (Test-Path -LiteralPath $Proof) { throw 'Preserve existing workspace; select a fresh owner-approved path' }
git clone --branch codex/x2-local-portability-proof-20261006 --single-branch https://github.com/jeremyrunningphotography/lightflow-studio.git $Proof
if ($LASTEXITCODE -ne 0) { throw 'Clone failed' }
Set-Location $Proof
if ((git rev-parse HEAD).Trim() -ne $ExpectedCommit) { throw 'Unexpected source commit' }
if (git status --porcelain) { throw 'Workspace is not clean' }
$DriveLetter = ([IO.Path]::GetPathRoot($Proof)).Substring(0,1)
Get-Volume -DriveLetter $DriveLetter | Select-Object FileSystem,DriveType,HealthStatus
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\tools\X2CatalogProof\Windows-Leg.ps1' -Phase Seed
if ($LASTEXITCODE -ne 0) { throw 'Windows seed failed' }
Get-FileHash '.\work\x2-windows-origin.zip' -Algorithm SHA256
```

The existing seed harness creates the representative 32-asset schema-19 Catalog using production migrations/services and explicitly disclosed fixture SQL for UI/media-bound rows. It writes a SQLite-aware standalone backup, `snapshot.json` containing every table, manifest and `X2.cube`. Stable IDs, authored state, runtime/native image hashes and actual source commit must be retained. No substitute Mac-origin fixture may be relabeled Windows-origin.

Return the exact closed origin ZIP, SHA256, source SHA and runtime logs through the owner. Do not copy an open Catalog, discard a WAL, send the machine-local Preview DB, or use cloud live synchronization. This stage establishes Windows creation and transfer, not direct exFAT operation.

## 2. Mac dependent leg

After explicit receipt, verify the externally supplied ZIP hash before extraction. From the matching isolated Mac clone, use fresh repository-relative directories:

```sh
shasum -a 256 work/data/portable-windows-origin.zip
mkdir work/data/portable-windows-origin
unzip work/data/portable-windows-origin.zip -d work/data/portable-windows-origin
python3 tools/X2CatalogProof/run.py tools/X2CatalogProof/bin/Release/net8.0/X2CatalogProof.dll mac-leg work/data/portable-mac-leg work/data/portable-windows-origin work/data/portable-mac-return
```

The existing `mac-leg` rejects the wrong phase, verifies incoming DB hash and every-table snapshot, restores locally, opens the production arm64 WAL Catalog and requires the same CatalogId. It remaps the existing RootId for this Mac, resolves all 32 AssetIds against deterministic source bytes, and changes only the first manifest asset's Notes to `X2 Mac return-leg authored note`, Revision +1 and UpdatedUtc. Exactly one Mac mapping is added; previous mappings and all other rows/fields remain unchanged. No Observe/reconciliation is used to rewrite source observations.

Create a closed return archive from `catalog.db`, `snapshot.json`, `manifest.json`, `runtime.json`, `checks.json`, and `X2.cube` in the output directory; record archive and file hashes. Return it through the owner. Preserve both origin and returned bytes. The harness closes/checkpoints its owned active Catalog; the exported backup is already standalone.

## 3. Windows return

Verify the returned archive hash, extract its files into `work/x2-mac-return` in that same Windows proof clone, then:

```powershell
$Proof = Read-Host 'Absolute path of the same Windows proof clone used for Seed'
Set-Location $Proof
Get-FileHash '.\work\x2-mac-return.zip' -Algorithm SHA256
Expand-Archive -LiteralPath '.\work\x2-mac-return.zip' -DestinationPath '.\work\x2-mac-return'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\tools\X2CatalogProof\Windows-Leg.ps1' -Phase Return
if ($LASTEXITCODE -ne 0) { throw 'Return verification failed' }
```

`windows-return` restores into a new local root, checks the exact Mac snapshot and authored delta, reopens Catalog 19 and compares all tables before/after backup. Capture actual native win-x64 SQLite version/hash, WAL/FULL/foreign-key policy, integrity, backup hashes and all logs. Then perform applicable Windows Release tests through the repository's noninteractive private-desktop boundary. No PR-ready or fresh packaged executable claim is made by this evidence-only handoff.

## 4. Separate physical SSD qualification

Only after the owner resolves the provided folder's access timeout and authorizes any whole-drive eject step: identify the actual exFAT filesystem, block/cluster sizes and USB device; create a unique fixture beneath the task-safe parent; keep Preview on the host. Require create/open/edit/reopen, separate-process SQLite locking, SQLite-aware backups, close/checkpoint/no handles, normal OS eject, the same logical Catalog on the other host, another clean close/eject and return. Verify all authored tables and expected mappings/deltas throughout. Do not format, repartition, force-detach or cable-pull a drive containing user data. A spare disposable device is needed for destructive/power-loss qualification.

Stop for any unexpected identity/row/bytes difference or ambiguous path. No silent merge, filename rewrite, schema migration, sidecar, authority-service restart or database replacement is authorized. G2 PASS is recommended for the revised P2 research scope, pending owner acceptance; physical SSD qualification is not a blocker.
