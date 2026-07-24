# Product Vision

## Product statement

**Lightflow Studio is a native Windows media utility toolbox for photographers and
videographers who need fast, safe, repeatable batch workflows without opening a full
editing application.**

It combines professional media-processing engines with a friendly desktop interface,
clear presets, strong safeguards, and practical recovery tools.

## Current identity

Lightflow Studio began as a focused LUT and video conversion application and has already
grown into a broader video workflow utility. The current product includes:

- Folder-based video encoding
- LUT processing
- Multiple destination strategies
- Media inspection and outlier warnings
- Configurable encoding presets
- FFmpeg and FFprobe readiness checks
- Normal, salvage, and video-only recovery modes
- Decode verification and CSV reporting
- Lossless rewrapping
- Proxy generation
- Contact sheets
- Experimental Premiere timeline clip export
- Detailed progress, cancellation, resume, and logging behavior

The long-term direction is to expand this foundation into a carefully organized suite of
image, video, metadata, file-management, verification, and workflow tools.

## Audience

### Primary users

- Photographers preparing galleries, proofs, and web deliverables
- Videographers transcoding footage, applying LUTs, generating proxies, and recovering media
- Hybrid photo/video professionals working with large folders
- Creators who want professional command-line capabilities without command-line complexity

### Secondary users

- Small studios standardizing repeatable delivery workflows
- Event photographers consolidating submissions from multiple shooters
- Technical users who need transparent access to FFmpeg, FFprobe, ExifTool, and related tools

## Product promise

Lightflow Studio should make complicated batch work feel predictable.

Users should know:

- What will happen before processing begins
- Where output files will go
- Which source files will be included
- Which settings are active
- Whether the system is ready
- How far the job has progressed
- What failed and why
- Whether the original media remains protected

## Product principles

1. **Protect originals by default.** Source files should never be silently overwritten.
2. **Batch-first design.** Every useful operation should support folders and large selections.
3. **Preview before commit.** Naming, deletion, synchronization, and other risky operations
   require a clear preview.
4. **Progress should be honest.** Show per-file progress, overall progress, estimated time,
   and the difference between waiting, processing, skipped, failed, and completed work.
5. **Errors should be actionable.** Preserve detailed logs while translating common failures
   into plain language.
6. **Professional defaults, deep controls.** New users should succeed with presets; advanced
   users should be able to inspect and customize the underlying behavior.
7. **Capabilities should compose.** Tools should eventually connect into reusable pipelines.
8. **Local-first operation.** Media stays on the user's computer unless a publishing feature
   explicitly sends it elsewhere.
9. **Native Windows experience.** The application should feel intentional on Windows rather
   than like a thin cross-platform wrapper.
10. **No unnecessary editor ambitions.** Lightflow Studio is not trying to replace Premiere,
    DaVinci Resolve, Lightroom, Photoshop, or a full digital asset manager.

## Differentiation

Lightflow Studio should not be marketed as merely an FFmpeg front end. Its value comes from:

- Workflow-oriented presets
- Strong source and destination management
- Media-aware validation
- Recovery and verification
- Reusable processing pipelines
- A cohesive set of photographer and videographer utilities
- A polished interface that reduces repetitive decisions

## Long-term outcome

Lightflow Studio becomes the application a photographer or videographer keeps installed
because it solves dozens of small but important workflow problems quickly and safely.
