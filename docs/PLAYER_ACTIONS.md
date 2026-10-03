# Player semantic action boundary (#345 / #349)

The first vertical slice extracts reusable action metadata, validation, target eligibility, repeat/single-flight
policy and Color gesture ownership into `Lightflow.Actions` (`net8.0`, no packages or Windows/WPF references).
It does not extract playback, create a second decoder or turn this assembly into a plugin API.

| Action ID | Arguments / phases | Repeat / execution |
| --- | --- | --- |
| `player.play-pause` | No arguments; Invoke | Suppress repeat; single flight |
| `player.step-frame` | `FrameStepArguments(-1 or +1)`; Invoke | Relative repeat; existing bounded/coalesced Player queue |
| `player.color-bypass` | No arguments; Begin, End, Cancel | Idempotent hold; one owning gesture session |

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

Input ownership precedes semantic admission. Windows keyboard adapters resolve text/editable controls, sliders,
thumbs, selectors, menus and tab-content boundaries, then map only Space, plain Left/Right and C down/up. Buttons
retain Space. #344's selected shell-tab content boundary and folder-tree origin route are preserved unchanged;
Ctrl+Arrow review and Alt+Arrow markers remain legacy routes, as do I/O/S/M, classification and Browser/Delete.
MainWindow, Player and native video surfaces propagate repeat state consistently for these three actions.
Space repeat cannot toggle; repeated C cannot rearm a cancelled hold; Arrow repeat enters the existing queue.

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
8. Architecture stays small: three action descriptors, one dispatcher/session owner, one narrow port.
9. macOS can consume the contracts: only the application/presentation adapter requires replacement.
10. Settings can discover metadata: immutable descriptor inventory carries binding/repeat/phase policies.

These establish automated architecture evidence. Owner architecture and packaged hands-on acceptance remain
required before merging the Draft PR. #350–#354 stay separate follow-ups; #346 still gates TourBox integration.
The independent near-source-start backward presentation observation preserved with #344 is not addressed.
