# Reproduction and bounded handoff

Independent Apple Silicon workspace /Users/jeremyrunning/Git/agents/Agent-X1-Player-Completion; base7dec8f7372ba0af06bc15969ec0a976de7984900. Task-only data/root and output files, no user Catalog/media or production executable. No subjective computer-controlled acceptance. Use a fresh output directory/branch to avoid replacing preserved raw evidence.

```sh
cd /Users/jeremyrunning/Git/agents/Agent-X1-Player-Completion
mkdir -p work/deps work/audio/data-root
git clone --depth 1 --branch n9.0.1 https://github.com/FFmpeg/FFmpeg.git work/deps/ffmpeg-source
# Verify bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa
python3 tools/X1MacPlayerG1b/build_ffmpeg.py
# Existing isolated audio-sync.mp4 fixture is preserved; regeneration command is
# in docs/research/mac-compatibility/x1-player/audio-sync-fixture.json.
python3 tools/X1AudioCompletion/build.py
work/audio/audio-proof feed "$PWD/work/data/audio-sync.mp4" --data-root "$PWD/work/audio/data-root"
python3 tools/X1AudioCompletion/clock_oracle.py
python3 tools/X1AudioCompletion/run.py
python3 tools/X1AudioCompletion/verify.py
```

Native output must be available. The matrix stops on failed start, preserves return code/logs, and does not substitute simulated clocks. Audio gain0, no system device/sleep/service change. Native backend snapshots are RenderReady fixtures, not actual Avalonia UIAccepted or physical display. The raw prior sandbox/access failure is not a Mac capability result. The final native matrix and bounded retry stress require ordinary native audio-service access.

Current owner handoff: confirm ordinary built-in audio playback works with Mac awake/unlocked; no answer is assumed. If output still fails, reproduce the frozen all-zero native baseline (gain1 but digital silence):

```sh
cd /Users/jeremyrunning/Git/agents/Agent-X1-Player-Completion
mkdir -p work/audio
clang -O2 tools/X1AudioCompletion/silent_start_rca.c -framework AudioToolbox -framework CoreFoundation -o work/audio/silent-start
work/audio/silent-start "$PWD/work/data/audio-native.f32"
```

The baseline is task-only, no app storage/configuration loader, consumes only the explicit task input, and does not listen on the owner's behalf. No packaged Lightflow executable or Windows startup command is applicable to this research-only native slice. Do not relaunch production/user data. Do not silently restart audio services or change devices to manufacture a pass. Successful native restoration should rerun only the bounded audio matrix, not other G1 investigations.

Build retains the frozen Metal fastMathEnabled deprecation warning. Prior failed versions and AAC-preroll fixes are described in DIAGNOSTIC_ATTEMPTS.json; unsuccessful raw rows remain negative evidence. Source/build/license pins and fixture hashes are in DEPENDENCY_PROVENANCE.json. After archival, remove task-only builds/dependencies, retain reproducible inputs and raw compressed logs, confirm no proof process, and stop for owner review. Do not merge the new Draft PR.
