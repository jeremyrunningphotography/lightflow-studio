# Reproduce the bounded native proof

Independent Apple Silicon clone only; no product launch or owner media/Catalog access.
Install the official .NET8 SDK8.0.425 task-locally at work/toolchain/dotnet8 (official
https://dot.net/v1/dotnet-install.sh supports --version8.0.425 --architecture arm64
--install-dir with an absolute task path). Apple CommandLineTools are required.
Source is tools/X3AvaloniaIOSurface, dependency versions/archives are pinned in
packages.lock.json and DEPENDENCY_PROVENANCE.json. No paid/runtime distribution assets.

From /Users/jeremyrunning/Git/agents/Agent-X3-Avalonia-Proof (substitute your independent
clone's absolute path for every data root):

```sh
python3 tools/X3AvaloniaIOSurface/run.py bridge
python3 tools/X3AvaloniaIOSurface/run.py restore tools/X3AvaloniaIOSurface/X3AvaloniaIOSurface.csproj --locked-mode -r osx-arm64
python3 tools/X3AvaloniaIOSurface/run.py build tools/X3AvaloniaIOSurface/X3AvaloniaIOSurface.csproj -c Release --no-restore -r osx-arm64 --disable-build-servers
# Only after a successful build; do not run an older binary after a build failure.
python3 tools/X3AvaloniaIOSurface/run.py tools/X3AvaloniaIOSurface/bin/Release/net8.0/osx-arm64/X3AvaloniaIOSurface.dll --mode smoke --data-root /Users/jeremyrunning/Git/agents/Agent-X3-Avalonia-Proof/.cache/x3-iosurface/reproduce-smoke
# Native exclusive/unlocked display session required. ~85–100 seconds, closes itself.
python3 tools/X3AvaloniaIOSurface/run.py tools/X3AvaloniaIOSurface/bin/Release/net8.0/osx-arm64/X3AvaloniaIOSurface.dll --mode matrix --data-root /Users/jeremyrunning/Git/agents/Agent-X3-Avalonia-Proof/.cache/x3-iosurface/reproduce-matrix
# Verify the frozen published evidence without executing a native experiment:
python3 tools/X3AvaloniaIOSurface/verify.py
```

Use a fresh empty data root per run; results.jsonl appends, so reusing one mixes runs.
run.py owns CLI/NuGet/temp paths and native bridge search path. No global input automation:
pinned reflection injects only into the proof window's TopLevel pipeline. Native captures
use Compositor.CreateCompositionVisualSnapshot and are distinct from physical screenshot/
scanout. Native fullscreen/visibility are read through AppKit, not UI automation.
Keep the desktop available; no power/lock/security settings are modified by this harness.

Final-source native matrix is final-2; its2517 raw JSON rows are compressed in this
artifact directory. All12 pattern identity checks, input assertions, independent PNG
pixel oracle and final0/0 native counts pass. A complete prior matrix-4 confirmed the
performance distinction; final-2 is authoritative. Initial input/missing-hit-region
failure and one native render-timer initialization failure are separately preserved.
Intermediate runs used for development are not silently scored as acceptance. Two runs
were inadvertently launched after compile failures using the prior DLL; one was allowed
to finish, one exact task-owned process terminated. Neither supplies final evidence.

Final Release build had zero warnings/errors. No Windows WPF functional/package test was
run on this Mac: source/evidence are outside the product solution. Draft PR is research
review only; root AGENTS's freshly packaged Windows executable gate remains required
before a product-ready hands-on PR claim. No signed/notarized Mac app is claimed.
Task-only bridge/build/cache/source-clone outputs are disposable after evidence archival;
prior frozen X3 evidence/toolchain/cache and unrelated NAS/Catalog fixtures are preserved.

Delivery cleanup removes only current proof bin/obj, work/x3-iosurface native binary,
work/avalonia-11.3.8 inspected source clone and .cache/x3-iosurface CLI/temp/run outputs
after archival. The previous X3 SDK/toolchain, frozen branch and previous evidence/caches
are retained. Shared task NuGet archives remain reproducibility inputs; no NAS/Catalog
fixture or system installation is altered. ANGLE license text is newline-normalized;
its exact package archive hash remains recorded.
