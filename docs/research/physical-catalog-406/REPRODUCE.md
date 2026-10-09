# Reproduction and private evidence

LF-BOTH-RES-007. Base main `c30601870d1c9736f6c6229e84b51546bf8f118e`. Read root AGENTS, current issues/PRs and accepted sources before reusing this bounded approach. Reidentify the physical drive; never blindly reuse disk4, disk4s1 or the mount string. No external writes are included below.

Use the independent task-owned full clone named `LF-BOTH-RES-007-ExternalCatalog`. Raw inventory, exact commands, logs, hashes, all three internal-image manifests (including two failed runs), environment versions and Project status proof are retained locally. They are deliberately excluded from publication. All three images were ordinarily detached, with zero counted native anchors. No owner process was terminated; the final process check found no task probe/build process running.

Successful native execution used approved sandbox escalation. Initial sandbox DiskManagement access failed and sandbox .NET restores stalled; those failures are retained. The empty legacy USB profiler result was corrected by discovering the current USBHost type. No system settings changed.

Commands executed for physical metadata: `diskutil info -plist JRPhoto4T`, `diskutil info -plist disk4`, `diskutil list -plist disk4`, `mount` filtered to `/dev/disk4s1`, `system_profiler SPUSBHostDataType -json`; supported public native APIs are in [inventory.m](inventory.m). No repair/erase/partition/eject/physical mount changes.

For reproduction in a fresh task clone with an independently installed/copied .NET SDK under `work/toolchain/dotnet`, copy `probe` to `work/Probe` and change its project reference to `../../Lightflow.Platform.MacOS/Lightflow.Platform.MacOS.csproj`. Copy `qualify_internal.py` and `inventory.m` to `work`. Create a new existing `work/protected` boundary. Offline package sources suffice for the package-free probe. These task-only harnesses are evidence tools, not production changes.

```sh
python3 tools/X2CatalogProof/run.py build tools/MacStorageHost/MacStorageHost.csproj -c Release -p:RestoreLockedMode=true
python3 tools/X2CatalogProof/run.py build work/Probe/Probe.csproj -c Release --configfile work/offline-nuget.config
python3 tools/X2CatalogProof/run.py run --project work/Probe/Probe.csproj -c Release --no-build -- '<verified existing mount root>' "$PWD/work/protected" "$PWD/work/evidence/physical-assessment.json"
xcrun clang -fobjc-arc -Wall -Wextra -Werror -framework Foundation work/inventory.m -o work/inventory
work/inventory '<verified existing mount root>'
python3 work/qualify_internal.py
```

Internal harness creates a new UUID run directory/image each time. It invokes only image-specific create/attach and **ordinary** detach, never force. It references the newly built production native library by this task's path. Do not run it from docs; copy into work as above. The physical probe writes only its output on internal task storage, disposes the managed assessor in finally and checks native descriptor baseline. Restrict reproduction output to internal task-owned paths.

Published [summary.json](summary.json) omits device UUID/serials, machine names, private paths and raw USB inventory. Synthetic IDs/hashes are safe fixture evidence. No claims of production Catalog provider/schema, Windows execution or physical SQLite qualification are implied.
