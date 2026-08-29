# Lightflow dark-only design system

## Shared Jobs status language

The compact drawer and full Jobs workspace share `JobsPresentation` state text and `JobsRadialProgress` semantics:
Exporting uses orange radial progress; Waiting is a neutral hollow state; Paused and Needs attention use distinct
text/icon treatments; Completed is green; Completed with warnings remains visibly distinct from Failed; Failed is
red; Cancelled is subdued; and Skipped truthfully communicates preserved output. Status is always conveyed with text
and shape/icon as well as color. The full workspace may add detail and diagnostics without changing this vocabulary.

Lightflow Studio uses one intentional dark appearance. There is no light theme, automatic system-theme variant, or theme selector. The interface is a neutral frame for photography and video rather than a decorative surface competing with the media.

## Surface hierarchy

The shell uses a small elevation vocabulary from `Themes/LightflowShell.xaml`:

- **Canvas** — the near-black media workspace background.
- **Surface** — persistent workspace headers and shell chrome.
- **Panel** — navigation, details, cards, and grouped controls.
- **Raised** — compact badges and controls that need separation from a panel.
- **Divider** — restrained boundaries; elevation should not depend on shadows.

Broad surfaces stay neutral. Orange, red, and magenta are brand accents, not background themes. Use them for primary actions, visible focus, compact status, and small identity details. Success and warning colors must be accompanied by text, iconography, shape, or another non-color cue.

Small semantic icons use resolution-independent vector geometry on a consistent design canvas, with intentional
stroke/shape weight and optical centering at their rendered size. Reuse shared geometry for the same semantic meaning
rather than accumulating subtly different font glyphs or near-duplicate paths.

## Type and spacing

Segoe UI Variable Text with Segoe UI fallback is the application typeface. Workspace titles, section titles, labels, body text, and muted supporting text form the standard hierarchy. Shell padding and panel padding are shared resources; new workspaces should reuse them before introducing additional spacing values.

Body text should remain readable against every dark surface. Muted text is for supporting information, never the only presentation of an essential state.

## Interaction and accessibility

- Keyboard focus must remain visible on every interactive control.
- The application menu, focused actions, full Jobs entry, and Back actions support normal keyboard traversal and visible focus.
- Hit targets should normally be at least the size of the existing shell navigation and standard buttons.
- Disabled, selected, warning, success, and failure states cannot be communicated by color alone.
- Layout must remain usable at the declared 1120 × 720 minimum and resize normally under standard Windows minimize, maximize, restore, and DPI behavior.
- Media/player surfaces receive visual priority over surrounding chrome.

## Workspace composition

The permanent shell treats Browser/Player as Home. It has no permanent module strip or peer capability rail. Focused actions and owned modals configure work from media context; the bottom status affordance enters secondary full Jobs; the right-edge pull independently controls the compact drawer; and a restrained upper-right gear menu exposes only application utilities such as Settings and About. Back actions restore the already-live Home content. Catalog, Preview, discovery, playback, and capability services remain behind Lightflow-owned contracts.

The Player's contextual Subclips surface is a restrained right-side drawer inside the Player host, not permanent shell navigation. Its Player-only right-edge pull is owned by the shell so it remains reachable while Jobs is open; the shell coordinates mutually exclusive drawer bodies, and the compact media-consuming Subclips column collapses completely while closed or retired. It uses the shared panel/card/focus vocabulary and establishes the compatible location for later contextual Inspector/Color work without speculatively building that architecture. Subclip cards pair a quiet poster with readable semantic name, exact range/duration text, desktop multi-selection, double-click quick review, and keyboard-focusable rename/order/delete actions; missing posters retain a deterministic media glyph rather than blocking interaction.

Right-edge drawer pulls are sibling controls in one shell-owned vertical switcher, never independently positioned overlays. The shared switcher supplies DPI-scaled layout and a consistent gap; `DrawerPullButton` owns common width, height, corner, typography, focus, and active chrome. Drawer bodies use `DrawerBody`, compact uppercase `DrawerHeaderText`, and `DrawerCard` for the same dark surface/border/padding hierarchy while retaining each drawer's own functionality and sizing behavior. Destructive header actions reuse `DangerButton`, and compact semantic actions that must remain unmistakable under font fallback use explicit vector geometry.

The Browser owns a resizable filesystem-oriented Locations panel and uses the remaining width for the current folder. Its 280-pixel initial width can be adjusted between sensible bounds through an invisible eight-pixel boundary whose resize cursor provides the interaction feedback; the width remains in place for the current window session. Deep hierarchies scroll horizontally instead of colliding with disclosure, icon, or scrollbar chrome. Familiar drives and mapped/removable storage are primary entry points; managed Media Roots appear as pinned libraries rather than setup prerequisites. The left pane is the single owner of folder hierarchy and selection. Its compact Back/Forward/Up/Refresh toolbar and editable path field remain synchronized with that hierarchy. The center is reserved for files/media in the selected folder and does not repeat child folders. Online state is reinforced with text as well as color; unavailable storage remains visible so the workspace can explain what happened. Loading and empty/error states occupy the media canvas without replacing navigation context.

Folder hierarchy rows use a compact 28-pixel interaction target. Disclosure, icon, and label occupy stable columns with eight pixels between icon and label; nested depth is expressed only by container indentation, so expanding a node never shifts its icon or label. Labels use the semantic light-neutral navigation text brush to reduce glare across large trees, while selected rows return to primary text. Storage-source status remains inline and source labels use restrained weight rather than a taller two-line treatment.

Issue #108 turns the center into a virtualized media Preview grid. Lightflow's Browser is a media browser, not a general-purpose file browser: the grid presents only supported still image, RAW image, and video assets. Folders, standalone audio, and unknown/unsupported files (documents, archives, executables, sidecar files) are excluded from the canvas entirely rather than occupying a tile of their own, so density and attention are never spent on filesystem noise the workspace cannot act on. Tiles are compact (168px wide), evenly spaced, and reflow with the available width rather than scrolling horizontally; media stays visually dominant, so a tile is mostly its Preview with a single line of restrained, trimmed filename text beneath. A tile without a generated Preview yet shows a calm, muted category glyph rather than a spinner or other decorative placeholder, keeping the grid quiet while large folders finish generating. Selection uses the shared selection/focus surface and border treatment rather than an unrelated accent, and remains restrained enough that many selected tiles in view do not read as visual noise. Player/Viewer, Inspector/Color, and Browser-to-capability handoff arrive in later Browser slices without changing the shell or Browser ownership of the left edge. Issue #161 supersedes #138's original aggregate diamond with three global presentations that follow the user across folders: clean, icon-free Preview; Lightroom-like Info with quiet upper frame, inset image, and active state icons below; and media-large Hybrid with the same icons in a compact upper-left overlay. The Lightflow-owned bracket (In/Out), stacked range bars (saved Subclips), and segmented wheel (Color Applied) are shared vector templates, distinct by shape rather than color, legible at Small, and retain their semantic colors across selected and unselected tile surfaces. Info and Hybrid use the same spacing policy—compact at Small, with restrained additional breathing room at larger sizes—and inactive icons collapse without reserving gaps. Tooltips and automation names carry concise state meaning without adding explanatory tile text.

Issue #147 consolidates navigation/location and #109 refinement controls into a compact Browse toolbar area directly above the grid, never inside the Locations sidebar. Location/scope is the permanent first row (Back/Forward/Up, current path, Go, Refresh, then Include Subfolders). The lower region treats refine/sort (All/Images/RAW/Video, search, `Filter ▾`, Sort) and Color/Export as stable logical groups: at 1120 or more device-independent pixels of Browser-center width they share one row in refine → Color → Export order; below that breakpoint the entire Color/Export group drops beneath refinement. The address field owns the flexible `*` remainder; refinement controls wrap only within their own group under extreme Jobs pressure. Every standalone refinement control shares one dark "chip" chrome (`BrowserToolbarChipStyle`/matching custom `ControlTemplate`s: a `ShellSurfaceBrush` fill, a 1px `ShellDividerBrush` border, and a 6px corner radius) instead of each falling back to its own default WPF control chrome, so the row reads as one purpose-designed toolbar rather than a mix of form controls.

A compact Color/Export action panel follows refinement on the shared lower row at wide Browser-center widths and moves
as one complete group beneath refinement at constrained widths. Camera LUT, Creative LUT, and Export remain visible
and become enabled only when the complete selection supports that operation, so selection changes never shift the
Browser layout. Corresponding selection actions appear in each tile's context menu and use Explorer-familiar
right-click selection semantics. Regenerate Previews instead sits
as a compact refresh-style icon in the Browser status/presentation area immediately left of the Preview-size controls:
it applies to the applicable selection when one exists, or to the current effective Browser scope when none does.
The bottom status bar otherwise remains limited to application health, Browser counts, Preview activity, Preview
presentation size, and the application-wide Jobs affordance. The Jobs drawer uses the same flat dark shell surfaces,
divider, text, warning, orange active, and green success vocabulary. Its reusable radial indicator fills clockwise
for real progress and pairs every color with text and a distinct hollow/check/pause/error/cancel shape. Lists retain
recycling virtualization and bound transient terminal feedback to avoid heavyweight unbounded activity controls.
The drawer's disclosure controls use the same quiet transparent-button, raised-hover, and orange focus vocabulary as
the rest of the shell rather than native WPF expander chrome. Its resize boundary remains visually empty, with only
the `SizeWE` cursor revealing the interaction. Destructive Jobs confirmations use Lightflow's dark card/window chrome
and explicit default/cancel actions instead of native message-box styling.
Jobs navigation and drawer access use distinct affordances: the bottom status action opens the full Jobs destination,
while a narrow vertical right-edge pull tab toggles the compact drawer. The tab uses subdued shell chrome when idle,
orange emphasis/count for active work, directional carets, and the standard keyboard-focus border; no duplicate close
button appears inside the drawer.

When the Jobs drawer reduces Browser width, Browser remains contained rather than clipped at the drawer boundary.
The Locations preference is temporarily constrained only when necessary, navigation/address keeps group integrity,
refinement moves through deliberate grouped rows, and selection Color/Export actions adapt independently. Removing
space must never let a child minimum arrange Browser content beneath the drawer; Player and Grid use the same bounded
media cell and resize in place.

Selection actions use compact purpose-built transparent button chrome. Camera and Creative are action-picker
`ComboBox` controls using the same
Lightflow dropdown/option templates as Player; their neutral prompts are restored after every bulk operation and
therefore never claim a single current LUT for a heterogeneous selection. Tile context menus retain conventional
submenu behavior but opt into application-scoped Lightflow `ContextMenu`, `MenuItem`, and separator templates,
including shell hover/focus accents, restrained disabled state, and explicit submenu arrows.

Issue #126 gives the Browser's status line — visible/total counts, selection count and size, and Preview-generation activity sharing one line via a middle dot separator rather than competing badges — a permanent seat in the application's single bottom status bar rather than a Browser-only card stacked above it. That bar already carried app-wide health text (e.g. "Encoding tools ready") on `ShellSurfaceBrush`/`ShellDividerBrush` (the same recessed surface/divider tone the toolbar's own chips use), spanning the full window width beneath every workspace, not just Browser; the Browser segment now docks to its trailing edge — a thin divider, the status text (trimmed with an ellipsis and capped at a maximum width so it can never crowd out what follows it), then #125's Preview-size control — and is shown only while the Browser tab is active, collapsing back to just the app-health text the rest of the time. Nothing here reintroduces a second raised panel/card: the bar keeps its existing flat, low-contrast, single-row treatment regardless of which segment is currently populated, so global health is never visually demoted and Browser context never duplicates it.

Issue #125's Preview-size control fills that trailing slot with the same restrained, media-focused language rather than a standard form-control `Slider`: a fully custom `ControlTemplate` (`BrowserThumbnailSizeSliderStyle`, matching the approach already established for `TrimEditorWindow`'s playback-timeline slider) reduces the track to a thin `ShellDividerBrush` line with one small decorative notch dot per `BrowserGridLayout.ThumbnailSizes` entry (six, after `Huge`/`Maximum` were appended for a meaningfully larger top end), and a compact round thumb (`MutedTextBrush`, brightening to `TextBrush` on hover and the same `ShellFocusBrush` accent used for tile/segment selection elsewhere while dragging) rather than a boxy native thumb. No permanent text label: a small rendering of the same grid glyph sits to the slider's left and a larger rendering of it to the right, so "denser" versus "larger" reads through iconography alone, with each control's own accessible name and tooltip carrying the same information for screen readers and mouse-hover users. The slider is deliberately small (64px wide, 20px tall) and snaps to exactly `BrowserGridLayout.ThumbnailSizes.Count` discrete positions (`IsSnapToTickEnabled`/`TickFrequency="1"`) rather than a continuous range, keeping it feeling like a lightweight, purpose-built control subordinate to the media it resizes rather than a prominent settings widget.

Both flanking glyphs are real `Button`s (one step per click, `Decrease Preview size`/`Increase Preview size`) rather than decorative `TextBlock`s, but a dedicated `BrowserThumbnailSizeStepButtonStyle` keeps them visually identical to the plain glyphs they replaced at rest — `Background="Transparent"`, no border, no padding-driven size change worth noticing. Only interaction reveals they're controls: hover fills a small rounded `Chrome` with `ShellRaisedBrush` and brightens the glyph to `TextBrush`; pressed deepens that fill to `ShellSelectionBrush`; keyboard focus draws a thin `ShellFocusBrush` border around the same `Chrome` — the identical hover/press/focus vocabulary already used for the quick-filter segments and tile selection elsewhere in the Browser, not a one-off. Disabled (at `Small` for decrease, at `Maximum` for increase) drops to 40% opacity with an arrow cursor, the same restrained disabled treatment the global `CheckBox` style already uses — understandable at a glance without drawing attention to itself.

The quick-filter row is a set of `ToggleButton`s inside one shared chip Border (`ClipToBounds` folds their square corners into the group's rounded shape), dividing with a thin right border rather than each carrying its own chrome, so they read as one segmented control — but each button is an **independent toggle**, not an exclusive pick: any combination of media types may be active at once, and "All" clears the facet entirely rather than being just another mutually-exclusive option. The unselected state is flat and dark; a checked button gets a restrained accent — a tinted selection-brush background plus a thin `ShellFocusBrush` underline, not a bright fill — matching the "dark tint + accent line" convention used for selection elsewhere in Lightflow rather than inventing a new one; with every individual type checked, all of them show this accent simultaneously (not normalized back to only "All" looking selected). `Filter ▾` sits alongside it for future fields to stack against media type, styled as its own standalone dark chip (the same selection/focus accent when its popup is open) rather than a default `ToggleButton`: it uses progressive disclosure rather than a permanent row of one-off filter controls, opening a small dark popup of checkboxes for the currently available predicates (media type today, with a muted caption noting more fields arrive over time) plus grouped placeholders for fields not yet implemented; checkbox rows use a slightly wider popup and more generous vertical rhythm than a first pass so options never read as cramped. Search carries its own magnifying-glass glyph and "Search assets…" placeholder inside its chip, both readable against the recessed surface without a separate label. Sort reads as one control — a muted "Sort" label, the field `ComboBox`, a thin divider, and the direction toggle, all inside one chip with their own chrome stripped to transparent — rather than a `ComboBox` next to a visually disconnected square button. An active *advanced* predicate (anything without its own permanent toolbar control — no field qualifies yet) appears as a compact removable chip — label plus a small "×" — in a second row that exists only then; media-type predicates never produce a chip, since the quick-filter row already shows that facet's complete state, so with only media-type filters active that row's height and visual weight are gone entirely rather than sitting empty or duplicating what the buttons already say. Chips use the same restrained selection-surface and divider-brush treatment as other quiet dark UI here, not a bright or saturated accent, keeping several active predicates from reading as noise. The remove control on a chip is a real button (keyboard-reachable and -activatable), not a bare clickable glyph. The quick buttons and `Filter ▾`'s checkboxes both write the same underlying facet and stay mutually consistent — checking "Video" via either control checks the other's corresponding control too.

The search box carries its own placeholder ("Search assets…") rather than a separate label, shown only while the box is empty, plus a "Ctrl+F" hint in its tooltip; the shortcut focuses the box directly rather than requiring a click, matching the workspace's existing preference for keyboard-reachable controls.
