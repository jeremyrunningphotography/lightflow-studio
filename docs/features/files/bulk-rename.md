# Feature: Bulk Rename

## Goal

Safely normalize media filenames using reusable metadata-aware templates.

## Tokens

- Original filename
- Original extension
- Date/time
- Camera
- Lens
- Creator
- Counter
- Width and height
- Video resolution
- Custom text

## Requirements

- Live before/after preview
- Collision detection
- Stable counter ordering
- Undo manifest
- Include folders or files
- Case normalization
- Find and replace
- Optional metadata date source
- No rename begins while validation errors remain
