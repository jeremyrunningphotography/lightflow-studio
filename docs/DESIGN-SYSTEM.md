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

One right-panel icon in Home (also Ctrl+I) opens and closes the shared Right Panel, with checked-state feedback,
a keyboard-reachable splitter, persisted width, and the shell's complete rounded border. The host offers retained
surfaces, beginning with Inspector. The filename leads directly into cached Preview and normalized metadata; raw
provider browsing and redundant Browser/Player/available labels are omitted. Ordinary hydration stays quiet; only
materially slow retrieval and persistent pending/offline/error states need text. Catalog organization is separately
labeled, and multi-selection uses common/mixed/missing values with partial aggregate coverage. A folder icon beside
Relative path opens the containing folder in Explorer. Cached posters stay visible during Player context and follow
preferred-frame regeneration. Open in Player uses the existing central presentation. Global Jobs is an independently owned peer tab,
and Subclips is a contextual peer tab for Catalog-backed Player videos. Its existing cards and compact actions fill
the shared width without an inner drawer border or redundant title. Switching surfaces and closing/reopening retain
current-video selection; asset changes clear it. Saved Subclips and creation reveal the Subclips tab automatically.
Outside applicable Player context the tab is hidden, with Inspector as the fallback; the preferred tab is remembered.

The permanent shell treats Browser/Player as Home. It has no permanent module strip or peer capability rail. Focused actions and owned modals configure work from media context; the bottom status affordance enters secondary full Jobs; the shared Right Panel Jobs tab exposes compact global activity; and a restrained upper-right gear menu exposes only application utilities such as Settings and About. Back actions restore the already-live Home content. Catalog, Preview, discovery, playback, and capability services remain behind Lightflow-owned contracts.

Settings uses a compact left category list and one scrollable contextual page rather than a permanent workspace rail
or one long stack of unrelated cards. General, Color, Export, Storage, and Tools are stable conceptual homes; future
preferences should extend the closest category before adding another. Category rows use the shared dark
selection/focus language and remain ordinary keyboard-reachable list items. Preference pages use a short title and
description, aligned path fields with trailing Browse/Change actions, restrained inset panels for related operations,
and progressive disclosure for uncommon technical or recovery controls. Destructive maintenance uses explicit danger
styling and confirmation. The Save/Restore footer remains visible while page content scrolls, and unavailable paths
are stated in text rather than inferred from color or truncation.

Settings control geometry is one scoped family layered on the existing application templates: text/path fields and
combos use the dark Canvas input surface, Divider border, primary text, and warm focus border; read-only locations use
the quieter Surface/Muted pairing. Inputs, combos, and secondary buttons share a 34-pixel height and six-to-seven-pixel
corner vocabulary, including compact numeric inputs such as Preview quota. Checkboxes retain the Lightflow square
dark indicator with explicit checked, hover, disabled, and two-pixel keyboard-focus states. Browse, Change, Add, and
ordinary maintenance actions use one restrained raised secondary button family. Refresh-like actions may be quiet;
rebuild actions use a warm outline without implying danger; destructive Clear/Restore actions use `DangerButton`-based
chrome and confirmation. Nested Settings groups prefer dividers and whitespace over stacking multiple bordered cards,
and Media Root availability is inline supporting text rather than a competing status badge. Every category uses the
same stretching, maximum-width content strategy so switching pages does not change the utility's visual geometry.

Subclips is a contextual peer of Inspector in the shared Right Panel. Player supplies one retained presentation control, while the shell owns generic tab availability and shared width/open state. Cards retain quiet posters, readable semantic names, exact range/duration text, desktop multi-selection, double-click quick review, and keyboard-reachable rename/delete actions; missing posters retain a deterministic media glyph. Catalog ordering is In timestamp ascending, then stable SubclipId.

Global Jobs is a presentation peer of Inspector/Subclips in the shared Right Panel. It keeps compact `DrawerCard`
rows and existing status, disclosure, lifecycle, and concurrency controls; no nested drawer border or redundant
Jobs title is retained. Queue controls occupy two rows to fit the shared minimum width, with Show all Jobs below
the virtualized list. The host owns the only toggle, border, width, and resize boundary.

The Browser owns a resizable filesystem-oriented Locations panel and uses the remaining width for the current folder. Its 280-pixel initial width can be adjusted between sensible bounds through an invisible eight-pixel boundary whose resize cursor provides the interaction feedback; the width remains in place for the current window session. Deep hierarchies scroll horizontally instead of colliding with disclosure, icon, or scrollbar chrome. Familiar drives and mapped/removable storage are primary entry points; managed Media Roots appear as pinned libraries rather than setup prerequisites. The left pane is the single owner of folder hierarchy and selection. Its compact Back/Forward/Up/Refresh toolbar and editable path field remain synchronized with that hierarchy. The center is reserved for files/media in the selected folder and does not repeat child folders. Online state is reinforced with text as well as color; unavailable storage remains visible so the workspace can explain what happened. Loading and empty/error states occupy the media canvas without replacing navigation context.

Folder hierarchy rows use a compact 28-pixel interaction target. Disclosure, icon, and label occupy stable columns with eight pixels between icon and label; nested depth is expressed only by container indentation, so expanding a node never shifts its icon or label. Labels use the semantic light-neutral navigation text brush to reduce glare across large trees, while selected rows return to primary text. Storage-source status remains inline and source labels use restrained weight rather than a taller two-line treatment.

Issue #108 turns the center into a virtualized media Preview grid. Lightflow's Browser is a media browser, not a general-purpose file browser: the grid presents only supported still image, RAW image, and video assets. Folders, standalone audio, and unknown/unsupported files (documents, archives, executables, sidecar files) are excluded from the canvas entirely rather than occupying a tile of their own, so density and attention are never spent on filesystem noise the workspace cannot act on. Tiles are compact (168px wide), evenly spaced, and reflow with the available width rather than scrolling horizontally. A tile without a generated Preview yet shows a calm, muted category glyph rather than a spinner or other decorative placeholder, keeping the grid quiet while large folders finish generating. Selection uses the shared selection/focus surface and border treatment rather than an unrelated accent, and remains restrained enough that many selected tiles in view do not read as visual noise. Player/Viewer, Inspector/Color, and Browser-to-capability handoff arrive in later Browser slices without changing the shell or Browser ownership of the left edge. Issue #161 supersedes #138's original aggregate diamond with three global presentations that follow the user across folders: Preview is pure media, with no durable icons, frame, persistent filename, or reserved filename footer; Lightroom-like Info uses a quiet upper frame, inset image, active state icons below, and visible filename; media-large Hybrid uses the same icons in a compact upper-left overlay and retains the visible filename. The outer tile tooltip and automation label identify the asset in every mode. The Lightflow-owned bracket (In/Out), stacked range bars (saved Subclips), and segmented wheel (Color Applied) are shared vector templates, distinct by shape rather than color, legible at Small, and retain their semantic colors across selected and unselected tile surfaces. Info and Hybrid use the same spacing policy—compact at Small, with restrained additional breathing room at larger sizes—and inactive icons collapse without reserving gaps. Tooltips and automation names carry concise state meaning without adding explanatory state text.

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
The bottom status action and compact Show all Jobs button open the full Jobs destination. The shared panel
Jobs tab is always available in Browser/Player. One checked right-panel toggle opens and closes the entire host;
there is no Jobs-specific pull, caret/count gutter, or duplicate close button.

When the shared Right Panel reduces Browser width, Browser remains contained rather than clipped at the panel boundary.
The Locations preference is temporarily constrained only when necessary, navigation/address keeps group integrity,
refinement moves through deliberate grouped rows, and selection Color/Export actions adapt independently. Removing
space must never let a child minimum arrange Browser content beneath the panel; Player and Grid use the same bounded
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

## Grid / Details Browser presentation (#227)

The far-left refinement toolbar starts with graphical Grid and Details buttons using the shared dark segmented-control style, selected tint/underline, tooltips, and automation labels. Grid retains Preview/Info/Hybrid tile composition. Details uses 64-DIP rows with 54-DIP cached Previews and the same restrained selection colors. Rows remain left-aligned with the shared column headers while columns resize; generated trailing header space uses the same dark header chrome.

Details starts with Preview, Name, Rating, Flag, Capture Date, Media Type, Dimensions, Duration, Frame Rate, and File Size. Optional columns are available from the column-header context menu, with a checkmark indicating each currently visible column. Headers support drag reordering, resizing, and sorting where the shared Browser sort model supports the field; ascending/descending indicators reflect that same sort. The configuration survives relaunch through workspace state. No row-density settings page or metadata-card presentation is introduced. Jeremy accepted this presentation hands-on before final regression validation.
