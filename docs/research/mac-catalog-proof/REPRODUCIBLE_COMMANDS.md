# Reproduction

Run from the independent clone; never reuse existing fixture directories. The wrapper selects the local SDK and isolates caches/tmp. Windows commands are in CROSS_OS_ROUNDTRIP_MANIFEST.md and Windows-Leg.ps1.

```sh
python3 tools/X2CatalogProof/run.py restore tools/X2CatalogProof/X2CatalogProof.csproj --locked-mode
python3 tools/X2CatalogProof/run.py build tools/X2CatalogProof/X2CatalogProof.csproj -c Release --no-restore
python3 tools/X2CatalogProof/run.py run --project tools/X2CatalogProof/X2CatalogProof.csproj -c Release --no-build -- seed work/data/new-control work/data/new-control-export
python3 tools/X2CatalogProof/run.py run --project tools/X2CatalogProof/X2CatalogProof.csproj -c Release --no-build -- probe work/data/new-probe work/data/new-media
```

Case-sensitive setup used a task-owned 512 MB sparse image:

```sh
hdiutil create -size 512m -fs 'Case-sensitive APFS' -volname X2CaseSensitive -type SPARSE -o work/data/x2-case-sensitive-512.sparseimage
hdiutil attach work/data/x2-case-sensitive-512.sparseimage -nobrowse -mountpoint "$PWD/work/data/case-mount"
python3 tools/X2CatalogProof/run.py run --project tools/X2CatalogProof/X2CatalogProof.csproj -c Release --no-build -- probe work/data/case-probe work/data/case-mount/media
hdiutil detach "$PWD/work/data/case-mount"
```

Do not recreate the existing image. Record stat while mounted; hashes alone do not distinguish aliases. crash.py names its fixture directory; preserve existing evidence and seed a fresh control before adapting for another run. Physical hardware locations remain pending.
