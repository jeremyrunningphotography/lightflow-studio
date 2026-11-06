# LF-MAC-DEV-036 — Phase B performance investigation and correction

## A. Assignment and provenance

Continuation of **LF-MAC-DEV-036 — Browser Thumbnail Foundation**, in the same independent full clone and branch. No new agent, issue, branch or PR was allocated.

- Workspace: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation`.
- Branch: `codex/036-browser-thumbnail-foundation`.
- Original packaged source: `32ac48ebbd91ab408775aa837a19fd153c8aa903`, tree `dc6b896c202e096a68f12706e28b1f322f58411a`.
- Current main verified: `7f755460e36375234281481cea3004f60344325b`.
- Existing [Draft PR #486](https://github.com/jeremyrunningphotography/lightflow-studio/pull/486), implementation [#481](https://github.com/jeremyrunningphotography/lightflow-studio/issues/481).
- Corrected immutable SHA/tree and package hashes are appended to the generated handoff after publication. This source report belongs to its containing implementation tree; it does not embed a self-referential commit hash.
- Phase A and Phase B reports, packages, binaries and acceptance/CI evidence are preserved. New results live only under `artifacts/lf036-performance`. The original packaged dylib was checksum-verified and copied before instrumentation.

## B. Executive summary

The owner reported 20–30+ seconds per DJI thumbnail on both NAS/Wi-Fi and an external SSD. This investigation reproduced the delay on the authorized real footage, including direct native calls outside Browser scheduling.

**Measured dominant cause:** the Mac descriptor resource-loader delegate synchronously served requested ranges toward EOF before returning, ignored AVFoundation's per-request cancellation/finished state, and blocked subsequent seek/range callbacks. Metadata loading could fail around the framework's waiting period while the adapter continued reading/draining. Poster extraction also reread large source ranges. The original instrumented three-file pipeline consumed gigabytes of logical reads and reached 2.39 GB sampled process RSS; it had two active workers, not a single decoder.

**Correction:** retain AVFoundation and the admitted descriptor, return promptly from the delegate, deliver one 64 KiB chunk per asynchronous queue turn, honor request/application cancellation, and stop/drain the read group before descriptor/flag/trace retirement. Shared policy, cache schema and Windows mechanisms are unchanged. Path-free, opt-in stage diagnostics and focused regression fixtures accompany the correction.

Primary instrumented baseline: first cache-ready completion **14,820.8 ms**, total three-file completion **30,572.2 ms**. Three corrected fresh-Preview runs: first completion **380.4–442.5 ms**, total **595.5–6,946.8 ms**. Corrected median completion across nine file observations was **442.9 ms**; the slowest remained **6,946.6 ms**, dominated by external source reads. This is substantial cold-Preview improvement on actual owner footage, not merely cache reuse. OS disk caches were not flushed; sample sizes are small and no universal video-time budget is asserted.

Owner acceptance remains pending. The fresh package and engineering smoke do not replace owner testing, independent DARKMATTER qualification, exact-head CI or explicit merge authorization.

## C. Authorized source handling

Authorized location: **owner-authorized DJI folder on JRPhoto4T; exact path is included in the local/portable owner handoff, not published filenames.**

JRPhoto4T was treated as read-only. Operations were enumeration/stat/xattr reads, existing sampled fingerprinting, ffprobe/FFmpeg reads, AVFoundation decoding and one necessary read-only copy into task-local SSD storage. No source write, rename, delete, timestamp/permission/ownership/xattr change, cleanup, remount, repair or file-management command was used. No owner Catalog was opened or modified.

Before testing, the older ThumbnailQualification harness was reviewed and rejected for owner-media use because it deliberately mutates disposable fixtures. The new performance harness has no source mutation operations, checks every generated path against this task's local performance root, and requires a new local Catalog/cache root. Production source effects use read-only descriptor admission and sampled preads; destination cache paths are guarded local paths. Direct file-URL AVFoundation comparison is restricted to a verified task-owned copy and is not a production hydration bypass.

The folder has **28 MP4s, approximately 20.06 GiB**, and discovery projects **35 supported media records**. Three MP4s were selected by size (smallest, middle, largest), aliased DJI-01–03. Private selection maps/filenames, owner-derived frame images, original stderr and Catalog databases stay outside portable/public evidence. Only DJI-02, 633,660,241 bytes, was copied for controlled local comparison; SHA-256 was calculated during that necessary copy and verified on the copy. No full-volume hash was performed.

Integrity checks compare selected size, modification/change times, mode, uid/gid, flags and xattr hashes, plus the directory entry list and all 35 Catalog-observed sizes/write times/version-1 sampled fingerprints across independent baseline/corrected Catalogs. These checks passed at the recorded checkpoint; the final GUI run is followed by another check. They do not assert an exhaustive full-file or whole-volume audit, nor filesystem-managed access-time preservation.

All generated data is within this clone's `artifacts/lf036-performance`, ordinary isolated test temporary directories, or task build/cache folders. No temporary output, sidecar, log, thumbnail or database was placed on JRPhoto4T.

## D. Baseline measurements

Original behavior was preserved in `baseline-native/`. Direct original calls reproduced metadata delays **13.6–30.0 seconds**, failed poster attempts **22.1–30.0 seconds**, and substantial memory/read amplification. One original direct poster read **3,142,707,440 logical bytes** for the 212,268,151-byte DJI-01 source. Instrumented counts are successful `pread` bytes, not physical device/network traffic.

Primary before/after uses the same managed diagnostic harness with the original native library resolved in a separate process. Its fresh local Catalog/cache discovered the same folder, then requested only the same three selected MP4s.

| Baseline stage/result | Measurement |
| --- | --- |
| Discovery/reconciliation | 14,194.5 ms; source fingerprint reads dominated |
| Queue/workers | Three queued requests; two actual running workers/readers |
| Metadata native calls | 14.4 / 24.3 / 14.6 seconds; failed, error code −11800 |
| Metadata logical reads | Approximately one full source each: 212.3 MB / 633.7 MB / 2.304 GB |
| Poster native calls | 224.9 / 273.8 / 1,013.6 ms after metadata had warmed much source data |
| Poster logical reads | 412.7 MB / 1.252 GB / 4.592 GB |
| Cache-ready completions | 14,820.8 / 24,772.8 / 30,572.0 ms |
| Gaps between completions | 9,952.0 / 5,799.2 ms |
| Total three-file demand | 30,572.2 ms |
| Populated-cache demand | 20.9 ms, with metadata still deferred/failed; not proof of correctness |
| Sampled maximum RSS | 2,386,870,272 bytes |

Open/initialization costs were tiny relative to the metadata read/drain delay. Pixel conversion/resize/PNG write was about 7–9 ms per poster; projection about 6 ms. Metadata and source-read timings overlap and must not be added as independent stages. Native work stayed in its worker slot until callbacks drained. Preview coordination/database work did not hold an exclusive lock across native decoding; two active readers were directly observed. The apparent serial arrival came from expensive work occupying those two slots and queueing later files.

An earlier uninstrumented full run took 70 seconds with one failed thumbnail and much slower discovery. It is preserved as a reproduction, not the primary controlled comparison: first-touch storage state and overlap with earlier direct diagnostics limit its comparability.

## E. Decoder comparison

All selected primary tracks are HEVC Main 10, 3840×2160, yuv420p10le, tagged BT.709. DJI-01 is 30000/1001 fps at about 80 Mb/s; DJI-02 is 60000/1001 fps at about 100 Mb/s. DJI-03 nominally reports 60000/1001, but average rate is **178040000/2971969**; frame identity therefore uses actual presentation samples, not nominal-rate multiplication. Additional AAC/data/embedded MJPEG streams were observed; no claim is made that all DJI files share parameters.

| Same-source direct measurement | DJI-01 | DJI-02 | DJI-03 |
| --- | ---: | ---: | ---: |
| Original native metadata | 13,579 ms | 30,002 ms, cancellation requested | 30,002 ms, cancellation requested |
| Original native poster | 1,023 ms | 22,103 ms, failed | 30,006 ms, failed |
| Corrected native metadata | 1,837 ms | 2,028 ms | 3.3 ms |
| Corrected native poster | 5,486 ms | 1,405 ms | 62.8 ms |
| Host FFmpeg Windows-style JPEG | 5,143 ms | 374 ms | 535 ms |

These serial trials reflect varying OS/device cache state and include external-read tails. They do not establish a universal ranking. Corrected repeated complete-pipeline cold-Preview results below are the practical comparison.

Controlled **identical local copy of DJI-02**: original descriptor metadata/poster **144.4 / 366.2 ms**, corrected **28.5 / 152.5 ms**. Logical reads fell from **634,439,332 / 1,205,257,892 bytes** to **1,303,379 / 6,873,939 bytes**. Ordinary file-URL AVFoundation on that copy took **213.7 ms total** (14.5 initialization, 10.3 metadata, 165.1 combined seek/decode, 23.8 PNG write). This isolates the adapter's read amplification from Browser scheduling and supports retaining the narrow descriptor path.

FFmpeg is the already-installed host tool (9.0.1), using established Windows-style input seek/scale/MJPEG arguments. It is not distributed in the Mac application. No production switch was justified: the corrected AVFoundation path is fast on the larger samples, preserves native guard/lifecycle architecture, and avoids introducing a Mac FFmpeg distribution/configuration complication. FFmpeg remains a comparison oracle, not a new runtime dependency.

Requested/actual source presentation times:

| Alias | Requested seconds | Mac actual | FFmpeg actual | Adjacent presentation-sample difference |
| --- | ---: | --- | --- | ---: |
| DJI-01 | 1.78178 | 53053/30000 | 54054/30000 | −1 |
| DJI-02 | 4.079075 | 244244/60000 | 245245/60000 | −1 |
| DJI-03 | 14.8608 | 890890/60000 | 891891/60000 | −1 |

Bounded ffprobe source-presentation windows identify actual neighboring samples, including the variable-timing clip. A timestamp-preserving diagnostic extraction produced byte-identical JPEGs to the unmodified FFmpeg reference command. No new frame was selected to improve speed. Baseline pipeline metadata failures sometimes forced its existing 1-second fallback; the correction makes duration available, so the unchanged 10%-with-clamps policy now uses that duration. Controlled decoder comparisons always requested identical explicit times.

## F. Correction

`Lightflow.Platform.MacOS/native/thumbnails.m` changes the resource-loading delegate to schedule bounded asynchronous reads and return immediately. Each 64 KiB chunk yields to new ranges/cancellations, checks request canceled/finished state and application cancellation, and preserves the existing read-only/non-following/residency/no-hydration admission. Session stop marks the reader stopping, removes its delegate and drains its tracked read group/queue before fd, cancel flag or diagnostics storage can retire. No reader slot is released early.

Native stage/byte counters, Application ActivitySource spans, Mac composition timings, optional Desktop trace writing and UI dispatch/assignment/next-frame callbacks provide path-free diagnostics. They are inactive without a listener/explicit trace flag. No Browser navigation/selection owner or media engine is replaced. The standard Avalonia Image remains in use.

Focused tests generate a tiny H.264 MP4 and insert a sparse 64 MiB free atom before its tail moov. The original reader reads 67,168,316 bytes; the corrected regression trial reads roughly 0.36–0.42 MB. The byte-bound oracle demonstrably fails the original and passes the correction in **separate processes**, avoiding duplicate Objective-C class registration. Native cancellation tests cover group/descriptor/flag drain.

Preserved: Preview schema 4, generator/probe versions, cache naming/identity and persisted deadlines, source/work revalidation, preferred-frame and Color/LUT intent, two-worker bounds, stale-result rejection, local Catalog safety, image/RAW mechanisms and Windows WIC/FFmpeg behavior. Existing failed-cache cooldowns are intentionally unchanged; use the fresh acceptance root for cold validation rather than an older deferred failure profile.

Rejected alternatives: increasing worker count (would amplify memory/I/O), changing poster time/keyframe policy, removing residency or source checks, adding a decoder/cache engine, or shipping a new FFmpeg bundle before demonstrating need. The metadata/poster asset instances remain separate, with their small repeated header work measured rather than hidden.

Apple documents [incremental data delivery](https://developer.apple.com/documentation/avfoundation/avassetresourceloadingdatarequest/respond%28with%3A%29?language=objc) and [canceled/superseded loading requests](https://developer.apple.com/documentation/avfoundation/avassetresourceloaderdelegate/resourceloader%28_%3Adidcancel%3A%29-3nl51?changes=la&language=objc); the application measurements establish this defect and correction.

## G. Corrected measurements

Three independent fresh-Preview data roots, identical selected files, one process at a time; populated-cache demand follows each cold demand. OS/device caches were not forcibly flushed.

| Run | Discovery ms | First completion ms | All three ms | Warm demand ms |
| --- | ---: | ---: | ---: | ---: |
| Corrected 1 | 11,612.5 | 380.4 | 6,946.8 | 18.2 |
| Corrected 2 | 3,619.0 | 442.5 | 608.7 | 19.0 |
| Corrected 3 | 854.9 | 435.7 | 595.5 | 20.2 |

Median cache-ready completion across nine observations: **442.9 ms**; maximum **6,946.6 ms**. With this small repeated corpus, the maximum is reported as the slower tail rather than a misleading universal percentile. Gaps were 187.7/6,378.5 ms in the external-read-tail run, then 0.4/165.6 and 0.4/159.3 ms. Baseline gaps were 9,952.0/5,799.2 ms.

Two actual native readers/workers remained active; only three selected requests were queued, within the existing 192-ID cap. Sampled RSS maximum across corrected runs was **141,754,368 bytes**, versus baseline **2,386,870,272**. Process CPU across whole discovery/cold/warm runs was 1.35–2.10 seconds corrected versus 4.57 baseline; most baseline wall time was waiting/reading, not CPU decoding. RSS is sampled at 50 ms and is not an exact OS/global GPU peak. FFmpeg child CPU and cumulative child-RSS results are in its comparison JSON; bytes/seek startup are not reliably separable for that external process.

Cold-Preview improvement is demonstrated even though one file has a remaining external-read tail. Discovery is separately variable and unchanged by the decoder fix: repeated source fingerprint reads dominate it. Do not attribute the 14.2→0.85-second discovery change to a new algorithm; caching and storage state matter. Larger synthetic-folder demand remains capped at 192 and is remeasured in `large-folder.json`; full-scope enumeration remains an existing scaling cost.

A real-media cancellation/revisit trial observed active native work, canceled the old batch, requested immediate replacement, and completed replacement plus session close in **722.6 ms**. All three replacements succeeded, readers returned to zero and no `.lightflow` temporary artifacts remained. UI stale-scope rejection remains covered by existing UI tests.

AVFoundation exposes combined seek/decode time; conversion/resize/native PNG write is also combined. UI timing logs dispatch queue wait, cache reads, bitmap assignment and the next render-frame callback. That callback is a presentation approximation, not compositor/hardware scanout. Final packaged GUI measurements and smoke results are appended to the generated report, not invented from worker completion timestamps.

## H. Regression validation

Final automated tests: **345 passed, 0 failed, 0 skipped** — Application 141, Catalog 67, UI 75, Mac platform 62. Windows application/test cross-build: **0 errors** (three pre-existing test-analyzer warnings). Native helpers compile with warnings as errors. Commands/logs are in `final-commands.json` and `final-*.log`.

The two new native regressions cover tail-moov read amplification and cancellation/drain. Existing shared tests preserve cache/retry, source/Color/preferred-frame mutation, queue promotion/cancellation, storage containment and stale UI behavior. The copied disposable 28-fixture still/RAW/video corpus again produced **25 Ready / 3 intentionally Unsupported**, with restart/rebuild/source-replacement checks and zero final readers. No old qualification harness ran on owner media. Synthetic CFR/VFR/HEVC/rotated/short/long poster comparisons were repeated; results are in `synthetic-posters/comparison.json`. Owner-media actual presentation samples were all −1 frame from FFmpeg.

Earlier failed fixture-atom parsing, unsupported Image.Render override instrumentation and the invalid same-process native-library comparison are preserved as failed engineering trials; corrected passing runs supersede only those trials. An optional heuristic whitespace cleanup was rejected by automatic approval review and dropped; the correction was validated without it. No approval block remains on necessary work.

Independent DARKMATTER Windows functional/package qualification and owner acceptance are not supplied by local cross-compilation or CI. Wider RAW/HDR/Color acceptance limits from Phase B remain; this correction does not expand advertised format support.

## I. Mac package and owner acceptance

Fresh app location: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-performance/package/Lightflow Studio.app`.
Executable: `Contents/MacOS/LightflowStudio.Mac` in that bundle. Package source/file hashes, timestamp, dependency checks and smoke results are recorded in the generated build receipt. It is an unsigned/unnotarized arm64 development bundle; no owner media is packaged.

Fresh isolated owner root: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-performance/owner-acceptance-data`. Its prepared empty `Catalog` directory is separate from every engineering root and earlier owner root.

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-performance/package/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-performance/owner-acceptance-data"
```

1. Create the disposable Catalog in the prepared folder; open the authorized DJI folder as a media source. Keep all Catalog/cache output local.
2. Time first visible thumbnails and subsequent arrivals with the fresh cache. Judge the corrected cold case, not only a warm revisit.
3. Scroll and switch between the authorized folder and a task-owned fixture folder during generation; confirm no stale images or navigation stall.
4. Revisit/reopen and check warm reuse, source states and overall Browser integrity.
5. Confirm representative still/RAW/video behavior remains acceptable. Do not use source-management commands on JRPhoto4T.
6. Report remaining slow files by private alias/path to the owner review, preserving original files. Wider mounted-NAS acceptance remains pending; no other NAS media location was authorized for this investigation.

The owner has explicitly authorized visible desktop control until revoked. Agent packaged smoke uses a separate `gui-smoke-data` root and never counts as owner hands-on acceptance.

## J. GitHub and historical maintenance

#481 remains open/In Progress/Browser, priority unset; native parent #480 retained, with #395/#366 above it. Project #4 was freshly read back. #480 and #482–#485 remain Backlog/Browser/unset, with no subsequent work or agent allocated. Draft PR #486 records the owner regression, measurements and bounded correction. No merge or closure.

Registry updated in the same implementation PR. **Historical maintenance: Not warranted for this correction** — a bounded performance/reader bug fix within the existing architecture, not a new product or architectural milestone. The earlier Phase B Chronicle/History/Source Index entries are preserved and remain implementation-pending-acceptance records.

## K. Artifact manifest

- Complete owner report: `artifacts/lf036-performance/REPORT.md`.
- Fresh portable evidence ZIP: `artifacts/LF-MAC-DEV-036-Performance-evidence.zip`.
- Fresh runnable app: `artifacts/lf036-performance/package/Lightflow Studio.app`; separate app ZIP `artifacts/LF-MAC-DEV-036-Performance-Mac-app.zip`.
- Baseline: `evidence/baseline-direct.json`, `baseline-profile.json`, `baseline-pipeline.json`, `baseline-traced.json` and stage stream.
- Corrected: `corrected-direct.json`, `corrected-pipeline.json`, `corrected-repeat-2.json`, `corrected-repeat-3.json`, stage streams and `performance-summary.json`.
- Comparison: `ffmpeg-comparison.json`, local-copy/native/file-URL results, `owner-frame-comparison.json`, generated read-regression result and synthetic posters.
- Validation: `final-*.log`, `final-commands.json`, `format-regressions.json`, `navigation-cancellation.json`, `large-folder.json`, `source-integrity.json` and Project readback.
- Source/build hashes, GUI timing, archive checksum receipts and full manifest are appended/generated after exact-head publication/build.
- Excluded: owner MP4s and derived frame images, private filename/selection maps, all Catalog databases, local media copy, baseline binaries, SDK/package/large caches and credentials.

## L. Outstanding gates

Owner hands-on acceptance of corrected cold DJI performance, independent DARKMATTER qualification, corrected exact-head CI/review, practical NAS acceptance and explicit merge authorization remain required. Agent GUI smoke is separate. Existing metadata/RAW/HDR acceptance limitations remain. A remaining external-read tail and full-folder fingerprinting cost are reported rather than hidden by cache results.

**STOP FOR OWNER ACCEPTANCE.** Do not merge #486, close #481, start #482 or allocate another agent.
