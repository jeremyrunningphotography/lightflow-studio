# Capability: Video Processing

## Goal

Provide a cohesive set of tools for inspecting, converting, preparing, validating, and
recovering video files.

## Current capabilities

- Batch encoding
- LUT application
- NVENC H.264 and HEVC
- Recovery modes
- Decode verification
- Lossless MP4 rewrap
- Editing proxy generation
- Contact sheets
- FFprobe inspection
- Experimental Premiere timeline export

## Planned expansion

- Broader hardware and CPU backends
- General delivery presets
- Configurable thumbnails and frame extraction
- Audio extraction
- Improved stream selection
- Better handling of multi-audio-track cameras
- Richer repair diagnostics
- Shared preset import/export
- Pipeline steps for all stable operations

## Experience requirements

All video tools should share input picking, media inspection, destination rules, progress,
cancellation, logs, and result summaries.

Export execution uses the shared, WPF-independent Jobs runtime over the existing typed Encoding plan and FFmpeg adapter. `Parallel exports` is persisted materialized file-level concurrency (1–8, default 2), not FFmpeg thread count. Pause is drain-and-pause: no new files start, active files finish, then the Job becomes Paused. Active drain time counts as processing time; only the fully Paused interval is excluded from elapsed time and resumed ETA. Cancellation reports `Cancelling` while accepted cancellation is terminating active FFmpeg process trees, then terminal `Cancelled`; incomplete `.lightflow` outputs are removed while completed results are preserved.

Operational Jobs checkpoints are separate from History. Restart never treats a previously Running item or a merely existing output as complete; source/output/LUT identity is revalidated and uncertain work becomes Needs Attention for the future Jobs UI. History remains the final provenance and Review & Rerun record.

## Export materialization contract (#167)

Export configuration is policy; each `JobItemDefinition.MaterializedExport` is immutable execution intent. Source-dependent choices are resolved independently for every input before planning. The materialized record, rather than mutable Catalog state or a later probe, drives FFmpeg, output extension, diagnostics, History, recovery, Review & Rerun, and resume identity. Older records without this optional field use the pre-#167 job-global compatibility adapter.

| Setting | Future label/default | Same as Source | Materialization and mixed batches | Fallback/error |
|---|---|---|---|---|
| Resolution | Same as Source | Yes | Keeps `Source`; FFmpeg adds no scale/pad filter, so dimensions and aspect remain input-specific | Explicit presets retain existing scaling |
| Frame rate | Same as Source | Yes | Keeps `0`; FFmpeg adds no `fps` filter, so 24/60/VFR inputs retain their own cadence | Explicit 1–240 fps adds conversion |
| Video codec | Same as Source | Yes, re-encode | H.264 resolves to H.264 NVENC and HEVC to HEVC NVENC per input; it is never stream copy | Other source codecs fail preflight explicitly |
| Container | Same as Source | Yes | MP4/MOV/MKV resolve per input and determine each output extension/muxer | Unsupported containers fail preflight; no silent MP4 substitution |
| Quality | Automatic | No | Deterministic recommended constant-quality strategy; source bitrate is not reused as a second-generation quality target | Explicit CQ/VBR/CBR and advanced bitrate controls remain available |
| Audio | Use source audio | Copy preference | Untrimmed Normal work stream-copies audio. The materialized intent also records deterministic AAC fallback bitrate/rate/channels | Trim or recovery processing uses the recorded AAC fallback; No Audio and explicit AAC remain distinct |
| Sample rate/channels | Same as Source | Yes when encoding | Source values are captured per input for fallback/explicit encoding; FFmpeg supports the materialized channel count rather than silently forcing mono/stereo | Explicit UI choices remain limited to supported product choices |
| Encoder | NVIDIA NVENC when available | N/A | Capability state distinguishes implemented+available, implemented+unavailable, and not implemented | A real one-frame H.264 and HEVC execution probe verifies packaged FFmpeg plus hardware/driver; results are cached |
| Camera LUT | As selected in Lightflow | Per-stage | Per-input Camera assignment, No LUT, or one content-addressed Camera override | Missing/wrong-stage resources fail preflight |
| Creative LUT | As selected in Lightflow | Per-stage | Independently resolves per-input Creative assignment, No LUT, or override; final order is Camera then Creative | Two-stage policy cannot stack with the legacy manual LUT |

`Same as Source` never means video stream copy. It means preserving the named source characteristic while performing the requested export encode. Heterogeneous input traits and Color assignments therefore remain heterogeneous inside one shared parallel Job.
