# Player semantic action boundary (#345 / #349 / #350)

The first vertical slice extracts reusable action metadata, validation, target eligibility, repeat/single-flight
policy and Color gesture ownership into `Lightflow.Actions` (`net8.0`, no packages or Windows/WPF references).
It does not extract playback, create a second decoder or turn this assembly into a plugin API.

| Action ID | Arguments / phases | Repeat / execution |
| --- | --- | --- |
| `player.play-pause` | No arguments; Invoke | Suppress repeat; single flight |
| `player.step-frame` | `FrameStepArguments(-1 or +1)`; Invoke | Relative repeat; existing bounded/coalesced Player queue |
| `player.color-bypass` | No arguments; Begin, End, Cancel | Idempotent hold; one owning gesture session |
| `player.set-boundary` | `SetBoundaryArguments(In or Out)`; Invoke | Suppress repeat; single flight |
| `player.traverse-review` | `TraverseArguments(Previous or Next)`; Invoke | Relative repeat; single flight |
| `subclip.create-from-working-range` | No arguments; Invoke | Suppress repeat; single flight |
| `marker.add` | No arguments; Invoke | Suppress repeat; single flight |
| `marker.navigate` | `TraverseArguments(Previous or Next)`; Invoke | Relative repeat; single flight |

Descriptors carry labels, category, argument shape, bindability and phase/policy metadata for future help,
controller mapping and Settings. They do not persist bindings. Invocation contains typed arguments, input source,
invocation/gesture identity and an immutable resolved Player target (host session identity, source generation,
optional AssetId). The first-slice context projects Player presentation, interaction availability, source readiness
and active Color. Browser selection/scope and other shell destinations will be added when their concrete actions
need them; no hypothetical context property bag is introduced.

Eligibility uses application lifecycle and playback snapshot state, not Button/Slider.IsEnabled. Readiness publishes
only after video initialization and presentation attachment. Absent, failed, loading, inactive or modal contexts
are unavailable. Stale targets return Superseded and cannot act on a replacement source. Results are Completed,
NoChange, Ineligible (structured reason), Cancelled, Superseded, Busy or Failed (diagnostic).

Input ownership precedes semantic admission. **Keyboard focus is evidence about local interaction ownership;
it is not itself the semantic Player target.** The target remains the presented Player session/source generation.
Windows maps Space, plain Left/Right, C down/up, I/O/S/M, Ctrl+Arrow review and Alt+Arrow markers
to this semantic boundary. Classification and Browser/Delete remain separate routes.
MainWindow, Player and native video surfaces propagate repeat state consistently for all migrated actions.
Space repeat cannot toggle; repeated C cannot rearm a cancelled hold; Arrow repeat enters the existing queue.

## Focus and local keyboard ownership

`PlayerKeyboardOwnership` is the shared Windows admission policy for semantic and legacy shortcuts. It tests
ownership of the particular key, rather than treating every focused control as an owner of every Player command.
Text/editable ComboBox input retains typing, navigation and editor commit/cancel. Menus and open dropdowns retain
their local interaction. Sliders/thumbs retain navigation keys, including Arrow adjustment; nonconflicting review
commands remain available. Deliberately keyboard-focused Buttons retain Space/Enter; selectors and closed pickers
retain their normal navigation, activation and type-ahead. Player/Filmstrip and selected shell-tab content remain
boundaries so #344 folder-tree-origin Player commands do not accidentally become shell TabControl navigation.

Mouse completion has a different intent from deliberate Tab/assistive control focus. `PlayerReviewFocus` tracks
the initiating routed mouse interaction across ordinary Buttons and noneditable ComboBoxes. Button Click/release
or ComboBox DropDownClosed returns to the Player review surface when Player is still presented. WPF popup fade
completion is handled by the actual close event, with no dispatcher delay or timer. A new editor, menu, slider,
list interaction or pending Inspector description transaction retains its focus. Tab explicitly ends this mouse
intent tracking. Control Focusable, IsTabStop, styles, automation names and normal WPF activation are unchanged.
Programmatic/keyboard Button invocation does not invent mouse intent or steal deliberate Button focus.

Successful Inspector Apply explicitly completes the edit transaction and restores review focus only if the same
editor/context still owns Inspector focus and has no draft. Pending, failed, cancelled or obsolete Apply cannot
restore focus, discard a draft or bypass transition guards. Mere nonediting Inspector/Right Panel chrome allows
Player review commands; Browser file/navigation commands keep the existing Right Panel guard. I/O/S invoke the semantic range/Subclip actions and return to review focus when invoked as review shortcuts. Subclip reveal/selection and rename typing/Enter/Escape retain their contracts.

#350 consumes this same key-ownership policy before dispatching range/review/Subclip/marker actions. Ownership
does not make an unavailable Player eligible: presentation, modal state, initialized source and Color readiness
still control semantic admission independently. A macOS adapter must reproduce the distinction between active
editing/control interaction, completed incidental focus and presented Player target using its native interaction
events. It should reuse the neutral targets/actions, not copy WPF ancestry or shell focus rules.

The unchanged-build RCA and corrected behavioral evidence are recorded in
[the focus revision](validation/player-focus-349.md). Packaged owner architecture/hands-on acceptance remains pending.

`PlayerViewerHost.Actions.cs` is the explicit Windows application/presentation adapter. Its narrow
`IPlayerActionPort` implementation calls existing range-aware play/pause, `FrameStepQueue` and
`ExecutePresentedStepAsync`, and the current Color pipeline. Existing transport buttons and surface clicks invoke
the same action instance as keyboard and the direct fake-controller fixture. Fullscreen feedback and retained
WPF frames remain in that adapter. `MediaPlaybackPresentation` still contains WPF and is not claimed neutral.

Frame invocation completion means the coalesced batch settled, not one decoded step per input event. The queue
still caps pending magnitude at 20, combines opposite directions, pauses/silences playback, and preserves decoded
PTS and retained-frame presentation. Batch failures are shared across waiting callers. Source replacement resets
pending intent and reports Superseded; target guards after presentation awaits prevent stale frames from publishing
into the replacement source. A caller cancellation token cancels its wait; it does not independently undo already
coalesced intent or abort a native decode. Source/backend cancellation retains its existing authority.

Color bypass retains the original target and source/session identity. Unmatched release cannot clear another
adapter's hold; overlapping distinct holds report Busy. End/Cancel can release even when ordinary action eligibility
is now false. Release recomputes bypass against current `_colorPipeline` / `_colorActive`, including assignments
changed during the hold, and writes no Catalog data. Keyboard focus loss cancels the keyboard source; application
window deactivation, WPF modal-loop entry, window/host disable, unload, source replacement and leaving Player cancel
active gestures. Controller adapters must call CancelSource on disconnect and dispose their session owner. Dispose
also clears an active hold. A release against an obsolete target cannot touch the new Player.

Calls/lifecycle run on the application's serialized interaction dispatcher. The shared contracts contain no
WPF dispatcher; a future hardware adapter must marshal to the application's dispatcher before using the Windows
port. A future macOS adapter can reuse this assembly and replace the port's range-aware playback, retained-frame
handoff, Color presentation and lifecycle hooks. Existing Windows/Flyleaf presentation interfaces remain Windows
boundaries; this slice does not complete a macOS playback backend.

## First-slice acceptance questions

1. Semantic actions work without keys: direct fixtures invoke all three through typed contracts.
2. UI/keyboard/controller share behavior: one PlayerActions and one current playback lease/port per host.
3. Ownership is separate: WPF ownership checks precede platform-neutral eligibility.
4. IDs/arguments/results are neutral: separately built net8.0 assembly with no Windows references.
5. #344 remains covered: existing folder-tree, local controls, modifiers, repeat and fullscreen regression tests.
6. Color lifecycle is explicit: release/cancel/disconnect/focus/deactivation/modal/source replacement tests.
7. Player behavior is preserved: existing range/review, queue, Color, decoded-frame and lease suites retained.
8. Architecture stays small: eight action descriptors, one dispatcher/session owner, one narrow port.
9. macOS can consume the contracts: only the application/presentation adapter requires replacement.
10. Settings can discover metadata: immutable descriptor inventory carries binding/repeat/phase policies.

These establish automated architecture evidence. Owner architecture and packaged hands-on acceptance remain
required before merging the Draft PR. #351–#354 stay separate follow-ups; #346 still gates TourBox integration.
The independent near-source-start backward presentation observation preserved with #344 is not addressed.

## Range, review, Subclip and marker slice (#350)

All five added descriptors are bindable discrete Invoke actions in the Player category. Boundary and traversal
arguments are neutral enums/records; invalid enum values or argument shapes are rejected before port admission.
The Windows port projects service/workflow eligibility rather than control IsEnabled values. Range and marker
operations require a ready video and an authoritative decoded presentation timestamp. Subclip eligibility reuses
`SubclipCreationEligibility` against the working range and authoritative duration. Review traversal has separate
review readiness so still images and unavailable captured members can be left through the same review set.

Set In/Out use the retained displayed frame timestamp when present, otherwise the playback snapshot. They reuse
`ReviewRangeBoundaryPolicy` and `IMediaRangeStore`; the requested boundary still wins across the opposite boundary.
Selected Subclip review exits before working-range edits. Clear-boundary editor controls retain their existing
workflow, with range publication guarded by generation. No frame duration is inferred or range policy changed.

Review traversal retains captured order, subset identity, no wrap, `ContextChanging` Inspector guards and the
existing paused destination/open/restored-range path. One semantic traversal is admitted at a time; later input
while it is resolving returns Busy and is not queued. Directional auto-repeat can traverse again after completion.
The existing filmstrip selection path can supersede pending review resolution. Resolver completions check generation
before opening, so a replaced source cannot be overwritten by an older destination request. Successful traversal
intentionally changes the target; completion describes that destination, rather than calling the change stale.

Subclip creation materializes current full/partial/restored working-range intent using `ISubclipService`, preserves
service-owned stable IDs, duplicate detection, current ordering and reveal/select. Duplicate invocation returns
NoChange while revealing the existing Subclip. Marker creation uses `IMarkerService` and exact displayed PTS;
duplicate identity remains service-owned. Marker navigation reuses strict previous/next `MarkerNavigation` and
`SeekMarkerAsync`, with NoChange at an endpoint or when no marker exists. Rename/edit/delete remain local workflows.

Boundary writes, S and M suppress IsRepeat. Review and marker directions allow relative repeat with single-flight
admission; no unbounded queue or repeat-driven creation exists. UI Set In/Out, Create Subclip, review previous/next,
marker add (including timeline menu) and marker previous/next dispatch the same actions as keyboard and controllers.
I/O/S keep their established review focus completion. #349 per-key ownership and mouse/Inspector focus policy remain
unchanged, including editor typing, deliberate Button activation, sliders, lists, menus and open dropdowns.

Every port entry validates the captured session/generation. Async range save, Subclip create, marker create/list,
review resolution/open and marker seek recheck ownership before publishing into Player presentation. An already
submitted Catalog mutation can finish for its original captured asset; it cannot mutate/reveal the replacement.
Structured failures stay inside the boundary. Direct-controller and native CFR/VFR evidence is recorded in
[the #350 validation record](validation/player-actions-350.md).

The #352 review/presentation and typed Export extension uses separate narrow presentation and shell ports;
see [the operation inventory and cross-platform contract](REVIEW_PRESENTATION_ACTIONS.md). The accepted
eight playback/range/Subclip/marker descriptors and input ownership above remain intact.
