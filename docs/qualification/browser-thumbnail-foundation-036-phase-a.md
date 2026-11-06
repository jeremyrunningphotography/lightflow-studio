# LF-MAC-DEV-036 — Browser Thumbnail Foundation: Phase A

AI-authored architecture audit and handoff, November 5, 2026 (America/Los_Angeles). **Proposal only; implementation is not authorized or started.** Source inspection is evidence of code behavior, not fresh Windows/Mac runtime or owner acceptance.

## A. Assignment identity

| Item | Verified value |
| --- | --- |
| Agent | LF-MAC-DEV-036 |
| Conversation | LF-MAC-DEV-036 — Browser Thumbnail Foundation |
| Independent full clone | `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation` |
| Branch | `codex/036-browser-thumbnail-foundation` |
| Independently verified starting remote main | `7f755460e36375234281481cea3004f60344325b` |
| Task source checkpoint | `c24acbf0735f8fd83ab148802970ed1c3c571dd7`, tree `5dc108c84539d9aa30d703bfbe59ef16aed00c91` |
| Reservation | Published on task branch at that checkpoint; main not modified. Table row and dated reservation preserve prior records. |

036 was absent from main's registry, every fetched remote branch's registry, and matching GitHub PR/issue searches. No competing 036 assignment was found. No additional agents were allocated. The canonical checkout was read only for initial orientation; its older AGENTS.md was superseded by the complete current-main instructions read in this clone. No worktree, production change, application launch, build, implementation PR, merge or closure occurred. This report and evidence are a subsequent documentation overlay; the final handoff manifest records its exact hashes and any later documentation commit.

## B. Executive summary

The existing Windows thumbnail pipeline is the appropriate behavior owner. Extract its neutral orchestration and contracts, retain the Preview database schema and cache semantics, and preserve Windows WIC/FFmpeg adapters. Mac should supply narrow image/RAW and video adapters plus Avalonia cached-image presentation. Do not create a separate Mac Browser, Catalog, selection owner or generic media framework.

Native hierarchy is repaired and independently read back. #480–#485 have Project #4 membership and **Backlog / Browser / Priority unset**, including explicit owner approval of these field values after automatic review initially rejected the bulk edit. Broader Epic statuses and dependencies remain unchanged. #480 now has unchecked delivery/reference gates; #395 records the planned subtree without completing its wider DoD.

Material findings:

- Windows recognizes RAW files for discovery but the scheduler only thumbnails `image` and `video`; there is no existing production RAW thumbnail decoder to extract. RAW format qualification is a new bounded capability in #481.
- The Mac currently composes discovery with a null derived-work scheduler. It has no Preview database peer, thumbnail generation or rendered previews.
- Windows's priority queues prioritize submitted work, but its Browser submits folder/recursive batches as Visible; that is not precise viewport-first demand. Fixed workers bound execution, not queue size or resident result count.
- Windows video selection uses duration-based seconds, not nominal-frame-rate multiplication or keyframe-only selection. There is no alternate-frame fallback after decode failure.
- Accepted ADR 0006 specifies common Skia pixels with narrow native gaps. Current Windows stills use WIC and current production Avalonia is 11.3.8; final historical G3 used a different runtime/Skia version. Do not copy its packages or silently replace Windows JPEG decoding.

Owner decisions before implementation: approve the extraction boundary; approve a bounded, fixture-backed native RAW support list and embedded-preview/decode policy; approve native video qualification and truthful unsupported formats; approve retaining conservative unpaired/deep Catalog startup while adding a real rebuildable Preview consumer. The original Windows Details screenshot is still missing. **STOP gate remains closed to implementation.**

## C. GitHub reconciliation

Repository: [Lightflow Studio](https://github.com/jeremyrunningphotography/lightflow-studio). Project: [Roadmap #4](https://github.com/users/jeremyrunningphotography/projects/4).

| Issue | Scope | Native parent before → after | Final Project Status / Area / Priority |
| --- | --- | --- | --- |
| #366 | Mac Compatibility | unchanged | In Progress / Application Framework / unset |
| #395 | R4 Shared Browser and Shell | #366 → #366 | Backlog / Browser / unset |
| #480 | Browser Contents Epic | absent → #395 | Backlog / Browser / unset |
| #481 | Thumbnail foundation | absent → #480 | Backlog / Browser / unset |
| #482 | Preview/Info/Hybrid Grid cards | absent → #480 | Backlog / Browser / unset |
| #483 | Details and presentation preferences | absent → #480 | Backlog / Browser / unset |
| #484 | Search/filter/sort | absent → #480 | Backlog / Browser / unset |
| #485 | Metadata/status parity | absent → #480 | Backlog / Browser / unset |

All remain Open. Parent GET endpoints initially returned “No parent issue found” for #480–#485, while #395 returned #366. Signed-in Chrome's native parent/sub-issue UI established the missing six relationships. Each child was read back in the parent's actual sub-issue tree immediately; independent connector REST parent reads subsequently confirmed all seven links. No textual parent reference substitutes for these results.

The initial Project title filter found no Browser Contents items. After native attachment, #480 appeared in Project #4 and its five children appeared under expansion. Editing each child's real Project Status/Area then reloading confirmed persisted fields and membership. Evidence is `project-final.txt`, `project-hierarchy.png`, `project-r4.txt`, and `project-mac-epic.txt`. Priorities were empty and preserved. Automatic approval review initially rejected the bulk field operation as lacking authorization for exact values. Owner then explicitly replied **Approve Backlog / Browser**; the operation succeeded and was reloaded/read back. That rejection is resolved, not an outstanding permission request.

Dependencies read through both native REST directions:

- #395 blocked by #393 and #394; blocks #397 and #398. Preserved.
- #480–#485 blocked-by and blocking lists are empty. Preserved.
- Delivery order remains #481 → #482 → #483 → #484 → #485. Existing roadmap conventions use blockers for deliverable prerequisites, not every scheduling preference. #482 genuinely consumes thumbnails and #483 consumes the Grid modes, but an additional native chain is unnecessary to record the already explicit sequential owner gates; no speculative edges were added.

#480's body now lists six unchecked completion/reference gates. #395's appended checkpoint identifies its new child and preserves R2/R3 and all wider DoD. These bodies were fetched again after edits for read-back evidence. #366's wider DoD and field values were preserved. The broader Project inspection also exposed historical #460 Closed with Project Review; this is recorded as adjacent pre-existing drift, not evidence that this subtree is incomplete, and was not silently changed in this bounded audit.

Limitations: `gh` is absent; sandbox DNS cannot resolve GitHub, but authorized network escalation supports Git clone/fetch/read-only remote verification. The connector supports issue body writes and repository branches/files, but exposes no Projects v2 or native hierarchy mutation tools. Signed-in browser access supplied those missing capabilities. No current #480–#485 access blocker remains. Historical access-gap paragraphs in older issues/reports remain historical; they are not rewritten as successes.

## D. Windows-to-Mac parity matrix

Two layouts: **Grid** and **Details**. Grid has three distinct compositions. The following matrix describes the current Windows source at starting main. The Mac has a provisional Name/Kind/Size ListBox, not any of these completed scenarios. Legend: **A** already present in the accepted Mac foundation; **T** #481; **G** deferred #482; **D** deferred #483; **Q** deferred #484; **M** deferred #485; **X** explicitly out of scope. Multiple letters divide ownership rather than imply full delivery.

Source anchors: `LightflowStudio/MainWindow.xaml` templates at lines 666–815 and host at 1330–1375; `BrowserGrid.cs` layout at 374–465; `BrowserDetails.cs`; `WorkspaceState.cs`; `MainWindow.xaml.cs` sizing, layout, selection and query handlers. Mac: `Lightflow.UI/BrowserPane.cs`, `BrowserViewModel.cs`, `BrowserPresentation.cs`, `Themes/Lightflow.axaml`.

| Parity item | Grid Preview (Windows) | Grid Info (Windows) | Grid Hybrid (Windows) | Details (Windows) | Mac disposition / owner |
| --- | --- | --- | --- | --- | --- |
| Geometry | Image-led card; filename row hidden | 22-DIP upper state frame, compact image, 24-DIP lower frame, filename | Normal image plus state overlays and filename | 64-DIP row, 54-DIP preview; column layout | All absent: G / G / G / D. T adds only a small image in current row. |
| Spacing/density | 7 padding, 6 radius, 1 border, trailing 12 right/bottom | Same outer footprint; info image height max(48, normal minus 30) | Same base card; overlays margin 4 | Bottom separator; fixed left-aligned width | G for card rhythm; D for table density. |
| Dark styling | ShellPanel/Divider; image #05070A; host #0D0F13 | #1B2028 base with authored color-label tints | Translucent #9905070A overlays | ShellSurface headers and shared selected/focus brushes | A shell palette exists; G/D reproduce center styling, M adds authored tints. |
| Thumbnail content | Cached JPEG, category glyph if absent | Same cache, smaller well | Same cache under overlays | Same cached path in preview cell | T shared lifecycle and resident still/RAW/video proof in all future modes. |
| Fit/aspect | Uniform, clipped rounded well; letterbox rather than fill crop | Uniform in shorter well | Uniform under overlays | Uniform in compact cell | T preserves decoded aspect/orientation; G/D final well geometry. No crop policy invented. |
| Orientation | Source orientation normalized; authored video adjustment via OrientedPreviewImage | Same | Same | Same | T exact-once EXIF/track transform and read existing authored rotation; rotation editing X. |
| Text/truncation | Filename hidden; name in tooltip/accessible label | Centered 12-DIP ellipsized name below image | Same filename row below image | Resident text cells; missing/not-applicable values blank | A provisional name ellipsis; G/D composition. |
| Selection/focus | Stable tile identity, selection background + focus border | Same overrides tinted background | Same selected treatment over state | Same selected tile, row focus and anchor | A single AssetId and focus route; T cannot replace it; G multi-card/range/reflow, D row/column focus. |
| Scroll/virtualization | Virtualized recycling vertical rows; horizontal tiles per row | Same grouping, mode-dependent height | Same | Recycling rows, visible cells and horizontal header/body sync | A ListBox scrolling only; T bounds demand/pixels; G wrapping/anchors; D retained cells/sync. |
| Resize/reflow | Column arithmetic includes trailing margin; minimum one column | Same size-dependent arithmetic | Same | Left-aligned fixed widths, horizontal scrolling | A flexible center/pane clamps; G final reflow, D table resize behavior. |
| Empty/loading/error | Choose-location/empty/filter-empty text; category placeholder, work bar, failure badge | Same plus OFFLINE status | Same plus OFFLINE status | Same preview placeholder/progress/failure | A folder progress/cancel/error and counts. T explicit thumbnail states; G/D final composition; M status badge polish. |
| Presentation controls | Grid/Details switch; bottom Preview/Info/Hybrid buttons; six-level size slider/steps | Mode switch selects Info | Mode switch selects Hybrid | Header resize/reorder/optional columns; layout toggle | G three mode switching; D layout toggle, six sizes, columns and persistence. T no dead controls. |
| Metadata/status | Deliberately sparse preview mode | Rating/flag above; range/subclips/Color below; labels and filename | Conditional top/bottom authored overlays | Curated default/optional columns | M full indicator parity; G/D structure uses available resident values only. T loading/unavailable/failure truth. |
| Keyboard | Arrows, current item, Ctrl/Shift selection, Home/End, contextual actions | Same | Same | Same shared projection, row navigation | A basic native list/Locations/navigation keyboard; G/D final layout traversal. Player/Open X for #481. |
| Search/filter/sort | One query projection; no layout-local engine | Same | Same | Sortable headers call same query | Q typed semantics, including Details sorting integration. D may route supported sorts, never invent second comparer. |
| Preferences | Saved mode and size, grid anchor/selection | Same retained preference | Same | Layout, ordered/visible columns/widths, horizontal/semantic vertical anchor | A pane/window preferences and current navigation context only; G transient card mode then D durable presentation. |

Concrete shared controls/behaviors to retain or assign:

| Behavior/control | Windows reference | Mac ownership |
| --- | --- | --- |
| Back/forward/up/path/Go/Refresh; Subfolders | Existing navigation + scope icons/recursive roots | A accepted #456/#460/#465. T preview demand follows navigation without another history owner. |
| Media segments | All / Images / RAW / Video | Q; absence does not mean RAW discovery is unsupported. |
| Search and advanced filters | Search assets, Filter menu/chips, field/comparison/All/Any logic | Q, using existing `BrowserQuery`, `BrowserFilterDescriptors`, `BrowserStructuredFilterInput`; no second query owner. |
| Sort vocabulary | Name, capture/modified date, type, size, duration, manual, rating, flag, dimensions, frame rate | Q; deterministic ties/null-last and canonical rate buckets preserved. Manual ordering only in supported scopes. |
| Filter fields | Media type, camera, lens, capture date, duration, resolution, frame rate, Color/Camera LUT/Creative LUT, review range, subclips, rating, flag, label, keyword, path/file, aspect ratio, bit depth | Q; no new metadata extraction implied. |
| Footer | Visible/total count, selected count/size, Preview progress | A count/loading; T generating truth; G/Q/M richer selection/query/status counts. |
| Authored indicators | Rating, flag, label, saved range, subclips, Camera/Creative LUT indicators | M projection/presentation only. Authored editing is X here; preserve values. |
| Regenerate Preview | Existing explicit force path/cooldown bypass | T lifecycle/repair tests; minimal retry affordance if needed, no full maintenance Settings UI. D final placement if presentation controls require it. |
| Fresh default | Issue #395 explicitly approves fresh-profile Details, preserving saved choices | D on Mac; Windows currently falls back to Grid/Preview in WorkspaceState. Windows default change needs separately approved implementation/qualification, never smuggled into T. |
| Details columns | Default: preview, name, rating, flag, capture date, type, dimensions, duration, frame rate, size. Optional: modified, label, camera, lens, Color/LUT, saved range, subclips, keywords | D columns/width/order; Q sort; M values/status. Codec, marker/provider/raw metadata editing are not existing table parity. |
| Collections/context actions/drag/export/color assignment/full Player/Inspector | Existing Windows functions are larger than center presentation | X for #481; report gaps, do not imply the five content issues deliver all remaining R4 surfaces. |

Original reference search: repository image inventory, earlier Mac qualification reference notes, #480/#481/#483 descriptions/comments, and the known “Review Mac Browser Epic” planning chat were inspected. No original owner Details image was exposed. The repository `docs/assets/readme/browser-overview.jpg` was visually inspected: it is a Grid reference, not the required Details screenshot. Historical G3 `details-retained.png`/`details-stock.png` are research UI, not the shipping owner image. **Please supply the original Windows Browser Details screenshot before visual implementation acceptance.** No approximation was created. This gap does not invalidate the source audit.

## E. Current architecture

### Discovery and identity

`Lightflow.Application/MediaDiscovery.cs` classifies still/RAW/video/audio. `Lightflow.Catalog/CatalogReconciliation.cs` maps RAW to `raw-image`. `CatalogMediaFolderEnumerator` → `CatalogReconciliationService` → `MediaAssetService`/`CatalogMediaAssetRepository` preserves RootId and AssetId at logical relative keys. A changed source at an existing location keeps identity and authored data; incomplete/unavailable scans must not infer deletion. `MediaAssetService.ObserveAsync` rechecks physical source and updates cheap Catalog observations. Version-1 fingerprints hash length plus up to 64 KiB head/tail, not all bytes; they are not exact content equivalence proof.

Windows `MediaDiscoveryRefreshService` submits reconciliation to `DerivedWorkScheduler`. MainWindow `AttachBrowserDerivedWork` and `ApplyBrowserPreviewRecordsAsync` batch-hydrate results with Browser generation/batch checks. `BrowserGridModel` and `BrowserGridTile.ThumbnailPath` notify both Grid and Details. `CachedPreviewImageConverter` loads detached immutable pixels and closes the cache file. `OrientedPreviewImage` separately reads Catalog video rotation and rejects stale/revision-invalid updates.

Mac `MacShellCatalogSession.DiscoverBrowserFolderAsync` uses shared folder enumeration/reconciliation/recursive services with `MacBrowserMediaEffects`, native fingerprints, and `new MediaDiscoveryRefreshService(reconciliation, () => null)`. BrowserViewModel owns navigation epochs, cancellation, selected AssetId and discovery projection. BrowserPane uses a ListBox with Name/Kind/Size, basic accessibility labels and selection. No thumbnails or Preview metadata hydration exists. `MacBrowserMediaEffects` excludes app packages, links/protected roots, performs resident-media checks without hydration, and revalidates native mount/path facts. These checks must also guard future actual source reads.

### Generation and metadata

`LightflowStudio/ThumbnailGeneration.cs` contains request/result/status types, renderer interfaces, per-asset gates, priority gate, generator and Windows renderers in one file. The core is largely neutral but **not presently a neutral assembly API**. It references WPF validation, Windows composition, logging, Color/LUT dependencies and the legacy Preview store. Extracting declarations alone is insufficient.

Call order: Preview shared-operation lease → per-store/per-asset gate → priority/concurrency lease → source observation → Preview source observation → preferred-frame/Color lookup → current-file/version/visual identity and retry checks → render unique temporary artifact → decode-validation → re-observe source and visual intent → promote → conditional `SetArtifactAsync(ExpectedSource)` → return path. A second visual-intent check surrounds promotion. Temp files are deleted in finally. Failure preserves a usable previous artifact with independent warning; missing/unavailable sources retain existing cache and availability state. `BrowserPreviewReuse` prevents reviving a stale failed artifact under a different computed identity.

Stills: WIC `BitmapDecoder` frame zero, EXIF orientation 1–8 before downscale, max dimension 512 with no still upscaling, JPEG quality 85. WIC's full OnLoad source decode may have high source-sized memory cost despite small output. Metadata uses `WicImageMetadataReader`; image probe version remains 1. Video/audio use `FfprobeMediaMetadataReader`, normalized by `FfprobeMetadataNormalizer`; current non-image probe version is 2. Metadata is rebuildable Preview JSON, not authored Catalog data. Broad extension classification does not prove decoder support.

RAW: scheduler `supportsThumbnail` is exactly image/video; composite renderer has no raw-image case. No RAW engine/package is present in this production pipeline. RAW metadata may fall through to ffprobe but that is not supported RAW preview generation. Historical G3 explicitly did not qualify RAW.

### Cache/lifecycle

`LightflowStudio/PreviewPersistence.cs`: `IPreviewStoreService`, `PreviewRecord`, `PreviewSourceIdentity`, `PreviewComponentUpdate`, `PreviewStoreService`. Separate `previews.db` (application ID LFPR, schema 4), thumbnails tree and standard-preview tree; local WAL, non-pooled operations, independent throughput durability and integrity/migration checks. Existing persistence references `ApplicationDataProfile.GuardAccess`, startup evidence/diagnostics, `MediaPathSemantics`, SQLite and retry policy; route these through existing shared storage guards rather than remove them. `Lightflow.Application/ILightflowStorageLocations.cs` already names cache paths. Concrete Preview storage has not been extracted to Catalog.

Cache path: AssetId N-format directory partitions (first two + next two chars); filename includes AssetId, generator version (1), fingerprint version, and first 32 lower-case hex chars of SHA-256 over size/write ticks/fingerprint version/fingerprint/visual identity. Visual identity is Original or Color identity plus preferred-position ticks/revision. No path-as-identity and no cross-platform AssetId equality requirement. Source change marks components stale; availability alone does not erase pixels. Version, Color/LUT and preferred-frame changes require new work. Authored video rotation is presentation state, excluded from cached pixel identity. Missing artifact files are repaired on demand even if the row says Current.

`PreviewRetryPolicy`: 30-minute persisted metadata/thumbnail deadlines, shared gates per store via ConditionalWeakTable. Explicit force or changed source/work identity allows retry; dependency-unavailable checks can re-evaluate newly available LUTs. No retry timer. `PreviewOperationCoordinator` has shared generation/exclusive maintenance leases and a writer turnstile; preserve lock order. Generator's nested metadata service intentionally does not reacquire the same coordinator, avoiding a maintenance-writer deadlock. `PreviewMaintenanceService` owns quota/retention/clear/rebuild; lifecycle cancels/drains before replacement.

Mac `MacCatalogLifecycleEffects.SupportsCompletionPeer` is false and its peer entry points throw. The Catalog host remains unpaired/Dirty with deep validation. Merely creating previews.db must not flip this flag or fabricate paired clean-start evidence. New Preview consumption needs real close/drain integration while retaining these conservative invariants unless separately qualified.

### Scheduling, cancellation and errors

`DerivedWorkScheduler` has fixed workers (default two), Visible/Normal/Background queues, one active dictionary item per AssetId, queued priority promotion via generation tags, and multiple subscribing batches. Cancel removes only the caller's demand; work cancels when its last batch leaves. Running low-priority work is not preempted. A folder batch can hold O(folder size) pending entries; worker count alone does not bound queue/memory. Windows navigation Preview batches have independent lifetimes and are not simply cancelled by folder-enumeration cancellation. UI generation checks prevent old-folder results from being applied.

`PriorityAsyncGate` also bounds generator execution and skips cancelled waiters. `ProbeProcessRunner` kills and joins a cancelled subprocess tree. WIC cancellation is checked around synchronous decode; it cannot interrupt the native call mid-decode. Native Mac adapters need the same honest distinction between cancelled demand and underlying operation completion. UI publication must recheck identity after dispatcher marshaling. Errors include source/root/asset missing, unsupported, generator unavailable, invalid output, deferred retry and failed; `PreviewFailureReason` is already neutral in Application. No one placeholder should claim all these are merely “loading.”

### Poster selection and decoder limits

`RepresentativePositionAsync`: preferred Catalog frame overrides automatic policy. Otherwise video with positive metadata duration d seconds uses d/2 if d < 1, else clamp(d × 0.1, 1, 30). Missing/invalid duration or absent metadata service uses 1 second. Examples: 0.4 s → 0.2 s; 5 s → 1 s; 20 s → 2 s; 600 s → 30 s. Exactly 1 s requests 1 s, which can be at EOF and fail; do not silently change that Windows behavior. Preferred frame identity uses TimeSpan ticks/revision, not an independently selected Mac frame.

`FfmpegCommandBuilder.ExtractThumbnail` formats TimeSpan seconds invariantly with up to nine decimals and emits input `-ss`, then `-i`, no audio, one video frame, ordered LUT filters, 512×512 aspect-decreasing scale, MJPEG q=3/image2. It does not pass frame number, fps, `-noaccurate_seek`, `-skip_frame nokey`, explicit `-map`, or `-seek_timestamp`. Stream selection/default track and nonzero timeline starts need fixtures; do not assume the first/default stream is interchangeable with Mac's track choice.

FFmpeg generally seeks to an earlier seek point and decodes/discards to the request when transcoding with default accurate seek. Thus input `-ss` is not a keyframe-only poster policy. This is decoder behavior, not an exact output-PTS guarantee: the current JPEG result does not record selected PTS/timebase. [Official FFmpeg seeking reference](https://ffmpeg.org/ffmpeg.html#Main-options). There is no application alternate-time/first-frame fallback; failure is classified, cached appropriately and later retried under existing policy. Returned prior pixels are cache retention, not another successful decode.

Mac inference/recommendation: use the exact shared seconds policy and explicit timeline-origin conversion, request a bounded near-time AVFoundation frame, record requested and actual rational CMTime, honor track transform exactly once, and compare actual source frames. Do not leave AVAssetImageGenerator's default infinite tolerances: that can choose noticeably different frames. Zero tolerances can add decode delay; start with a measured bounded ±two-frame window for CFR and actual neighboring PTS intervals for VFR, then qualify. Never infer VFR frame distance from nominal fps. Prefer identical frame, accept one/two adjacent source frames, reject larger unexplained mismatch. [Apple tolerance contract](https://developer.apple.com/documentation/avfoundation/avassetimagegenerator/requestedtimetolerancebefore), [asynchronous image generation](https://developer.apple.com/documentation/avfoundation/creating-images-from-a-video-asset). These references establish API intent, not this app's measured performance or corpus coverage.

### Discrepancies to preserve

Older architecture prose says thumbnail generation never modifies Catalog records: in actual source it calls `MediaAssetService.ObserveAsync`, which can update source observations, though authored data is not changed. Metadata documentation's historical probe-version-1 text is superseded by current video/audio version 2. “UI-independent” is a behavior design, not current assembly portability. Accepted shared-pixel direction does not mean current WIC output or Mac RAW is already converged. Existing Windows default Grid/Preview also differs from the accepted future Details default. None was silently reconciled by Phase A.

## F. Proposed architecture

**Recommended: mechanically extract existing policy, keep adapter seams small, add viewport demand to the shared Browser.** This is a bounded prerequisite tranche for #481, not full R3/R4 completion.

| Component | Recommended treatment |
| --- | --- |
| Existing Catalog/discovery/domain/Actions | Reuse unchanged: identities, reconciliation, filesystem safety, selection and lifecycle admission. |
| Application contracts/workflows | Move neutral Preview record/status/update/store contracts; thumbnail request/result/render ports; existing generator/scheduler/gates/retry/maintenance coordination and metadata models/service orchestration. Keep numeric statuses stable. Extract logging, output validation and LUT resolution as narrow injected functions/ports only where current reverse dependencies demand it. |
| Catalog infrastructure | Move existing SQLite PreviewStoreService with schema 4, SQL, integrity checks and storage guards intact. Reuse existing StartupDiagnostics/evidence and shared storage contracts. No Catalog schema change or authored tables. |
| Windows adapters/composition | Keep WIC still/metadata/validation, FFmpeg/ffprobe process behavior, command builder, output quality, versions, colors, preferred-frame handling, WPF binding/converters and existing scheduling entry points. Factories wire extracted services; wrappers retain legacy callers without duplicate policy. |
| Shared image mechanism | In existing UI imaging adapter area, use production-compatible Skia for common formats and encode a small detached cache artifact; framework bitmap ownership remains here. No Skia types in Application. A small neutral owned-pixel descriptor carries dimensions/stride/alpha/color-space/orientation provenance for native fallback. Do not promote the whole research harness. |
| Mac native adapters | ImageIO for tested format gaps and bounded resident RAW; AVFoundation for duration/poster/actual CMTime; native residency/path preflight immediately before source open. Native buffers/CGImage/AV objects are disposed by adapter; no native UI/view. |
| Shared Browser integration | BrowserViewModel stays sole navigation/selection owner. Add viewport-scoped derived-work subscriptions and immutable per-asset preview state. BrowserPane realizes small images with explicit loading/unavailable/failure text and existing list geometry. UI dispatcher rechecks Catalog epoch, scope generation, AssetId and visual work identity before applying. |

No new assembly is recommended initially: Application can own orchestration and neutral ports; Catalog already references Application/SQLite; UI owns Skia/Avalonia conversion; Mac owns native adapters. If a supposedly small extraction pulls shell settings, full LUT library, player, platform storage owner or unrelated services into neutral projects, stop and review closure rather than copying everything. A dedicated Lightflow.Media assembly is a reasonable later R3 home, but not a reason to design a new subsystem now.

The renderer may retain the existing temporary-file port; add a validator port instead of importing BitmapDecoder into neutral orchestration. An owned-pixel descriptor is only needed across the native gap seam, not as a new requirement for every Windows renderer. Preserve Windows WIC unchanged during #481 as a temporary accepted compatibility adapter; broad common-JPEG migration and goldens remain R3. Mac common Skia decoding follows ADR0006's direction; small cross-decoder pixel differences are not promised identical. Source profile → explicit SDR sRGB output, exact-once orientation, alpha flattening intent and release of source handles must be explicit. Current Windows still transparency/JPEG behavior is preserved; Mac's proposed background flattening needs vectors, not an invented visual claim.

RAW proposal: ImageIO's supported resident camera formats with an explicit OS/format/camera fixture list; prefer a usable embedded preview, otherwise bounded native thumbnail decode where supported. Unsupported variants return truthful unavailable/unsupported state. Do not turn every extension in the registry into a support promise, add LibRaw, invoke Quick Look hydration, or deliver a RAW conversion engine. Embedded-preview versus developed-RAW appearance is a material product boundary needing owner approval. [ImageIO thumbnail/orientation API](https://developer.apple.com/library/archive/documentation/GraphicsImaging/Conceptual/ImageIOGuide/imageio_source/ikpg_source.html).

Video proposal: bounded asynchronous AVFoundation adapter, max 512 output dimension, shared duration policy and track/timeline mapping, requested/actual timestamp diagnostics. Use system frameworks; no new FFmpeg distribution solely to eliminate one/two-frame differences. AVFoundation format coverage is narrower than the Windows distributed decoder; unsupported codecs/containers must be reported and owner-qualified. Existing authored Camera→Creative LUT intent must be read, not silently replaced by Original. A small existing-compatible Skia/CPU LUT path can render thumbnail pixels; if the LUT dependency or color-intent extraction becomes broad, stop for an explicit tranche decision. Missing LUT returns dependency-unavailable and never tags uncolored pixels with colored identity. No Player integration is required.

Cache/lifecycle: same source/work invalidation, relative containment, local Preview database and deterministic partitions. Independent Catalogs naturally have different AssetIds/files; comparison uses fixture SHA-256, never path/cache-name equality. No cache export/import or NAS Catalog admission. Scope demand can be cancelled without deleting valid completed artifacts. Preserve generator version 1 for unchanged Windows output; separate Mac local caches may have their own qualified mechanism version so future decoder changes invalidate local output without forcing Windows regeneration. Persist preferred/color/version identity and retry deadlines, not platform handles. Mac opens one real Preview store under its task/local data root, closes/drains workers and store with the Catalog consumer, and retains deep/unpaired startup until a real paired completion protocol is separately qualified. Preserve the existing local-only Catalog admission and all Move/publication refusals.

Scheduling: reuse the three-priority scheduler and per-asset gates. Shared UI submits only visible assets plus a small bounded overscan window, promotes newly visible pending work, withdraws offscreen/navigation subscriptions, and deduplicates repeated demands. Initial proposal: two workers, one overscan viewport and a pending-demand cap derived from realized viewport; benchmark before adopting a numeric product limit. Do not submit the entire recursive result as Visible on Mac. Preserve Windows entry points in this slice. Cancellation must reach queues/native operations; in-flight uninterruptible native work retains its slot until it exits. Never free the slot early and silently exceed concurrency. Bound decoded bitmap retention by current viewport/overscan and reuse detached cache images; do not materialize one bitmap/task per Catalog asset. Existing Catalog asset lists can still be O(n); report this separately from bounded thumbnail memory.

Tests should exercise actual extracted shared services with fake renderer/time/store and native contract fixtures, not only parallel copies of policy. Verify source mutation/Color/preferred-frame races before and during promotion; queue promotion/cancel races; maintenance lock ordering; last-subscriber cancellation; restart/foreign-db/path containment; native memory disposal; and stale dispatcher completion. Performance evidence should include worker count, pending demand, active/native buffers, bitmap retention, first-visible latency, warm/cold revisit, scroll responsiveness and peak RSS. Large files may still have source-sized decoder cost; ImageIO thumbnail APIs can reduce this but do not establish a guaranteed memory bound without measurements.

Alternatives rejected/deferred: Mac-only thumbnail/cache engine (duplicates identity/retry/lifecycle); wholesale WPF/Player/Color refactor (unneeded regression scope); global Skia/JPEG switch (changes Windows pixels and unqualified package closure); universal FFmpeg Mac bundle for parity (packaging/codec/license cost before need is established); exact frame-accurate full-GOP/linear decode to guarantee hashes (unjustified when adjacent frames suffice); stock Details control in #481 (wrong slice/accepted retained-cell direction).

## G. Proposed #481 implementation plan

1. **Contract/closure tranche:** record approved decisions in #481; extract neutral contracts and existing store/orchestration with compatibility wiring. Qualify Windows unchanged output and cache/schema/version behavior before native integration. Expected areas: Application, Catalog, legacy ThumbnailGeneration/PreviewPersistence/DerivedMediaMetadata/DerivedWorkScheduling/PreviewRetryPolicy/PreviewMaintenance and Windows factories.
2. **Native decoder tranche:** common image adapter, ImageIO gaps/RAW, AVFoundation duration/poster with requested/actual PTS and native resource accounting. Wire guarded resident-media reads. No app-wide codec or toolchain upgrades. Stop if accepted RAW/codec/color boundary cannot be met.
3. **Lifecycle and demand tranche:** one Preview consumer under MacShellCatalogSession, real cancel/drain/dispose and conservative startup evidence; viewport/overscan subscriptions in BrowserViewModel and minimal images/states in existing BrowserPane. Preserve selected AssetId, history, recursive discovery and shell layout.
4. **Qualification tranche:** neutral/native suites, same-video independent-Catalog comparison, large-folder cold/warm runs and mounted NAS media where practical. Independent Windows private-desktop Release tests through Test-Noninteractive.ps1, fresh Build-Release.ps1 PullRequest package, smoke/dependencies and owner hands-on. Exact-head CI is additional evidence, not the Windows local package substitute.
5. **Handoff tranche:** fresh arm64 app using existing Mac packaging scripts; exact source/tree/dependency/build hashes, isolated acceptance data root, launch command, full Markdown and requested ZIP. Confirm artifact provenance before declaring it current. Draft implementation PR only after separate implementation authorization; owner architecture and hands-on acceptance, Windows qualification and explicit merge remain gates.

Do not implement final card layouts, Details, advanced query controls, cloud-only preview/hydration (#468), Player, Inspector, editing, TourBox, general Catalog portability, active network Catalog, signing/notarization or release/distribution. No work begins on #482–#485. No owner acceptance can be inferred from screenshots or tests.

## H. Proposed owner acceptance

Create a new task-owned acceptance root and disposable fixture media. Use no normal Catalog or personal media. Fixture manifest includes SHA-256, size, codec/container/camera format, dimensions, duration, orientation/profile, native OS/decoder version and expected support. Automated validation precedes the hands-on scenarios; owner records actual outcomes separately.

| # | Fixture / action | Expected behavior and required evidence |
| --- | --- | --- |
| 1 | Resident JPEG/PNG/TIFF/HEIC, portrait/panorama and EXIF 1–8; alpha/ICC vectors | Correct visible small thumbnails, aspect preserved, orientation exactly once, explicit color/alpha policy; native resource and normalized-pixel checks plus owner inspection. Unsupported variants honestly identified. |
| 2 | At least a DNG plus representative owner-approved camera RAW formats | Visible preview for every advertised supported fixture; unsupported list explicit. Embedded/developed provenance recorded. No source mutation or automatic download. |
| 3 | Resident H.264/HEVC MOV/MP4 and a declared unsupported/corrupt video | Correct representative poster or specific truthful state; requested/actual PTS logs, no Player/audio activation. |
| 4 | Quit normally, reopen exact fresh package/root, revisit same folder | Same Catalog/AssetIds/authored values; Preview file reuse and persisted retry deadlines. Logs/counters establish no unnecessary regeneration. |
| 5 | Browse two folders/back/forward/Refresh, retain selected item | Navigation remains usable, correct folder images, stable AssetId, no duplicate discovery/selection owner. |
| 6 | Rapidly alternate folders and scroll out of view during slow decode | Demand withdraws; bounded running count; obsolete completion never replaces new row/image. Record queue/native operation counts and cancellation latency. |
| 7 | Disposable 1,000-asset set, then practical 10,000-asset result set | Only viewport/overscan requests/bitmaps retained; scroll and toolbar respond during decode; queue/RSS/latency traces and owner observation. Numerical budgets agreed from baseline, not claimed by this proposal. |
| 8 | Controlled delayed renderer; source change and Color/preferred-frame update mid-work | Old source/work cannot publish Current or appear in recycled/new-scope row. Deterministic automated race evidence; valid replacement remains available. |
| 9 | Delete task fixture, disconnect only task-owned media where safe; corrupt cache/missing artifact | Unavailable/missing/failure distinct from loading; retained permissible cache labeled truthfully, repair on subsequent valid demand, authored state preserved. No forced unmount of owner storage. |
| 10 | Read approved fixture set on already-mounted NAS | Media read through existing mount/residency safeguards; Catalog/cache remain local. Record mount/format and cold/warm behavior; absence of safe fixture/access is NEEDS WORK, not PASS. |
| 11 | Same exact CFR/VFR videos independently cataloged on Windows/Mac | Prefer same frame, accept ±1–2 adjacent source frames; greater mismatch FAIL/NEEDS WORK with requested/actual PTS evidence. See method below. |
| 12 | New isolated data root; record IDs before/after cache rebuild/restart | Catalog identity/authored rows preserved, no normal profile writes, no active network Catalog. Preview removal only in disposable fixture; actual native lifecycle checks. |
| 13 | Independent Windows same-source qualification | Existing thumbnail/persistence/retry/scheduler/Color/preferred-frame/Browser suites, fresh installer/portable package, smoke/dependencies and owner Windows acceptance. Preserve failures/exceptions without relabeling. |

PASS requires all expected behavior for the advertised supported corpus, bounded work evidence, no identity/data regression and owner hands-on acceptance. FAIL means wrong pixels/frame policy, stale publication, source/authored mutation, unsupported storage admission, unbounded active work or a demonstrated responsiveness regression. NEEDS WORK means fixture/platform/reference unavailable, visual/numeric ambiguity or incomplete qualification; it never becomes PASS by omission. Final Grid/Info/Hybrid composition and Details are not prerequisites for #481's minimal proof.

Cross-platform poster method:

1. Generate or obtain licensed disposable CFR 24/29.97/60 fps and VFR clips with burned-in source frame number and timestamp; include long GOP/B-frames, rotation, nonzero start/edit-list, 0.4/1/5/20/600-second durations and unknown-duration/failure cases. Keep the same bytes on each platform, verified by whole-file SHA-256 (not sampled Catalog fingerprint).
2. Use separate new local Catalogs/cache roots, no preferred frame or Color for default-poster comparison. Record AssetIds only as local associations. Compute the shared requested time once from the same normalized fixture duration; separately log native versus ffprobe duration differences near policy boundaries.
3. Decode/cache once through each actual product adapter. Windows's current JPEG has no actual PTS; use burned-in frame ID plus an independently generated ffprobe source-frame PTS/timebase oracle. If ambiguity remains, add a test-only diagnostic decode trace without changing product selection arguments. Mac records actual CMTime numerator/denominator and timeline origin.
4. Compare frame ordinal and actual neighboring PTS positions, not compressed JPEG hashes or nominal fps for VFR. Record exact/±1/±2/different/undetermined. Inspect visual content as well; tolerable decoder/color differences are not another frame. Reset/reopen and repeat a warm-cache pass.
5. For >2-frame differences, first inspect duration/track/start-time/tolerance/rounding mismatch. Do not add expensive precision/dependencies until evidence identifies a real policy defect and owner approves any scope change. Unknown-duration 1-second failures and unsupported native codecs remain explicit limits.

## I. Validation performed

- Read complete current cloned AGENTS.md; reviewed current architecture/shared boundaries/ADRs, Catalog/discovery and Mac qualification, design/input/metadata/Browser documents and three history records. Source manifest identifies exact file hashes and immutable Git blob IDs.
- `git ls-remote ... refs/heads/main` independently returned starting SHA above; full `git clone`, branch inventory and every fetched remote registry check completed. Connector issue/PR identity searches returned no 036 allocation. Remote task registry reservation was fetched, compared byte-for-byte to local edit, then fast-forwarded.
- Actual Windows source call paths, cache SQL, renderers, scheduler, query/templates/preferences and current Mac composition were inspected. A release/1.0-to-main diff shows Browser template/scheduler differences; this audit intentionally targets current development source of the shipping WPF implementation, not an assertion that a previously released executable has every main behavior. ThumbnailGeneration.cs and PreviewPersistence.cs match that maintenance branch at inspection; UI differences remain explicit.
- Native parent before/after reads, dependency direction snapshots, reloaded browser Project read-back and updated Epic bodies are included. No inferred mutation success.
- A real GitHub Project screenshot was captured; no Lightflow application screenshot or runtime visual verification was performed. The signed-in browser worked; application GUI lock status was not tested and did not block this source audit.
- Documentation validation, whitespace, production-unchanged check, evidence manifest/hash verification and ZIP integrity results are recorded in `evidence/validation.txt`. No product build or test run was necessary or claimed for this documentation-only Phase A. Existing automated tests were inspected to plan future validation, not rerun as implementation acceptance.
- No personal media, private Catalog, credential, cache tree or source tree is included. Known planning chat was inspected only for the requested reference; private transcript/sidebar content is not packaged or published.

## J. Historical maintenance

**Historical maintenance: Not warranted.** The three history documents were reviewed, including their current continuation/editorial policy. This phase reserves an assignment, performs routine roadmap hygiene and proposes an unapproved architecture. It does not deliver an accepted consequential architecture or product capability. No Chronicle/Development History/Source Index changes; existing AI attribution, source provenance and pending historical wording remain intact. Reassess when an approved thumbnail integration is actually delivered within this same assignment/PR.

## K. Artifact manifest

| Artifact | Exact location |
| --- | --- |
| Complete report (repository) | `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/docs/qualification/browser-thumbnail-foundation-036-phase-a.md` |
| Portable report copy | `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-phase-a/REPORT.md` |
| ZIP | `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/LF-MAC-DEV-036-Phase-A.zip` |
| Supplementary standalone reports | `artifacts/lf036-phase-a/PARITY-MATRIX.md`, `THUMBNAIL-AUDIT.md`, `PROPOSED-ARCHITECTURE.md`, `ACCEPTANCE-CHECKLIST.md`, `GITHUB-RECONCILIATION.md` |
| Source/provenance manifest | `artifacts/lf036-phase-a/SOURCE-MANIFEST.json`, `PROVENANCE.json` |
| Evidence | `artifacts/lf036-phase-a/evidence/` parent/dependency/issue snapshots, Project text and real screenshot, validation output |
| Per-file hashes | `artifacts/lf036-phase-a/SHA256SUMS.txt` (excludes itself); ZIP hash in sibling `LF-MAC-DEV-036-Phase-A.zip.sha256` |

The source manifest hashes inspected source/documents without bundling the source tree. Portable standalone reports are extracts of this complete document and labeled proposals. Exact production baseline and reservation commit are immutable; future report edits are identified by content hashes. The ZIP excludes private transcript, personal media, Catalogs and build outputs.

Ready-to-copy locator command:

```sh
open -R "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/LF-MAC-DEV-036-Phase-A.zip"
```

## L. Outstanding gates

1. Owner review/adjustment and explicit approval of proposed extraction/native decoding/cache/demand boundaries.
2. Owner RAW format/embedded-preview policy and native video codec/track/tolerance qualification scope; no all-extension promise.
3. Owner acceptance of bounded Preview consumer with conservative unpaired/deep Catalog startup; no automatic clean-certificate expansion.
4. Original Windows Details screenshot, still unavailable; attach/reference durably before relevant visual acceptance.
5. Explicit authorization to implement #481 in this same identity/chat, followed by independent Windows qualification, fresh packages, exact-head CI and owner hands-on/merge gates. None has occurred.

Project #480–#485 hygiene is reconciled; adjacent historical #460 status drift is separately disclosed. Broader R2/R3/R4/#366 requirements remain incomplete. **Phase A stops here. No production implementation or implementation PR is authorized by this report.**
