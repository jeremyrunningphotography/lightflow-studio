# Bounded Mac reproduction

Research tools only. Start at the published proof commit in an independent clone with the pinned local SDK and dependencies from the original proof. Use fresh directories; retain old evidence. Do not run the superseded authority-service prototype.

```sh
python3 tools/X2CatalogProof/run.py build tools/X2CatalogProof/X2CatalogProof.csproj -c Release --no-restore --disable-build-servers
hdiutil create -size 256m -fs ExFAT -volname X2PORTABLE -type SPARSE -o work/data/new-portable.sparseimage
mkdir work/data/new-portable-mount
hdiutil attach work/data/new-portable.sparseimage -nobrowse -mountpoint "$PWD/work/data/new-portable-mount"
```

Verify with `diskutil info` that the exact mount is writable exFAT. The partition-type display can say `Windows_NTFS`; that is not a filesystem classification. Use the read-only `volume_probe.c` adapter, not the image filename or a missing mount directory. The launcher below fails before creating a fixture if the actual filesystem differs:

```sh
mkdir work/data/new-local-backups
python3 tools/X2CatalogProof/run_local_portability.py --catalog-parent work/data/new-portable-mount --expected-filesystem exfat --backup-parent work/data/new-local-backups --source work/data/final-control-export/catalog.db
```

The source is the retained controlled 32-asset fixture, not a personal Catalog. If absent, recreate a fresh synthetic source using the existing `seed` command and use its exported `catalog.db`; its random IDs/hashes will differ while invariants remain the same. Runtime/source/package hashes and all logical expectations must be recorded.

The launcher prints its unique local output directory. Preserve its matching Catalog directory beneath the mount. Then explicitly supply those two directories to `portable_crash.py --local ... --catalog ...`. It kills only its spawned child after a ready signal; it never deletes WAL. This is process failure, not physical removal.

Path evidence uses `portable-paths <fresh-local-data> <fresh-media-directory-on-image>`. It intentionally creates ambiguous names only inside the disposable media directory. Normalize private paths from JSON before public publication; retain raw results privately.

For closed-NAS transfer, create the SQLite-aware backup locally and finish it as a standalone database before byte-copying to a unique task-owned NAS staging file. Flush, compare SHA256, then rename to a new closed-backup name. Copy back locally, verify the same hash, and run `portable-restore <fresh-local-data> <local-returned-db>`. The currently implemented direct production live-WAL-to-SMB backup path failed; do not represent it as equivalent.

After all task-owned processes exit, normal `hdiutil detach "$PWD/work/data/new-portable-mount"` is permitted for this disposable image. Reattach, verify exact database hash and reopen locally; finally detach the task image again. Never apply these image commands to a physical user volume or NAS mount. Physical-drive access/eject and Windows execution remain separate owner-operated steps.
