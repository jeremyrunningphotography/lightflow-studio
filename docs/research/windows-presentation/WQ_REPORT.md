# LF-WIN-RES-002 — Windows Avalonia presentation proof

## Recommendation: CONDITIONAL

The supported Windows path is credible for a narrow presenter adapter: **synthetic D3D11 BGRA8 shared keyed-mutex texture → Avalonia 12.1.3 public GPU import → GPU snapshot copy → composition surface visual**. No CPU readback/upload is used for presentation, private reflection, framework fork, native child-window video surface, production Player change or whole-view Windows fork is required.

The executed bounded correctness matrix passes **273 assertions**, with **75 full-identity UIAccepted records**, ten same-token source captures, native HWND composition captures and **78 textures/imports created and destroyed, zero live**. Owner acceptance is still required for the measured full-frame copy/allocation costs and the qualification boundaries below. This does not authorize R5, production Player migration or WPF retirement.

Issue: [WQ #384](https://github.com/jeremyrunningphotography/lightflow-studio/issues/384), native child of [Epic #366](https://github.com/jeremyrunningphotography/lightflow-studio/issues/366). Agent 001/R1 is independent. Assignment workspace: `C:\Git\Agents\LF-WIN-RES-002-Presentation`; canonical checkout unchanged.

## Exact evidence and versions

Qualification source: `5a1c95a5eaafe39006b5534cf0eeda85dcc839b2`, based on fetched main `ab4bfa5ee7a02745537ca1d46a832ad63ac5b376`. [qualified.zip](evidence/qualified.zip) contains raw BGRA source/window frames, JSON frame/capture/timing/assertion records and source/binary SHA-256 provenance. [development-runs.zip](evidence/development-runs.zip) preserves intermediate failures and earlier passes; those pre-commit experiments do not have complete source/binary provenance and are not the final qualified artifact. Extract archives to a task-owned directory; pixel dimensions and hashes are in the records.

DARKMATTER: NVIDIA GeForce RTX 4080 SUPER; Windows build 10.0.26300; .NET SDK 8.0.423, Roslyn compiler 4.11, runtime 8.0.29; Avalonia.Desktop/Themes.Fluent 12.1.3; Silk.NET Direct3D11/DXGI 2.22.0. NuGet transitive packages/content hashes are locked. Framework source tag 12.1.3 resolves to `8eeda4f6f546165b3f72e63c9f42247abb306905`. Runtime inspection reports D3D11 global/NT image handles, KeyedMutex support, no semaphore handle types, adapter LUID `C173010000000000`. Producer selects that LUID.

This is a code-only candidate probe. Avalonia's analyzers/generator target Roslyn 4.14 and emit CS9057 under this SDK; they are not successfully qualified here. A task-local compiler 4.14 trial failed because its MSBuild tasks need System.Runtime 9; it was removed. No system SDK was changed. A production XAML/compiler pairing must be selected and validated before integration; this proof does not pin Lightflow's production toolchain.

## Identity, acceptance, capture and release

Each immutable record carries source generation, raw PTS/rational timebase, monotonically unique token, Color revision, surface epoch and host generation. The GPU clears a deterministic token color and a white top-left quarter marker. Tokens in this bounded run have distinct RGB codes; the complete identity remains in metadata rather than being inferred from pixels alone. Source captures compare every pixel with that token's pattern.

Each candidate gets its own `CompositionDrawingSurface`. After the keyed-mutex import update succeeds, the dispatcher checks all current fields, installs that exact candidate, awaits public `Compositor.RequestCommitAsync`, checks identity again, and only then publishes UIAccepted. This is applied logical composition state, not physical scanout. An unrelated render callback or the update task alone cannot publish the current token.

Producer readiness and consumer release use different mutex keys. Release observation and retained/capture leases independently govern destruction; UIAccepted never grants reuse. Capture acquires the retained token's lease and the shared texture mutex, then copies that same texture to a staging resource for readback. There is no decoder, seeking or nearby-frame substitution. Evidence readback is excluded from presentation cadence measurement. Each replaced surface/import is retired once its own update, GPU release and leases allow it.

## Qualification matrix

| Case | Executed evidence / boundary |
| --- | --- |
| Current frame | Complete identity, committed surface and matching full-pixel capture |
| Stale source / Color | Rejected before import; in-flight changes also reject callbacks |
| Superseded frame | Deliberately obsolete token rejected |
| Stale host / surface | Both explicitly rejected; host invalidation during import prevents callback |
| Paused retention | Same token/pixels remain after delay and rejected newer offers |
| Same-source Color | New render revision/token keeps exact source PTS |
| Same-token capture | Ten raw source captures with accepted full identities and hashes |
| Ready delay | 100 ms withheld producer key keeps update incomplete, then recovers |
| Independent release | 80 ms withheld **release observation** prevents reuse after UIAccepted; actual consumer GPU release is independently acquired |
| Premature reuse | Retained lease blocks retirement even after GPU release; no mutable pool overwrite route exists |
| Shared overlays | Opaque shared Avalonia Button appears over the GPU marker in native HWND pixels |
| Clipping / transform | Native HWND capture proves translated pixels and no parent clip punch-through |
| Input alignment | Avalonia hit tests match overlay/video pixels and reject vacated translated area |
| Resize | Composition bounds follow layout; native checks repeated at wider size |
| Detach / reattach | Host generation changes, candidate reinstalled, new capture verified |
| Producer replacement | New surface epoch and retained token verified |
| Resource / device replacement | Deliberately destroy/recreate producer D3D device after draining all owned resources; fresh host/surface/token accepted |
| Physical device loss | Driver TDR/removal and compositor-device loss were **not induced**; no passing claim |
| Recovery / teardown | Producer recreation passes; all counted imports/textures balanced |
| Repeated cycles | Eight extra surface epochs plus 60 high-resolution offers; bounded process, no long soak |
| 1080p / 4K | Thirty serialized offers at each source size, fit into the same shared viewport |

The delayed-release case tests delayed adapter observation/permission and retained ownership; it does not inject a prolonged consumer GPU execution fence. Real device loss while import waits, concurrent production capture/cancellation, optimized surface reuse, multi-adapter/display/DPI, minimized/locked/session recovery and long soak remain integration/release qualification. Physical pointer synthesis was not used; these are logical hit tests paired with actual rendered pixels. No unsupported bypass is proposed for those later checks.

## Measured costs

Final qualified run; no numeric product acceptance thresholds have been invented:

| Source size | Offers | Observed cadence | CPU (% of one core) | Peak working / private MiB | Offer p50 / p95 / max ms |
| --- | ---: | ---: | ---: | ---: | --- |
| 1920×1080 | 30 | 58.28/s | 88.02% | 159.36 / 212.23 | 9.48 / 11.44 / 14.16 |
| 3840×2160 | 30 | 57.63/s | 93.05% | 177.46 / 455.91 | 9.40 / 12.91 / 13.27 |

Resize commit: **7.06 ms**. Detach/reattach through fresh acceptance: **30.78 ms**. Producer-device recreation through fresh acceptance: **47.72 ms**. These individual observations are not SLAs or physical latency measurements.

Cadence includes synthetic GPU clears, import/snapshot, dispatcher/commit, retirement, buffered JSON records and per-frame process-memory sampling. Offer timings exclude subsequent process sampling; aggregate cadence includes it. Capture readback and full log serialization occur outside the cadence loop. CPU is process time divided by elapsed time, expressed relative to one core, not total machine capacity. This is an allocation-heavy immutable-candidate probe, not an optimized decoder/pool benchmark. First high-resolution offers include allocation; there is no warm-up claim. Peak private bytes are not a demonstrated persistent leak, and zero counted resources does not prove all driver/framework caches return to baseline.

Pinned source inspection identifies **one full-source-frame GPU snapshot copy** (`CopyToNewTexture` / `CopyTexSubImage2D`) before normal composition; the producer clears its own texture, and explicit capture adds a separate staging copy. Driver-internal copies are not measured. Do not claim zero total GPU copies. At nominal 60 Hz the snapshot copy alone represents about 498 MB/s of 1080p BGRA payload or 1.99 GB/s of 4K payload, before read/write/composition traffic; these are byte arithmetic, not measured bandwidth. The observed same-viewport cadence does not prove full-screen physical 4K60, FFmpeg decode cost, HDR/ICC parity or a performance limit on other GPUs.

## Preserved failures and limits

* `native-001/002`: capture returned zero pixels because this probe copied a shared texture without acquiring its mutex. Correcting capture ownership fixed the full-pixel check; these failed runs remain failed.
* `native-004`: default bottom-left import orientation failed the top-left marker. Explicit `TopLeftOrigin=true` corrected it.
* `native-005/006`: the themed overlay test used an ambiguous white pixel; explicit opaque magenta background supplies independently verifiable overlay pixels. This does not establish a framework overlay bug or invalidate the later opaque-overlay pass.
* `native-008`: the repeated clipping assertion sampled x=650 after resizing the parent to 720. The test now samples outside the actual parent width; the original failed assertion remains preserved.
* `native-003/007/009` and final source-committed runs passed their executed matrices; early runs have less coverage and different instrumentation/resource retention. Their performance is not substituted for the final table.

No fundamental contract incompatibility was demonstrated. The current Windows production owners (`MediaPlayback`, `MediaPlaybackService`/coordinator, Flyleaf backend and Color postprocessor) remain unchanged; their WPF-bearing surfaces are not imported/reused by this proof. Production adaptation would need its own accepted issue/contract tranche, finite pool/backpressure policy, device-loss handling and focused real-media qualification.

## Reproduction and owner acceptance

From the task clone:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\research\windows-presentation\Build-Proof.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\research\windows-presentation\Run-Proof.ps1 -RunName owner-reproduction
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\research\windows-presentation\Verify-Evidence.ps1 -DataRoot .\artifacts\windows-presentation\owner-reproduction
```

`Run-Proof` uses the existing unique private desktop, bounded timeout and owning process job; no desktop switch or owner-process termination. The ready-to-copy **explicit interactive** executable command displays the bounded matrix and exits on completion:

```powershell
& "C:\Git\Agents\LF-WIN-RES-002-Presentation\artifacts\windows-presentation\package\Harness.exe" --data-root "C:\Git\Agents\LF-WIN-RES-002-Presentation\artifacts\windows-presentation\owner-acceptance"
```

The repository-required unchanged Lightflow package is separately refreshed with `Build-Release.ps1 -Mode PullRequest -SkipInstaller`; it does not contain the research harness or qualify Avalonia. Its isolated owner command is:

```powershell
& "C:\Git\Agents\LF-WIN-RES-002-Presentation\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-RES-002-Presentation\.cache\acceptance-lf-win-res-002"
```

Required PR CI remains the existing Unit tests / Windows installer and portable package jobs. The added proof job builds the isolated harness and verifies recorded native evidence; it does not claim hosted GPU execution. Current CI/package status belongs in the live PR handoff. Stop at owner architecture/hands-on acceptance; do not merge automatically.

## Pinned public-source references

* [D3D11 sample](https://github.com/AvaloniaUI/Avalonia/blob/8eeda4f6f546165b3f72e63c9f42247abb306905/samples/GpuInterop/D3DDemo/D3D11Swapchain.cs), [interop API](https://github.com/AvaloniaUI/Avalonia/blob/8eeda4f6f546165b3f72e63c9f42247abb306905/src/Avalonia.Base/Rendering/Composition/CompositionExternalMemory.cs), [update lifetime](https://github.com/AvaloniaUI/Avalonia/blob/8eeda4f6f546165b3f72e63c9f42247abb306905/src/Avalonia.Base/Rendering/Composition/CompositionDrawingSurface.cs).
* [ANGLE external objects](https://github.com/AvaloniaUI/Avalonia/blob/8eeda4f6f546165b3f72e63c9f42247abb306905/src/Windows/Avalonia.Win32/OpenGl/Angle/AngleExternalObjectsFeature.cs), [Skia GPU snapshot copy](https://github.com/AvaloniaUI/Avalonia/blob/8eeda4f6f546165b3f72e63c9f42247abb306905/src/Skia/Avalonia.Skia/Gpu/OpenGl/GlSkiaExternalObjectsFeature.cs), [composition commit](https://github.com/AvaloniaUI/Avalonia/blob/8eeda4f6f546165b3f72e63c9f42247abb306905/src/Avalonia.Base/Rendering/Composition/Compositor.cs).
* [Accepted G1 final contract](../mac-compatibility/x1-player-completion/PRESENTED_FRAME_FINAL_CONTRACT.md), [accepted G3 callback limits](../mac-compatibility/x3-g3-completion/G3_COMPLETION_PROOF.md), [ADR 0004](../../decisions/0004-player-frame-ownership.md), [WQ authority](../../architecture/migration-and-qualification.md).

The producer's public interop setup follows the official MIT sample; its attribution/license is retained in `research/windows-presentation/AVALONIA-LICENSE.md`. No framework internals are referenced by executable code.
