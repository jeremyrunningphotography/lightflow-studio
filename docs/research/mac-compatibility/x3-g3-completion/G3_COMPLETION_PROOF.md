# Final bounded G3 completion — recommendation: PASS

Owner review requested; **not owner acceptance**. #370 remains Open / In Progress and #366 remains 3/4 complete. G1 and G2 remain owner-accepted PASS. No production migration, merge, M4+, or changes to #368/#369 are part of this proof.

Base: `1777a4bbb7921ed481f49b204262bb3521ea2922`; fresh branch `codex/370-avalonia-g3-completion`. All new source is in `tools/X3G3Completion`; all new evidence is here. Previous `x3-iosurface`, X1 and Windows evidence is unchanged. Frozen original UI branch/commit `codex/370-avalonia-ui-image-proof` / `d25e2ad591547d3a012175606f930c9f4af414fe` remains preserved locally; this report does not imply that older commit was published. Its accepted positive findings are inputs to this bounded continuation, not new Windows test results.

## Decision and limits

Free Avalonia remains a credible **one shared UI** direction. The actual IOSurface/Metal interop now has a token-specific UIAccepted boundary. Public virtualizing ListBox hooks support a retained-cell Details implementation with bounded containers and substantially less allocation. Stable AssetId restoration, the unchanged shared action resolver, native accessibility exposure, ImageIO format fallback, and explicit SDR color tagging provide credible implementation boundaries.

The Details result is **classification C: a custom Details implementation is preferred**, not a claim that stock TableView churn is acceptable. Estimate remains **20–40 person-days plus 1–3 days/month initially** for headers, columns, sorting, keyboard/range selection, accessible semantics, style and cross-platform regression work. This is a research estimate, not an approved budget. The earlier 8–15 day TableView adapter estimate is a contingency only if upstream recycling improvements prove sufficient. No mandatory paid dependency or whole-view fork is required by the evidence.

There is no remaining material UI/image architecture blocker identified. Implementation and packaged acceptance risks below are real; PASS does not certify a production control, all image variants, physical display parity, VoiceOver usability, or launch reliability.

## Evidence and environment

`raw/final-1.jsonl.gz` and `raw/final-2.jsonl.gz`: two complete arm64 matrices, each 257 passing assertions. `raw/input-final-qualified.jsonl.gz`: final focused input/menu verification after additional arrows, Command+A and exactly-once menu tests; supersedes earlier input assertions. Failed intermediate runs/builds are retained. `RUN_INDEX.json` records counts and errors. `SOURCE_HASHES.json` covers final source; intermediate edits were not individually committed, so failed runs are diagnostic history, not exact reproducible revisions. No executable changed outside this isolated harness.

Mac14,9, Apple M2 Pro, 32 GiB; macOS 26.6.2 (25G83); arm64 process; task-local .NET SDK 8.0.425; Apple clang 21.0.0; actual RenderScaling 2. Fullscreen native window reported 1800×1130 points. `ENVIRONMENT.txt` records toolchain/display facts without device serial numbers. No Rosetta or paid tooling.

Composition snapshots are native Avalonia-rendered images at 2×, **not physical desktop screenshots or proof of scanout**. Retained Details shows dark rows, readable typography and multi-selection, but has no finished interactive header/drag UI and is not final Lightflow styling. Previous actual Retina/browser/thumbnail evidence remains distinct.

## UIAccepted

Nine callbacks accepted pause, step, seek, Color, Compare-style revision, new Color, latest rapid offer, reattach and resized surface. Six obsolete offers were rejected: generation, Color, in-flight Color, superseded rapid frame, detached host, old surface epoch. Every callback includes serial, generation, Color revision, surface epoch, host generation and actual IOSurface identity.

Each offer uses a new candidate CompositionDrawingSurface. After timeline-controlled import/copy and consumer fence completion, current identities are rechecked, the candidate is attached, and public `Compositor.RequestCommitAsync()` is awaited. Avalonia 12.1.3 documents that completion as pending composition changes applied on the render thread. The UI dispatcher rechecks current identity before publishing the callback. This is logical **UIAccepted**, not physical scanout.

The consumer release timeline is independent from semantic acceptance. A retained source lease blocks producer reuse until capture/replacement ends; nine captured snapshots decoded the same serial barcode, with source hash and matching metadata recorded. Native surface/producer counts reached zero. Resize creates a new producer epoch; host detach invalidates acceptance. This proves the narrow adapter contract using a synthetic native producer, not a reimplementation or rerun of accepted FFmpeg/G1 behavior.

`UIACCEPTED_RESULTS.json` and the nine `screenshots/*-source.png` files provide tokens, timing and source captures. The 16-bit barcode is sufficient for these serials 1–15; it is not a production identity mechanism. Production must carry the complete token.

## Details allocation cause and measurements

Each 10k/100k variant has 18 columns, 32-point rows and 180 scroll/layout steps: three cycles of 30 short vertical moves and 30 large jumps. No thumbnail decoding runs during this comparison. Counts observe realized containers and unique identities. Allocations are process-wide managed bytes including rendering, logging and measurement; timings include layout and a 1 ms scheduled yield. They are not physical frame-time or input-to-photon measurements. Initial usable timing includes a deliberate 140 ms delay and is not a startup SLA.

| Rows / variant | Allocation GB | GC gen0/1/2 | Max rows/cells | Unique rows/cells | p50 / p95 / max ms |
|---|---:|---|---|---|---|
| 10k stock | 2.129 | 256/83/81 | 23/414 | 24/432 | 27.91 / 122.44 / 149.45 |
| 10k recycling template | 2.494 | 300/100/98 | 23/414 | 24/432 | 39.07 / 43.35 / 53.39 |
| 10k retained | 0.600 | 77/40/12 | 24/432 | 25/450 | 6.00 / 16.70 / 36.16 |
| 100k stock | 1.976 | 264/106/54 | 23/414 | 24/432 | 23.90 / 52.74 / 72.97 |
| 100k recycling template | 2.493 | 325/126/61 | 23/414 | 24/432 | 34.22 / 59.25 / 84.90 |
| 100k retained | 0.598 | 74/36/5 | 24/432 | 25/450 | 5.87 / 12.27 / 36.36 |

Containers really recycle; item count does not cause proportional realization. Stock cells clear/recreate property bindings and content/template state during row recycling. Official `TableViewCellsPresenter.ClearCells`, `TableViewCell.Column`, `ClearProperties` and `SetProperties` show that path. Retaining cells but rebuilding bindings is still costly: merely supplying a recycling template did not improve allocation. Removing those binding/template rebuilds with retained TextBlocks reduced 100k allocation by 69.7% and gen2 collections from 54 to 5. This controlled difference and source inspection identify the dominant mechanism; no byte-by-type allocation profiler was used and not all residual allocation is attributed.

Both counts reuse 24 stock row identities or 25 retained row identities across thousands of preparations. Stock has no column virtualization; 18 columns are realized for each row. Retained cells keep the same bounded design. At 100k, stock cycle RSS was 315.9/316.2/321.4 MB; retained 389.0/389.0/395.4 MB. Variants run sequentially in one process: higher later RSS is not a fair per-control memory comparison. Bounded short traversals show no item-proportional runaway; they are not a long soak or proof of zero leaks. The independent first complete matrix shows the same allocation direction.

Column width/order mutation, multiselect and sort projections work. Interactive column headers/drag handles are implementation work. Recommended later targets: keep realized count proportional to viewport, avoid sustained RSS growth after warmup, aim for p95 UI work inside a 16.7 ms frame budget and prompt selection/mode changes under representative thumbnail load. These are proposed targets, not owner-approved limits; this harness cannot certify them.

## Selection, shortcuts, menus and accessibility

Selection state is AssetId set + primary AssetId + semantic visible anchor. Twelve restoration cases cover Grid/Details cycles, refresh with new instances, reverse sort, surviving filter, Player return and large jumps. Exact pixel position is not required. Hidden-anchor fallback and preservation of filtered-out selections are a defined policy, not a tested hidden-selection UI implementation. Reset transient range anchors when changing projection. Grid retains six four-item bands (30 preparations); selected item 055555 exposes native AXRow selected=true.

Final input tests use Avalonia's actual window raw-input pipeline, with a test-only reflective adapter for the 12.1.3 InputRoot. They do not simulate global OS keystrokes. The unchanged `Lightflow.Actions` resolver supplies command/conflict semantics. Meta maps to Primary; Control, Option and Shift stay distinct. All four arrows, Space, normal keys, modifier-only ignore, Escape, conflict and Tab ownership pass. Delete is reserved and Backspace unsupported by the present catalog; the adapter preserves that distinction. Captured Space key-up is consumed to prevent accidental Apply activation. Local text receives input and Command+A; global Player Space is suppressed in editors and resumes outside them.

Right-click and explicit Control-click open the context menu; Escape closes it. One routed menu click invokes its command once. A stale context command rejects execution in both CanExecute and Execute. Final native NSMenu export contains Settings with Command+comma. Export requires activation/readiness; short earlier input runs sampled only the root menu. Default framework About branding and Hide Others (observed Option+Command+Q) need explicit Mac convention correction. MenuItemAutomationPeer lacks an IInvokeProvider; the failed assumption is recorded and the final test uses routed Click. Native accessibility action behavior remains a packaged VoiceOver check.

Native NSAccessibility traversal observes meaningful Grid item rating/flag/color and selected state; Details AXRows with selected state and AXStaticText child values carrying row/column/name; named Player AXGroup and play button; Settings buttons/capture text; Collections AXOutline/AXCell; AXMenuItem context action. A small TextBlock peer supplies explicit cell names. This is accessible row/list structure, **not a proven AXTable/grid-cell provider**. Grid selection-provider aggregation and richer table navigation need shared custom peers. Dynamic name updates are observed in later snapshots; spoken announcements were not tested. Focus checks are bounded (Tab button to button, editor restore), not a complete accessibility audit.

VoiceOver hands-on acceptance later: in a retained packaged proof, enable VoiceOver; navigate Browser item then selected Details row/cells; confirm asset/row/column and rating/flag/color context; activate Play; edit/cancel a shortcut and check no Player action; open/dismiss a context menu; return focus. Check selection changes and capture status announcements. No owner VoiceOver or calibrated visual interaction was silently simulated.

## Images and color

Use shared owned pixel/metadata/identity contract, common Skia path, and a narrow platform gap decoder. On Mac, ImageIO decoded uncompressed, LZW and PackBits TIFF plus generated HEIC where SkiaSharp 3.119.4 failed. PNG/JPEG/WebP/BMP/GIF use common Skia; first-frame scope only. Windows may use its existing/native gap adapter; it is not required to use ImageIO. Unsupported/malformed data returns an explicit unavailable result. RAW and arbitrary TIFF/HEIF variants are not qualified.

Normalize all eight EXIF orientations exactly once before Avalonia conversion. Orientation 8 literal pixels are `[2,5,1,4,0,3]`. Direct Avalonia loading of orientation 5–8 JPEG retained unrotated 3×2 dimensions; the normalized adapter produces expected 2×3. Authored rotation remains separate. ImageIO decoded output, normalized PNG and Avalonia dimensions are checked. Current bridge copies native RGBA → owned shared pixels → PNG → Avalonia; no zero-copy claim. Future direct bitmap upload can eliminate encoding copies after lifetime tests.

Lossless fixture comparisons match; JPEG decoders disagree by up to 52 channel levels on tiny sharp-edged vectors. Therefore use the same Skia JPEG path on Windows/Mac, not interchangeable Skia/ImageIO JPEG pixels. Golden tests must compare normalized pixels with per-codec tolerances, orientations, metadata, profile and alpha intent, not appearance.

Twenty linear-profile intensity/alpha vectors compare explicit sRGB transfer math against both Skia and ImageIO within one premultiplied channel level, with exact alpha. Embedded iCCP is detected; source bytes/hashes remain preserved. ImageIO returns a canonical color-space ICC representation, not proof that its copied ICC bytes equal the original embedded payload. Keep original encoded source/profile metadata separately; normalized output is tagged with its destination profile. Original straight-channel roundtrip can differ by one through premultiplication.

The native CAMetalLayer initially had nil colorspace. A narrow adapter successfully tagged it and the window sRGB; native display inspection reports Color LCD ICC 4064 bytes at 2×. Proposed bounded SDR path: source profile → explicit sRGB pixels → sRGB-tagged presentation → OS display-profile conversion. Reapply tagging on layer/reparent/display changes; avoid double-applying a monitor transform. No calibrated physical display parity is claimed. HDR/wide gamut and per-monitor transitions remain later scope. One hundred TIFF native decode/free cycles balanced to zero; decoded source-file release and owned bitmap lifetime passed.

## Lifecycle, versions and risks

Native fullscreen at 2×, two activate/deactivate events, detach/reattach, surface replacement, close/new-window reopen and focus restoration passed. This is not Dock reopen after last-window closure, sleep/wake, multiple displays or full packaged lifecycle certification.

Avalonia/core TableView 12.1.3 aligns the previous UI baseline with the earlier 11.3.8 IOSurface work. SkiaSharp/native 3.119.4; HarfBuzzSharp/native 8.3.1.3. Core UI/TableView MIT; dependency lock, package hashes, licenses and native hashes in VERSION_BASELINE.json/licenses. ANGLE includes BSD/third-party notices; Apple frameworks use system terms, not an MIT redistribution grant. No Avalonia commercial product is used. TableView is maintained in the same upstream core tag, but its present recycling behavior needs owned adaptation. No support SLA is inferred.

Version migration risk includes API/source-generator changes and Windows requalification. .NET 8.0.425 compiled the code-only harness with two analyzer/compiler-version warnings (4.14 analyzers versus 4.11 compiler) and two obsolete Bitmap.Save warnings; no XAML generator compatibility claim. Choose a supported compiler/generator pairing before production migration. Test-only raw-input reflection changed in 12.1.3 and must not become a product dependency. The native seam targets macOS 12.0 APIs; only macOS 26.6.2 is exercised. Product minimum OS is not established.

Several native launches failed at RenderTimer initialization (-6661), before tests. One sandboxed direct bundle launch aborted without managed output; successful complete matrices and focused input runs followed. An independent CoreVideo display-link creation succeeded; cause remains unresolved. A temporary app bundle alone did not establish a fix. This is a recorded launch/package reliability risk, not evidence of a UI/media composition contradiction. Do not ship without packaged launch/activation soak and diagnosis. No claim that screen lock caused it.

Deferred: custom Details headers/keyboard/range/AX polish; application-menu conventions; full Grid selection provider; VoiceOver hands-on; common Windows golden tests; calibrated display parity; bounded copy optimization and input resource limits; package signing/notarization, Dock/quit/reopen, sleep/wake, multi-display and launch reliability. None requires two independent view implementations on current evidence. No production WPF packaging gate was run on this Mac; this is a Draft research PR, not a ready production executable.
