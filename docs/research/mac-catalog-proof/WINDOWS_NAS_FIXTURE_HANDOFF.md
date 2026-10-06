# Exact NAS-control fixture: Mac → Windows-local → Mac

Prepared, unexecuted Windows commands. This is a supplemental same-fixture protocol; the original Windows-origin-first protocol remains pending/preserved. It does not qualify live NAS storage. Owner root/storage decision gate remains.

Source baseline `aed6c2906637c8a5a9d71c1c18ac24bed48a8724`; branch `codex/x2-nas-qualification-20261005`. Exact final commit and source bundle/evidence hashes will be in task-local `work/evidence/RESUME_HANDOFF_CHECKPOINT.md`. Fetch pushed branch into the EXISTING task-owned Windows clone without merging or resetting existing work:

```powershell
Set-Location 'C:\Git\Agents\Agent-X2-Mac-Catalog-Proof'
git status --short
git fetch origin codex/x2-nas-qualification-20261005
git switch -c codex/x2-nas-windows-verify FETCH_HEAD
git rev-parse HEAD
```

Stop if work/branch exists or the source differs from the supplied exact commit. Do not force-reset. If network is unavailable, fetch supplied Git bundle branch instead. Copy `/Users/jeremyrunning/Git/agents/Agent-X2-Mac-Catalog-Proof-20261005-01a10e68/work/evidence/x2-nas-origin.zip` to `C:\Git\Agents\Agent-X2-Mac-Catalog-Proof\work\x2-nas-origin.zip`. ZIP SHA256 `fa87579250df4ddeefb4798ed728185d8b128ac4264a265d90b0a7bb85a2e27e`; DB SHA256 `6FAD733E5116519CFD13BE4402DB345627E6AD87CED6FF0944C9DC338ECB26DC`. Extract its files at work/x2-nas-origin. Media bytes are deterministically recreated by the harness under the WINDOWS data root; no absolute Mac workspace path is required for execution. Mac mapping strings stay inert and unchanged.

```powershell
Get-FileHash '.\work\x2-nas-origin.zip' -Algorithm SHA256
Expand-Archive -LiteralPath '.\work\x2-nas-origin.zip' -DestinationPath '.\work\x2-nas-origin'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\tools\X2CatalogProof\Windows-Leg.ps1' -Phase VerifyNasFixture
```

Windows-local production factory uses WAL/FULL/FK/5000ms. Before mutation, exact DB hash, all-table snapshot and CatalogId must match. Existing root must begin Unmapped for the new machine; add exactly one Windows-machine mapping to existing RootId and resolve all32 AssetIds to deterministic source bytes. Through the production optimistic description store edit first manifest AssetId Notes to `X2 Windows NAS-fixture return note`, increment Revision1 and update UpdatedUtc. Every other description field/row and every other table must remain unchanged except one new mapping; original Mac mapping remains intact. No Observe/reconciliation/source-observation edits. This defined delta is a proposed verification contract for owner review.

Outputs: work/x2-windows-nas-return/{catalog.db,snapshot.json,manifest.json,runtime.json,checks.json,X2.cube} plus work/x2-windows-nas-return.zip and SHA256. Return that exact ZIP/hash/logs/native selected win-x64 payload/version/options and source commit through Jeremy. Do not return a different seed Catalog. Closed SQLite-aware backup finalization precedes archive creation; never raw-copy an open DB or discard WAL.

After explicit Mac resume and receipt, verify outer ZIP hash and extract into Mac work/data/windows-nas-return. Use a fresh work/data/mac-nas-return-verify data root:

```sh
python3 tools/X2CatalogProof/run.py tools/X2CatalogProof/bin/Release/net8.0/X2CatalogProof.dll mac-nas-return work/data/mac-nas-return-verify work/data/windows-nas-return work/data/mac-nas-return-verified
```

This checks returned hash, all tables, CatalogId/RootId/AssetIds, arm64 production reopen and outgoing SQLite backup equality. No fixture is renamed to fake another OS phase. Windows script/modes compiled but not executed on Windows. Preserve origin/pre-crash backup and all returned manifests. Windows private-desktop focused Release tests/packaged validation remain separate, required before PR-ready acceptance. Project-v2 fields must be reverified by a capable Windows workflow; parent366/prerequisite367 freshly verified here. No merge, no X1/X3 messaging/resume.
