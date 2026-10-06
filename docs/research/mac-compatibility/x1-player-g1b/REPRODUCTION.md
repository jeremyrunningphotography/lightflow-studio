# Reproduce G1b

Use an independent Apple Silicon task clone. Read AGENTS.md and obtain native Mac execution isolation before running shown-window probes. Do not use production data. A logged-in, unlocked desktop matters: drawable callbacks with `presentedTime == 0` are failures/unknown, never a presentation pass. Keep native runs serial. Outputs are written into the **new G1b evidence directory only**; existing G1 tools/docs remain frozen.

Hardware used: Mac14,9 M2 Pro (12 CPU/19 GPU), 32 GiB, macOS 26.6.2 build 25G83, arm64. Apple CLT clang 21.0.0 and Swift 6.3.3; no full Xcode or .NET was needed. [Original environment](../x1-player/environment.json) records the same machine/toolchain, with G1's historical versions distinct from G1b's [exact dependencies](DEPENDENCY_PROVENANCE.json).

Prerequisites: Python 3, Git, CLT, and an existing FFmpeg/ffprobe **9.0.1** generator with libx264. That GPL generator only creates task fixtures; native probes link the separately source-built minimal LGPL libraries. Do not substitute its libraries for the proof build.

```sh
cd /Users/jeremyrunning/Git/agents/Agent-X1-Mac-Player
mkdir -p work/deps work/g1b work/data

git clone --depth 1 --branch n9.0.1 https://github.com/FFmpeg/FFmpeg.git work/deps/ffmpeg-source
git -C work/deps/ffmpeg-source rev-parse HEAD
# Must be bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa
python3 tools/X1MacPlayerG1b/build_ffmpeg.py
python3 tools/X1MacPlayerG1b/prepare_inputs.py
python3 tools/X1MacPlayerG1b/fixtures.py
python3 tools/X1MacPlayerG1b/input_oracle.py
python3 tools/X1MacPlayerG1b/build.py
python3 tools/X1MacPlayerG1b/run.py
python3 tools/X1MacPlayerG1b/run_supplemental.py
# Explicitly confirm the Mac is unlocked before this bounded shown-host repeat:
python3 tools/X1MacPlayerG1b/run_unlocked.py
```

The first matrix lasts about five minutes; supplemental measurements add about three minutes and the unlocked repeat about one minute. Native windows close automatically. All audio playback segments use queue gain zero; the volume roundtrip briefly sets the parameter to .25 and immediately returns to zero, without an audibility test. Supplemental fullscreen changes are confined to the temporary task host. No physical sleep, output-device switch or system audio preference change is induced.

Expected injected process statuses: surface-pressure **13**, missing-file decode **2**, others **0**. The harness does not hide those injected failures. A process returning zero is not itself a presentation pass: inspect acknowledgements and oracle rows.

Optional historical short hardware variant:

```sh
/usr/bin/time -l work/g1b/color_hardware work/data/rotation.mp4
```

`hardware-color-unactivated` preserves the earlier source variant at `tools/X1MacPlayerG1b/color_hardware_unactivated.mm`. `hardware-color` preserves the later activated run before the Mac lock was discovered. `hardware-color-unlocked` is the authoritative successful end-to-end hardware Color run. Recreating a locked desktop failure is not required or recommended.

To repeat the pinned API investigation without X3's workspace:

```sh
git clone --depth 1 --branch 11.3.8 https://github.com/AvaloniaUI/Avalonia.git work/deps/avalonia
git -C work/deps/avalonia rev-parse HEAD
# Must be 6dd9eb473b74a56cc42e5bc118cfe918b48a940b
```

No Avalonia runtime integration is implemented or claimed. Before cleanup, `provenance.py` records dylib configuration, hashes and linkage. After new measurements, derive and preserve them:

```sh
python3 tools/X1MacPlayerG1b/provenance.py
python3 tools/X1MacPlayerG1b/analyze.py
python3 tools/X1MacPlayerG1b/pack_raw.py
python3 tools/X1MacPlayerG1b/verify_evidence.py
```

The analysis can read either fresh JSONL or archived `raw/*.jsonl.gz`. Decompress with Python `gzip.decompress` or `gzip -dc`. Archives preserve every original row, including linear reverse pixel oracles, failures and zero presentation timestamps. [Manifest](RAW_EVIDENCE_MANIFEST.json) records compressed and original SHA256/row counts. Numeric performance varies between runs; exact frame/PTS/lease identities must not.

Input bytes retained locally: original `work/data` (~59 MB), original G1 `work/results` (~42 MB) and new long fixtures in `work/g1b` (~9 MB). Exact regeneration commands/hashes and independent PTS arrays are in [fixtures](fixtures.json), [input oracle](INPUT_PTS_ORACLE.json), and unchanged G1 manifests referenced by `prepare_inputs.py`. No media or dylib is committed/distributed in the PR. After validation, remove only task-created dependency/build caches and native executables; retain exact corpus/evidence, then verify no task probe remains.

This is Mac-native research validation. It creates no packaged Lightflow executable, makes no Windows package readiness claim and requires no SSH/Windows control. Stop for owner review; no merge, X3 resumption or production implementation follows automatically.
