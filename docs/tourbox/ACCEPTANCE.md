# Owner physical acceptance: Elite Plus / Lightflow

**Pending. Nothing below is marked passed by automated mapping validation.** Follow
[setup](README.md#owner-setup-in-order) and the [control map](CONTROL-MAP.md). Use an isolated,
task-owned Lightflow data root, not normal user storage. The PR handoff supplies a fresh package of
the unchanged accepted application and an isolated profile preconfigured with the 13 additions.
Record its exact executable/source/data-root paths. No normal user shortcut profile is overwritten.

## Evidence sheet

| Field | Value to record |
| --- | --- |
| Date / owner | Pending |
| Device model / firmware | TourBox Elite Plus / pending |
| Console exact version | Pending (recommended research baseline: stable 5.11.3) |
| Windows version / build | Pending physical test (research host: Windows 11 Pro / 26200) |
| USB or Bluetooth / channel | Pending |
| Lightflow commit / executable / data root | Pending; baseline a827470ab0338790c5697d9fcfaebe9231a70670 |
| Shortcut compatibility / deviations | Pending; attach checker output and deliberate changes |
| Rotary speed / haptic labels and scope | Pending |
| Export filename / SHA-256 / import result | Pending |
| C2 hold result / exported assignment | Pending; default is unassigned until passed |

## 1. Association and base controls

- [ ] Link the running LightflowStudio.exe with Auto Switch. Foreground Lightflow activates this preset.
- [ ] Switch to another application and back repeatedly; no preset sticks to the wrong application.
- [ ] Restart Lightflow; relink after moving/updating its executable; test the intended installed path.
- [ ] Browser: Knob selects exactly previous/next items in Grid and Details, with first/last boundaries,
      expected current/selection behavior and no selection extension. Tour opens the expected captured
      review set; dirty Inspector Cancel prevents navigation, Apply completes through existing guards.
- [ ] Player: Knob press toggles once; long press/release does not toggle repeatedly. Knob rotates
      backward/forward decoded frames and pauses as usual. Dial changes review media without wrap.
- [ ] Tall sets In, Short sets Out, Scroll press creates/reveals the working-range Subclip. Verify full,
      partial/restored range and duplicate behavior. Long presses do not repeatedly create anything.
- [ ] Top adds a displayed-timestamp marker. Side + Dial selects previous/next markers with endpoint/no-marker behavior.
- [ ] Dial press performs normal Escape: exit fullscreen first, then Back; editor/menu/dialog Escape stays local.
- [ ] Ratings 0–5 via D-pad/C1/Side+Down assign the exact rating in Player and Browser. Confirm Browser
      multi-selection, Folder/static/Smart scope, durable reopen and expected Smart membership changes.
- [ ] Scroll down/up steps reject/unflagged/picked and clamps at the ends; no cyclic wrap.

## 2. Every rotary, including Side combinations

Repeat this procedure for each row of the matrix, in every applicable context. Use ready video/audio,
still images and Grid/Details as appropriate. Record speed/transport for failures.

| Rotary | Negative / positive | Contexts / boundaries |
| --- | --- | --- |
| Knob | Counterclockwise / clockwise | Player frames; Browser single items; both ends |
| Dial | Counterclockwise / clockwise | Player captured-review media; Browser has no semantic action |
| Scroll | Down / up | Player/Browser flags; reject/pick clamp |
| Side + Knob | Counterclockwise / clockwise | Player zoom levels; Browser Grid thumbnail notches (not Details sizing) |
| Side + Dial | Counterclockwise / clockwise | Player marker traversal; Browser has no semantic action |
| Side + Scroll | Down / up | Player ±5-point volume, 0–100 clamp; no-audio source unavailable; Browser has no semantic action |

- [ ] Ten slow movements in each direction: correct action, sign and context, no unexpected second command.
- [ ] Rapid bursts and sustained rotation for at least five seconds in each direction, plus alternating
      direction bursts. Stop/release: no runaway backlog or continued navigation. Existing coalescing
      and Busy-drop policy can mean fewer completions than detents; one-for-one completion is not required.
- [ ] Boundary/no-change behavior stays truthful. Frame queue remains bounded; no stale destination opens
      after media/context changes. Perform sustained movement while switching Browser↔Player and media.
- [ ] Repeat with both supported connection modes if both are used. Record lower-level haptic setting
      and direction feel; no claim of action-triggered haptic feedback.
- [ ] Inspect whether speed/haptic settings affect another preset and whether export/import retains them.
      Record the observed scope rather than assuming preferences are stored inside the export.

## 3. Combination suppression and presentation

- [ ] Side alone sends no key/action; release alone does nothing. All mapped buttons have Standard mode;
      UP/REP/AB are off. No inherited double-clicks, macros or extra chords remain.
- [ ] Hold Side first and test each Side combination. No unmodified base action fires before/after the
      combination (especially Side+Down must set **2 only**, Side+Scroll press must mute without creating
      a Subclip, Side+Top must toggle Right Panel without adding a marker, Side+Tour must not Open media).
- [ ] Repeat the same combinations with release order reversed; then fast transitions between modified
      and unmodified rotations. No latched Ctrl/Shift/Alt, Escape, base command or unexpected action.
- [ ] Side+Knob press restores Fit/pan; Side+Tall enters/exits fullscreen without reopening the source;
      Side+Short toggles existing full/range/Subclip loop behavior; Side+C1 toggles filmstrip as allowed.
- [ ] Side+Top toggles the contextual Right Panel without committing/discarding drafts or grabbing focus.
- [ ] Side+Scroll changes volume and mute through existing audio ownership; changing volume does not
      implicitly unmute. Verify zoom bounds on still/video/retained frames and thumbnail six-level bounds.
- [ ] Same Knob and Side+Knob emissions choose Browser versus Player meaning, never both. Home flags,
      ratings and Right Panel remain unambiguous. Test on the owner's actual keyboard layout/top-row digits.

## 4. Local focus ownership

For Inspector text/multiline editing, Subclip rename, search, editable/noneditable dropdowns, sliders,
lists/tree navigation, deliberate keyboard-focused Buttons, menus and a modal dialog:

- [ ] TourBox input follows the same local ownership as the equivalent physical keyboard shortcut.
      Text keys may type locally; they must not also assign ratings, create a Subclip/marker or set a range.
- [ ] Navigation keys adjust the focused local control; a rotary does not override caret/dropdown/slider
      ownership to navigate media or mutate Catalog state.
- [ ] Enter/Space/Escape retain activation, commit/cancel and modal behavior. No global hook takes over.
- [ ] Complete/cancel incidental edits and return to review: configured shortcuts resume according to
      existing Lightflow focus behavior. Console association alone does not bypass local ownership.

## 5. Conditional C2 Compare Original probe

Use active Color with a visually obvious LUT. Compare first with physical keyboard C. Then assign
plain C to C2, Standard mode, no UP/REP/AB/macro. This must be a genuine down→Begin / up→End lifetime.
Do not use AB two-command switching or a toggle to obtain a superficially similar result.

- [ ] Press: Original appears immediately. Hold for at least five seconds: Original stays steady.
- [ ] Release: current Color returns immediately; no flicker, toggle, repeat-driven Begin or stuck bypass.
- [ ] Repeated separate presses behave consistently; no second unrelated Begin/End binding is involved.
- [ ] While held, deactivate/reactivate the window, move focus, enter a modal, replace the source, leave
      Player, and change Color assignment. Existing cancellation releases only the original session;
      returning while still held does not rearm it. Release and a fresh press work afterward.
- [ ] Disconnect/reconnect the controller and restart Console while held. No stuck original view or
      modifier remains. Lightflow cannot detect vendor hardware disconnect through a keyboard-only path:
      if Console loses key-up and no normal cancellation occurs, this preset cannot promise safe hold.
- [ ] If any case fails or key lifetime is unclear, clear C2 and mark Compare Original **unsupported in
      this Console configuration**. Keyboard C remains supported. Export must leave C2 unassigned.
      That documented limitation can accompany owner acceptance of the rest of the useful preset;
      any product behavior change needs explicit separate approval.

## 6. Export and lifecycle

- [ ] Side+Tour in Browser opens existing Browser Videos Export for the intended selected inputs.
- [ ] Side+Tour in Player opens existing Player Video Export with current working-range/Color/rotation
      intent. It does not accidentally choose a Subclip entry or queue a Job by itself.
- [ ] Cancel returns to the exact origin. Ordinary configuration/preflight/queue still works. No repeated
      modal after a long press, rotary movement, stale selection, scope change or source replacement.
- [ ] Restart Console, reconnect Elite Plus, restart Lightflow, and repeat auto-switch/base commands.
      Other foreground applications must not receive a stale held key or Lightflow preset by accident.
- [ ] Export the real Console preset, import a copy through Preset List import, relink and repeat core,
      all rotary, combination, C2-outcome and haptic checks. No personal paths/templates leak into it.
- [ ] Record layout changes, limitations, exact tested versions and the actual file's SHA-256. Provide
      explicit architecture/visual/hands-on acceptance; keep the Draft PR unmerged until authorized.

Existing Export bugs are not silently fixed by this issue. A wrong preset entry or runaway invocation
is a #354 failure; an unchanged underlying Export bug is recorded under its existing follow-up.
Deep vendor/transport discovery remains #346; no protocol probing is part of this checklist.
