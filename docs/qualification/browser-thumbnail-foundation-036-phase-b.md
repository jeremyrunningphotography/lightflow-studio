# LF-MAC-DEV-036 — Phase B: Shared Thumbnail Foundation Implementation

## A. Assignment and provenance

- Agent/conversation: **LF-MAC-DEV-036 — Browser Thumbnail Foundation**; continuation of the same assignment, no sub-agents.
- Issue [#481](https://github.com/jeremyrunningphotography/lightflow-studio/issues/481), native hierarchy #366 → #395 → #480 → #481. #482–#485 are unstarted.
- Workspace: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation`.
- Branch: `codex/036-browser-thumbnail-foundation`; PR target `main`.
- Starting accepted main: `7f755460e36375234281481cea3004f60344325b`; Phase A audit source: `ea8f3242bf68d153880c2cef7775f2ea60675bc8` (reservation `c24acbf0735f8fd83ab148802970ed1c3c571dd7`). Phase A evidence is preserved.
- The final immutable implementation SHA/tree, Draft PR URL and package hashes are appended to the generated handoff after publication, avoiding a self-referential source hash. The package's adjacent `provenance.json` stamps HEAD, branch, dirtiness and every bundle file. Unrelated Finder metadata `.DS_Store` and `docs/.DS_Store` remains untracked and excluded from publication and ZIPs; the docs file predated implementation.
- Registry: Phase B implemented for Draft review; not Done, accepted, merged or released.

## B. Executive summary

Implemented the approved shared schema-4 Preview foundation, narrow Windows compatibility wrappers, native Mac still/RAW/video mechanisms and a minimal thumbnail column in the existing Browser. This proves real thumbnails without implementing final Grid/Info/Hybrid or Details layouts.

**343 automated tests passed:** Application 141, Catalog 67, UI 75, Mac platform 60; zero failed or skipped in the final run. The existing Windows application and test suite cross-compile with zero errors; Windows runtime qualification has not run. The native corpus produced thumbnails for 25 of 28 fixtures, including three camera RAW samples; three deliberate invalid/unsupported files remained honestly Unsupported. Restart, cache rebuild, real source replacement, missing-source retention and admitted native cancellation checks succeeded.

**Acceptance is pending.** No owner Mac hands-on result, mounted-NAS test, real cloud-only fixture, independent DARKMATTER result or merge authorization is claimed. No new architectural decision is requested. Unsupported camera variants and HDR/extended-range treatment must not be advertised based on this limited corpus.

## C. Architecture delivered

Shared Application code owns Preview records/ports, stable enum numbers, source/visual identities, metadata models/orchestration, generation, per-asset gates, retry deadlines, operation/maintenance coordination, poster policy and derived-work scheduling. Catalog owns the mechanically extracted SQLite store with schema **4**, existing SQL/integrity/migration checks and injected path admission. Video rotation and the bounded existing Cube LUT parser moved to neutral layers where needed. No Catalog schema or authored-table redesign was introduced.

Windows retains existing WIC image decoding, FFmpeg discovery/process arguments, derived Color resolution, logging, artifact validation and data-profile guard through small wrappers. Existing constructor signatures, generator version 1, probe versions, retry cooldown, artifact naming and default two-worker limits remain. Runtime behavior still requires independent Windows verification. The only shared scheduling corrections are cancellation/revisit ownership (an old canceled completion cannot remove replacement work), stale queue compaction after cancellation storms, and queued-token disposal at shutdown; targeted tests cover those races. This is not a broad Windows subsystem rewrite.

Mac common JPEG/PNG/WebP/BMP/GIF uses the existing Avalonia production dependency **SkiaSharp 2.88.9**. It requests subsampling, preserves the encoded color profile through decode, converts on an sRGB output canvas, applies EXIF exactly once, composites alpha over #05070A, applies Camera then Creative cube LUTs, and writes JPEG quality 85 at a maximum dimension of 512. A decoder that still requires more than 64 Mi decoded pixels is refused, rather than attempting an unbounded managed bitmap allocation. Some codecs still decode a full source under that ceiling.

ImageIO handles native image gaps and camera RAW, preferring an existing preview and falling back to a native thumbnail render. It receives the original UTType hint even though source reading uses an admitted descriptor; that hint was necessary for the tested Sony ARW. AVFoundation reads video through a custom resource-loader delegate over the admitted descriptor, applies the track transform, requests at most 512×512 and records actual rational PTS. Its tolerance is ±1/120 second; measurements, not a presumed nearest-frame guarantee, determine consistency. No Player or audio surface is activated.

Source admission uses existing Mac media fingerprint/protected-path rules plus regular-file, dataless, no-follow and inode/device checks. Source reads disable dataless materialization on the reading thread. AVFoundation cannot reopen an ordinary media pathname through this adapter. Preview/LUT artifacts remain beneath the admitted local data root. Mac's Catalog completion-peer capability remains **false**: startup retains conservative unpaired/deep validation. Creating previews.db does not manufacture clean paired completion evidence.

The Browser submits visible-first demand (up to 64 visible, one viewport on each side, at most 192 IDs), with a 60 ms viewport debounce and fixed two-worker scheduling. It never queues the entire folder for thumbnails. Row-owned detached bitmaps are disposed on recycling/detach; view-model thumbnail state remains bounded to current demand. Scope/epoch/generation checks reject late completion. Cache reads compare source, Color and preferred-frame identity. Failed replacement can retain only matching pixels with a visible warning. Missing sources can retain matching pixels while labeled Unavailable.

Session close/switch cancels and drains the scheduler before disposing generators, Preview store and coordinator, then retires the Catalog owner. Native cancellation does not release a worker slot before callbacks and resource-loader activity drain. The 30-second native timeout requests cancellation; an uninterruptible OS decoder can exceed it while retaining its slot. It is not a hard termination promise.

Deviations/limits: Mac derived metadata is limited to video duration in this slice; successful still/RAW thumbnails can therefore coexist with a metadata failure/cooldown. Aggregate scheduler Failed includes partial metadata failure and is not a thumbnail failure count. Mac HDR/tone mapping and broad camera conformance are unqualified. No major dependency, LibRaw, Mac FFmpeg distribution, hydration bypass or later Browser issue was added.

## D. Format and color support

| Tested corpus | Result and limit |
| --- | --- |
| JPEG/PNG/WebP/BMP/GIF | Ready; static/first frame. JPEG EXIF 1–8 corner identities passed; 1800×900 image caps at 512×256. |
| TIFF, HEIC | Ready through ImageIO; generated files, not every compression/profile variant. |
| Canon 5D Mark IV CR2 | Ready, ImageIO existing preview, 512×341. Standard CR2 sample, not Dual Pixel RAW conformance. |
| Ricoh GR III DNG | Ready, ImageIO existing preview, 512×341. One camera-native DNG variant. |
| Sony a7 III ARW | Ready, ImageIO existing preview with UTType hint, 512×342. One compressed ARW variant. |
| H.264 MP4, HEVC MOV, VFR H.264, display-matrix MOV | Ready; track rotation produces 288×512, ordinary posters 512×288. Long low-resolution video stays 256×144. |
| Corrupt MP4, invalid CR3, synthetic CFA DNG | Unsupported. This does not establish that a real Canon CR3 is unsupported. |

RAW sources are public CC0 samples from [the publisher's repository/release](https://github.com/tellodaniel/revelraw-sample-raw-files), verified against its SHA256SUMS. Exact URLs, hashes and byte counts are in `raw-fixture-sources.json`; RAW files are excluded from the ZIP. No universal RAW/camera support claim is made.

The explicit output policy is SDR sRGB with flattened alpha. An embedded linear-profile fixture converts raw mid-gray to the expected sRGB range; transparent pixels composite correctly. Pixel tests verify Camera-before-Creative ordering. Authored intent comes from existing Catalog Color rows, and only explicitly configured CameraLutFolder/CreativeLutFolder locations are inspected. Reads of Catalog intent do not scan LUT folders. Worker resolution validates content identity, stages local content-addressed LUT bytes and preserves missing-dependency truth. Missing/changed LUTs never publish untreated pixels under a colored identity. Tests cover matching retained artifacts and invalidation when Color identity changes. The full native authored-Color workflow and HDR appearance still need owner/Windows qualification; unit pixel tests are not a universal cross-platform color match.

## E. Validation

Environment: Apple Silicon arm64, macOS 27.2 build 26B123, task-owned .NET SDK 8.0.415. Native code compiles with `-Wall -Wextra -Werror`. Lock files remain authoritative; no production NuGet dependency was added.

Commands use `DOTNET_CLI_HOME="$PWD/work/dotnet-home"` and the task-owned `.cache/dotnet/dotnet`. For each of `Lightflow.Application.Tests`, `Lightflow.Catalog.Tests`, `Lightflow.UI.Tests`, `Lightflow.Platform.MacOS.Tests`:

```sh
.cache/dotnet/dotnet test <project> -c Release --disable-build-servers -m:1 -p:RestoreLockedMode=true
.cache/dotnet/dotnet build LightflowStudio.Tests -c Release --disable-build-servers -m:1 -p:EnableWindowsTargeting=true -p:RestoreLockedMode=true
```

Final pass/fail/skip: **141/0/0**, **67/0/0**, **75/0/0**, **60/0/0** respectively. Windows cross-build: **0 errors**, not executed. Final logs are `final-*.log` and commands are `final-commands.json`. Earlier failed build/test/fixture trials remain in evidence; final passing logs supersede only their corrected scope. Initial shared tests exposed test enum/nullable-data assumptions; Windows compilation exposed the moved internal test seam; the ICC fixture initially used color-converting setters. None of those failed attempts is counted as PASS.

The native harness uses real Mac session/Catalog/store/decoders with disposable source media. `native-final-handoff.json` records three rounds (cold/reopen/cache rebuild), whole-file SHA-256, Catalog/AssetIds, output dimensions, component results, actual PTS, RSS and readers. A real source replacement returns no stale cache path and publishes a different artifact; removing that source retains matching pixels labeled Unavailable. Catalog/asset identities survive restart/cache deletion. Shared tests additionally reject source, Color and preferred-frame mutations during an in-flight render; cooldown persists and Force bypasses it. A 2,000-cancellation queue stress test asserts at most 64 stale queue entries. A real 1,000-row headless Browser asserts 1–64 retained bitmaps and zero after detach, at most 192 thumbnail records, and late completion cannot repopulate a retired scope.

Twelve cancellation trials waited until a native reader was observed, then canceled: all 12 admitted, all 12 canceled, all returned with **0 readers**, as did final session close. These are process-local admission counters, not proof of every OS decoder allocation or a kernel leak audit. Existing 60 Mac platform tests retain lifecycle/readiness/linked/protected/cloud-policy/refusal coverage. No normal-profile launch or network Catalog was used.

### Complete engineering matrix retained from Phase A

| # | Scenario | Phase B disposition |
| --- | --- | --- |
| 1 | Still/orientation/alpha/ICC | Automated PASS on declared generated corpus; owner visual acceptance pending. |
| 2 | RAW/camera variants | Automated PASS for the three exact samples above; wider camera coverage unqualified. |
| 3 | H.264/HEVC/posters/unsupported | Automated PASS on declared corpus; HDR and other codecs unqualified. |
| 4 | Quit/reopen/cache reuse | Native session PASS, stable IDs and paths; exact packaged GUI reopen pending owner. |
| 5 | Navigation/Refresh/selection | Existing UI regressions PASS; owner interaction check pending. |
| 6 | Rapid navigation/scroll/cancel | Shared stress and stale-completion/retention tests PASS; real GUI frame-time/scroll acceptance pending. |
| 7 | Large folder | 1,203-file discovery with demand capped at 192 measured; headless 1,000-row retention PASS. Live GUI latency pending. |
| 8 | Source/Color/preferred-frame change | Shared in-flight tests and native source replacement PASS; full authored/native Color hands-on pending. |
| 9 | Unavailable/deletion/decoder failure | Native missing-source retention and deliberate invalid-format states PASS; physical detach/permission-loss GUI cases pending. |
| 10 | Mounted NAS fixture media | NEEDS WORK: no owner-approved mounted NAS corpus used. No network Catalog activated. |
| 11 | Identical CFR/VFR on Windows/Mac | Mac reference measured; independent Windows run NEEDS WORK. |
| 12 | Isolated lifecycle/identity/rebuild | Native cache rebuild and existing safety suites PASS; package owner root remains separate. |
| 13 | Independent Windows same-source | NOT RUN; prepared [DARKMATTER checklist](browser-thumbnail-foundation-036-windows.md). |

No incomplete scenario is promoted to overall acceptance. Real cloud-only source hydration behavior also needs a safe owner fixture; native policy tests do not substitute for that field check.

## F. Video poster-frame comparison

Reference: host FFmpeg 9.0.1, using the existing Windows ExtractThumbnail seek/scale/JPEG arguments **on Mac**. Source presentation timestamps come from ffprobe. Ordinals are zero-based: nearest source PTS to native actual PTS versus first source presentation frame at/after requested time. Actual Mac PTS is reported as the native rational, not inferred from duration.

| Fixture | SHA-256 | Average fps | Requested s | Mac actual PTS | Mac / reference ordinal | Difference |
| --- | --- | --- | --- | --- | --- | --- |
| cfr-h264.mp4 | `3b371308d09e8de74e71c58fe384f63e8fc02452e5def960148f1af137124623` | 30/1 | 1 | 15360/15360 | 30 / 30 | 0 (exact) |
| cfr-hevc.mov | `c79d2fcbf068e2c13cda2f90a22caae236e3a8dc0917278122d16ccc42ceb264` | 24/1 | 1.2 | 14336/12288 | 28 / 29 | -1 (+/-1) |
| rotated-h264.mov | `b79eca190d096a19b997e024750b1ce2f7faddfe10d72dd5c95e2b5fb8a49b55` | 30/1 | 1 | 15360/15360 | 30 / 30 | 0 (exact) |
| short-h264.mp4 | `dd0ac5a15e5bdac9a7fed89bbf9f6290559c11d7ba159188e02328fd6b603448` | 30/1 | 0.3 | 4608/15360 | 9 / 9 | 0 (exact) |
| long-h264.mp4 | `4be0e17c438b190d3bd3c1563c2e27ce8d1838458f03c76862f11a22b23baa95` | 5/1 | 30 | 307200/10240 | 150 / 150 | 0 (exact) |
| vfr-h264.mp4 | `f031f6a352de8222045de92fb2b674440c698f53953585317e73bcdb2986ce4a` | 900/73 | 1 | 15360/15360 | 15 / 15 | 0 (exact) |

All repeated rounds stayed within the proposed ±1–2 frame review budget: five fixture types exact, HEVC −1 frame. None differed by ±2 or more than 2 in this corpus. Short video uses midpoint 0.3 seconds; ordinary 5-second and VFR clips use the 1-second floor; 12-second HEVC requests 1.2 seconds; 310-second video clamps to 30 seconds. Unit policy tests cover null/zero/short/1-second/minimum/10%-region/clamp boundaries. Preferred-frame mutation is covered separately.

The comparator also records a coarse 64×36 RGB mean absolute difference; values are not zero (decoder/color/scale/JPEG differences). It is not an acceptance threshold and does not prove color parity. Synthetic test patterns and ordinal/PTS evidence avoid relying on visual similarity alone, but the Mac-host FFmpeg version is not the packaged Windows reference. Independent Windows output and appearance qualification remain mandatory.

## G. Performance

One measured native run of 28 mixed fixtures: **cold demand 1,481.5 ms**, warm reopened demand **97.9 ms**, cache rebuild **1,172.8 ms**. Total session start/discovery/demand was 2,311.0 / 605.3 / 1,678.4 ms. End-of-round RSS was 186.8 / 187.8 / 163.8 MiB. These are single process snapshots and one local run, not peak, percentile or universal budgets.

A separate 1,203-file generated folder requested only **192** thumbnails: cold demand **7,914.6 ms**, warm demand **695.1 ms**; total start/discovery/demand **33,244.8 / 19,453.6 ms**; RSS **110.6 / 111.5 MiB**, readers 0. Existing discovery/fingerprinting of the full folder is the dominant large-folder cost; thumbnail virtualization does not remove it. This is not a claim of instant folder enumeration.

Worker configuration is two; thumbnail state ≤192; visible demand ≤64; recycled bitmap assertion ≤64; canceled stale queue entries ≤64 under the specific stress test. Native cancellation timings and admission counts are in JSON. Neither RSS snapshots nor the 512-pixel output cap establish a strict whole-native-process memory ceiling. Common-codec decoded allocation is capped at 64 Mi pixels per operation; ImageIO/AVFoundation internal allocations remain OS-managed and need broader memory profiling.

Proposed review budgets (engineering hypotheses, not measured product guarantees): two active decoders, ≤192 viewport IDs, no off-scope retained bitmaps; ≤10 seconds for this 192-item cold generated batch, ≤1 second warm batch, <300 MiB observed mixed-corpus end-of-batch RSS; cancellation returns resources before slot reuse. Owner must judge visible-first useful-pixel time and scrolling on actual media. No live GUI p95 frame-time or 10,000-item measurement is claimed. LUT parsing/per-pixel treatment, per-item identity reads and existing full-folder discovery are known scaling costs.

## H. Mac package and owner acceptance

Fresh development bundle: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-phase-b/package/Lightflow Studio.app`.
Executable: same bundle's `Contents/MacOS/LightflowStudio.Mac`. Adjacent package provenance stamps the final source and every file; generated handoff footer adds the executable/native-library and archive hashes. Unsigned/unnotarized Apple Silicon development bundle, built by existing `scripts/Build-MacShell.sh` with the task-owned SDK and locked restore. Runtime/dependency/icon validation evidence accompanies the package. GUI startup and normal GUI quit/reopen remain part of owner acceptance; no desktop was controlled to infer those results.

Isolated owner root: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-phase-b/owner-acceptance-data`.

Ready-to-copy Terminal command:

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-phase-b/package/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-phase-b/owner-acceptance-data"
```

Prepare an empty `Catalog` folder beneath that root and select it when creating the disposable Catalog. Use generated fixtures or verified disposable copies; do not use normal storage. The engineering Catalog/cache is not the owner root.

1. Inspect JPEG/PNG/TIFF/HEIC thumbnails, EXIF 1–8, aspect and transparency.
2. Inspect the three exact supported RAW samples; compare the source appearance/embedded-preview limitations.
3. Inspect CFR/VFR/HEVC/rotated/short/long posters; confirm no Player/audio activation.
4. Quit normally, relaunch the same command/root, revisit the same folder and check cache reuse.
5. Navigate folders, Back/Forward, Refresh and selection; confirm existing Browser behavior remains intact.
6. Scroll quickly and switch folders during generation; confirm current rows replace old demand without stale pixels.
7. Browse the generated large folder; judge useful-pixel time and responsiveness separately from enumeration.
8. Check deliberate invalid files and missing/renamed disposable source states; confirm labels and valid retained cache behavior.
9. Where practical, browse approved resident media on an already-mounted NAS; keep Catalog/cache local. Do not force-unmount owner storage.
10. Judge overall visual/functional integrity. The leading thumbnail column is a foundation proof, not final Grid or Details design.

Project-status screenshot is `evidence/project-active.png`; no Mac GUI screenshot or owner acceptance is fabricated. Automated headless UI tests are separate evidence.

## I. GitHub and historical maintenance

#481 remains open/In Progress, Browser area, priority unset. Parent #480 remains open/Backlog, and #482–#485 stay Backlog/Browser/unset and unstarted; native relationships were repaired/verified during Phase A and retained during Phase B. Owner architecture authorization is durable on #481. Draft PR targets main, relates to #481 without a closing keyword; URL is in the generated handoff. No merge, closure, later issue or new assignment was performed.

Registry is updated in the implementation. **Historical maintenance: Updated** Chronicle, Development History and Source Index for the shared derived-media implementation checkpoint. All explicitly distinguish implementation from acceptance, merge and release; accepted historical cutoffs and AI-authored attribution remain preserved.

## J. Artifact manifest

- Complete copyable report: `artifacts/lf036-phase-b/REPORT.md` (generated from this source report with immutable publication/build footer).
- Evidence ZIP: `artifacts/LF-MAC-DEV-036-Phase-B-evidence.zip`.
- Mac bundle: `artifacts/lf036-phase-b/package/Lightflow Studio.app`; portable app ZIP: `artifacts/LF-MAC-DEV-036-Phase-B-Mac-app.zip`.
- Package stamps: `artifacts/lf036-phase-b/package/provenance.json`, `evidence/package-validation.json`, `evidence/final-provenance.json`; archive hashes in adjacent `.sha256` files.
- Automated logs: `evidence/final-*.log`, `final-commands.json`; native run: `native-final-handoff.json/.log`; poster references/comparison: `posters-handoff/`; large-folder measurements: `large-folder.json/.log`.
- Fixture manifest/source hashes: `fixtures-manifest.json`, `raw-fixture-sources.json`, `raw-SHA256SUMS.txt`; small generated fixtures and selected thumbnail examples accompany the ZIP. RAW source files, Catalog databases, large caches, SDK/package caches and personal media are excluded.
- Preserved failed trials are labeled by their original filenames; final evidence paths above identify the applicable passing runs. Phase A report/evidence remain unchanged.
- Missing evidence: owner Mac GUI acceptance/quit/reopen, mounted NAS, actual cloud-only field fixture, full native authored-Color workflow, broad camera/HDR conformance, independent packaged Windows poster/runtime/acceptance, final exact-head CI/merge authorization.

## K. Outstanding gates

1. Owner Mac packaged hands-on acceptance, including normal GUI startup/quit/reopen and focused checklist.
2. Safe practical NAS/cloud-only checks and any required fixes; do not weaken admission to obtain a PASS.
3. Independent DARKMATTER same-source Windows tests, fresh package/dependency/smoke and poster comparison; no agent allocated by this handoff.
4. Full exact-head CI and review of extraction/compatibility; CI success is not owner acceptance.
5. Resolve any acceptance-discovered correctness/performance/color limitations within #481 or obtain an explicit architecture decision if a mandatory stop condition is reached.
6. Explicit merge authorization. Until then keep the PR Draft and #481 open; do not begin #482–#485.
