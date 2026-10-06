# Owner-gated root/identity proposal

Proposal only, recorded on #369 before production implementation. Keep Lightflow-authored metadata Catalog-owned; no media sidecar architecture. G2 is not accepted.

## Smallest initial support boundary

Retain Catalog19's CatalogId/RootId/AssetId and authored state. Logical RootId and exact source-relative display names are portable intent; PhysicalPath/MachineId, mount/file identity observations and availability are local resolution facts. Copying a Catalog must not make a Windows mapping valid on Mac or copy a machine identity into another installation.

Require an explicit root/remap preflight and per-operation enforcement before discovery/authoring/file operations: reject distinct files whose names collide under existing folded identity; reject alias spellings that would create multiple IDs; reject lossy whitespace/backslash interpretation and unsupported portable characters/reserved names; resolve actual symlink/file targets before containment. Never return an existing asset as if it were the requested different file. Fail with an actionable diagnostic, retain existing IDs/authored state and perform no repair or permanent deletion. Mapping must verify candidate identities without treating a sampled fingerprint as cryptographic proof or treating timestamps as sufficient ownership.

Jeremy must select whether all CS roots are initially unsupported or whether explicitly preflighted collision-free CS roots are allowed. Neither option alone addresses CI Unicode aliases and Unix exact names. Root-level preflight may cost a scan; do not silently add broad scans to Browser startup. Bound it to explicit enrollment/remap plus operation checks, with an agreed race/availability policy. The proof does not implement or qualify this rejection path.

## Future migration if general exact-name support is required

Introduce versioned per-root comparison/naming policy and an exact relative-name identity independent of display/path separator rewriting, while preserving logical IDs. The affected seam includes MediaAssets and BrowserRecursiveScopes keys, containment/mapping, Browser scope/selection, Preview artifacts, file/output operations and integration path consumers. Unicode normalization must reconcile filesystem aliases AND distinct Windows names; applying NFC/uppercasing globally can merge distinct source files.

Before migration: online protected backup, enumerate ambiguous existing records and physical observations, require explicit resolution for collisions/aliases, transactionally preserve references/IDs/authoring, and validate reopen/rollback. Do not silently coalesce duplicate IDs or rewrite historical paths. New schema must reject older writers; ordinary older-schema readers cannot be assumed safe. Document downgrade through protected old backup or reviewed export, rather than making version19 code write a newly interpreted key. No schema20, production migration, package/TFM extraction or file-operation rewrite occurs in this spike.

## Storage and lifecycle

Initially qualify sequential transfer through validated SQLite-aware local snapshots and exclusive closed ownership. Central NAS location is a separate product target; direct live WAL on SMB and concurrent writers remain unsupported pending an explicit architecture. A centrally stored portable snapshot checked out locally with exclusive lease/version/return conflict protocol, or a service that owns SQLite on its host, are possible future designs; neither is selected here.

Mac production recovery needs a real stable file/volume identity and exclusive application ownership adapter, all-handle drain, checkpoint and durable publication/rollback. Unix rename/unlink behavior invalidates Windows file-sharing assumptions. mtime-only fingerprints and a successful manual restore are insufficient. Owner decision is the stop gate, not authorization for M4+.
