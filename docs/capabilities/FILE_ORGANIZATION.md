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

Normal Delete first captures and preflights the entire selection at the Windows platform boundary. The adapter
queries the actual volume's Recycle Bin and asks the shell to plan each deletion with a progress sink that always
vetoes mutation. Unsupported or uncertain capability requires the styled permanent-delete confirmation before
an operation or Job exists. For mixed selections, accepting permanently deletes **all** selected items; the dialog
says so explicitly. Cancel changes nothing. Shift+Delete retains its explicit permanent-delete confirmation.
Execution uses the same noninteractive shell recycling policy and vetoes a permanent-delete proposal; capability
changes after confirmation are runtime failures, never silent fallback. Successful mutations alone reconcile Catalog.

During filesystem drag/drop, the validated folder row uses an orange-tinted background and accent border, distinct
from selection. It consumes the existing planner's accepted target and clears on leave, drop, or native drag cleanup.
The shared tree template exposes opt-in presentation properties so Collection rows do not acquire filesystem behavior.

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
