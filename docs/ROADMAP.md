# Product Roadmap

This roadmap begins with the actual 0.8.1 product rather than treating the project as
greenfield. Version assignments are directional and may change as technical discovery
continues.

## Current baseline: 0.8.1

Available today:

- LUT-aware batch encoding
- NVIDIA NVENC H.264 and HEVC
- Named encoding presets and advanced options
- Media inspection
- Flexible destinations
- Recovery modes
- Progress and logging
- Resume and skip behavior
- Decode verification
- Lossless MP4 rewrap
- Proxy generation
- Contact sheets
- Experimental Premiere helper
- Installer and portable distributions
- Pinned and verified FFmpeg packaging

## 0.9 — Product foundation and workflow polish

Goal: make the existing application architecture ready for additional media utilities.

- Formalize capability-based navigation
- Introduce a shared job model across tools
- Standardize input selection, output planning, progress, cancellation, and result summaries
- Add persistent job history
- Improve batch review before processing
- Improve error translation and troubleshooting links
- Define preset import/export
- Establish documentation and architectural decision records
- Expand automated tests around job state and destructive safeguards

## 1.0 — Stable video workflow suite

Goal: establish Lightflow Studio as a dependable Windows video utility.

- Generalize the converter beyond NVIDIA-only workflows where practical
- Mature video salvage and verification UX
- Improve proxy presets and naming
- Expand thumbnail and frame extraction
- Add audio extraction
- Add video compression presets for common delivery targets
- Improve contact sheet configuration
- Bring the Premiere helper to a clearly supported or clearly experimental state
- Accessibility and keyboard navigation review
- Stable settings migration and release upgrade behavior

## 1.1 — Image processing

Goal: introduce the first cohesive image capability set using the same job framework.

- Image resize
- Image format conversion
- RAW conversion
- Watermarking
- Image optimization
- Configurable image contact sheets
- Metadata preservation controls
- Output naming templates

## 1.2 — Organization, integrity, and metadata

Goal: support the work that happens before and after media processing.

- Bulk rename with preview
- Copy verification
- Checksum manifests
- Folder comparison
- Folder synchronization
- Metadata browser
- Metadata export
- Metadata editing
- Exact and perceptual duplicate detection
- Cleanup tools for known sidecar and proxy file patterns

## 1.3 — Workflow pipelines

Goal: allow users to save an entire multi-step workflow rather than individual tool settings.

- Pipeline editor
- Typed pipeline steps
- Validation before execution
- Shared input/output context
- Saved workflows
- Workflow templates
- Failure policy per step
- Dry-run summaries
- Example workflows for web galleries, client previews, and social delivery

## 1.4 — Gallery and publishing

Goal: make final delivery and sharing faster.

- Static HTML gallery export
- Thumbnail and derivative generation
- QR code generation
- Gallery manifest
- Image sitemap
- Optional pluggable publishing destinations

## Future research

These features should remain research items until the core job and pipeline architecture
is stable:

- Blur and focus-quality detection
- Visual similarity clustering
- Smart culling assistance
- Automatic keyword suggestions
- Face clustering
- Caption generation
- On-device model packaging
- Privacy-preserving optional cloud inference
