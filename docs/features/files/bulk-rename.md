# Feature: Bulk Rename

## Goal

Safely normalize media filenames using the shared, typed Name Parts architecture established by Export issue #168.

## Shared Name Parts

Bulk Rename must consume ordered, serializable Name Parts rather than define a token/template language of its own.
The shared vocabulary includes Original name, Custom text, Date, Time, Sequence width variants, and source-derived
Index Number. Rename-specific metadata parts (for example camera, lens, creator, dimensions, or video resolution)
may extend that vocabulary when #44 is implemented, without changing the shared rendering/materialization contract.

The source extension remains separate from the rendered stem. Bulk Rename may preserve or explicitly change it as
rename policy, just as Export derives it from the materialized output container.

## Requirements

- Live before/after preview
- Collision detection
- Stable sequence ordering from the immutable input plan
- Undo manifest
- Include folders or files
- Case normalization
- Find and replace
- Optional metadata date source
- No rename begins while validation errors remain
