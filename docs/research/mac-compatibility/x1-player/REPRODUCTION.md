# Reproduction and isolation

Workspace used: `/Users/jeremyrunning/Git/agents/Agent-X1-Mac-Player`. Data root: `work/data`; output/captures: `work/results`; dependencies: `work/deps`. No Lightflow production executable is launched; no real Catalog/user data is read or written. Sources and small measurements are tracked; downloads, media, builds and captures are ignored under `/work/`.

Requirements: actual Apple Silicon, macOS SDK/Command Line Tools with clang and Apple frameworks, Python3, Git, make, an FFmpeg/ffprobe fixture-generator build matching recorded 9.0.1 configuration. No .NET, paid software, Apple program enrollment or global installation is required for this C/Objective-C spike. Test output remains muted. AppKit windows remain unshown and do not activate the user's desktop. Hardware/Metal/CoreAudio services require execution outside Codex's filesystem/process sandbox; approvals were obtained for these bounded probes. This does not establish general Mac private-desktop test isolation.

Run from an independent task clone at this branch, not another agent's workspace. These commands are task-owned and intentionally separate:

```sh
mkdir -p work/deps work/data work/results
git clone --depth 1 --branch v0.41.0 https://github.com/mpv-player/mpv.git work/deps/mpv
git clone --depth 1 --branch n9.0.1 https://github.com/FFmpeg/FFmpeg.git work/deps/ffmpeg-source
python3 tools/X1MacPlayerProof/fetch_bottles.py
python3 tools/X1MacPlayerProof/relocate_bottles.py
python3 tools/X1MacPlayerProof/build_lgpl.py
python3 tools/X1MacPlayerProof/build_probes.py
python3 tools/X1MacPlayerProof/fixtures.py
python3 tools/X1MacPlayerProof/run_mpv.py
python3 tools/X1MacPlayerProof/analyze.py
python3 tools/X1MacPlayerProof/run_decode.py --lgpl
work/decode_probe_lgpl work/data/rotation.mp4 0 no-save > docs/research/mac-compatibility/x1-player/ffmpeg-lgpl-rotation.jsonl 2> docs/research/mac-compatibility/x1-player/ffmpeg-lgpl-rotation.log
work/metal_probe work/data/decoded.bgra > docs/research/mac-compatibility/x1-player/metal-results.jsonl
```

Exact mpv header/source commit: `41f6a645068483470267271e1d09966ca3b9f413`. FFmpeg source: `bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa`. `fetch_bottles.py` uses the checked-in dependency manifest's exact bottle URLs/hashes when present. It extracts, verifies and relocates only task-owned files; it does not invoke `brew install`. Original bottle hashes and post-relocation dylib hashes differ intentionally. Minimal LGPL build verifies source commit, no source changes and disabled GPL/nonfree/version3 flags. The earlier Homebrew-linked controlled decode logs are preserved separately; final acceptance of component measurements uses the `ffmpeg-lgpl-*` logs.

Supplemental audio fixture, FFmpeg-decoded PCM, CoreAudio measurements and performance:

```sh
ffmpeg -v error -y -f lavfi -i testsrc2=size=320x180:rate=30:duration=40 -f lavfi -i sine=frequency=1000:sample_rate=48000:duration=40 -c:v libx264 -threads 1 -g 120 -bf 3 -c:a aac -shortest work/data/audio-sync.mp4
ffmpeg -v error -y -i work/data/audio-sync.mp4 -vn -ac 1 -ar 48000 -f f32le work/data/audio-sync.f32
work/mpv_probe work/data/audio-sync.mp4 0 audio > docs/research/mac-compatibility/x1-player/mpv-audio-sync.jsonl 2> docs/research/mac-compatibility/x1-player/mpv-audio-sync.log
work/audio_decode_probe work/data/audio-sync.mp4 work/data/audio-native.f32 > docs/research/mac-compatibility/x1-player/audio-native-decode.jsonl 2> docs/research/mac-compatibility/x1-player/audio-native-decode.log
shasum -a 256 work/data/audio-sync.f32 work/data/audio-native.f32
work/coreaudio_probe work/data/audio-native.f32 > docs/research/mac-compatibility/x1-player/coreaudio-results.jsonl
python3 tools/X1MacPlayerProof/summarize_audio.py
python3 tools/X1MacPlayerProof/performance.py
python3 tools/X1MacPlayerProof/supplemental.py
python3 tools/X1MacPlayerProof/provenance.py
python3 tools/X1MacPlayerProof/verify_evidence.py
git diff --check
```

Performance reuses existing fixtures; remove/move those task-owned files deliberately if regenerating with changed commands. Execute dependent runs sequentially: performance intentionally does not replace canonical captures. The measurement oracle retains decoded rational stream PTS and per-frame MD5 data; short corpus media is 320×180, performance supplements are 1080p/4K. Files/hashes/commands are in fixture manifests. Baseline libmpv results used an unoptimized probe; performance decode used `-O2`. Timed decode includes linear decode, three earlier-seek scans, CPU transfer/BGRA conversion and FNV hashing; it is not pure decoder throughput or an integrated realtime benchmark.

The rotation generator uses `-display_rotation 90` and asserts a real display matrix; FFmpeg 9 ignored the older `rotate=90` metadata command. The corrected rotated fixture aborts the pinned libmpv software renderer in `mp_image_crop` (SIGABRT, -6). This is an expected recorded failure in `run_mpv.py`, excluded from successful paused-sequence claims. Native FFmpeg reads counter-clockwise 90° / clockwise -90° without changing PTS.

Known exploratory failures: sandboxed VideoToolbox initialization was denied; approved native execution succeeded. The first AppKit probe crashed during autorelease teardown because `NSWindow.releasedWhenClosed` defaulted to true under ARC; explicitly setting false fixed it, followed by successful rendering and 20 host recreate/close cycles. Shader fast math caused a viewport edge sampling mismatch; precise math removed it. The unrelocated/relocated standalone mpv CLI was not the qualification target; an exploratory CLI version invocation exited abnormally, so exact runtime/dependency version is obtained from the successfully running libmpv C probe. No result from a failed attempt establishes capability.

This is a Draft spike artifact, not a Windows packaged hands-on handoff or PR-ready production change. Windows Release/private-desktop tests and the Windows package script were not run on Mac; no production files changed. If Windows comparison later becomes necessary, hand off a bounded reproduction to the existing private-desktop workflow. Do not control the Windows PC remotely.
