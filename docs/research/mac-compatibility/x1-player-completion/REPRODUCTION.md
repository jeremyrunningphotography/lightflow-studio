# Reproduce the bounded completion slice

Use an independent Apple Silicon clone and current accepted main. No production project or owner media/Catalog data is used. All inputs, libraries, binaries and explicit data roots stay in this clone. Native execution is serial and AudioQueue gain is zero. The initial native baseline creates one bounded drawable window; other capacity/semantic tests use GPU snapshots without a shown window. Do not change system output, induce sleep or infer physical presentation from offscreen results.

Requirements: Python3.9+, Apple CLT/clang, installed FFmpeg9.0.1/ffprobe with libx264 for generated fixtures. Native library linkage is a separate pinned minimal LGPL FFmpeg build, never the installed GPL generator libraries.

```sh
cd /Users/jeremyrunning/Git/agents/Agent-X1-Player-Completion
mkdir -p work/deps work/g1b work/completion work/data-root
git clone --depth 1 --branch n9.0.1 https://github.com/FFmpeg/FFmpeg.git work/deps/ffmpeg-source
git -C work/deps/ffmpeg-source rev-parse HEAD
# Required: bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa
python3 tools/X1MacPlayerG1b/build_ffmpeg.py
python3 tools/X1PlayerCompletion/fixtures.py
python3 tools/X1PlayerCompletion/build.py
python3 tools/X1PlayerCompletion/run.py
python3 tools/X1PlayerCompletion/run_extended.py
work/completion/completion render-fault "$PWD/work/data/color.mp4" \
  --data-root "$PWD/work/data-root" \
  > docs/research/mac-compatibility/x1-player-completion/raw/render-fault.jsonl
python3 tools/X1PlayerCompletion/state_oracle.py
python3 tools/X1PlayerCompletion/analyze.py
python3 tools/X1PlayerCompletion/verify.py
```

Use a new output directory/branch for future reproduction; these commands otherwise replace this new evidence set. Never overwrite frozen G1/G1b/X3 reports. `build.py` derives native code from the frozen G1b source with checked single-match transformations; only `work/completion/generated.mm` is generated. Final executable requires an absolute `--data-root`, has no user configuration/storage loader, consumes explicit task input paths, and emits stdout. Original run records retain their actual pre-root-guard command arguments; every input was task-owned and no owner storage was used.

The matrix is approximately four minutes, plus fixture/build time. Native baseline12s;4K Color on/off60s each;1080p15s;.5x/1x20s each;2x15s;live rates22s;loops8s;recovery8s. Extended checks cover semantic capture, tempo content, real refill starvation and corrected startup timing. `run.py` stops on nonzero process status; do not treat a failure as a pass. One additional audio attempt returned3/CannotStart(-66681); this negative result is archived separately and not force-retried. Reproduction may encounter native service availability issues; exact cause must be diagnosed rather than changing owner system settings.

Original matrix used the initial timing derivative; its first-frame decode duration was incorrectly initialized to an absolute timestamp. Analysis excludes that one invalid startup metric per run, preserving raw values. Final source fixes it; corrected5s repeat and final GPU semantic smoke succeeded. Compiler retains one deprecated `fastMathEnabled` warning from the frozen numerical shader settings; no behavior change or zero-warning claim. Final source also includes the explicitly labelled render-delay injection; reproducing the fault case adds that delay, while sustained cases remain unchanged.

Raw JSONL is compressed losslessly after processes stop; manifest checks both compressed and original SHA256 and row count. The verifier is offline and must remain runnable after deleting native builds/caches. State oracle is a deterministic protocol simulation, not an Avalonia integration test. Actual UIAccepted callback remains later X3 work after owner authorization. No physical AV/scanout claim, packaged Lightflow executable, Windows package readiness or paid dependency is inferred.

After archival/verification, remove only task-created `work/deps` and binaries/build outputs. Preserve exact fixtures as useful audit inputs, or regenerate from FIXTURES.json. Preserve negative logs and accepted prior reports. Check no completion process remains; stop for owner review, no merge or production implementation.
