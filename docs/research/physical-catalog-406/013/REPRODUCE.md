# LF-MAC-RES-013 reproduction boundary

The physical assignment already owns exactly one retained root. **Do not execute a new physical run or alter that fixture without a separately reviewed owner scope.** The script deliberately does not resume, reuse a directory, delete evidence or clean up metadata. Its private baseline is an explicit input, not a device-discovery shortcut.

All commands run from the independent clone. Use task-local `work/toolchain/dotnet/dotnet`, .NET CLI/cache/tmp environment supplied by `tools/X2CatalogProof/run.py`. SDK 8.0.425/runtime 8.0.31 were copied into this task; no other agent's build output was reused. Build/test commands are in the report. Physical scripts are not CI tests.

## Exact local seed preparation used

Executed once inside task-local internal `work/evidence`:

```sh
python3 - <<'PY'
import sqlite3, uuid, json, pathlib
p = pathlib.Path('work/evidence/local-snapshot.db')
assert not p.exists()
c = sqlite3.connect(str(p))
cid, rid = str(uuid.uuid4()), str(uuid.uuid4())
c.executescript('create table identity(catalogid text, rootid text, note text); create table authored(assetid text primary key, rating integer, note text); pragma application_id=1279660081; pragma user_version=1;')
c.execute('insert into identity values(?,?,?)', (cid, rid, 'synthetic local finalized LF013'))
for i in range(16):
    c.execute('insert into authored values(?,?,?)', (str(uuid.uuid4()), i % 6, 'LF013 synthetic ' + str(i)))
c.commit()
assert c.execute('pragma integrity_check').fetchone()[0] == 'ok'
c.close()
pathlib.Path('work/evidence/source-identity.json').write_text(json.dumps({'CatalogId': cid, 'RootId': rid}))
PY
```

Generate canonical accepted-format bytes using the probe, after building it:

```sh
python3 - <<'PY'
import json, pathlib, subprocess
w = pathlib.Path.cwd()
cid = json.loads((w / 'work/evidence/source-identity.json').read_text())['CatalogId']
args = [str(w / 'work/toolchain/dotnet/dotnet'),
        str(w / 'docs/research/physical-catalog-406/013/bin/Release/net8.0/FormatProbe.dll'),
        'plan', str(w / 'work/evidence/local-snapshot.db'), cid]
r = subprocess.run(args, capture_output=True, text=True, check=True)
(w / 'work/evidence/format-plan.json').write_text(r.stdout)
PY
```

## Private baseline and physical execution already performed

Native discovery used `diskutil list external physical`, then `diskutil info -plist <discovered whole disk>` and `diskutil info -plist <discovered partition>`, plus `system_profiler SPUSBHostDataType -json`. Exact operands/outputs are private `current-disk.plist`, `current-volume.plist`, `current-usb-private.json`. Do not reuse a disk number or mount string from an earlier session.

Private baseline `safety-baseline-private.json` contains native disk/volume facts, exact original 007 root, original native 40-object check, proposed new UUID root, original 343-check verifier pass, absent-root/containment/empty-holder evidence, allocation unit, 100 MiB planned budget and 256 MiB limit. All stable device/partition/volume fields match retained `mac-return-native-private.json`; writable/local/physical/USB/ExFAT checks are required. The published `root_safety.m` checks Foundation aliases/cloud/volume facts and native statfs; compile as recorded in the report. Baseline generation and every private identity comparison must be reviewed before considering any separately authorized future run.

007 immutable verification used the unchanged command below, with independently discovered existing root and a new **internal** output filename:

```sh
python3 -B docs/research/physical-catalog-406/verify_mac_return.py \
  --root '<verified original 007 root>' --output "$PWD/work/evidence/<new-verifier-output>.json"
```

Actual physical command, once, after all gates:

```sh
python3 -B docs/research/physical-catalog-406/013/qualify.py
```

Authorized native execution outside the shell sandbox was necessary for DiskManagement and physical writes. Only a new task-owned root was claimed. File operations are exclusive/relative to retained descriptors; SQLite path-based effects are bracketed by native checks and are not a hostile-race-proof production lease. All synthetic failed containers, companions, symlink and manifests remain. No recursive delete, eject, formatting or repair occurs.

## Read-only protocol/native/final verification

Run the probe on the existing independently rebound fixture root; it creates no physical files:

```sh
work/toolchain/dotnet/dotnet \
  docs/research/physical-catalog-406/013/bin/Release/net8.0/FormatProbe.dll \
  verify '<verified existing 013 root>' > work/evidence/<new-readonly-output>.json
```

Do not convert the result into a PASS by omitting `._` entries. The actual physical good container is refused. An explicit internal projection of only the declared four files was used solely to diagnose the extra-entry blocker; it does not implement a supported producer or importer. Shared manifest/readiness serialization and validation are production code; enumeration/frame reading is an inspected research mirror of Windows effects code, not Windows native execution.

Read-only final audit uses `lstat` for every task object, SHA-256 on regular files only, complete SQLite state comparisons with `mode=ro&immutable=1`, and sums `st_blocks*512` for all objects including directories/links/sidecars. Known symlink is listed without dereferencing. Compare every prior regular-file hash, include `FINAL-MANIFEST.json`, and verify both fixture roots with `lsof -nP +D '<exact-root>'` (exit 1 and empty stdout/stderr). Native library create/probe/destroy count was 0→1→0. No path/identity uncertainty permits cleanup.

Read-only AppleDouble inspection used `/usr/bin/xattr '<exact task file>'` for names and first eight bytes of its task-owned companion. Python 3.9's missing `os.listxattr` diagnostic is retained; raw provenance attribute contents were not published. Header is AppleDouble 0x00051607/version 0x00020000.

## Private evidence retention

`work/evidence` retains the initial/final 007 verifier outputs; native raw inventories/baseline; local seed and canonical plan; physical manifest; actual protocol refusal and internal projection results; xattr/header observations; complete SQLite read-only audit; native anchors; final all-file hash/allocated-byte/holder audit; command/build logs and TRX. `work/native-host-013/results.json` retains 52 standalone host assertions. Public `summary.json` strips filesystem UUIDs/mount epochs/raw paths from diagnostics and contains all synthetic file hashes.

The new fixture's exact root is available to Jeremy in private evidence; publication uses only synthetic relative names. Reported namespace/flush API success is not crash or hardware persistence certification. No full Windows local Release/package or Mac production application acceptance is claimed.
