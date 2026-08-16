# Lightflow dark-only design system

Lightflow Studio uses one intentional dark appearance. There is no light theme, automatic system-theme variant, or theme selector. The interface is a neutral frame for photography and video rather than a decorative surface competing with the media.

## Surface hierarchy

The shell uses a small elevation vocabulary from `Themes/LightflowShell.xaml`:

- **Canvas** — the near-black media workspace background.
- **Surface** — persistent workspace headers and shell chrome.
- **Panel** — navigation, details, cards, and grouped controls.
- **Raised** — compact badges and controls that need separation from a panel.
- **Divider** — restrained boundaries; elevation should not depend on shadows.

Broad surfaces stay neutral. Orange, red, and magenta are brand accents, not background themes. Use them for primary actions, visible focus, compact status, and small identity details. Success and warning colors must be accompanied by text, iconography, shape, or another non-color cue.

## Type and spacing

Segoe UI Variable Text with Segoe UI fallback is the application typeface. Workspace titles, section titles, labels, body text, and muted supporting text form the standard hierarchy. Shell padding and panel padding are shared resources; new workspaces should reuse them before introducing additional spacing values.

Body text should remain readable against every dark surface. Muted text is for supporting information, never the only presentation of an essential state.

## Interaction and accessibility

- Keyboard focus must remain visible on every interactive control.
- Top-level workspace navigation supports normal keyboard traversal and keeps selection visibly distinct through both surface and font weight.
- Hit targets should normally be at least the size of the existing shell navigation and standard buttons.
- Disabled, selected, warning, success, and failure states cannot be communicated by color alone.
- Layout must remain usable at the declared 1120 × 720 minimum and resize normally under standard Windows minimize, maximize, restore, and DPI behavior.
- Media/player surfaces receive visual priority over surrounding chrome.

## Workspace composition

The permanent shell hosts one workspace at a time. Browser is the default. Encoding, Settings, and existing utilities remain peers in the same host and are reached from the compact horizontal application header. Application navigation must not occupy a permanent left rail: Browser owns that edge for Media Roots/folder navigation, and future Player/Viewer presentation needs the width for media plus Inspector. Catalog, Preview, discovery, playback, and capability services remain behind Lightflow-owned contracts.

The Browser owns a resizable filesystem-oriented Locations panel and uses the remaining width for the current folder. Its 280-pixel initial width can be adjusted between sensible bounds through an invisible eight-pixel boundary whose resize cursor provides the interaction feedback; the width remains in place for the current window session. Deep hierarchies scroll horizontally instead of colliding with disclosure, icon, or scrollbar chrome. Familiar drives and mapped/removable storage are primary entry points; managed Media Roots appear as pinned libraries rather than setup prerequisites. The left pane is the single owner of folder hierarchy and selection. Its compact Back/Forward/Up/Refresh toolbar and editable path field remain synchronized with that hierarchy. The center is reserved for files/media in the selected folder and does not repeat child folders. Online state is reinforced with text as well as color; unavailable storage remains visible so the workspace can explain what happened. Loading and empty/error states occupy the media canvas without replacing navigation context.

Folder hierarchy rows use a compact 28-pixel interaction target. Disclosure, icon, and label occupy stable columns; nested depth is expressed only by container indentation, so expanding a node never shifts its icon or label. Storage-source status remains inline and source labels use restrained weight rather than a taller two-line treatment.

Issue #107's file list is intentionally a basic media surface, not a preview grid or full Windows Shell namespace. Thumbnails, media selection, sorting/filtering/search, Player/Viewer, Inspector/Color, and Browser-to-capability handoff arrive in later Browser slices without changing the shell or Browser ownership of the left edge.
