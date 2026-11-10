# Elite Plus proposed Windows control map

Generated from `elite-plus-windows.mapping.json` and the compiled neutral catalog. Regenerate with
`dotnet run --project tools/TourBoxPresetValidation -c Release -- --write-guides`.

**Proposed, not physically accepted. C2 is a hold probe only; leave it unassigned in a distributed preset until it passes.**

Side alone, all double-clicks and all unlisted combinations are unassigned. Hold Side first for combinations; it emits no standalone modifier/key. All mapped buttons use Standard mode with UP/REP/AB off. Rotaries emit discrete shortcuts, not macros or mouse-wheel events.

| Physical input | Console keyboard emission | Player meaning | Browser meaning | Binding policy |
| --- | --- | --- | --- | --- |
| Knob: Counterclockwise | Left | Previous Frame (`player.previous-frame`) | Navigate: Left (`browser.navigate-left`) | Default |
| Knob: Clockwise | Right | Next Frame (`player.next-frame`) | Navigate: Right (`browser.navigate-right`) | Default |
| Dial: Counterclockwise | Ctrl+Left | Previous Media (`player.previous-media`) | No semantic mapping | Default |
| Dial: Clockwise | Ctrl+Right | Next Media (`player.next-media`) | No semantic mapping | Default |
| Scroll: Down | Ctrl+Down | Step Flag Down (`asset.flag-previous`) | Step Flag Down (`asset.flag-previous`) | Default |
| Scroll: Up | Ctrl+Up | Step Flag Up (`asset.flag-next`) | Step Flag Up (`asset.flag-next`) | Default |
| Knob: Press | Space | Play / Pause (`player.play-pause`) | No semantic mapping | Default |
| Dial: Press | Escape | Local Escape / Back; normal ownership applies | Local Escape / Back; normal ownership applies | Local |
| Scroll: Press | S | Create Subclip (`subclip.create`) | No semantic mapping | Default |
| Tall: Press | I | Set In (`player.set-in`) | No semantic mapping | Default |
| Short: Press | O | Set Out (`player.set-out`) | No semantic mapping | Default |
| Top: Press | M | Add Marker (`marker.add`) | No semantic mapping | Default |
| Tour: Press | Enter | No semantic mapping | Open Current Media (`browser.open`) | Default |
| D-pad Up: Press | 5 | Set Rating 5 (`asset.rating-5`) | Set Rating 5 (`asset.rating-5`) | Default |
| D-pad Right: Press | 4 | Set Rating 4 (`asset.rating-4`) | Set Rating 4 (`asset.rating-4`) | Default |
| D-pad Down: Press | 3 | Set Rating 3 (`asset.rating-3`) | Set Rating 3 (`asset.rating-3`) | Default |
| D-pad Left: Press | 0 | Set Rating 0 (`asset.rating-0`) | Set Rating 0 (`asset.rating-0`) | Default |
| C1: Press | 1 | Set Rating 1 (`asset.rating-1`) | Set Rating 1 (`asset.rating-1`) | Default |
| Side + D-pad Down: Press | 2 | Set Rating 2 (`asset.rating-2`) | Set Rating 2 (`asset.rating-2`) | Default |
| C2: Press / hold / release **TEST ONLY** | C | Compare Original (hold) (`player.color-bypass`) | No semantic mapping | Default |
| Side + Knob: Counterclockwise | Ctrl+Shift+Left | Zoom Out (`viewer.zoom-previous`) | Smaller Thumbnails (`browser.thumbnails-previous`) | Additional |
| Side + Knob: Clockwise | Ctrl+Shift+Right | Zoom In (`viewer.zoom-next`) | Larger Thumbnails (`browser.thumbnails-next`) | Additional |
| Side + Scroll: Down | Ctrl+Shift+Down | Lower Volume (`player.volume-previous`) | No semantic mapping | Additional |
| Side + Scroll: Up | Ctrl+Shift+Up | Raise Volume (`player.volume-next`) | No semantic mapping | Additional |
| Side + Dial: Counterclockwise | Alt+Left | Previous Marker (`marker.previous`) | No semantic mapping | Default |
| Side + Dial: Clockwise | Alt+Right | Next Marker (`marker.next`) | No semantic mapping | Default |
| Side + Knob: Press | F6 | Viewer Zoom: Fit (`viewer.zoom-fit`) | No semantic mapping | Additional |
| Side + Scroll: Press | Ctrl+Shift+M | Toggle Mute (`player.toggle-mute`) | No semantic mapping | Additional |
| Side + Tall: Press | F11 | Toggle Fullscreen (`player.toggle-fullscreen`) | No semantic mapping | Additional |
| Side + Short: Press | Ctrl+Shift+L | Toggle Loop (`player.toggle-loop`) | No semantic mapping | Additional |
| Side + Top: Press | Ctrl+I | Toggle Right Panel (`review.toggle-right-panel`) | Toggle Right Panel (`review.toggle-right-panel`) | Default |
| Side + C1: Press | Ctrl+Shift+B | Toggle Filmstrip (`player.toggle-filmstrip`) | No semantic mapping | Additional |
| Side + Tour: Press | Ctrl+Shift+E | Export: PlayerVideo (`export.playervideo`) | Export: BrowserVideos (`export.browservideos`) | Additional |

## Explicit additional Lightflow bindings

These commands are unassigned in accepted Windows defaults. Set them individually through Settings → Keyboard Shortcuts, then Save Settings. Do not replace a customized profile file. Browser/Player reuse is deliberate; Home overlaps both.

| Search command ID | Settings row | Context | Recommended gesture |
| --- | --- | --- | --- |
| `viewer.zoom-previous` | Zoom Out | Player | Ctrl+Shift+Left |
| `browser.thumbnails-previous` | Smaller Thumbnails | Browser | Ctrl+Shift+Left |
| `viewer.zoom-next` | Zoom In | Player | Ctrl+Shift+Right |
| `browser.thumbnails-next` | Larger Thumbnails | Browser | Ctrl+Shift+Right |
| `player.volume-previous` | Lower Volume | Player | Ctrl+Shift+Down |
| `player.volume-next` | Raise Volume | Player | Ctrl+Shift+Up |
| `viewer.zoom-fit` | Viewer Zoom: Fit | Player | F6 |
| `player.toggle-mute` | Toggle Mute | Player | Ctrl+Shift+M |
| `player.toggle-fullscreen` | Toggle Fullscreen | Player | F11 |
| `player.toggle-loop` | Toggle Loop | Player | Ctrl+Shift+L |
| `player.toggle-filmstrip` | Toggle Filmstrip | Player | Ctrl+Shift+B |
| `export.browservideos` | Export: BrowserVideos | Browser | Ctrl+Shift+E |
| `export.playervideo` | Export: PlayerVideo | Player | Ctrl+Shift+E |

Existing bindings used by the other rows must also be checked when the user's profile is customized. See [setup and recovery](README.md) and [physical acceptance](ACCEPTANCE.md).
