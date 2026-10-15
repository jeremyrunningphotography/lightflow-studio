# TourBox Elite Plus Console preset (#354)

This is a **proposed Windows layout awaiting owner hardware acceptance**, built against main
`a827470ab0338790c5697d9fcfaebe9231a70670`. It uses the supported Console keyboard-preset path.
Lightflow production code, Settings UI and neutral action contracts are unchanged. #345 remains
open; #346 independently gates SDK, socket, serial, BLE and native device integration.

Start with the [complete control map and 13 additional bindings](CONTROL-MAP.md),
[capability evidence](RESEARCH.md), and [physical acceptance checklist](ACCEPTANCE.md).
The [23-descriptor / 77-variant inventory](COMMAND-INVENTORY.md) is generated directly from
the merged catalog. `elite-plus-windows.mapping.json` is our human-readable mapping specification;
**it is neither a Console preset nor a Lightflow shortcut-profile import file**.

## What can ship

The current deliverable is a documented/manual setup plus a read-only compatibility validator.
The intended distribution is that specification together with a real, owner-authored Console export
after import and hardware acceptance. No `.tb` or `.tbx` file has been fabricated or committed.
Desktop Console officially supports preset sharing/import/export and identifies `.tb` files;
its internal schema and external generation contract were not established. Author in Console and
retain its actual exported extension. See [official desktop export instructions](https://www.tourboxtech.com/en/news/import-and-export-presets-in-tourbox.html).

## Layout and workflow

The main loop is: turn Knob to select a Browser item, press Tour to open, press Knob to play/pause,
turn Knob for precise frames, press Tall/Short for In/Out, and press Scroll to create the Subclip.
Turn Dial to move through the captured Player review set. Dial press sends ordinary Escape:
leave fullscreen first, then return to Browser; it remains subject to local editor/dialog ownership.

| Control family | Reason for the assignment |
| --- | --- |
| Knob | Highest-frequency fine movement: frames in Player, single-item selection in Browser. Left/Right already have disjoint semantic contexts. |
| Dial | Larger review decisions: previous/next Player media, without a frame-step modifier. In Browser its Ctrl+Left/Right have no semantic mapping. |
| Scroll | Quick relative classification in both contexts: reject → unflagged → picked, clamped. |
| Tall / Short | A memorable In/Out pair; Scroll press creates the resulting Subclip. |
| Top | One marker at the displayed timestamp; Side + Dial moves between markers. |
| D-pad / C1 | Ratings 5 / 4 / 3 / 0 and 1; Side + D-pad Down supplies 2. Common keep/clear decisions are immediate. |
| Side | A single, otherwise unassigned combination modifier. Same-direction movement keeps its meaning: zoom/thumbnail scale on Knob, volume on Scroll, markers on Dial. |
| C2 | Compare Original **probe only**. If keyboard hold lifetime fails, leave C2 unassigned and continue using keyboard C. |
| Side + Tour | Deliberate source-video Export entry in the current Browser/Player context. It opens the existing configuration workflow; it cannot submit a Job on its own. |

There is one Console preset, no persistent manual mode, no double-click vocabulary and no macros.
Side is a physical Console combination, not a separately emitted Ctrl/Shift key. Hold Side first,
operate the other control, release the other control, then release Side. It has no standalone action.
Use Rotating Section's combinations for Knob/Scroll; use Custom Section for Side + Dial and any
other combination not listed directly. Create rotation actions separately from press actions.
These paths are documented by the [official custom-action tutorial](https://www.tourboxtech.com/en/news/how-to-create-custom-actions.html).

All command buttons use **Standard**, with UP, REP and AB disabled. Keep double-clicks and unlisted
combinations empty. This avoids standalone modifier actions and avoids repeat-driven toggles/creation.
Console REP can emit multiple separate key presses: Lightflow's suppression of OS key auto-repeat
does not promise to suppress separate completed press/release pairs. Actual emission and combination
suppression still require the physical checklist.

## Shortcut policy: defaults plus explicit additions

Use strategy C: retain accepted defaults and explicitly assign the 13 currently unassigned variants
listed in the control map. A separate store, automatic profile replacement or new Settings UI is not
needed for this scope. Settings already exposes search by command ID, conflicts, staging and Reset.

For a customized installation, the exact Console emissions are only compatible if the saved Lightflow
gestures agree. A mismatch is not fixed silently. Prefer adapting the corresponding Console shortcut
to the user's saved binding. If the user chooses this recommended layout instead, stage that one
Lightflow row explicitly; note its previous value for recovery. If a new gesture conflicts, retain the
existing assignment and choose another valid exact gesture for both sides. Reset is not permission
to discard unrelated customizations.

Three deliberate Browser/Player reuse pairs have no overlapping contexts:

- Left/Right: Browser item selection versus Player frame stepping (existing defaults).
- Ctrl+Shift+Left/Right: Browser thumbnail size versus Player zoom steps (four explicit additions).
- Ctrl+Shift+E: Browser Videos versus Player Video Export (two explicit additions).

Home ratings, flag stepping and Right Panel overlap both contexts and retain unique gestures.
The compiled validator checks the entire resulting 77-variant resolver, including unmodified defaults,
reservations, exact modifiers, typed relative arguments and the session phases. It found no conflicts.
It does not prove Console emits those events correctly.

### Read-only validation

From the repository root with .NET 8 SDK:

```powershell
dotnet run --project tools/TourBoxPresetValidation -c Release -- --self-test
dotnet run --project tools/TourBoxPresetValidation -c Release -- --profile "C:\path\to\keyboard-shortcuts.json"
```

The profile is beside the **resolved** `settings.json` for that Lightflow data root; consult the active
profile rather than assuming a global path. Never point the checker at another profile and treat its
result as proof for the running app. If no shortcut file exists, the app inherits code defaults:
the 13 additions still need explicit Settings assignments. Omitting `--profile` validates the proposal,
not a user's installation. `--profile` reports missing/customized/conflicting bindings without writing.
Exit 0 means static compatibility; exit 2 means a current-profile mismatch; exit 1 means invalid input.
Unknown/malformed/future-schema diagnostics prevent a compatibility claim.

To regenerate the two catalog-derived reference tables:

```powershell
dotnet run --project tools/TourBoxPresetValidation -c Release -- --write-guides
```

The tool only writes those documentation files when requested. It never generates a vendor preset,
applies a shortcut profile, opens Console, sends keyboard input or touches hardware.

## Owner setup, in order

1. Record Elite Plus firmware/connection, Windows build, Console version and the exact Lightflow source
   in the acceptance sheet. Stable desktop Console 5.11.3 is the research baseline; an installed version
   has not been verified. The currently listed Windows beta is not required. Back up existing Console
   presets and your shortcut file before changing your own setup.
2. Use the fresh package of the unchanged accepted Lightflow source supplied in the PR handoff, with
   its exact task-owned acceptance data root. The handoff includes a ready-to-copy startup command.
   If a newer/different build is used, record it and recheck compatibility. Do not run an experimental build against normal
   storage. Add a small Folder/static/Smart test set, video with audio and LUT, still image, saved
   working range, Subclips and markers. Save a copy of starting ratings/flags before mutation tests.
3. The task-owned acceptance profile is preconfigured with the 13 additions; inspect them in Settings
   and run the checker against that exact file. No normal/user profile is overwritten. For any other
   profile, open Settings → Keyboard Shortcuts. Search each additional command ID in CONTROL-MAP.md, choose
   Edit, record its recommended gesture, choose Use Shortcut, and finally Save Settings. Do not use
   Reset All. Verify the other 24 default bindings if your profile was customized. Run the checker
   against this saved acceptance profile; fix mismatches explicitly on the Lightflow or Console side.
4. In Console, create a **blank** preset named `Lightflow – Elite Plus – Windows v1`. Give it useful
   command labels from the map. Enter the base rotary and button mappings first, leaving C2 empty.
   Side alone stays empty. Set Standard mode and disable REP/UP/AB and all inherited double-clicks.
5. Add the Side combinations. Expand Knob/Scroll combinations; use Custom Section → Create an action
   for Side + Dial rotation and missing button pairs. Select the rotation icons, not the press icons,
   when assigning the two directions. Do not emit mouse scroll for these semantic operations.
6. Start Lightflow so it appears in Console's application list. Enable Auto Switch, select Not Linked,
   select the running **LightflowStudio.exe** instance, and confirm. Test foreground switching away
   and back. The official UI association is the supported route; executable-name-only matching and
   automatic portability between installation paths are not established. Relink after moving/installing
   or updating the executable, and after importing on another computer. See
   [official application linking](https://www.tourboxtech.com/en/news/link-and-unlink-application-in-tourbox.html).
7. Start each rotary at Console's slowest available speed and lower tactile feedback level. Verify
   direction before increasing speed. UI labels vary across documented versions (Slow/Fast or speed
   icons); record the actual chosen label. Do not equate a Console speed setting to an exact number
   of Lightflow actions per detent. Verify whether haptic/speed preferences are global or exported
   with this preset; their storage scope is not assumed. Lightflow supplies no haptic pulses.
8. Complete base controls, combination suppression, focus and rotary acceptance before trying C2.
   For the isolated Color probe only, assign normal keyboard C to C2 in Standard mode. Hold/release
   must match the existing keyboard C behavior. UP delays a command; REP repeats commands; AB sends
   two shortcut commands. None is evidence of a genuine arbitrary-C key-down/key-up lifetime. If
   it flashes/repeats/sticks, clear C2. Do not configure alternating/toggle macros or separate Begin/End keys.
9. Complete [ACCEPTANCE.md](ACCEPTANCE.md), including lifecycle/reconnect and Export cancellation.
   Export the real preset only after its configuration and C2 outcome are recorded. Use the dedicated
   Preset List import button to import a copy for round-trip testing; the per-preset Import command
   replaces the selected preset. Relink the copy, test again, and retain the known-good original.
10. Provide the Console-exported file and completed acceptance record for this Draft PR. The
    [preset destination](presets/README.md) describes provenance and distribution gates. #354 and
    #345 remain open until explicit owner acceptance and later merge authorization.

## Recovery and limitations

- Disable Console Auto Switch/unlink Lightflow or select another preset to stop the mappings.
  Lightflow's ordinary keyboard/UI workflows remain available.
- For each of the 13 added rows, More → Reset to Default returns it to unassigned. For a pre-existing
  custom assignment, restore the recorded gesture instead. Save Settings to apply. Restore a backed-up
  whole profile only while Lightflow is closed and only when intentionally restoring that entire profile.
- All input is keyboard input in the foreground application. Text, rename, sliders, dropdowns, lists,
  menus and modals retain local ownership. A button can type C/I/O/S/M or a digit into an editor;
  it must not also invoke the semantic command. This is not focus-independent hardware invocation.
- The Dial does not provide timeline scrubbing/shuttle or accelerated seeking. Frames use the existing
  bounded/coalesced queue (pending magnitude at most 20); media/marker/navigation use single-flight
  admission and may drop Busy requests. A detent is not a promise of one completed operation.
- Zoom uses existing Fit/50/100/200/400% levels; thumbnail size uses the six existing Grid levels;
  volume changes by five percentage points, clamps 0–100 and does not implicitly unmute. Unavailable
  source/audio/panel/fullscreen conditions retain their existing eligibility rules.
- Ratings operate on the existing Browser selection or current Player asset; Smart membership can
  change. Flags clamp instead of cycling. No destructive Delete mapping is included.
- Speed presets, Browser row/page/range extension, Color labels, explicit panel selection and the
  three Subclip Export variants stay unmapped. They are lower-value additions for this first review
  layout, not missing semantic actions. Export source-video on a guarded combination is enough to
  prove the existing handoff; Subclip Export remains available through its ordinary UI. No Export fixes.
- The proposed C2 hold, combination suppression, exact modifier emission, rotary speed/feel,
  reconnect and application association remain untested until the owner completes the sheet.

## macOS expectations

Console is available on macOS, and Elite Plus supports its desktop preset/combination tools. The action
IDs, typed arguments, relative bounds, session lifetime and semantic context pairs can be reused.
Lightflow has not shipped its native macOS keyboard adapter/default table: all 77 MacDefault slots
are unassigned. Do not copy the Windows profile and call it parity. Primary maps to Command, but
explicit Control chords do not automatically become Command chords.

Once Lightflow for macOS exists, approve a native logical-key/modifier table, check OS reservations and
local ownership, author/import and relink to the running macOS application, and repeat every physical
test. Exact Windows↔macOS file import compatibility, shortcut translation and app-link portability are
not established for this Lightflow preset. Mobile `.tbx` documentation is not desktop portability proof.
Windows acceptance need not wait for that port.

Vendor SDK/plugin support, Console socket permission/framing/lifecycle, direct transport ownership,
bidirectional state/HUD/haptics and native parity remain under independent #346.
