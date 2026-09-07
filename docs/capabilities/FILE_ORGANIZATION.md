# Capability: File Organization

## Browser file operations

The media-only Browser supports Explorer-style Cut, Copy, Paste, drag-to-folder Move/Copy, Recycle Bin Delete, and
confirmed permanent Delete. Folder destinations remain explicit nodes in the Folders hierarchy; the central grid
continues to contain only supported media. Collection drops use separate typed payloads and never imply filesystem
movement.

Small known local operations execute directly. More than eight items, more than 256 MiB of known work, unknown-size
work, recursive folders, or cross-volume transfers promote automatically into Jobs through one testable policy.
Preflight rejects collisions, duplicate sources, self destinations, and recursive folder destinations without
overwriting. Cross-volume Move copies completely before removing its source. Normal Delete uses the Windows Recycle
Bin adapter and never silently becomes permanent deletion.

## Goal

Make large media folders safer and easier to normalize, verify, compare, and deliver.

## Planned tools

- Bulk rename
- Folder rename
- Directory comparison
- Folder synchronization
- Exact duplicate detection
- Sidecar cleanup
- Folder statistics
- Copy verification
- Checksum manifests

## Safety requirements

Every potentially destructive operation requires a dry run, collision detection, clear
source/destination labeling, and a persistent result report.
