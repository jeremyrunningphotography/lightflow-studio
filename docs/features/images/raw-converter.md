# Feature: RAW Converter

## Goal

Batch-convert common camera RAW formats into high-quality delivery or intermediate images.

## Initial formats

- CR3
- NEF
- ARW
- RAF
- RW2
- DNG

## Outputs

- JPEG
- TIFF
- PNG where technically appropriate

## Requirements

- Preserve original RAW files
- Preserve EXIF by default
- Select output color space
- Select bit depth where supported
- Quality and compression controls
- Resize option
- Sharpening option
- Embedded preview fallback must be clearly identified
- Sidecar policy must be defined
- Camera-specific failures must be actionable

## Technical discovery

Select and document the RAW decoding engine before implementation.
