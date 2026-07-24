# Capability: Metadata

## Goal

Make embedded media metadata visible, exportable, and intentionally editable.

## Planned tools

- EXIF and media metadata browser
- Batch metadata export to CSV and JSON
- Creator and copyright editing
- Keyword editing
- GPS removal
- Date normalization
- Metadata copy between derivatives

## Requirements

- Clearly distinguish embedded metadata, file-system dates, and sidecar data
- Preserve unknown metadata unless the operation explicitly removes it
- Preview batch edits
- Record changed fields in the completion report
