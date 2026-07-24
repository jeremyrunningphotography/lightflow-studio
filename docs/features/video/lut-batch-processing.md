# Feature: LUT Batch Processing

## Status

Existing capability; specification and refinement.

## Goal

Apply a selected `.cube` LUT consistently while batch-converting video files.

## User value

A creator can quickly produce corrected or normalized video derivatives without building
a Premiere or Resolve timeline.

## Functional requirements

- Select files or a folder
- Optionally recurse
- Inspect included files before processing
- Choose a LUT from a configurable library
- Refresh the library without restarting
- Choose an encoding preset or advanced settings
- Choose source-adjacent, subfolder, or explicit destination output
- Preview output names
- Run normal, salvage, or video-only mode
- Show per-file and overall progress
- Cancel safely
- Resume or skip completed output
- Record complete commands and output in the activity log

## Acceptance criteria

- The LUT is applied once and only once
- Source files are unchanged
- Unsupported LUTs or unreadable files are rejected before execution
- Output paths cannot silently resolve to source paths
- Multi-audio-stream files follow the selected stream policy
- A result summary distinguishes completed, warned, skipped, cancelled, and failed items

## Future enhancements

- Visual before/after frame preview
- LUT strength
- Technical transform plus creative LUT chain
- Per-camera LUT rules
- Pipeline step support
