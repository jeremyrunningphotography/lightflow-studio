# Configurable keyboard shortcuts (#353)

Baseline: main `bdb120f8985d27580730f29e27550ee0d200d6e1`. The merged catalog has
23 semantic descriptors across Player, Browser, Presentation and Review. This slice adds
an input configuration catalog, not another command dispatcher or business-logic registry.

## Neutral contracts and variants

`Lightflow.Actions/KeyboardShortcuts.cs` defines `KeyboardGesture(Key, Modifiers)`,
`BindableCommand`, `ShortcutProfile`, `KeyboardShortcutResolver` and the atomic store.
Key is a canonical logical identifier: A–Z, 0–9, F1–F24, Space, arrows, Home/End,
PageUp/PageDown, Enter, Escape, Delete and Tab. The last three remain reserved for local
interaction. No WPF enum, native virtual key, control, handler or delegate enters JSON.

Modifiers are None, Primary, Control, Alt, Shift and Meta. Matching is exact after platform
resolution: Primary is Control on Windows and Meta/Command on macOS. Ctrl+Shift+X differs
from Ctrl+X. Each variant has independent WindowsDefault and MacDefault slots. Only the
accepted Windows table ships; macOS mappings deliberately remain unassigned until its
native adapter approves platform conventions. Windows review traversal and flag stepping
retain explicit Control; Right Panel uses Primary. There is no automatic Ctrl-to-Command
conversion of the whole table.

Variant IDs identify fixed typed arguments without multiplying semantic actions. Examples:
`player.next-frame` → `player.step-frame` + `FrameStepArguments(1)`;
`player.set-in` → `player.set-boundary` + `SetBoundaryArguments(In)`;
`asset.rating-5` → `asset.set-rating` + `SetRatingArguments(5, false)`.
Labels/categories/repeat/phases come from accepted action descriptors. The curated catalog
covers all 23 actions; direct flag/color variants are Browser-only. Volume exposes ±5
percentage-point relative variants, zoom/thumbnail steps expose ±1, and speed/zoom/panel/
Export expose the existing accepted enums. No arbitrary argument editor or new semantics.
Row/page navigation stores typed movement/extension plus neutral distance intent. The
platform adapter supplies the bounded distance from its existing viewport geometry,
independently of whichever key the user assigns.

`Query()` returns label, category, command/context/arguments, current/default gesture,
customization and unassigned state for Settings, help/tooltips and #354 documentation.
Resolvers snapshot profiles and cache effective metadata, avoiding JSON work on key repeat.

## Accepted Windows defaults

| Context | Gesture | Variant / meaning |
| --- | --- | --- |
| Player | Space | `player.play-pause` |
| Player | Left / Right | `player.previous-frame` / `player.next-frame` |
| Player | I / O | `player.set-in` / `player.set-out` |
| Player | S / M | `subclip.create` / `marker.add` |
| Player | Ctrl+Left / Ctrl+Right | `player.previous-media` / `player.next-media` |
| Player | Alt+Left / Alt+Right | `marker.previous` / `marker.next` |
| Player | C down/up | `player.color-bypass` hold |
| Browser | Left / Right | Item previous/next |
| Browser | Up / Down | Row previous/next |
| Browser | Home / End | First/last item |
| Browser | PageUp / PageDown | Page previous/next |
| Browser | Shift + each navigation gesture above | Extend existing selection |
| Browser | Enter | `browser.open` |
| Home (Browser or Player) | 0–5 | `asset.rating-0` through `asset.rating-5` |
| Home (Browser or Player) | Ctrl+Up / Ctrl+Down | `asset.flag-next` / `asset.flag-previous` |
| Home (Browser or Player) | Ctrl+I (Primary+I) | `review.toggle-right-panel` |

Presentation, direct flags/labels, panel selection, thumbnail sizing and Export have no
new defaults. Existing intended shortcuts retain their behavior. Legacy switch fallthrough
that accidentally accepted extra modifiers is replaced with exact matching; modified I/O/S/C/
Space and modified ratings are not implicit aliases. This prevents collisions and avoids
interpreting AltGr as an application shortcut.

## Contexts, local ownership and conflicts

Browser and Player are mutually exclusive semantic presentations. Home overlaps both;
Global overlaps every presentation and is a future configuration hook, with no global
default commands in this slice. Contexts are neutral bit sets, not WPF control classes.
Conflict = equivalent resolved gesture + overlapping context. Browser Enter and a remapped
Player Enter can coexist, subject to local Button/editor ownership. Two Player commands
cannot share Enter. Home Ctrl+I overlaps Player. Ambiguous loaded profiles fail closed;
they never select an arbitrary action. Reset-one checks conflicts before staging a default.

Runtime order is local input ownership, presented semantic context, neutral lookup, then
existing action admission/target resolution. Windows translation is centralized in
`WindowsKeyboardShortcuts`; no OS hook exists. MainWindow, PlayerViewerHost and native/
retained/still Player surfaces consume the same resolver snapshot. WPF ownership remains
in `PlayerKeyboardOwnership` and the existing Browser scope/Right Panel guards. Text and
multiline editing, editable ComboBoxes, dropdowns, sliders/thumbs, list/tree navigation,
menus, deliberate Button Space/Enter, Subclip rename and Inspector transactions keep their
keys. Modal/inactive contexts reject dispatch independently of focus.

Escape/fullscreen/Back and Jobs Back, Ctrl+F search, Ctrl+A, Ctrl+X/C/V, contextual Delete
(including #343 Smart no-destructive-fallback), Tab, menu/dialog defaults and pointer
gestures remain local. Those gestures cannot be reassigned as semantic fallbacks.
Windows additionally rejects Windows-key chords, Alt+F4/Space/Tab and F10 menu activation.
Ctrl+Alt is rejected because it may be AltGr. The platform reservation function includes
a small macOS hook for Command+Q/H/Space/Tab; a native implementation must extend it.

Letters use the OS logical alphabet identity, not physical keyboard position or text
produced by Shift. Top-row digits identify logical digit keys, with modifiers still exact.
This is a deliberately limited alphabet/special-key surface, not a complete layout engine;
punctuation, numpad, modifier-only, dead-key and IME-processed events are ineligible.
No composed text is interpreted as a shortcut. Layout-specific OEM keys cannot be captured.
Numpad digits are not aliases for top-row rating shortcuts. macOS must normalize its native
logical input and preserve composition/local ownership before lookup.

## Persistence and Settings

Profile file: `keyboard-shortcuts.json`, beside the resolved `settings.json` (including
isolated `--data-root` profiles). Schema 1 stores only explicit overrides/unassignments:

```json
{
  "Version": 1,
  "Overrides": [
    {
      "CommandId": "player.set-in",
      "ActionId": "player.set-boundary",
      "Arguments": { "Boundary": "In" },
      "Gesture": { "Key": "B", "Modifiers": "Shift" }
    },
    {
      "CommandId": "player.play-pause",
      "ActionId": "player.play-pause",
      "Arguments": {},
      "Gesture": null
    }
  ]
}
```

Null is explicit unassignment. Missing entries inherit current code defaults, including
defaults of future commands. Returning to the code default removes the override. There
is no first-launch shortcut write. A temporary sibling and atomic replace/move implement
save; errors are surfaced without applying the draft. Unknown entries and extension fields
are retained. Incompatible arguments/action IDs are diagnosed and use safe defaults;
unknown future schema versions retain the file and block shortcut writes. Malformed files
recover to defaults without startup failure and retain the original until an explicit save.
Reset-all removes recognized action/variant overrides while preserving unknown entries.
No Catalog, media metadata, workspace state or Export recipe stores shortcut preferences.

Settings uses the existing Lightflow inputs/buttons, visible focus, automation labels and
fixed footer. Browser, Player, Presentation and Review / Shell sections start collapsed,
with counts and subgroup summaries; expansion choices persist for the window's Settings
session. Subgroup headings separate compact command → shortcut rows without nested scrolling.
Search covers labels, section/subgroup, variant ID, context and current/default gestures;
matching sections expand without overwriting normal expansion choices, restored on clear.
Assigned gestures use restrained key badges. Context/state is secondary, with default
gestures shown only for customized rows. Edit stays visible; the trailing More menu exposes
Unassign (when assigned) and Reset to Default (when customized), including keyboard access.
The capture panel is brought into view when Edit starts. Reset All is a quiet top action.
Edit, Unassign, Reset and Reset All stage changes. Save Settings validates the draft and
atomically saves the shortcut profile before replacing the runtime resolver. The existing
AppSettings store separately saves its owned preferences in the same explicit Save flow;
there is no multi-file transaction. Restore Defaults also stages recognized shortcut defaults.
Neither category navigation nor capture writes disk.

Edit enters explicit recording. Modifier-only/composition input is ignored, Escape cancels,
and Tab cancels recording while preserving normal focus navigation. Reserved/conflicting
gestures show a reason before commit. An eligible gesture exits recording and focuses Use
Shortcut; its key-up and auto-repeat are consumed so capture cannot activate that Button.
Use Shortcut stages the candidate; Tab/Enter/Space work normally for confirmation afterward.
Cancel, changing category/destination and deactivation end capture. Only active Settings
records; normal Lightflow dispatch is unavailable there. Changed rows return focus to search.

## Hold/repeat lifecycle and #354

`player.color-bypass` remains one session command: first down begins with a unique source/
gesture ID, matching key-up ends even after modifiers change, and existing focus/window/
modal/source/unload cancellation releases the original target. The adapter retains the
pressed key latch until release; repeat cannot rearm a cancelled session. Changing the
resolver cancels active gestures. No two unrelated Begin/End bindings or durable Color
writes are introduced. The accepted PlayerActions session owner retains disconnect rules
for future controller adapters. Classification in Player continues using its current-asset
authoritative mutation methods; it does not borrow a Browser selection target.

Relative frame/review/marker/navigation/flag/volume/zoom/thumbnail input follows descriptor
repeat policy and existing bounded/coalesced/single-flight services. Toggles, assignment,
creation and Export suppress repeat; matching repeated events are consumed by adapters.

#354 must choose defaults, a dedicated profile, or both through its own acceptance. Most
presentation/Export variants remain unassigned, so a Console preset must explicitly verify
required variant mappings, context, local focus ownership and customization before use.
It must not silently assume an arbitrary customized key still has the documented action.
Document expected gestures using Query(), validate exact conflicts/reservations, supply
press/release for Color and bounded repeated events only for relative variants. Preset IDs
and transport remain outside shared semantics. #346 still gates SDK/socket/native integration;
no such integration or TourBox preset is implemented here.
