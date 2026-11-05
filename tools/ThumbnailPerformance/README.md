# LF-MAC-DEV-036 performance diagnostics

This is an engineering harness, not an app dependency. It never creates, rewrites, renames, deletes, repairs, or changes metadata on source media. **Do not use ThumbnailQualification on owner media:** that older harness intentionally mutates disposable fixtures.

All generated data must be beneath this clone's `artifacts/lf036-performance`. The C# harness rejects any output root outside it and requires a new data root. Sources must already exist in one explicitly authorized folder; they are accessed only through the production read-only Browser/source/decoder paths. Catalog/cache writes remain local. No permission or mount change is used. The owner's exact MP4 filenames and private Catalogs are excluded from portable evidence.

Create a private `source-selection.json` containing an array of `{ "alias": "DJI-01", "path": "<authorized absolute source file>", "size": <bytes> }`. Aliases, not filenames, appear in exported observations. Do not publish that selection file. The owner authorization in this assignment is limited to the named DJI folder; a harness argument is not permission to inspect other media.

```sh
DOTNET_CLI_HOME="$PWD/work/dotnet-home" .cache/dotnet/dotnet run --project tools/ThumbnailPerformance -c Release --disable-build-servers -- "$PWD/artifacts/lf036-performance/private/source-selection.json" "$PWD/artifacts/lf036-performance/new-data" "$PWD/artifacts/lf036-performance/evidence/new-run.json"
```

Optional fourth argument: preserved baseline native library in task storage, allowing the identical managed harness to compare original native behavior. It uses a resolver before any native call. Do not load original and corrected Objective-C libraries in one process. `LF036_CANCEL=1` exercises canceled demand, immediate replacement and session drain. Normal mode performs fresh-Preview cold demand and populated-cache demand; neither claims an OS disk-cache flush. Capture repeated runs rather than relabeling warm OS caches as physical cold storage.

`measure-native.py` profiles direct native metadata/poster calls. Set `LF_PROFILE=1` for instrumented libraries; `LF_SELECTION` can select the verified task-owned local copy. The original binary and instrumented-original source are preserved outside git in `baseline-native/`. Native read bytes are logical `pread` bytes, not physical disk traffic. `compare-ffmpeg.py` uses existing Windows-style extraction arguments with host FFmpeg; no FFmpeg dependency is added to the app. Owner-derived images and original stderr stay private. `compare-frame-identity.py` uses actual bounded source presentation samples, including variable timing, and verifies timestamp instrumentation leaves FFmpeg reference JPEG pixels unchanged.

`direct-av.m` compares ordinary file-URL AVFoundation **only on a verified local task-owned copy**; both input and output paths are checked against `LF036_TASK_ROOT`. It is not an alternative production path or an authorization to bypass cloud admission. `check-read-regression.py` generates a tiny H.264 movie plus a sparse 64 MiB free atom, compares native libraries in separate processes, and demonstrates that the byte-bound oracle fails the original reader and passes the correction.

For packaged GUI timing, set `LIGHTFLOW_THUMBNAIL_TRACE=1` while launching with an explicit isolated `--data-root`. The app writes path-free `thumbnail-trace.jsonl` inside that root. Trace IDs, ephemeral AssetIds, monotonic timings, queue counts and native counters correlate work without filenames. `ui.present.next_frame` is the next render-frame callback after assignment, not hardware/compositor scanout. AVFoundation exposes combined seek/decode time; native conversion/resize/PNG write is also combined. Do not invent separate substage measurements.

The report distinguishes owner-media evidence from synthetic regression fixtures, generated results from owner acceptance, and cross-build/CI from independent DARKMATTER qualification. Never include owner MP4s, owner-derived frame images, private selection maps, Catalog databases or large caches in the portable ZIP.
