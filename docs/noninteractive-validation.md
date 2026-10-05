# Noninteractive Windows validation (#284)

Automated validation uses a unique `LightflowValidation-<GUID>` Windows desktop that is never switched to the input desktop. The process is assigned to it by `CreateProcess` **before** CLR/STA/WPF initialization. All descendant processes inherit the desktop. Real WPF windows can remain shown and focusable there; they cannot appear on the user's input desktop. This is desktop isolation, not headless rendering.

## Supported commands

From a Windows checkout:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -NoBuild -Filter "FullyQualifiedName~PlayerArrow"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller
```

Both scripts enter the same process boundary automatically. GitHub Actions uses the same test entry point. `-NoRestore`, `-NoBuild`, `-Filter` and `-ResultsName` support focused Release runs. WPF hosts refuse a direct `dotnet test` launch on an ordinary desktop before initializing their dispatcher/Application. Pure logic tests may still be run directly if they do not enter a WPF host.

Logs are retained beneath the owning checkout's `artifacts\validation\<run>\output.log`; TRX files remain under `LightflowStudio.Tests\TestResults`. A setup/desktop/launch failure fails closed; there is no interactive fallback. Validation needs permission to create a Windows desktop and job. An unavailable desktop is a validation failure, not a skipped test. The launcher timeout is two hours; VSTest retains the existing five-minute hang-dump policy.

The child process receives task-local TEMP/TMP beneath `.cache\validation-temp\<run>`, so existing temporary test profiles and native fixtures remain in their owning checkout. This is process-local; the user's environment is unchanged. Package smoke continues to pass its explicit disposable `--data-root` under `artifacts\release`.

## RCA and safe reproduction

At main `a827470ab0338790c5697d9fcfaebe9231a70670`:

- `FlyleafPostProcessIntegrationTests.CaptureLiveSurfaceAsync` calls `Show` on a **topmost** 480x300 window at (40,40). `ShowActivated=false` prevents initial activation but leaves native video visible. Its `PrintWindow` checks require real rendered HWND pixels.
- WPF hosts across Browser/Player/Jobs call `Show`; some disable activation and use -32000 coordinates or opacity, others do not. Opacity does not reliably hide hosted native HWNDs. Settings row-local shortcut capture explicitly calls `Activate` and `Focus` to establish actual WPF focus semantics.
- `Build-Release.ps1` starts a smoke executable with `-WindowStyle Hidden`, but `App.OnStartup` shows a splash, reveals the MainWindow, enables its taskbar entry and calls `Activate`. The launcher's initial hidden hint does not override subsequent application presentation.

The noninteractive regression deliberately creates a real topmost shown HWND, activates it and focuses its TextBox on a private desktop. It verifies a presentation source and actual keyboard focus, enumerates the input desktop to prove no test-process window exists there, and verifies the HWND is destroyed on close. Native unchanged PrintWindow/frame tests prove genuine rendered pixels remain available. This reproduces the offending operations safely rather than stealing the owner's focus to establish the symptom. The same old paths run locally and in Windows CI; CI's dedicated workstation does not justify local interference.

Thread reassignment was evaluated first and failed before any test presentation: STA initialization already owned desktop resources (`SetThreadDesktop`: resource in use). Process selection before initialization avoids that constraint.

## Inventory and classification

Classes are classified by their strongest relevant dependency; a family can also contain pure logic cases. Searches covered Window/Show/ShowDialog/Activate/Focus/Keyboard.Focus/MoveFocus/Mouse/Cursor, native input/hook calls, screen coordinates, monitor/DPI use and dispatcher setup.

| Class | Relevant families / infrastructure | Desktop need and policy |
|---|---|---|
| A: logic | Catalog/model/controller, semantic actions, services, Jobs planning, Premiere protocol/bridge, packaging source/manifest checks | No desktop required. Semantic action tests keep direct controller/action invocation. |
| B: WPF objects/layout | Thumbnail bitmap decoding/regeneration producer threads; VideoRotation bitmap producer; converters, resources, detached controls/layout | STA/layout or imaging; no shown HWND. Producer threads inherit the isolated process desktop. |
| C: presentation source/HWND | BrowserCatalogPresentationLive, BrowserCollectionCreationLive, BrowserDetailsWpf, BrowserRegenerationCommand, BrowserTreeExpansionLive, BrowserToggleOffLive, BrowserSubfoldersCapabilityLive, BrowserRecursiveIconStartupRestorationLive/TopLevelLive; InspectorDescriptionView, MediaInspectorHydration, VisualIndex; JobsWorkspaceLiveInteraction; ReviewShellLive, ReviewExportLive; PlayerFilmstrip, PlayerMarker, PlayerViewerHostLease; BrowserPlayerViewerLiveInteraction | Preserve Show/Loaded/layout/virtualization/routed events and screen-coordinate logic on private desktop. |
| C: popup HWNDs / display coordinates | MenuPresentationTests (including #274 submenu lifecycle); popup work-area/handedness and PointToScreen checks | Preserve actual popup HWNDs and Windows placement/monitor semantics on the private desktop. |
| C: local keyboard semantics (formerly E) | KeyboardShortcutIntegration / BrowserActionIntegration Settings row capture; PlayerKeyboardFocus, PlayerArrowInput, PlayerActionIntegration, PlayerReview/ReviewAction/ReviewPresentationAction | Keep actual WPF Keyboard.FocusedElement, Focus/MoveFocus, ownership/cancellation and routed-key checks. Activation affects only the private desktop. No conversion to weaker semantic-only tests. |
| D: real native playback/rendering | FlyleafPlaybackIntegration, FlyleafPostProcessIntegration, FlyleafHdrIccIntegration, StartupVideoPresentation, VideoRotationPixel native cases, PlayerActionDecodedFrame, PlayerArrowDecodedFrame, PlayerReviewBackend, PlayerRotation; native cases within live Browser/Player/lease tests | Real Flyleaf/FFmpeg/D3D and child HWNDs; live PrintWindow pixels, retained frames, stepping and taskbar/surface replacement remain asserted. Private desktop avoids hiding/minimizing renderer HWNDs. No mocks or changed assertions. |
| C/D: whole packaged app | Build-Release startup/splash/workspace/Jobs smoke, Catalog/runtime/backup and graceful shutdown | Entire build process tree uses private desktop. Real splash and normal startup reveal still execute there and are invisible on the input desktop. Existing `--startup-smoke-test` now requires verified private desktop plus isolated `--data-root`, before logging/storage/instance activation. |
| E: deliberate external acceptance | Existing `PremiereLiveAcceptanceTests` installed companion driver | Already explicitly opted in with `LIGHTFLOW_PREMIERE_ACCEPTANCE` and a disposable Premiere project. Ordinary automated runs retain the existing skip; this does not claim control of Premiere's external interactive UI. Owner application acceptance remains interactive. |
| F: environment-dependent | Hardware D3D11VP/ICC/display capabilities | Existing optional hardware requirements remain explicit. Real HWND/render assertions run normally; no new skips or relaxed assertions. Hardware/display coverage does not imply physical foreground input. |

Every current test `Show` path uses the shared `StaDispatcher`; its guard and `TestWpfApplication` independently verify the actual desktop name and separation from `OpenInputDesktop`, rather than trusting an environment flag. Windows native helper windows/threads are also isolated through process inheritance. Existing tests still close/dispose their own WPF/player resources; the process job supplies the outer boundary.

## Focus, pointer, native rendering and teardown

No launcher calls `SwitchDesktop`, `SendInput`, `SetCursorPos`, or global mouse synthesis. Current tests contain no such input calls. Low-level wheel hooks in production Player code remain real but Windows restricts hooks/messages to their desktop. Synthetic WPF events and semantic seams remain unchanged. #337's synchronous no-movement-click suppression/removal-in-finally stays untouched.

`ValidationDesktop` starts the child suspended, assigns a kill-on-close job, then resumes it. It does not enable job breakaway. Nonzero results, timeout and orphan descendants fail the run. Success requires zero active owned processes; disposal terminates any remaining descendants, including smoke processes, without touching user-started processes. Unique desktops isolate concurrent clones; they do not isolate unrelated system resources such as Premiere's fixed TCP port.

Test builds and publishing disable persistent .NET build servers inside the boundary so they cannot outlive an otherwise successful validation command. This changes build-process lifetime, not test selection or application behavior.

No new interactive test exception was added. No ordinary test was skipped. A production-visible validation mode was not added: only the existing explicit smoke switch gains a safety guard. Normal executable/acceptance startup, branding/splash, render pipeline, local focus and activation remain unchanged.

Microsoft contracts: [Desktops](https://learn.microsoft.com/en-us/windows/win32/winstation/desktops), [CreateDesktop](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createdesktopw), [SetThreadDesktop](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setthreaddesktop). The generic contract is noninteractive presentation; this implementation is intentionally Windows-specific. No macOS or broad fixture/parallelism framework is introduced.

## Owner desktop acceptance

In the Agent V clone, this representative command exercises topmost HWND isolation, keyboard focus, real native pixels, restored video and the #337 click fixture without running the whole suite:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Git\Agents\agent-v-284\scripts\Test-Noninteractive.ps1" -NoBuild -Filter "FullyQualifiedName~NoninteractiveValidationTests|FullyQualifiedName~NoLut_LiveChildHwnd|FullyQualifiedName~ConfiguredShortcuts_RowLocalCaptureKeepsLowRowFocusScrollAndStaging|FullyQualifiedName~StartupRestore_NativeVideoSurvivesTaskbarReveal|FullyQualifiedName~SurfaceClick_TogglesOnce" -ResultsName owner-desktop-acceptance.trx
```

While it runs, type in another app and move the mouse normally. Observe no Lightflow/native windows, splash, taskbar activation, focus theft or pointer movement. The terminal reports the result and requires no owned process remaining. For packaged desktop acceptance, run the Build-Release command above and continue using the desktop through its smoke stage; it retains the startup/shutdown/dependency checks.

Explicit interactive acceptance of the fresh package uses:

```powershell
& "C:\Git\Agents\agent-v-284\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\agent-v-284\.cache\acceptance-284"
```

Draft PR stays unmerged pending architecture and owner desktop-behavior acceptance.

