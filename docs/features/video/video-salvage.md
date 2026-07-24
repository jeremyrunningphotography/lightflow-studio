# Feature: Video Salvage and Recovery

## Status

Existing capability; UX and diagnostic maturity planned.

## Goal

Recover the maximum usable media from files that standard decoding cannot process reliably.

## Modes

- Normal
- Salvage audio and video
- Video only

## Requirements

- Explain that missing media data cannot be reconstructed
- Preserve raw FFmpeg diagnostics
- Identify which streams were retained
- Detect and explain multi-audio-track choices
- Use rebuilt timestamps and corrupt-packet tolerance where appropriate
- Allow output even when warnings occur
- Mark salvage output clearly in naming or metadata
- Never replace the damaged source

## Acceptance criteria

- Known damaged fixtures produce either usable output or an actionable failure
- Partial success is not reported as clean success
- Frozen, duplicated, skipped, silent, and visibly damaged sections are disclosed as possible
- Cancellation removes incomplete temporary output
