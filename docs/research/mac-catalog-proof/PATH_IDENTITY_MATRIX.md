# Measured path identity matrix

Measurements2026-10-05 on actual M2 Pro. CI=task workspace volume with case-insensitive behavior; CS=task-owned case-sensitive APFS image. Raw service calls: `evidence/local-paths.json`, `evidence/case-sensitive-paths.json`; physical equivalence: `evidence/path-pair-file-identities.json`. `samePhysical` in original service JSON means equal contents after fixture writes; stat identity is the independent stronger alias observation.

| Scenario | CI measurement | CS measurement | Safety finding |
|---|---|---|---|
| Case.mov / case.mov | Same inode; AlreadyExists; same lookup bytes | Distinct inodes/bytes; AlreadyExists; lookup resolves first bytes | Unsafe identity conflation on CS; no explicit unsupported-root rejection |
| NFC café / NFD café | Same inode; two successful creates/two AssetIds | Same inode; two successful creates/two AssetIds | Silent duplicate identity even on CI APFS |
| nested/name / literal nested\\name | Distinct inodes; second AlreadyExists; wrong lookup bytes | Same hazardous outcome | Unix backslash is reinterpreted as separator |
| trim.mov / surrounding spaces | Distinct inodes; second AlreadyExists; wrong lookup bytes | Same hazardous outcome | Legal exact name lost by trim |
| CON.mov, colon:name, question?, trailing dot | Successful create | Successful create | Windows portability unsupported-name gap; actual Windows failures still require return-side proof |
| Emoji | Successful create | Successful create | Bounded name observation only |
| Symlink to outside logical root | Successful import | Successful import | Lexical containment does not bound actual target; target was task-owned |
| Second machine | Unmapped→remap existing RootId | Same | Logical/local mapping separation works for tested root |
| Offline/restored root | Rows retained; Unavailable→Online | Same | Local directory simulation only; physical remount pending |
| Drive letter / Mac mount | Source inference from .NET normalization and machine-specific mappings | Not a cross-OS runtime measurement | Windows-origin fixture pending |
| Physical SMB/removable | Not exercised | Not exercised | Owner locations pending |

Current UNIQUE(RootId,RelativePathKey) protects folded-key uniqueness, not physical identity truth. No fixture media was renamed/merged/deleted to hide collisions. The CSV is generated from these raw calls and contains hashes/IDs/statuses. No future schema or identity correction has been implemented.
