# FFprobe technical metadata (#311)

Baseline: main `1434713edf17b77258d03f5c808f77a7c9be1213`. This is the dependency-free
normalization work extracted from retired #299. It adds no provider, camera-profile
interpretation, vendor parsing or automatic LUT rules.

## Pixel-format authority

Pinned FFprobe 8.1.2-34-g9b6c8969e0 exposes `-show_pixel_formats`: individual component
`bit_depth` values, component counts, RGB/palette/hardware flags and
`log2_chroma_w` / `log2_chroma_h`. These are different from total `bits_per_pixel`:
8-bit yuv420p has 12 bits per pixel; 10-bit yuv420p10le has 15.

`scripts/Update-PixelFormatFacts.ps1` uses the existing checksum-verified FFmpeg setup
and generates `FfprobePixelFormats.Generated.cs` from those descriptors. Run it when
updating the pinned FFmpeg manifest and review the generated diff. A regression checks
that its recorded descriptor version matches the manifest. At runtime this is an
exact-name in-memory lookup; no descriptor process, native library, filename parsing
or suffix heuristic runs for each asset. Unknown/custom names remain unknown.

Valid explicit video `bits_per_raw_sample` (1–64) wins. Missing, zero, malformed or
out-of-range values fall back only to a known software format whose components all
have the same depth. Palette/hardware formats and mixed component depths cannot
supply this scalar fallback. Container, audio and embedded attached-picture depths
never supply primary video depth. Attached pictures are excluded from video selection.

Chroma is a separate normalized `ChromaSubsampling` fact. Known non-RGB formats with
at least three components and descriptor subsampling (1,1), (1,0), (0,0) yield 4:2:0,
4:2:2, 4:4:4 respectively. Other combinations remain unrepresented; RGB, gray and
palette/hardware formats do not receive invented YUV chroma labels.

## Query and presentation

The existing single FFprobe invocation explicitly selects format and stream tag
sections for creation time, timecode and encoder, plus legacy stream `rotate`.
Stream side-data selection retains type, display matrix and rotation. Sample/display
aspect ratios and attached-picture disposition are also requested. There are no
packet/frame/full-duration scans. Unrelated tags are not selected.

Generic tags remain in the existing raw/rebuildable JSON for their proper semantics;
this change invents no capture-date, camera-identity or encoder Inspector rows. Encoder
can describe an export application and is never treated as manufacturer/model.

Inspector reads cached normalized facts and adds Chroma beside Bit depth. Matrix
replaces Space; Transfer and Primaries remain separate. Existing row aggregation and
publication conventions are retained. #233 and #286 remain open on this baseline;
this change follows current Inspector text conventions and introduces no parallel UI
resource or layout system. Video Profile remains the existing codec profile.

The code property is `ColorMatrix`, with `JsonPropertyName("colorSpace")` retaining the
existing serialized key. Old offline records deserialize without migration; absent
Chroma remains missing until normal refresh. Matrix BT.709 implies no recording
profile or gamut. Raw metadata is not exposed as an Inspector dump.

## Cache and orientation

FFprobe metadata version advances to 2. `ProbeVersionFor` keeps image metadata at
WIC version 1. The service, scheduler and Browser publication use the same selector.
Online video/audio metadata refreshes as assets enter normal bounded discovery work;
there is no global cache deletion, startup-wide rescan or Preview-image version change.
Source-identical thumbnail artifacts remain current. Offline records remain readable.
After refresh, restart/revisit reuses metadata without repeated source probing.

Display geometry still uses `MediaDisplayGeometry` and `VideoRotation`. Correctly
selected source side data feeds the existing source-plus-Catalog adjustment model;
non-square/ambiguous geometry remains unknown. #312's detached Preview bitmap boundary
is untouched. The accepted hands-on extension adds a component Bit depth facet to the shared Browser/Smart Collection query model.

## Validation and cost

Focused regression coverage includes component-depth precedence and exclusions,
chroma, matrix serialization, real selected tags/side data, source rotation/composition,
metadata-only version refresh, unchanged image-cache reuse, offline Inspector reads,
restart reuse, thumbnail scheduling, Inspector publication and rotation pixels.
The local focused run passed 233 tests, including shared Browser/Smart Collection
query compatibility. The full suite is deliberately deferred until hands-on acceptance.

Five alternating-order baseline/changed pairs used fresh task-owned Catalog/Preview
roots and the actual two-worker scheduler, with the same pinned tools. Seven synthetic
clips covered H.264, rotated H.264, ProRes 4:2:2, and FFV1 8/10-bit 4:2:0/4:2:2/4:4:4.
Three prior real-library representatives added Pocket 4, Action 6 and Neo 2 HEVC.
These are Jeremy's available files, not independent certification of camera originals.
Hashes, byte lengths and modification times were verified unchanged; no media is tracked.

| Median, milliseconds | Main | #311 |
| --- | ---: | ---: |
| Complete metadata + thumbnails | 1706.94 | 1684.90 |
| First metadata | 111.73 | 115.37 |
| First thumbnail | 261.56 | 262.03 |
| Summed overlapping metadata service time | 833.84 | 851.95 |
| Warm revisit | 13.60 | 14.11 |

Every run generated ten metadata and ten thumbnail results with zero failures. Every
warm pass made zero further metadata/thumbnail service calls. This shows no material
Preview-path regression in the measured set, not a performance guarantee or a full
interactive-library benchmark. OS/filesystem caches were not flushed. Small latency
differences fall within run-to-run variation.

Read-only execution of the changed normalizer recovered 10-bit / 4:2:0 for Pocket 4
and Action 6, and 8-bit / 4:2:0 for Neo 2, all lacking explicit component depth.
Their Matrix/Transfer/Primaries remain separately reported BT.709 declarations.
Detailed measurements and source hashes are in
[the compact evidence file](validation/FFPROBE_METADATA_PERFORMANCE.json).
The small diagnostic harness, synthetic media, private input paths and full local logs
remain under the owning clone's ignored `.cache`; no benchmark runtime ships.

Local-first gate: focused validation and a fresh isolated package for Jeremy's hands-on
acceptance. Full suite, push, PR, merge and #311/#30 closure remain unauthorized here.


## September 25 hands-on iteration

Inspector now presents Camera Make and Model for every selection, using the same
normalized image facts that feed Camera Filters. Absent fields display Missing;
no encoder-to-camera inference is introduced. The screenshot folder contains four
JPEGs and ten MP4s (plus ten LRF companions): the four known cameras and ten known
frame rates reflect those different available facts. A read-only full FFprobe tag
query on the selected MP4 returned encoder `DJI OsmoPocket4` but no camera make/model.
This explains the observed coverage without establishing camera identity for video.

Bit depth choices, counts, predicates and saved Smart Collection intent use normalized
video component depth. Image pixel depth is bits per pixel and is deliberately excluded.
Unknown values do not match; metadata publication refreshes active depth filters.
Single-value facets use the same informational presentation as Camera/Frame rate;
multiple values offer choices. Existing enum identities remain stable.

No probe contract changes or further cache-version bump is required for this iteration.
Focused metadata, Inspector, Browser query, Aspect Ratio and Smart Collection tests:
153 passed. Full-suite and hands-on acceptance remain separate gates.
