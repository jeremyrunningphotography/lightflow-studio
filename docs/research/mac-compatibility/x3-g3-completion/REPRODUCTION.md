# Reproduction and evidence verification

Run only in an independent clone and an available native Apple Silicon desktop. No canonical checkout, normal app data, NAS/Catalog data or another agent's output is used. All commands below start at repository root. Network restore/download is needed once. Use Apple Command Line Tools already installed; no paid tooling.

The task used .NET 8.0.425 installed under `work/toolchain/dotnet8`. To recreate it, obtain the official installer from https://dot.net/v1/dotnet-install.sh, inspect it, and run it with `--version 8.0.425 --architecture arm64 --install-dir "$PWD/work/toolchain/dotnet8" --no-path`. Official release metadata is https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json. Keep the installer/archive inside the task workspace. Do not install a global SDK or trust a development certificate.

```sh
python3 tools/X3G3Completion/run.py restore tools/X3G3Completion -r osx-arm64 --locked-mode
python3 tools/X3G3Completion/run.py bridge
python3 tools/X3G3Completion/run.py build tools/X3G3Completion -c Release -r osx-arm64 --no-restore
python3 tools/X3G3Completion/run.py tools/X3G3Completion/bin/Release/net8.0/osx-arm64/X3G3Completion.dll --mode all --data-root "$PWD/.cache/g3-completion/owner-all-1"
```

Use a new absolute data root on each run: results are append-only. The supported subsets are `interop`, `details`, `input`, `images`, `lifecycle`; `smoke` runs interop. The process closes its windows and exits after the requested matrix. `all` includes actual native fullscreen/activation/window transitions. Do not run on an owner's busy desktop. Native startup needs normal desktop access and can fail under sandbox restrictions; recorded -6661 failures do not have an established single cause. Do not silently discard retries or call them successes.

The source uses unchanged `Lightflow.Actions/*.cs` from the pinned base. Native bridge imports system frameworks; no separate FFmpeg build/player implementation is required for this UI contract proof. Tiny TIFF LZW/PackBits fixtures are committed in the harness; other fixtures are deterministic generated data (HEIC output can vary with OS codec).

No owner packaged production executable is supplied. The task's native DLL path was `tools/X3G3Completion/bin/Release/net8.0/osx-arm64/X3G3Completion.dll`; recorded primary roots were `.cache/g3-completion/final-1`, `final-2`, and `input-final-qualified`. Disposable executables/caches are removed after archival. This Draft research PR does not satisfy or bypass the Windows `Build-Release.ps1` production hands-on gate.

## Offline checks (no UI, SDK or network)

```sh
python3 tools/X3G3Completion/verify_evidence.py
```

This verifies evidence hashes, raw-run assertions/teardown, callback/capture identities, stale rejection, bounded Details containers and measured allocation improvement, anchor results, final input semantics/native menu, eight EXIF results, twenty ICC vectors and native AX item/cell exposure. It validates saved evidence; it is not a rerun of native experiments.

`archive_evidence.py` is the collection script for the named original task runs. It preserves failed diagnostics, normalized TASK_ROOT paths, license texts, result projections and selected screenshots. Its input cache/source checkout must exist to recreate the exact package; ordinary reproductions should retain their own raw outputs and compare rather than overwrite original evidence. `DIAGNOSTIC_HISTORY.tar.gz` contains intermediate logs/JSONL; `LICENSES.tar.gz` contains package/upstream notices; `raw/` contains authoritative complete/focused runs. `EVIDENCE_MANIFEST.json` hashes all published evidence and harness source. No binary compilation artifact is distributed.

## Public source references

- Avalonia 12.1.3 source: https://github.com/AvaloniaUI/Avalonia/tree/12.1.3 (resolved `8eeda4f6f546165b3f72e63c9f42247abb306905`).
- `src/Avalonia.Base/Rendering/Composition/Compositor.cs`, RequestCommitAsync: render-thread application boundary.
- `src/Avalonia.Controls/TableViewCell.cs`, `Presenters/TableViewCellsPresenter.cs`: row/cell reuse with binding/content reset; no column virtualization.
- `src/Avalonia.Controls/Automation/Peers/MenuItemAutomationPeer.cs`: toggle rather than invoke provider.
- `native/Avalonia.Native/src/OSX/PlatformRenderTimer.mm`: CoreVideo display-link startup.

Source was inspected locally from that official tag, not inferred from web summaries. Exact NuGet versions, provenance URLs, content hashes and native binaries are recorded in VERSION_BASELINE.json. Production must preserve all relevant third-party notices and validate the selected compiler/package/OS matrix on Windows and Mac.
