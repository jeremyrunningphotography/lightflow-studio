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

Export execution uses the shared, WPF-independent Jobs runtime over the existing typed Encoding plan and FFmpeg adapter. `Parallel exports` is persisted materialized file-level concurrency (1–8, default 2), not FFmpeg thread count. Pause is drain-and-pause: no new files start, active files finish, then the Job becomes Paused. Cancellation terminates active FFmpeg process trees and removes incomplete `.lightflow` outputs while preserving completed results.

Operational Jobs checkpoints are separate from History. Restart never treats a previously Running item or a merely existing output as complete; source/output/LUT identity is revalidated and uncertain work becomes Needs Attention for the future Jobs UI. History remains the final provenance and Review & Rerun record.
