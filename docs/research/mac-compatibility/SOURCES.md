# Source register and evidence discipline

Access date for **all entries: 2026-10-05**. Primary sources unless explicitly labeled otherwise. Current web documentation is evidence of advertised APIs/terms; all performance/Lightflow parity judgments remain inference. Some Apple symbol pages require JavaScript; indexed official excerpts were used when the rendered body was unavailable. No runtime assertion is based on such an excerpt.

## Repository and local evidence

Repository [accepted baseline](https://github.com/jeremyrunningphotography/lightflow-studio/tree/1119a907b02ef5cc97c9c62b4f2fddb196e540df), [read-only main branch API](https://api.github.com/repos/jeremyrunningphotography/lightflow-studio/branches/main). Remote main remained at the expected SHA during verification. Repository metadata reports public visibility.

At that SHA: AGENTS.md; docs/ARCHITECTURE.md; PLAYER_ACTIONS.md; BROWSER_ACTIONS.md; REVIEW_PRESENTATION_ACTIONS.md; KEYBOARD_SHORTCUTS.md; SMART_COLLECTIONS.md; VIDEO_ROTATION.md; FFPROBE_TECHNICAL_METADATA.md; premiere-companion.md; premiere-markers-259.md; noninteractive-validation.md; decisions/0001-lightflow-catalog-persistence.md; catalog-backup-272.md; RELEASE_PLAN.md; DESIGN-SYSTEM.md; UI_GUIDELINES.md. Project/source/packaging/lockfile evidence is indexed in PLATFORM_DEPENDENCY_INVENTORY.md and the raw JSON/CSV.

Current issues [#322](https://github.com/jeremyrunningphotography/lightflow-studio/issues/322), [#345](https://github.com/jeremyrunningphotography/lightflow-studio/issues/345), [#349](https://github.com/jeremyrunningphotography/lightflow-studio/issues/349), [#350](https://github.com/jeremyrunningphotography/lightflow-studio/issues/350), [#351](https://github.com/jeremyrunningphotography/lightflow-studio/issues/351), [#352](https://github.com/jeremyrunningphotography/lightflow-studio/issues/352), [#353](https://github.com/jeremyrunningphotography/lightflow-studio/issues/353) were fetched read-only; snapshots are in research/work. [Draft PR #360](https://github.com/jeremyrunningphotography/lightflow-studio/pull/360) was inspected as background only and was not attached or modified.

Lightflow.Actions independent Release build succeeds zero warnings/errors; research/work/actions-build.log. No app native tests were run. Native SQLite metadata: SQLITE_MAC_ASSET_EVIDENCE.json, package obtained from the official [NuGet flat-container archive](https://api.nuget.org/v3-flatcontainer/sqlitepclraw.lib.e_sqlite3/2.1.13/sqlitepclraw.lib.e_sqlite3.2.1.13.nupkg), SHA256 D07D13B4779123CFA56B3348B504D57A2AB6FC10C764FB6E2C9D787987E39201. The package was inspected as an archive; dylibs were neither loaded nor executed.

## Frameworks

| Source | Fact supported / limitation |
|---|---|
| [Avalonia MIT](https://github.com/AvaloniaUI/Avalonia/blob/main/licence.md) | Core license; not all separate vendor products |
| [Avalonia tooling FAQ](https://docs.avaloniaui.net/tools/faq) | Free framework versus professional tools; legacy FOSS options |
| [Avalonia releases](https://github.com/AvaloniaUI/Avalonia/releases) | Active 12.1.3/11.3 releases observed; not performance evidence |
| [Avalonia Mac](https://docs.avaloniaui.net/docs/platform-specific-guides/macos) | Native backend, ordinary cross-build versus macOS workload, native menus/views |
| [Avalonia Windows](https://docs.avaloniaui.net/docs/platform-specific-guides/windows) | Windows/native-host integration |
| [Avalonia native interop](https://docs.avaloniaui.net/docs/app-development/native-interop) | Surface embedding/layer constraints |
| [Avalonia accessibility](https://docs.avaloniaui.net/docs/app-development/accessibility) | Accessibility backend and custom-control responsibility |
| [Avalonia performance](https://docs.avaloniaui.net/docs/app-development/performance) | Virtualization/layout recommendations, not Lightflow measurements |
| [Avalonia DataGrid](https://docs.avaloniaui.net/controls/data-display/structured-data/datagrid/) | Current deprecated status and replacement suggestions |
| [Avalonia headless](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform) | Control/layout tests without native window/input evidence |
| [XPF commercial license](https://avaloniaui.net/licenses/XPF-ENT.pdf) | Distinct commercial WPF compatibility offering |
| [MAUI upstream](https://github.com/dotnet/maui) | MIT and active project, desktop/mobile abstractions |
| [MAUI supported platforms](https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms?view=net-maui-10.0) | Mac Catalyst / Windows WinUI; multi-version page not chosen product floor |
| [Uno upstream](https://github.com/unoplatform/uno) | Apache2 core and active project |
| [Uno desktop](https://platform.uno/docs/articles/features/using-skia-desktop.html) | Skia with AppKit/Win32 shells; native AOT target constraints |
| [Uno native hosting](https://platform.uno/docs/articles/features/using-skia-hosting-native-controls.html) | Mac embedding documented gap; recheck chosen release |
| [Uno requirements](https://platform.uno/docs/articles/getting-started/requirements.html) | Toolchain/platform requirements, not native media proof |
| [Uno accessibility](https://platform.uno/docs/articles/features/accessibility/index.html) | Advertised accessibility support |
| [Uno headless](https://platform.uno/docs/articles/features/using-skia-headless.html) | No native chrome/input proof |

## Media and data

| Source | Fact supported / limitation |
|---|---|
| [Flyleaf upstream wiki](https://github.com/SuRGeoNix/Flyleaf/wiki) | Windows/D3D design; not a Mac-capable backend |
| [FFmpeg AVFrame](https://www.ffmpeg.org/doxygen/9.0/structAVFrame.html) | Decoder timing fields/heuristic best-effort distinction |
| [FFmpeg VideoToolbox encoders](https://www.ffmpeg.org/doxygen/8.1/videotoolboxenc_8c.html) | H.264/HEVC/ProRes encoder definitions, not every hardware/profile availability |
| [FFmpeg legal](https://ffmpeg.org/legal.html) | LGPL/GPL build effects and compliance/patent separation |
| [mpv stable manual](https://mpv.io/manual/stable/) | Stepping limitations; media API availability not parity proof |
| [mpv copyright](https://github.com/mpv-player/mpv/blob/master/Copyright) | Default GPL and conditional LGPL builds/dependency impact |
| [libmpv render header](https://github.com/mpv-player/mpv/blob/master/include/mpv/render.h) | Frame render/swap timing/control; target display time is not itself source PTS |
| [LibVLCSharp media API](https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html) | Mac NSView and conditional NextFrame |
| [LibVLC](https://images.videolan.org/vlc/libvlc.html) | LGPL2.1 runtime |
| [Apple stepping](https://developer.apple.com/documentation/avfoundation/avplayeritem/canstepbackward?changes=_2_1_8) | Item-dependent backward capability |
| [CoreMedia PTS](https://developer.apple.com/documentation/CoreMedia/CMSampleBuffer/presentationTimeStamp) | Sample timing API |
| [VideoToolbox](https://developer.apple.com/documentation/videotoolbox?language=objc) | Apple hardware codec framework |
| [miniaudio](https://github.com/mackron/miniaudio) | Mac CoreAudio support and free license options |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | Cross-platform graphics/bitmap API and MIT |
| [ImageIO](https://developer.apple.com/documentation/imageio) | Apple image framework; specific format parity still needs tests |
| [Six Labors pricing/terms](https://sixlabors.com/pricing/) | Commercial-use threshold; not universally unconditional free |
| [Pinned SQLite NuGet](https://www.nuget.org/packages/SQLitePCLRaw.lib.e_sqlite3/2.1.13) | Exact version native package identity |
| [Microsoft SQLite customization](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/custom-versions) | Provider/bundle/native selection |
| [SQLite file format](https://www.sqlite.org/fileformat2.html) | Database representation, not application path policy |
| [SQLite WAL](https://www.sqlite.org/wal.html) | WAL is persistent state; no network WAL sharing assumption |
| [SQLite backup/corruption](https://www.sqlite.org/howtocorrupt.html) | Safe backup/locking/unlink hazards |

## Apple UX, trust, costs, hardware and CI

| Source | Fact supported / limitation |
|---|---|
| [Apple membership](https://developer.apple.com/programs/) | $99 annual program and direct-download option |
| [Account comparison](https://developer.apple.com/support/compare-memberships/) | Free development versus paid distribution capabilities |
| [Included benefits](https://developer.apple.com/programs/whats-included/) | Services and applicable sales commission distinctions |
| [Gatekeeper](https://support.apple.com/en-us/102445) | May 27, 2026 public trust/exception guidance; managed settings caveat |
| [Notarization](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution?changes=_1_8_5) | Developer ID/hardened-runtime/timestamp/native code requirements |
| [Notary workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow) | notarytool/stapler and packaging handling |
| [Notary failures](https://developer.apple.com/documentation/security/resolving-common-notarization-issues) | Actual logs/signature/system-policy verification |
| [Hardened runtime](https://developer.apple.com/documentation/Security/hardened-runtime) | Runtime capabilities distinct from sandbox |
| [JIT entitlement](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.security.cs.allow-jit?changes=_3) | Entitlement evaluation for hardened-runtime JIT |
| [Signing](https://developer.apple.com/documentation/xcode/creating-distribution-signed-code-for-the-mac?changes=_9) | Distribution certificate roles/nested code |
| [Packaging](https://developer.apple.com/documentation/xcode/packaging-mac-software-for-distribution?changes=_7) | .app/archive/container qualification |
| [Store review rules](https://developer.apple.com/app-store/review/guidelines/#software-requirements) | Mac sandbox/self-contained packaging and review |
| [Sandbox files](https://developer.apple.com/documentation/security/accessing-files-from-the-macos-app-sandbox?language=objc) | Grants/bookmark lifetimes/helper access |
| [NSURL](https://developer.apple.com/documentation/Foundation/NSURL) | Scoped URL start/stop access |
| [Directory guidance](https://developer-mdn.apple.com/library/archive/documentation/FileManagement/Conceptual/FileSystemProgrammingGuide/MacOSXDirectories/MacOSXDirectories.html) | Archived Apple guidance for support/cache organization; locations resolved via APIs |
| [Trash](https://developer.apple.com/documentation/foundation/filemanager/trashitem%28at%3Aresultingitemurl%3A%29?changes=_1) | Native operation; recoverability on every mounted volume not established |
| [APFS variants](https://support.apple.com/en-gb/guide/disk-utility/dsku19ed921c/22.7/mac/26) | Case-sensitive and insensitive formats |
| [App lifecycle](https://developer.apple.com/documentation/appkit/nsapplicationdelegate?changes=la) | Native reopen/termination callbacks |
| [Xcode host matrix](https://developer.apple.com/xcode/system-requirements) | Current toolchain host/deployment distinction; pin at implementation |
| [Apple Mac mini](https://www.apple.com/mac-mini/) | Representative current Apple Silicon hardware; no purchase/price recommendation |
| [.NET lifecycle](https://dotnet.microsoft.com/en-us/platform/support/policy) | .NET8/9 support ending Nov10 2026, .NET10 LTS |
| [GitHub billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions) | Public standard and self-hosted execution currently free |
| [GitHub prices](https://docs.github.com/en/billing/reference/actions-runner-pricing) | Current standard Mac $0.062/min billable, larger runner caveats |
| [GitHub runner matrix](https://docs.github.com/en/actions/reference/runners/github-hosted-runners) | Mac arm64/Intel hosted architecture availability |

## External integrations

| Source | Fact supported / limitation |
|---|---|
| [Adobe installation](https://developer.adobe.com/premiere-pro/uxp/plugins/distribution/install/) | CCX/UPIA on both OSes, Mac helper path |
| [Premiere API](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/) | Host API surface; actual object persistence needs runtime evidence |
| [Adobe developer tools](https://developer.adobe.com/premiere-pro/uxp/introduction/essentials/dev-tools/) | General UXP/tooling floor differs from Lightflow26.5 contract |
| [TourBox Console](https://www.tourboxtech.com/oap/downloads/) | Current Mac Console release/requirements; not accepted Lightflow map |
| [TourBox Mac setup](https://www.tourboxtech.com/en/mac-download/) | Vendor permissions/setup workflow |
| [Blackmagic staff forum](https://forum.blackmagicdesign.com/viewtopic.php?f=21&t=205175) | Primary vendor staff guidance: external scripting Studio; older than current release, recheck installed Developer README |
| [Blackmagic feature guide](https://documents.blackmagicdesign.com/SupportNotes/DaVinci_Resolve_17_New_Features_Guide.pdf) | Developer documentation location; historical source, not current edition parity proof |

Failed accesses were not used as positive evidence: guessed MAUI/Uno license filenames, guessed SQLite csproj/tag paths, JavaScript-only symbol bodies, and restricted shell gh requests. Repository connectors and official source pages resolved the meaningful questions. Current installed Resolve APIs, Mac runtime/signatures, native image format coverage and physical controller behavior remain open, deliberately labeled rather than inferred from metadata.
