# Capability and UI parity matrix

Baseline main 1119a907b02ef5cc97c9c62b4f2fddb196e540df; research 2026-10-05. **No Mac runtime row is verified.** Expected statuses: P = plausible equivalent after implementation; N = intentional native difference; G = high-risk proof gate; E = external/vendor constraint. Difficulty S/M/L/XL is relative engineering scope, not elapsed time. Required first-release rows must pass or receive an explicit owner-approved exception. Tests below are proposed, not executed.

## Complete UI surface map

| Surface | Reusable logic | WPF coupling to replace | Mac-native difference | Difficulty | Parity/risk | Acceptance |
|---|---|---|---|---|---|---|
| Splash/startup handoff | Readiness and dependency/storage admission | WPF bitmap/window plus DWM/user32 | Bundle/icon/Dock startup, app activation | M | P/N | Splash closes only after real shell readiness; no empty flash |
| Shell/Home/Back/status | Workspace navigation and retained intent | MainWindow/XAML, templates, HWND chrome | Native menu, close/reopen/Quit, titlebar | L | P/N | Retain Browser/Player and Jobs ownership, minimum size and resizing |
| Browser Locations/tree | Roots, scope navigation, lazy expansion | Tree controls, scroll/focus/drag state | Mounted volumes/Finder paths, SMB permissions | L | P/G | No descendant scans; offline roots retained; deep scrolling |
| Browser Grid | Query, stable selection, thumbnail sizing/layout intent | Custom virtualized WPF realization, brushes/images | Retina/pixel scaling, native drag modifiers | L | P/G | Large corpus, bounded containers/memory, anchor retention |
| Browser Details | Query/selection/order and column intent | DataGrid/row templates/scroll metrics | Font metrics, keyboard navigation | L | P/G | Same IDs selected in Grid/Details; maintained free table path |
| Search/filter/refinement | Typed predicates/query lock/descriptor semantics | Editors, menus, routed key ownership | IME/Option/Command/menu focus | M | P | Unicode/mixed operators, text arrows/Delete owned locally |
| Collections | Static membership and Catalog revisions | Tree/menu/dialog views | Native drag/file distinctions | M | P | Removing membership never deletes source |
| Smart Collections | Saved query intent and fresh projection | Query editor/preview controls | Text/menu conventions | M | P | Query membership truthful; no destructive fallback |
| Favorites | Future location/scope intent; current backlog #323 | Future sidebar view/context menu | Volume remount/reveal | M | E | Not a shipped Windows parity requirement; keep outside Mac scope unless separately accepted |
| Player canvas/video | Review/source/lease coordinator and authoring | Flyleaf host, WPF surfaces/overlays | Metal/NSView native composition | XL | G | PTS/retained/capture/Color/fullscreen contract |
| Player transport/timeline | Actions, eligibility/ranges and operation intent | WPF sliders/hit targets/dispatch | Keyboard layout, pointer/trackpad gestures | L | P/G | No duplicate commands; scrub settles actual frame |
| Filmstrip/Visual Index | Work planning/cache and navigation semantics | Images/grid/view host | Scaled image/render/color conversion | M/L | P/G | Position/result identity, cancel/reuse, file-handle release |
| Range/In/Out editor | Source-relative range authority, validation | WPF trim indicators/thumbs | Native key conflicts/local control ownership | L | P/G | Inclusive UI Out retained; exact exclusive Export boundary |
| Markers | MarkerId/position/classification and revision checks | Timeline markers/menus/thumbnail surfaces | Native gestures, VoiceOver labels | M/L | P/G | Exact displayed PTS; point-only timing and readback |
| Subclips panel | Stable IDs, selection, saved range/Color authority | Cards/images/dialogs and retained selection | Font/layout/dialog conventions | M/L | P | Asset change clears; panel toggle retains; source not duplicated |
| Inspector/descriptions | Normalized metadata, mixed-field patch/revisions | Editors/preview images/tab controls | Multiline/IME and focus conventions | L | P | No save-on-focus; dirty transition Apply/Discard/Cancel unchanged |
| Right Panel/splitters | Contextual panel eligibility/preference | WPF splitters/rounded resources/state | Keyboard/Retina layout metrics | M | P | Width bounded; retained tabs; no underlap at narrow size |
| Compact Jobs | Common state/command projections | Card controls/dispatcher/radial geometry | Optional native status affordance | M | P | Text/icon/color states; no unsupported running pause |
| Full Jobs/history | Scheduler/history/search/removal/rerun policy | Virtualized list/details views | Native reveal/output dialogs | L | P | Global 1–8 admission; rerun opens review; history delete preserves outputs |
| Export configuration | Materialized inputs/defaults/preflight | Owned WPF modal, combobox/editor controls | Destination picker, VideoToolbox settings/capability | L | P/G | Cancel returns truthfully; range/Color/provenance preserved |
| Settings/storage/recovery | Preference authority, cache/backup maintenance | Categories/rows/dialogs/templates | Native Settings menu, storage permission grant | L | P/G | Staged Save/Restore; explicit destructive actions; isolated profile |
| Keyboard Shortcuts Settings | Descriptor catalog, gesture/conflict/profile schema | Windows recording/row focus/scroll | Primary Command, explicit Control, reserved OS combos | M/L | P/G | 23 descriptors/77 variants; exact modifiers and local ownership |
| Dialogs/menus | Intent, eligibility and explicit action vocabulary | WPF owned dialogs/context menu styling | App menu/native file dialogs vs styled app confirmations | M | P/N | Modal cancellation/owner focus; explicit destructive labels |
| Status/errors/About | Structured results, diagnostics, product/dependency identity | Visual controls and stock OS helpers | Native About/menu/notification conventions | M | P/N | Essential status never color-only; actionable truthful errors |

Design reference: docs/DESIGN-SYSTEM.md, UI_GUIDELINES.md and Themes/LightflowShell.xaml. Reuse semantic geometry/tokens; WPF resources require translation. No new layout, theme option or local workflow semantics are implied.

## Media, data and integration capability gates

| Capability | Shared authority | Mac-specific work | Status / difficulty | Required evidence |
|---|---|---|---|---|
| Open/replace/close source | One generation/lease, stale result suppression | Native decoder/GPU/output lifetime | G / XL | Repeat open/close/cancel; no leaked process/texture/file handle |
| Exact forward step | Bounded request/action policy | Decode/display acknowledgement | G / XL | Immediate next real PTS, including VFR and B-frame fixtures |
| Exact backward step | Immediate-predecessor contract | Bounded earlier seek/decode/cache | G / XL | Near zero/EOF, long GOP, alternating direction; truthful failure |
| Pause/seek/retained frame | Settled source timestamp authority | Renderer surface/retained texture | G / XL | Paused image never replaced by stale seek; audio silent |
| Speed/rational cadence/loop | Presets, range/traversal intent | Clock/audio timing and boundary decode | G / L | Drift/loop tolerance and authoritative end behavior |
| Volume/mute/audio streams | Typed 0–100 and preference semantics | CoreAudio output/device routing | G / L | Seek/restart/sleep/device loss; no source reset of volume |
| In/Out/markers/Subclips/review | Catalog state and source-relative rules | PTS/capture delivery | P/G / L | Timestamp equality and save/reopen revision checks |
| LUT chain/Compare Original | Cube validation/order/hash and hold session | Metal/native shader stages | G / XL | Camera→Creative, domain clamp/trilinear; hold release/focus cancel |
| Rotation/zoom/pan | Source orientation + authored rotation, viewport intent | Texture/display coordinate transform | G / L | All 90-degree orientations; no double source rotation |
| Fullscreen | Same source/lease and view intent | Spaces/native host/overlay input | G/N / L | No reopen/audio restart; retain frame/zoom; exit restores focus |
| Screengrab/poster/review capture | Chosen current frame and provenance | Full-resolution GPU/pixel readback | G / L | Current Color/rotation agrees with live and Export tolerances |
| FFprobe normalized/raw metadata | Shared parser and provider contract | Mac binary/process/cancellation | P / M | Same fixtures; malformed/nonzero-start/timecode/source rotation |
| H.264/HEVC export | Job/materialization/range/output semantics | Probe VideoToolbox/profile/quality flags | G/N / L | Actual encode then probe/decode; unavailable stays disabled |
| ProRes | No accepted new capability implied | Mac encoder availability and settings | E / M | Owner product decision plus actual device/profiles |
| Image metadata/thumbnail decode | Request/EXIF/orientation/cache contract | WIC replacement/ICC/native format gaps | G / L | JPEG/PNG/TIFF/BMP/GIF plus promised WDP/JXR behavior |
| RAW | Preserve current placeholder scope | Optional future native/full decoder | E / L | No RAW parity claim without accepted feature/fixtures |
| Catalog native runtime | Schema/repositories/integrity policy | Signed arm64/x64 SQLite dylib | G / M | Metadata exists; actual package reports 3.53.3/options/schema19 |
| Catalog move Windows↔Mac | Stable IDs/authoring | Root mapping/case/Unicode/recovery evidence | G / L | Consistent backup round trip; identical domain intent/IDs |
| Preview rebuild/recovery | Derived state/generation/quota | Cache location/decoder/native handles | P/G / M | Delete/regenerate cache with durable Catalog unchanged |
| Copy/move/rename/bulk verify | Jobs/plan/collision/Catalog mutation | Per-volume semantics/permissions/links | G / L | Interrupted operation, case-only rename, cross-volume/network |
| Recoverable delete | Explicit selected operation and no fallback | Trash native capability | G/N / M | Local/removable/SMB support or truthful rejection; never fallback |
| Watchers/removable/network roots | Hints→reconcile/offline policy | Native watch/mount behavior | P/G / M | Overflow/disconnect/remount/no destructive reconciliation |
| File clipboard/drag/Finder reveal | Selected logical items and requested operation | Pasteboard URLs/grants/native reveal | P/N / M | Lifetime, native modifier choice, missing/denied paths |
| Single instance/activation/reopen | Product/profile identity before storage | Mac IPC/AppKit lifecycle | G/N / M | Two launches/quit/crash/Dock/file-open and stale lock handling |
| Shortcuts/control actions | Accepted neutral model | Mac gesture/focus/menu adapter | P/G / M | Primary Cmd; explicit Ctrl; exact modifiers, repeats/holds |
| Premiere source/Subclips/markers | Handoff/journal/revision semantics | Install/discovery/credentials/path/API evidence | E/G / L | Real 26.5+ host, folder grant, project change, save/reopen/retry |
| Resolve | Future shared projection intent | Edition/API/process topology | E / XL | Separate Epic #322 proof; no new runtime claim here |
| TourBox Console/deeper transport | Shared actions/profile semantics | Vendor permissions/association/physical map | E / L | Separate #354/#346 gates; unchanged by this task |
| Signed direct .app | Version/provenance/license gates | Code sign/notary/staple/DMG | G / L | Clean quarantined install, native load, upgrade/data preservation |
| Intel Mac | Same product intent | x64 dependencies/runtime/performance | E / L | Add support only with funded physical qualification |

Expected parity ceiling: equivalent authoring/results and common visual identity; native window/menu/fonts/shortcuts, encoder controls/bitstreams and hardware performance differ. Unknown rows cannot be labeled implemented. Detailed technical evidence and source links are in the main report and SOURCES.md.
