# Completed P2 round trip and final G2 recommendation

2026-10-06 · M2 #369 · Draft PR #375 · parent Epic #366

**Recommend G2 / Catalog gate PASS for the owner-selected P2 initial research scope, pending owner acceptance.** No evidence blocker remains in that bounded scope. This is a persistence/architecture proof, not packaged Mac application acceptance or a claim that the proposed production guards already exist. Keep #369 Open / In Progress and #366 open; do not merge #375 before acceptance.

## Executed sequence and provenance

Windows local NTFS → verified CLOSED archive through NAS → Mac local APFS / production arm64 SQLite WAL → defined authored edit → clean close/checkpoint → verified CLOSED archive through NAS → Windows local NTFS / production-linked restore and return verification.

All legs used proof source commit `7d7adc1a6eb43292275aad3013e599ab1c8d407a`. Authoritative main verified at publication: `21adb19f760e4ec07c17270e995cb5588044396c`. Windows origin and return are actual owner-executed results supplied on 2026-10-06, not simulation or a Mac rerun. The Mac leg and transfer hashes were directly observed. Raw Windows runtime/log files were not newly supplied with the return report; do not infer unreported Windows native-library hashes or a final Windows DB byte hash. The retained Mac manifest's hardcoded `source` field identifies historical production baseline `aed6c2906637c8a5a9d71c1c18ac24bed48a8724`, not the executed proof commit; use the verified proof SHA above.

| Leg | Result | Checks | Release build |
|---|---|---:|---|
| Windows origin, local NTFS Fixed/Healthy | seed completed | 36 | 0 warnings / 0 errors |
| Mac dependent leg, local APFS | mac-leg completed | 106 | 0 warnings / 0 errors |
| Windows local return | windows-return completed | 34 | 0 warnings / 0 errors |

Machine-readable evidence: [P2_ROUNDTRIP_RESULTS.json](P2_ROUNDTRIP_RESULTS.json); directly observed [P2_MAC_CHECKS.json](P2_MAC_CHECKS.json). The 32 assets are deterministic synthetic source-byte fixtures with representative authored Catalog state, not a codec/media playback corpus. All 23 Catalog tables were compared.

## Hash chain

| Artifact | SHA256 |
|---|---|
| Windows-origin ZIP; received NAS copy; local Mac copy | `20adb6ad88378cfe06ca588d02dd2e99f070edf9cdc85df4afbf35749fc7a33c` |
| Incoming Windows Catalog | `890068c386ffe62065e64b659592ddd8df7bd91c02f86376b7d825aae920c9ca` |
| Mac-return Catalog | `480f57575fa78aca7cfa8f2fcbae2d0ac720ebe5a9e68dd360670c4f996c5f9b` |
| Mac-return ZIP; Mac NAS copy; Windows received NAS copy; Windows local copy; final Windows transfer ZIP | `2beb47dc1c08530de026e216306bcdbe726e1f57f094c251a5751a9db4fcdb70` |

Each incoming NAS ZIP was hashed before local copying; the local copy was independently hashed before local extraction. The NAS was only transport for closed immutable archives. No active Catalog was opened there. The Mac Catalog was closed/checkpointed before archive creation; no WAL/SHM remained. Return archive contains only `catalog.db`, `snapshot.json`, `manifest.json`, `runtime.json`, `checks.json`, and `X2.cube`. Preview/cache was excluded. Windows reports no manual modification of the returned fixture before verification.

## Identity and exact authored delta

CatalogId `40eef1f6-31bd-4f33-a6ed-56a6851f6f6d` and RootId `833df658-1de9-45ad-989c-f9b3e5f0099a` were preserved. All 32 AssetIds were preserved; the Mac resolved every one against its expected deterministic source bytes. Exactly one current-Mac mapping was added for the existing RootId; original mappings were unchanged. Windows return restored the Mac snapshot locally and passed its every-table comparison. This establishes the tested cross-platform restore and identity/authoring round trip; it does not assert new Windows media-path resolution beyond the checks the return harness performs.

Only first manifest AssetId `607cdb09-62c1-4737-89cb-5bf84237450f` changed authored fields:

- Notes: `Catalog-owned\nNo media sidecars` → `X2 Mac return-leg authored note` (the original contains an actual newline).
- Revision: 1 → 2.
- UpdatedUtc: `2026-10-06T18:33:33.6073799Z` → `2026-10-06T18:40:52.0265040Z`.

Across 23 tables, all other expected rows/fields were preserved, apart from the one defined mapping addition. Current RootId + per-MachineId mapping supports this Windows/Mac remapping with schema 19 unchanged. No schema migration is required for this tested capability; do not create one for architectural uniformity alone.

Production-linked Mac runtime: Apple Silicon Arm64, SQLite 3.53.3, WAL, synchronous FULL=2, foreign keys enabled, busy timeout 5000 ms, integrity `ok`. Existing APFS lifecycle, locking, process-crash/recovery and Preview control evidence remains applicable within its documented limits.

## Selected initial policy and follow-through

- Active Catalogs: supported local storage (tested Windows NTFS and Mac APFS); SQLite and Catalog-owned authored metadata remain the baseline. Active NAS/network filesystems are unsupported. NAS/network media remains supported as a distinct role.
- P2: verified closed-Catalog transfer is the selected initial cross-platform model. Maintain one active authoring owner; this is not live synchronization, concurrent multi-machine use or conflict merging.
- Preview/cache: machine-local and rebuildable; never transport machine-local Preview state with the Catalog. The existing rebuild control left all Catalog-owned rows unchanged; it was a store/metadata rebuild, not a full rendering benchmark.
- Path safety: **fail closed** on case-folding collisions, Unicode aliases/normalization ambiguity, lossy trim/backslash normalization, unsupported portable-name collisions and symlink/reparse containment ambiguity. Reject or clearly mark affected roots unsupported rather than guessing. No silent identity merges, duplicate repair or destructive renaming. Preserve original spelling, IDs and authored rows. Revalidate resolved containment during operations, not only during admission. Existing unsafe examples are negative evidence motivating this policy, not proof of implemented safety guards.
- Network rejection: OS-specific adapters must inspect resolved volume/filesystem identity, mount/reparse targets and locality/capabilities. UNC syntax or Mac mount-path spelling alone is insufficient; unknown/ambiguous classification fails closed for active Catalog authoring. Keep active Catalog, media, Preview and backup roles distinct.
- NAS backup: preserve the direct active local WAL → SMB failure at destination journal finalization, SQLite error 14/CANTOPEN. Recommend local SQLite-aware backup → finalized CLOSED artifact → NAS byte transfer → hash verification → verified local copyback → production restore. Keep prior verified backups; partial transfers must not be published as usable backups. Existing same-Mac restore plus this cross-platform closed-restore result support the staged model; interrupted-network backup qualification is not claimed.

These are selected storage direction and conservative implementation recommendations, not production changes. Later authorized implementation must enforce path admission/runtime guards and resolved network rejection, provide staged backup/transfer and remapping UX, and qualify packaged applications. No remaining native experiment is required to make this M2 recommendation; owner acceptance is outstanding. The bounded proof is not blanket filesystem, power-loss or arbitrary media-name qualification.

## Deferred and non-goal items

P1 direct physical portable-exFAT SSD qualification is **deferred**, not an initial Mac compatibility requirement and not a G2 blocker. Positive exFAT disk-image results do not qualify a physical SSD. Physical eject/surprise removal, power-loss/device-cache durability and physical SSD-to-NAS backup remain unproved. General permissive case-sensitive/Unicode key support and any future migration need separate review. Active network Catalogs, authority-service continuation, simultaneous multi-machine use, database replacement and sidecars are excluded. Player/UI, combined G1/G2/G3 acceptance and packaged product release remain separate. No M4+ or X1/X3 work starts from this recommendation.

## Unexpected finding and reproduction limits

The unchanged harness compares pretty-printed snapshot JSON as text. The first Mac attempt stopped before authored editing because Windows CRLF and Mac LF differed. All 23 parsed table snapshots were identical, and line-ending normalization alone made the byte strings equal. Originals were retained; a separate local Mac input copy used LF. The return package snapshot used CRLF for the unchanged Windows reader; parsed JSON equality was checked again. No database or authored evidence was changed by this formatting accommodation. The Windows return then passed without any manual fixture modification there. A future harness fix should compare canonical/parsed content; none was implemented in this task.

Use the commands in [WINDOWS_HANDOFF.md](WINDOWS_HANDOFF.md) pinned to the proof source SHA, not a moving branch head. Apply only the disclosed snapshot line-ending accommodation when reproducing with unchanged tooling. The closed ZIP hashes bind the actual transferred bytes. Raw local fixtures and logs remain retained until owner acceptance; public artifacts omit workspace paths, NAS host/share names, machine-local paths and transfer-only archive filenames. Synthetic identities, versions, hashes, checks, negative findings and architecture meaning are preserved.
