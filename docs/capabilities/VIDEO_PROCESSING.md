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
