# Platform dependency inventory

Baseline: main 1119a907b02ef5cc97c9c62b4f2fddb196e540df; accessed 2026-10-05. Repository paths below are evidence locations, not proposed edits. Classification: A neutral; B shared behavior plus small adapter; C Windows implementation with clear Mac API equivalent; D major replacement/rewrite; E unknown until proof. A subsystem can contain multiple classes; the table distinguishes reusable authority from concrete implementation.

## Ownership inventory

| Responsibility / observed location | Class | Keep shared | Mac replacement / qualification |
|---|---|---|---|
| Lightflow.Actions/*.cs, package-free net8.0 | A | Typed IDs/args/results, phases, repeat/execution policy, targets, holds, shortcut gestures/conflicts | OS key and focus adapters only; independent Release build passed |
| BrowserQuery*, BrowserCatalogScope, BrowserNavigation, BrowserSelectionActions | A/B | Query/scope/projection identity, selection and navigation policies | Split WPF publishing, physical folder enumeration and WIC helper references |
| CatalogDatabaseContracts, CatalogClassifications, CatalogDescriptions, CatalogCollections, CatalogSmartCollections | A/B | Asset IDs, authoring transactions/revisions, static membership and query intent | Native SQLite load and physical path semantics are separate |
| CatalogMediaRanges, CatalogSubclips, CatalogMarkers, VideoRotation | A | Source-relative intent, stable identities, immutable range rules, authored rotation | Runtime PTS/capture adapters must supply truthful evidence |
| CatalogMigrations, CatalogSqliteConnectionFactory, CatalogDatabaseService | B | Schema 19, persistence and integrity policy | Per-RID native assets; verify one library and required compile options |
| CatalogRecovery/ExitBackup/BackupPathVerifier, StartupStoreEvidence | B/C | Admission, backup and recovery ownership, failure behavior | File identity, durability, replacement, long paths; kernel32 GetFileInformationByHandle replacement |
| MediaRoots.cs, BrowserStorage/Tree | B/E | Logical RootId and portable relative display paths; offline nondestructive semantics | Current uppercase keys/ignore-case containment must change through accepted policy; volume/mount/SMB discovery |
| PreviewPersistence/Maintenance/RetryPolicy, BrowserPreviewReuse | B | Rebuildable cache, scheduling, generations, work/source identity | Cache root policy, filesystem identity, decoder output; invalidate incompatible old derived state |
| DerivedMediaMetadata.cs, MediaMetadataParser/Presentation, FFprobe metadata | A/B/C | Provider-normalized metadata, provenance and raw-readback contracts | WicImageMetadataReader → ImageIO/shared reader; ffprobe executable/process adapter |
| ThumbnailGeneration.cs, CachedPreviewImageConverter, DetachedBitmap, OrientedPreviewImage | C/D | Request sizing, orientation and detached-resource guarantees | WPF/WIC pixels and cache decode replaced; common format/ICC/EXIF test corpus |
| MarkerThumbnails, SubclipPosters, VisualIndex, FrameScreengrab | B/D | Which frame and current authored presentation to capture, scheduling/provenance | Neutral pixel result + UI image conversion; current BitmapSource/encoder usage cannot share unchanged |
| JobModel/Execution/Runtime, JobsAdmission, IndependentExportJobs | A/B | One global 1–8-slot scheduler, cancellation, typed lifecycle, per-file recoverable units | Process containment and UI dispatcher projections; preserve state across sleep/quit safely |
| ExportMaterialization, EncodingJobPlanning, EncodingRangeResolver, ExportDefaultsStore | A/B | Immutable ranges/Color/provenance, review and job policy | Capability mapping, path collisions/filename rules, executable and output verification |
| EncodingJobExecutor / encoding presets / hardware capability code | B/D | Export intent and result contract | NVENC → probed VideoToolbox; no automatic equivalent of NVENC quality/flags |
| ColorManagement, LutCatalog, EncodingColor, DerivedFrameColor | A/B | Camera→Creative order, domain ranges, interpolation, content hashes, bypass authority | ICC/tone-map boundary and renderer pixel conversions |
| LightflowColorPostProcessor + FlyleafPlaybackBackend | D | Desired Color contract and test vectors | D3D resources/shaders → Metal/native media path; live/capture/export agreement |
| MediaPlayback.cs / PlayerFrameStepQueue / coordinator | B/D | Source generations, bounded serialization, seek/step/loop/capture semantics | FrameworkElement signatures must split; backend timing/surface leases |
| Flyleaf fork + FFmpeg bindings + Vortice | D/E | Windows backend remains accepted during transition | No Mac Flyleaf backend established; binding loader/calling convention/native versions must qualify |
| FFmpeg subprocess audio + NAudio.WinMM WaveOut | D | Bounded PCM/cancel/restart and volume intent | CoreAudio sink or one-clock media backend; latency/drift/device-change proof |
| PlayerViewerHost*.cs/xaml, video overlay/retained bitmap/fullscreen | D | Review set, range/Subclip/marker actions and view intent | UI surfaces, native compositing, clipping/input, same-lease fullscreen |
| MainWindow*.xaml/cs, App.xaml/cs, themes and UserControls | D | Shell state and shared view-model intent once extracted | Avalonia views/templates; WPF XAML, DependencyProperty, routed events not drop-in |
| Dispatcher, DispatcherTimer, INotifyPropertyChanged projections | B/C | Notifications and application scheduling intent | Framework UI scheduler; avoid core importing UI dispatcher |
| HwndSource, WindowChrome, WindowAppearance, StartupSplash | C/D | Window identity, startup handoff and dark tokens | AppKit/native chrome/activation; DWM, user32 and ICO do not translate |
| Open/SaveFileDialog, WinForms folder dialogs | C | Dialog intent and chosen path validation | Native Mac file/folder UI, permission grant lifetime |
| Clipboard / DragDrop / BrowserFolderDragGesture | C | Selection IDs and operation choice | Native file URL pasteboard and promised-file lifetime; Mac Option copy convention |
| ApplicationWindowActivation.cs user32 APIs | C | Activation/reopen intent | AppKit activation; preserve focus/session cancellation |
| ApplicationInstance.cs named mutex and current-user pipes | B/C | Stable per-user/profile singleton before opening storage | Mac user-scoped IPC/lock + Dock/file-open event forwarding; crash cleanup |
| LowLevelMouseWheelHook.cs user32/kernel32 | C/E | Relative zoom/input meaning | Local Mac events preferred; do not add global permissions gratuitously |
| DeletePreflight.cs shell32 COM IFileOperation/GetVolumeInformation | C/E | Full selection preflight; recoverable versus explicit permanent policy | FileManager Trash capability/verification, especially network/removable locations |
| FileOperations.cs WindowsFileOperationPlatform | B/C | Copy/move/rename/executor/Catalog mutation contracts | Per-volume comparison, links, atomic rename, cross-volume copy/move, Trash |
| Explorer/open-containing-folder helpers, JobOutputLocation | C | Reveal/open requested result | Finder/NSWorkspace or qualified framework service |
| ProcessSuspender.cs ntdll | C/D | Legacy pause intent only where supported | Do not emulate thread suspension as modern job pause; define safe process lifecycle |
| ValidationPresentation, scripts/ValidationDesktop.cs and NoninteractiveValidation.ps1 | D | Noninterference, owned cleanup, task data isolation | Dedicated Mac GUI host/session/VM; no CreateDesktop equivalence |
| WindowsKeyboardShortcuts.cs + MainWindow/PlayerViewerHost shortcut adapters | C | Neutral profile, Primary, conflicts and semantic action registry | Mac key translation/menu ownership/layout/hold cancellation; local control ownership retained |
| PremiereHandoff/Markers/Jobs, Companion JS | A/B/E | Durable Lightflow identity, one-way projections, tick conversion, journal/conflict retry | Shared case policy, native project path, host API/save/reopen acceptance |
| PremiereBridge WindowsIdentity/FileSecurity | C | Exact loopback host/bearer protocol and pairing UX | POSIX-private credential file or Keychain adapter; atomic secret creation and permissions |
| PremiereInstallation/UPIA discovery, companion packaging | C | CCX payload and setup/status model | Mac Adobe application/helper paths, install/version discovery; user grant UI |
| Resolve future Epic #322 | E | Handoff intents/Jobs and product authority | Supported edition/API/process topology still research-gated |
| TourBox #345/#346/#354 and Draft #360 | E | Accepted actions and profile map semantics | Vendor transport/Console association, Mac permissions and hardware hold/release; untouched |
| Registry/installer migration discovery | C | Upgrade product identity and data preservation | Mac bundle install/update/uninstall policies; no registry analogue needed |
| Inno .iss, Build-Release win-x64/single-file extraction | D | Version/source gates, isolated smoke and verified dependency provenance | .app, RID-native dylibs, sign/notary/staple, DMG/ZIP release pipeline |
| Manifest, ICO, embedded branding PNG resources | C | PNG brand assets, product identity | Info.plist/bundle ID/icns, native menu/About names, no state inside bundle |
| CI windows-latest, xUnit Windows TFM, private-desktop bootstrap | B/D | Test corpus and semantic assertions | Extract neutral test project; add Mac native/headless/package jobs with correct isolation |

## Direct and locked transitive dependencies

| Dependency | Current version | License / platform classification | Recommendation |
|---|---|---|---|
| Microsoft.NET SDK/runtime / WindowsDesktop | App net8.0-windows; Actions net8.0 | Runtime generally MIT; WPF/WinForms Windows-only D | Preserve current build; separately plan supported .NET LTS upgrade |
| Microsoft.AspNetCore.App | Framework reference | MIT; Kestrel portable B | Share bridge transport; adapt credentials/security and packaging runtime |
| FlyleafLib | 3.11.8-lightflow.1 exact | LGPL3+; Windows graphics/native D | Keep Windows adapter; cannot be Mac runtime by TFM edit |
| Flyleaf.FFmpeg.Bindings | 9.0.0 | LGPL3+ per repository notice; bindings E for new native load | New backend may reuse after Mac symbol/ABI proof |
| NAudio.WinMM / NAudio.Core | 2.3.0 | MIT; WinMM D, parts of Core A/B | Replace device output on Mac |
| Microsoft.Data.Sqlite / Core | 8.0.29 exact | MIT; managed portable B | Keep provider policy, verify signed native assets |
| SQLitePCLRaw bundle/core/provider/lib e_sqlite3 | 2.1.13 exact all four | Apache-2.0; native SQLite public domain; B | Both Mac RID assets inspected; runtime proof pending |
| Vortice.D3DCompiler/Direct3D11/DirectComposition/MediaFoundation/XAudio2/DirectX/DXGI/Direct2D1 | 3.8.3 | MIT; Windows D | Confine to Windows backend |
| Vortice.Mathematics | 2.1.1 | MIT; mathematical types A but dependent use B | Do not carry graphics ownership into domain |
| SharpGen.Runtime / COM | 2.4.2-beta | MIT; COM/native wrappers C/D | Windows infrastructure only unless independently proven |
| System.Text.Json/Encodings.Web/IO.Pipelines | 9.0.1 | MIT; A | Share serialization/protocol; schema compatibility tests |
| System.Memory | 4.5.3 | MIT; A | Shared dependency; avoid redundant explicit additions |
| Export FFmpeg bundle | 8.1.2-34-g9b6c8969e0 | Pinned Windows LGPL build; B orchestration / D binary | Mac source-controlled build manifest, legal/native audit |
| Playback FFmpeg bundle | 9.0.1-6-g9d4ca21220 | Pinned Windows LGPL shared build; D binary | Match native binding ABI; separate from export until validated |
| Tests: Microsoft.NET.Test.Sdk, xUnit, runner | 17.14.1 / 2.9.3 / 3.1.4 | Tooling no required runtime fee; test project Windows D | Reuse cases/assertions after neutral/native test split |

License facts for shipped packages are from THIRD-PARTY-NOTICES.md, dependencies/licenses and locked source. Flyleaf local fork provenance is recorded there. Proposed packages must receive their own version-specific native/transitive audit; this table is not a legal opinion or a complete future SBOM.

## WIC / bitmap dependency closure

Actual image decode/encode owners: DerivedMediaMetadata.WicImageMetadataReader; ThumbnailGeneration.WicImageThumbnailRenderer/metadata; DetachedBitmap; OrientedPreviewImage; FrameScreengrab; preview cache converters/regeneration verifier. BitmapSource/Frame surfaces also appear in FlyleafPlaybackBackend, PlayerViewerHost, SubclipPosters, VisualIndex, Inspector marker previews and StartupSplash. BrowserQuery calls the WIC owner's pure DisplayDimensions helper: extract that geometry function rather than treating a model's absence of System.Windows as proof of independence. The raw scan includes BrowserGrid brushes and test usage; not every match means WIC decoding.

## Scan methodology and limitations

research/work/inventory.ps1 uses rg file discovery across C#, XAML, projects, packaging, JSON and Companion JS. PLATFORM_DEPENDENCY_MATCHES.json records file/line/group/text. SOURCE_PORTABILITY_SCREEN.csv screens the 248 production C# files. Group matches overlap and include tests/support data: UI 1,732; Win32 139; filesystem 1,476; media 952; integration 1,714; packaging 60. They are search hits, not dependency counts. Registry may mean a semantic registry; case-insensitive comparison may be a harmless enum/name comparison. Read semantic owners before classification. The inventory above is the architecture-based classification; raw evidence supports drill-down.

Mac uncertainty is greatest at runtime contracts, case-sensitive identity and native permissions; a successful managed compilation would not resolve those. Source build artifacts in the canonical checkout were observed to be stale relative to main, so current lockfiles and task-local evidence were used rather than inferring current dependencies from another task's obj folder.
