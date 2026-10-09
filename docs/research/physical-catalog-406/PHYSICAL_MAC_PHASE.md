# Approved Mac physical fixture phase

LF-BOTH-RES-007 · #406 · continuation of Draft #407 · 2026-10-09

**CONDITIONAL for the physical Mac phase only.** Bounded physical ExFAT SQLite checks passed, including cross-process writer exclusion, checkpoint/reopen, synthetic authored-state preservation, backup/restore and closed-copy/hash rejection controls. Exclusive rename was unsupported. Successful flush API returns do not qualify external hardware power-loss durability. Active Catalog admission remains rejected under the unchanged production policy; Windows, safe eject/reconnect and complete R2 acceptance remain unqualified.

This report supplements the prior [initial report](INITIAL_MAC_REPORT.md) and [internal baseline](summary.json) without rewriting their historical evidence. Prior documentation head `f59534b7520815caf905997c029abd9401422bc9` remains in history; its CI run [37968221822](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/37968221822) completed successfully. PR #407 remains Draft/unmerged and #406 open/In Progress/Catalog/Priority unset. Native parent #77, #406 blocks #393, and Catalog/migration ownership remain unchanged. No support matrix, production source, provider, schema, writer lifecycle, system setting or driver changes.

## Authority, identity and bounded ownership

Jeremy explicitly approved exactly one new UUID task directory on JRPhoto4T, with a maximum **256 MiB total footprint**, synthetic files only, no existing owner-data modification, no eject/handoff or Windows execution. Volume and partition UUIDs matched the previous inventory before the first write. Native disk metadata confirmed the same SanDisk Extreme Pro 55AF, physical/external USB, ExFAT, GUID scheme, local writable mount and over 2.28 TB free. The negotiated USB link remains the earlier 10 Gb/s metadata observation, not a throughput result. Actual device identifiers and full canonical path are retained privately.

The one exclusively created directory is:

`LF-BOTH-RES-007-Qualification-<private run UUID>`

The exact basename and canonical path are retained privately for the owner decision gate. Exclusive `mkdir` and `O_EXCL` file creation never replace an existing owner object. The harness holds read-only volume/root directory descriptors and records the new root's device/inode identity. Before each SQLite/filesystem mutation, it rechecks DiskManagement volume/partition/source identity, native statfs fsid/mount/local/readwrite state, Foundation alias status, canonical containment, live root inode/descriptor identity and the bounded footprint. Paths are flat task-owned leaves; reads use `O_NOFOLLOW`; enumeration is confined to this task directory. No symlinks, permissions changes, repairs, unrelated-directory scans, deletions or mount changes occurred.

Foundation's ubiquitous-item property is unavailable on this ExFAT mount. The task guard reports that fact rather than treating it as positive evidence. Native physical USB/ExFAT identity, local statfs and alias/symlink checks establish the bounded task target independently; the production assessor's policy is never overridden. Rechecks and retained descriptors reduce ordinary substitution risk but are not a universal atomic check-and-use guarantee.

Final independent audit: **177,523 logical bytes; 25,165,824 allocated bytes including root directory (24 MiB)**, versus the 256 MiB cap. The observed peak allocated footprint was also 24 MiB. The filesystem uses 1 MiB allocation blocks. Automatically generated task-local `._` metadata sidecars, WAL/SHM, backup/restored DBs, retained successful/failed staging, canary and manifest are included. Only task-owned files were hashed. The manifest's own hash and its metadata sidecar are included in the separate final audit, avoiding a circular self-hash claim.

## Physical SQLite results

Provider: Python 3.9/system SQLite **3.51.0**, not production `Microsoft.Data.Sqlite` or its pinned `e_sqlite3`, and not the Lightflow Catalog schema. Synthetic tables contain CatalogId, RootId, 32 AssetIds and rating, keyword, collection, note, marker and subclip fields. The fixture changes one defined asset note to `Mac physical authored delta`; every other expected field and all IDs compare equal.

| Check | Observed result |
| --- | --- |
| Configuration | WAL; synchronous FULL=2; foreign keys ON; temp_store MEMORY=2; bounded 250 ms lock timeout; fullfsync=0, checkpoint_fullfsync=1 |
| WAL/SHM | Created physically and observed nonempty while active. After TRUNCATE/close: zero-byte WAL and 32 KiB SHM retained by this SQLite build; no sidecars deleted to obtain success |
| Cross-process exclusion | Independent process attempting BEGIN IMMEDIATE while parent holds it receives exact `database is locked`; corrected child exits 0 and closes its connection |
| Checkpoint / close / reopen | TRUNCATE `(0,0,0)`; normal close; integrity `ok`; all expected identities and authored rows preserved |
| Backup / restore | SQLite-aware backup physically on this drive; separately restored fixture integrity and every-row state equal. Backup byte hash differs from source as expected; logical equality is the criterion |
| Closed copy | Source and staging hash equal; final closed copy hash and every-row state equal |
| Incomplete/mismatched staging | Truncated and deliberately different task artifacts rejected; retained incomplete artifacts never activated; prior good destination and source hashes/state unchanged |
| Final assertions | 26/26 in the corrected same-root continuation, plus independent final audit and unchanged production-policy assessment |

Measured small-fixture costs: initial committed transaction 2.04 ms (first run before the diagnostic failure), successful physical checkpoint 2.25 ms, close 0.69 ms, backup including close guard 294.52 ms, guarded reopen/integrity 269.05 ms, guarded copy/verification/fallback publication 1,548.79 ms. Guard and native subprocess overhead dominate several values. These one-run, 32-row measurements are not representative large-Catalog performance or release budgets.

`fsync` returned success on a new task-owned canary (~0.247 ms). Darwin `fcntl(F_FULLFSYNC=51)` returned success (~0.518 ms), using the public SDK constant. These are observed API acknowledgements, not trace proof of SQLite's internal syscalls or evidence that the USB bridge/controller retains committed data through sudden power loss. SQLite PRAGMAs were observed, not changed to hide limitations. No intentional disconnect, crash injection or power interruption occurred.

## Negative evidence and safe correction

1. The first physical attempt reached WAL/FULL/sidecars and correctly observed `database is locked`. Its Python diagnostic handler then failed because Python 3.9 lacks `sqlite3.SQLITE_BUSY` and exception numeric-code attributes. Failed manifest/log retained. Corrected handler checks the exact lock diagnostic without claiming a numeric code. It resumed only the recognized diagnostic failure, reverified existing root ownership/inode and closed fixture integrity, and created **no second task directory**. Original IDs and all seeded rows were retained.
2. Native `renameatx_np(RENAME_EXCL)` returned **errno 45 / Operation not supported**. The staging file was preserved, and a new final copy was exclusively created, flushed and independently verified. This proves the bounded fixture's verified copy, **not atomic staged publication**. #392 must account for this physical filesystem limitation; no production transfer implementation is supplied or accepted here.
3. Read-only final audit initially supplied an arbitrary task directory to `diskutil info`, which refused that operand. Corrected audit queries the verified mount root and uses Foundation/statfs to bind the task directory to its volume. No external write resulted from the diagnostic correction.

No unexpected SQLite write/integrity errors occurred. Expected writer contention and the unsupported rename are retained separately from the harness/API-operand mistakes. Failed staging remains inside the task root for inspection; no task directory cleanup is authorized or performed.

## Cleanup and unchanged admission

All task transactions finished, checkpoint completed, SQLite/backup connections and file streams closed, child process exited, and root/volume descriptors closed. Native assessor count returned **0 → 0** after the post-phase assessment; the disposed provider failed closed. Independent `lsof +D` on the exact task root returned no holders (exit 1, empty stdout/stderr). Final hashes and allocated/logical sizes independently matched the run manifest/audit. No task-owned process continues using the drive. No physical eject was attempted.

Unchanged accepted Mac policy: ActiveCatalog/Open **Rejected / MissingCapabilities**; readable media Eligible; closed-transfer Read RequiresVerifiedStaging. Physical fixture success does not change ExFAT qualification/durable-write/companion facts from Unknown to Supported. These direct SQLite research operations are expressly owner-authorized outside product admission and create no Lightflow product writer.

## Next owner gate

**Ready from this task's handle/fixture perspective for a future ordinary-eject handoff**, subject to explicit owner authorization and the OS actually accepting safe eject. This does not certify that unrelated applications have no volume holders or that disconnect is already safe. Leave JRPhoto4T connected until that gate is approved.

Proposed Windows phase: owner-authorized ordinary Mac eject after application/assessor shutdown, DARKMATTER read-only native reidentification, device/partition and ExFAT identity reconciliation without assuming Windows GUIDs equal Mac UUIDs, fresh Windows assessor/shared policy, then separately approved bounded synthetic tests in this same retained task root. Verify incoming closed-copy hash before opening; retain its known-good bytes while writing a distinct Windows fixture and defined authored delta. Use one machine at a time; close/checkpoint, ordinary Windows removal, then return to Mac and verify fresh mount facts, IDs and expected state. Preserve the 256 MiB allocated-footprint bound on both OSs and stop on ambiguous identity or owner-data risk.

Still required: actual Windows native read/write/SQLite behavior, normal physical eject/reconnect, same-Catalog Windows-origin → Mac → Windows product round trip, #389 acceptance and #391/#392 integration, broader paths/Unicode and recovery qualification. Controller flush honesty, surprise removal and power-loss durability remain unqualified and are not proposed on owner data. Neither filesystem support nor full #406/R2 completion follows from this Mac phase.

## Evidence and reproduction

[physical-summary.json](physical-summary.json) contains sanitized results, complete fixture/audit hashes, assertion names, footprint, negative evidence and policy/cleanup. Exact private native identities, full path, failed/successful logs and environment/source fingerprints remain in the independent task workspace. All previous phase files are preserved.

Task-only harness sources: [physical_fixture.py](physical_fixture.py), [root_safety.m](root_safety.m). They are not production adapters or transfer orchestration. Copy to the task's internal `work` directory to match relative library/tool paths; compile helper with `xcrun clang -fobjc-arc -Wall -Wextra -Werror -framework Foundation work/root_safety.m -o work/root_safety`. Actual execution: `python3 -B work/physical_fixture.py`, followed by the disclosed `--resume <private failed manifest>` in the same root. **Do not rerun a fresh creation command for this assignment**: it already owns exactly one directory. Reproduction requiring another directory needs its own bounded owner approval. CI never executes the physical harness.
