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

The permanent shell hosts one workspace at a time. Browser is the default. Encoding, Settings, and existing utilities remain peers in the same host. Browser owns navigation and selection presentation; Catalog, Preview, discovery, playback, and capability services remain behind Lightflow-owned contracts.

The #106 Browser canvas is deliberately skeletal. Media Roots, thumbnails, selection, sorting/filtering/search, Player/Viewer, and Browser-to-capability handoff must not be simulated in the shell foundation; later issues add real behavior to the established regions.
