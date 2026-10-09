# Read-only Mac return — LF-MAC-RES-007

Assignment 007 · Issue #406 · Draft #407 · 2026-10-09

**PASS for the bounded read-only synthetic fixture return; CONDITIONAL overall qualification.** The same physical ExFAT drive returned from Windows with all original files preserved and its Windows-authored derivative intact. This demonstrates compatible synthetic closed-SQLite portability, not production Lightflow Catalog support. Active ExFAT Catalog admission remains rejected. Stop for independent/owner technical acceptance; no merge, support declaration or production implementation.

## Identity, authority and confinement

Jeremy authorized read-only return verification and confirmed reconnection after normal Windows safe removal. The Windows evidence at `8de9295b71b916168c6249667166a40d100d4cce` records CM_Request_Device_EjectW CR_SUCCESS=0/no veto. Earlier normal Mac eject is owner-reported, not agent-observed.

Native discovery independently found the mounted external physical USB Extreme Pro 55AF, GPT, ExFAT and local mount. Disk size 4,000,753,467,904 bytes and partition size 4,000,751,370,752 bytes exactly match retained pre-handoff Mac diskutil metadata. Original private volume and partition UUIDs also match; no BSD identifier or mount spelling was assumed. Full path/identifiers remain private. Existing root ownership was verified; 40 objects (root, Windows subdirectory, 38 files) passed native alias checks and symlink/type/volume checks. Enumeration stayed within the existing synthetic root. No replacement directory, repair, physical writes, authoring or eject occurred.

An initial strict assertion incorrectly compared the long USB product label against diskutil MediaName. Comparing the retained pre-handoff diskutil record resolved it: both report `Extreme Pro 55AF` with identical size. This diagnostic refusal occurred before verifier execution and is preserved privately; no changed device identity was ignored.

## Executed verification

The unmodified published [verifier](verify_mac_return.py) ran with Python/system SQLite **3.51.0**, `mode=ro&immutable=1` for every SQLite connection. Output was exclusively internal task storage. Windows used production-pinned Microsoft.Data.Sqlite 8.0.29 / SQLitePCLRaw bundle 2.1.13 / native SQLite 3.53.3, with the same two-table synthetic schema; actual Lightflow schema/service parity remains untested.

```sh
python3 -B docs/research/physical-catalog-406/verify_mac_return.py \
  --root "<native-verified existing physical task root>" \
  --output "<internal Mac task workspace>/work/evidence/mac-return.json"
```

The exact private command/path and native inventory are retained in `work/evidence` of the independent Mac clone. The clone remains `LF-BOTH-RES-007-ExternalCatalog`; historical names/evidence are unchanged.

| Result | Observation |
| --- | --- |
| Published verifier | **343/343 checks passed**; many are per-file containment/hash assertions, not 343 independent scenarios |
| Original Mac files | **24/24** sizes and SHA-256 match private original Mac audit and Windows final manifest |
| Windows-created files | **14/14** sizes and SHA-256 match Windows final manifest |
| Immutable reads | Original closed Mac copy and five Windows database artifacts integrity `ok`; complete expected rows/identities match |
| Stable identities | CatalogId, RootId and all **32 AssetIds** preserved |
| Authored delta | Only asset `015db1c9-1b7f-41d9-8d07-8e3a73fbee9c` note becomes `Windows physical authored delta LF-WIN-RES-007` in Windows derivatives; original retains `Mac physical authored delta` |
| Other metadata | Every rating, keyword, collection, note, marker/subclip field matches the full expected-state rows |
| Mutation control | All files rehashed after SQLite reads and again after native assessment; no hash/size mismatch, unexpected sidecar or extra task file |
| Footprint | **340,050 logical bytes; 38,797,312 allocated bytes including directories (37 MiB)**, below 256 MiB |

Original Mac closed SHA-256 remains `d9a9010a5733d8342c57cece8a75896ade106265b3c205d250e7f35f88352008`; Windows closed derivative remains `a1e7a33dba9dde85b0d60c4f6baf3b5d90ee4f615a586d421c5c4adb2e634e30`. All 38 hashes, complete verifier assertions and sanitized native/cleanup facts are in [mac-return-summary.json](mac-return-summary.json); full expected rows remain in [windows-summary.json](windows-summary.json).

## Resource and policy evidence

All immutable SQLite connections/file streams closed in the verifier; the process exited 0. Native helpers exited normally. The unchanged accepted Mac assessor was run for metadata-only Open/Read requests and disposed deterministically: native anchor count **0 → 0**, disposed assessor Status Failed, cleanup passed. Independent final `lsof -nP +D <exact-root>` returned exit 1 with empty stdout/stderr: no task-root holders. No task process retains fixture files. The drive remains connected; this phase did not request eject.

Sandboxed metadata assessment initially reported Write Unsupported and ActiveCatalog Rejected/ReadOnly. Native statfs/diskutil reported a writable mount. A metadata-only rerun outside the sandbox resolved the capability observation: ActiveCatalog/Open **Rejected/MissingCapabilities**, MediaSource/Read Eligible, ClosedCatalogBackupTransfer/Read RequiresVerifiedStaging. Both observations are retained privately; neither requested a physical write. ExFAT qualification/durability/companion facts remain Unknown, not Supported. No policy/allowlist overrides.

## Remaining acceptance boundary

No fixture hash, integrity, ID or authored-state discrepancy was found. Earlier Mac/Windows failed experiments remain preserved. No unrelated owner files were read or modified; this is confinement evidence, not a whole-drive before/after personal-data audit.

This completes Mac → pinned-provider Windows authored derivative → immutable Mac synthetic return. It does not demonstrate the full Windows → Mac → Windows product workflow, production Catalog lifecycle/schema, Settings relocation/restore activation, root/media remapping, broader case/Unicode/alias/cancellation or fault recovery. #389/#391/#392 accepted integration remains required; blocked Draft #403 is not shipping support. Mac exclusive rename errno45 still limits atomic staging publication; a Windows canary rename does not resolve that. Hardware controller flush honesty, surprise removal and power-loss durability remain unqualified.

ExFAT remains a CONDITIONAL closed synthetic-transfer candidate and unqualified/refused for active Lightflow Catalog use. Preserve #406 Open/In Progress/Catalog/Priority unset, native parent #77 and blocker #393; #407 Draft/unmerged. Historical Mac and Windows reports are untouched. Independent review and Jeremy's technical/support-matrix acceptance remain outstanding.
