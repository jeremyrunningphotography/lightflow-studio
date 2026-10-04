# Review presentation and Export semantic actions (#352)

Base: accepted main `390544cdc7e198d899d0f1aeb44fce7491cae779` (#349/#350).
This slice consumes `Lightflow.Actions` contracts/results/metadata and the existing serialized application
interaction dispatcher. It introduces two narrow ports, rather than expanding the playback/Catalog port into
shell ownership. No keyboard simulation, controller transport, mapping persistence or new Export engine exists.

## Inventory and supported subset

| Existing operation / owner | Semantic exposure | Policy |
| --- | --- | --- |
| Playback service Volume / Mute | `player.volume`, `player.presentation-toggle(Mute)` | Audio stream required; integer percent, Set 0–100, Relative −100–100 percentage points, result clamped 0–100; no implicit unmute |
| PlaybackReviewOptions / current cadence | `player.review-speed` | Existing typed six presets 1/8× through 4×; preserve cadence and playback service ownership |
| Player viewport / DPI sizing / retained-frame and still transforms | `viewer.zoom`, `viewer.step-zoom` | Fit, 50%, 100%, 200%, 400%; one signed preset step, no wrap; Fit resets pan |
| Current PresentedRange loop policy | `player.presentation-toggle(Loop)` | Ready video only; retains full-source/working-range/selected-Subclip loop behavior |
| Same-window PlayerFullscreenPresentation | `player.presentation-toggle(Fullscreen)` | Ready visual and Window; same Player/lease/source; existing overlay and exit restoration |
| FilmstripVisible / workspace capture | `player.presentation-toggle(Filmstrip)` | Existing review presentation preference; fullscreen keeps filmstrip hidden |
| BrowserGridLayout / ApplyBrowserThumbnailSize | `browser.step-thumbnail-size` | One signed notch, bounded existing six levels; Browser Grid only; same cached data/reflow/persistence lifecycle |
| Shared contextual Right Panel | `review.toggle-right-panel`, `review.show-panel` | Inspector, Jobs, Subclips, Visual Index only when available; Home only, unavailable in fullscreen; no focus grab or editor commit/discard |
| EncodingCapabilityHandoff / SubclipExportCapabilityHandoff | `export.open(ExportEntry)` | Explicit BrowserVideos, BrowserSubclips, PlayerVideo, PlayerSelectedSubclips, PlayerAllSubclips; current authoritative handoffs and owned modal |

Cadence authoring, viewport pan gestures, timeline scrubbing, splitters/window geometry, Details/grid switching,
Inspector editing, Color assignment/grading, screengrab, Visual Index generation and Job management stay with
their existing local workflows. They are not necessary to prove this controller-facing subset. Browser selection,
classification and scope authoring remain #351; Settings/bindings remain #353; presets remain #354; TourBox
transport remains gated by #346. No Export bug-squash, range, selected-Subclip boundary or engine semantics change.

## Admission, targets and completion

Player presentation uses the accepted `ActionInvocation` and session/source-generation target, independently of
keyboard focus. Its port projects readiness/audio/dimensions from the application and playback snapshot rather
than IsEnabled. Keyboard input still uses #349 per-key ownership. Mouse/keyboard UI controls invoke the same
presentation actions; cadence and lifecycle restores remain local. Set-volume repeats are suppressed; relative
volume and zoom steps allow repeats. Preset changes and toggles suppress repeat. Per-action single flight drops
concurrent work as Busy, without an accumulating controller queue. Cancellation is checked before admission;
async speed rechecks the captured target before publishing UI state. In-flight playback service cancellation
retains the existing service's authority.

Shell targets are transient session/revision identities. Windows compares Home destination, Browser presentation,
layout, navigation generation, folder/Collection identity, ordered selected AssetIds, Player generation and current
Subclip identities. Browser selection publication observes this context, so returning to an earlier selection does
not revive an observed old target. No selection/navigation mutation is added. Stale calls return Superseded.
A hardware adapter must capture a fresh target at dispatch and marshal all calls onto the application dispatcher.
No WPF object, OS key or controller identity is embedded in either target.

Export eligibility reuses current Browser selection applicability, Player video readiness and the requested
Subclip selection. Browser source context and deterministic order are captured before asynchronous preparation;
selected Player Subclips retain current service/panel order. Existing materializers resolve sources, saved ranges,
Subclip provenance, Color and rotation. The existing range toggle, fallback choice, preflight, confirmation,
owner Window and ExportJobCoordinator/Jobs flow remain authoritative. Source Export retains its existing Browser
source-context eligibility, including the current location/Collection requirement; this slice does not repair
that policy. Browser Subclip fallback remains enabled, exactly as before.

Only one Export entry may prepare at once. Preparation cancellation uses the existing handoff CTS; cancellation
or a replaced context cannot publish a new modal. Modal ownership is rechecked after preparation and before
failure dialogs; shutdown invalidates the shell target. The existing modal owns interaction after opening and is not
remotely submitted or cancelled. `Completed` means preparation and the configuration modal returned (including
ordinary user Cancel), **not** that a Job was queued. Preflight rejection is Ineligible/ExportUnavailable, preparation
cancellation is Cancelled, and stale context is Superseded. Structured errors do not silently queue work. Existing
failure dialogs are retained. No changes to engine, defaults, output collision policy, ranges or Job execution.
Standalone legacy Player event consumers remain compatible; the application wires the awaitable typed shell
handoff, so controller completion does not depend on fire-and-forget events.

## macOS replacement boundaries

Reuse the neutral descriptors, enums, arguments, targets, validation and result/repeat/single-flight rules.
Replace WindowsReviewPresentationPort with native viewport/DPI/retained-frame transforms, fullscreen hosting,
filmstrip presentation and audio/playback-service access. Replace WindowsReviewShellPort with native Home/panel
availability, Browser notch/reflow/workspace capture and an owned configuration-sheet handoff to the same Export
materialization and Jobs semantics. Reproduce #349's distinction between local input ownership and semantic target;
do not copy WPF ancestry, Focus(), Dispatcher, ComponentDispatcher modal state or HWND overlay internals into the
neutral assembly. This slice does not claim to deliver a macOS playback backend.

## Packaged hands-on review

Use the branch's fresh packaged executable with its task-owned acceptance root. Verify all volume/mute and speed
choices, cadence preservation, Fit/percentages/zoom bounds on still/video/retained frames, loop full/range/Subclip,
fullscreen enter/exit without reopening, filmstrip persistence, every Browser thumbnail notch, panel availability,
editor/focus behavior, and all five Export entry kinds. Cancel and queue via the ordinary modal; verify selected
inputs/ranges/Subclip provenance/Color and return to the exact originating Browser/Player context. Also verify
#349/#350 keyboard ownership and #343/#344 behavior. Owner architecture and packaged hands-on acceptance remain
required; Draft PR must stay unmerged.

## Automated acceptance coverage

`ReviewPresentationPolicyTests` covers typed values, phases, repeat policy, single-flight, cancellation, errors and
source replacement during asynchronous completion. `ReviewPresentationActionTests` uses the existing playback
fixture to compare direct controller and UI effects for audio, six speed presets with retained cadence, Fit/zoom,
loop, fullscreen and filmstrip, and tests no-audio, loading-video and ready-still eligibility. `ReviewShellActionTests`
proves explicit Export entries and shell lifecycle/results without keys. `ReviewShellLiveTests` drives the actual
MainWindow thumbnail and Right Panel paths against an isolated Catalog. `ReviewExportLiveTests` drives all five
entries through real Catalog materializers and real owned modals, validates working/Subclip ranges, cancels through
the existing modal, proves modal ownership and preserves context/busy feedback. Its source fixture is intentionally
synthetic; native frame/Color/encoding correctness remains covered by the existing generated-media suites.

Existing #343/#344, Player semantic/range/marker/Color, Browser presentation and Export materialization/modal/Jobs
regression coverage remains in the full Release suite. Run the pinned dependency preparation scripts before the
full suite, build `Lightflow.Actions` independently, run the Companion tests, and rebuild with the required
PullRequest packaging command after the final commit. Automated evidence does not substitute for owner visual,
architecture or packaged hands-on acceptance.
