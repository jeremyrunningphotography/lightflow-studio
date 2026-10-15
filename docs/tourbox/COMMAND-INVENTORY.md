# Merged semantic action and shortcut inventory

Baseline: `a827470ab0338790c5697d9fcfaebe9231a70670`; 23 descriptors and 77 curated variants. This generated inventory is a reference, not a request to map every command.

## Semantic descriptors

| ID | Label | Argument shape | Phases | Repeat | Execution |
| --- | --- | --- | --- | --- | --- |
| `player.play-pause` | Play / Pause | None | Invoke | Suppress | SingleFlight |
| `player.step-frame` | Step frame | FrameDirection | Invoke | BoundedRelative | Coalesced |
| `player.color-bypass` | Compare Original (hold) | None | Begin, End, Cancel | Session | Momentary |
| `player.set-boundary` | Set working range boundary | Boundary | Invoke | Suppress | SingleFlight |
| `player.traverse-review` | Traverse review set | TraversalDirection | Invoke | BoundedRelative | SingleFlight |
| `subclip.create-from-working-range` | Create Subclip from working range | None | Invoke | Suppress | SingleFlight |
| `marker.add` | Add marker | None | Invoke | Suppress | SingleFlight |
| `marker.navigate` | Navigate markers | TraversalDirection | Invoke | BoundedRelative | SingleFlight |
| `browser.navigate-selection` | Navigate Browser selection | BrowserNavigation | Invoke | BoundedRelative | SingleFlight |
| `browser.open-current` | Open Browser media | BrowserOpen | Invoke | Suppress | SingleFlight |
| `asset.set-rating` | Set Asset rating | Rating | Invoke | Suppress | Serialized |
| `asset.set-flag` | Set Asset flag | Flag | Invoke | Suppress | Serialized |
| `asset.step-flag` | Step Asset flag | TraversalDirection | Invoke | BoundedRelative | SingleFlight |
| `asset.set-color-label` | Set Asset Color label | ColorLabel | Invoke | Suppress | Serialized |
| `player.volume` | Playback volume (%) | Volume | Invoke | BoundedRelative | SingleFlight |
| `player.review-speed` | Playback speed | ReviewSpeed | Invoke | Suppress | SingleFlight |
| `viewer.zoom` | Viewer zoom / Fit | ReviewZoom | Invoke | Suppress | SingleFlight |
| `viewer.step-zoom` | Step viewer zoom | LevelDirection | Invoke | BoundedRelative | SingleFlight |
| `player.presentation-toggle` | Toggle review presentation | PresentationToggle | Invoke | Suppress | SingleFlight |
| `browser.step-thumbnail-size` | Step Browser thumbnail size | LevelDirection | Invoke | BoundedRelative | SingleFlight |
| `review.toggle-right-panel` | Toggle Right Panel | None | Invoke | Suppress | SingleFlight |
| `review.show-panel` | Show review panel | PanelSurface | Invoke | Suppress | SingleFlight |
| `export.open` | Open Export configuration | ExportEntry | Invoke | Suppress | SingleFlight |

## Bindable variants

macOS defaults remain unassigned; this Windows plan does not approve a macOS mapping table.

| Variant ID | Action ID | Context / distance | Typed arguments | Windows default | macOS default |
| --- | --- | --- | --- | --- | --- |
| `player.play-pause` | `player.play-pause` | Player / Item | `{}` | Space | Unassigned |
| `player.color-bypass` | `player.color-bypass` | Player / Item | `{}` | C | Unassigned |
| `player.set-in` | `player.set-boundary` | Player / Item | `{"Boundary":"In"}` | I | Unassigned |
| `player.set-out` | `player.set-boundary` | Player / Item | `{"Boundary":"Out"}` | O | Unassigned |
| `subclip.create` | `subclip.create-from-working-range` | Player / Item | `{}` | S | Unassigned |
| `marker.add` | `marker.add` | Player / Item | `{}` | M | Unassigned |
| `player.previous-frame` | `player.step-frame` | Player / Item | `{"Direction":-1}` | Left | Unassigned |
| `player.previous-media` | `player.traverse-review` | Player / Item | `{"Direction":"Previous"}` | Ctrl+Left | Unassigned |
| `marker.previous` | `marker.navigate` | Player / Item | `{"Direction":"Previous"}` | Alt+Left | Unassigned |
| `asset.flag-previous` | `asset.step-flag` | Home / Item | `{"Direction":"Previous"}` | Ctrl+Down | Unassigned |
| `player.volume-previous` | `player.volume` | Player / Item | `{"Mode":"Relative","Percent":-5}` | Unassigned | Unassigned |
| `viewer.zoom-previous` | `viewer.step-zoom` | Player / Item | `{"Direction":-1}` | Unassigned | Unassigned |
| `browser.thumbnails-previous` | `browser.step-thumbnail-size` | Browser / Item | `{"Direction":-1}` | Unassigned | Unassigned |
| `player.next-frame` | `player.step-frame` | Player / Item | `{"Direction":1}` | Right | Unassigned |
| `player.next-media` | `player.traverse-review` | Player / Item | `{"Direction":"Next"}` | Ctrl+Right | Unassigned |
| `marker.next` | `marker.navigate` | Player / Item | `{"Direction":"Next"}` | Alt+Right | Unassigned |
| `asset.flag-next` | `asset.step-flag` | Home / Item | `{"Direction":"Next"}` | Ctrl+Up | Unassigned |
| `player.volume-next` | `player.volume` | Player / Item | `{"Mode":"Relative","Percent":5}` | Unassigned | Unassigned |
| `viewer.zoom-next` | `viewer.step-zoom` | Player / Item | `{"Direction":1}` | Unassigned | Unassigned |
| `browser.thumbnails-next` | `browser.step-thumbnail-size` | Browser / Item | `{"Direction":1}` | Unassigned | Unassigned |
| `asset.rating-0` | `asset.set-rating` | Home / Item | `{"Rating":0,"ToggleCurrent":false}` | 0 | Unassigned |
| `asset.rating-1` | `asset.set-rating` | Home / Item | `{"Rating":1,"ToggleCurrent":false}` | 1 | Unassigned |
| `asset.rating-2` | `asset.set-rating` | Home / Item | `{"Rating":2,"ToggleCurrent":false}` | 2 | Unassigned |
| `asset.rating-3` | `asset.set-rating` | Home / Item | `{"Rating":3,"ToggleCurrent":false}` | 3 | Unassigned |
| `asset.rating-4` | `asset.set-rating` | Home / Item | `{"Rating":4,"ToggleCurrent":false}` | 4 | Unassigned |
| `asset.rating-5` | `asset.set-rating` | Home / Item | `{"Rating":5,"ToggleCurrent":false}` | 5 | Unassigned |
| `asset.flag-unflagged` | `asset.set-flag` | Browser / Item | `{"Flag":"Unflagged"}` | Unassigned | Unassigned |
| `asset.flag-picked` | `asset.set-flag` | Browser / Item | `{"Flag":"Picked"}` | Unassigned | Unassigned |
| `asset.flag-rejected` | `asset.set-flag` | Browser / Item | `{"Flag":"Rejected"}` | Unassigned | Unassigned |
| `asset.color-red` | `asset.set-color-label` | Browser / Item | `{"Label":"Red"}` | Unassigned | Unassigned |
| `asset.color-yellow` | `asset.set-color-label` | Browser / Item | `{"Label":"Yellow"}` | Unassigned | Unassigned |
| `asset.color-green` | `asset.set-color-label` | Browser / Item | `{"Label":"Green"}` | Unassigned | Unassigned |
| `asset.color-blue` | `asset.set-color-label` | Browser / Item | `{"Label":"Blue"}` | Unassigned | Unassigned |
| `asset.color-purple` | `asset.set-color-label` | Browser / Item | `{"Label":"Purple"}` | Unassigned | Unassigned |
| `asset.color-clear` | `asset.set-color-label` | Browser / Item | `{"Label":null}` | Unassigned | Unassigned |
| `browser.navigate-left` | `browser.navigate-selection` | Browser / Item | `{"Movement":"Previous","Extend":false,"Distance":1}` | Left | Unassigned |
| `browser.navigate-left-extend` | `browser.navigate-selection` | Browser / Item | `{"Movement":"Previous","Extend":true,"Distance":1}` | Shift+Left | Unassigned |
| `browser.navigate-right` | `browser.navigate-selection` | Browser / Item | `{"Movement":"Next","Extend":false,"Distance":1}` | Right | Unassigned |
| `browser.navigate-right-extend` | `browser.navigate-selection` | Browser / Item | `{"Movement":"Next","Extend":true,"Distance":1}` | Shift+Right | Unassigned |
| `browser.navigate-up` | `browser.navigate-selection` | Browser / Row | `{"Movement":"Previous","Extend":false,"Distance":1}` | Up | Unassigned |
| `browser.navigate-up-extend` | `browser.navigate-selection` | Browser / Row | `{"Movement":"Previous","Extend":true,"Distance":1}` | Shift+Up | Unassigned |
| `browser.navigate-down` | `browser.navigate-selection` | Browser / Row | `{"Movement":"Next","Extend":false,"Distance":1}` | Down | Unassigned |
| `browser.navigate-down-extend` | `browser.navigate-selection` | Browser / Row | `{"Movement":"Next","Extend":true,"Distance":1}` | Shift+Down | Unassigned |
| `browser.navigate-home` | `browser.navigate-selection` | Browser / Item | `{"Movement":"First","Extend":false,"Distance":1}` | Home | Unassigned |
| `browser.navigate-home-extend` | `browser.navigate-selection` | Browser / Item | `{"Movement":"First","Extend":true,"Distance":1}` | Shift+Home | Unassigned |
| `browser.navigate-end` | `browser.navigate-selection` | Browser / Item | `{"Movement":"Last","Extend":false,"Distance":1}` | End | Unassigned |
| `browser.navigate-end-extend` | `browser.navigate-selection` | Browser / Item | `{"Movement":"Last","Extend":true,"Distance":1}` | Shift+End | Unassigned |
| `browser.navigate-pageup` | `browser.navigate-selection` | Browser / Page | `{"Movement":"Previous","Extend":false,"Distance":1}` | PageUp | Unassigned |
| `browser.navigate-pageup-extend` | `browser.navigate-selection` | Browser / Page | `{"Movement":"Previous","Extend":true,"Distance":1}` | Shift+PageUp | Unassigned |
| `browser.navigate-pagedown` | `browser.navigate-selection` | Browser / Page | `{"Movement":"Next","Extend":false,"Distance":1}` | PageDown | Unassigned |
| `browser.navigate-pagedown-extend` | `browser.navigate-selection` | Browser / Page | `{"Movement":"Next","Extend":true,"Distance":1}` | Shift+PageDown | Unassigned |
| `browser.open` | `browser.open-current` | Browser / Item | `{}` | Enter | Unassigned |
| `review.toggle-right-panel` | `review.toggle-right-panel` | Home / Item | `{}` | Ctrl+I | Unassigned |
| `player.speed-eighth` | `player.review-speed` | Player / Item | `{"Speed":"Eighth"}` | Unassigned | Unassigned |
| `player.speed-quarter` | `player.review-speed` | Player / Item | `{"Speed":"Quarter"}` | Unassigned | Unassigned |
| `player.speed-half` | `player.review-speed` | Player / Item | `{"Speed":"Half"}` | Unassigned | Unassigned |
| `player.speed-normal` | `player.review-speed` | Player / Item | `{"Speed":"Normal"}` | Unassigned | Unassigned |
| `player.speed-double` | `player.review-speed` | Player / Item | `{"Speed":"Double"}` | Unassigned | Unassigned |
| `player.speed-quadruple` | `player.review-speed` | Player / Item | `{"Speed":"Quadruple"}` | Unassigned | Unassigned |
| `viewer.zoom-fit` | `viewer.zoom` | Player / Item | `{"Zoom":"Fit"}` | Unassigned | Unassigned |
| `viewer.zoom-half` | `viewer.zoom` | Player / Item | `{"Zoom":"Half"}` | Unassigned | Unassigned |
| `viewer.zoom-actualpixels` | `viewer.zoom` | Player / Item | `{"Zoom":"ActualPixels"}` | Unassigned | Unassigned |
| `viewer.zoom-double` | `viewer.zoom` | Player / Item | `{"Zoom":"Double"}` | Unassigned | Unassigned |
| `viewer.zoom-quadruple` | `viewer.zoom` | Player / Item | `{"Zoom":"Quadruple"}` | Unassigned | Unassigned |
| `player.toggle-mute` | `player.presentation-toggle` | Player / Item | `{"Toggle":"Mute"}` | Unassigned | Unassigned |
| `player.toggle-loop` | `player.presentation-toggle` | Player / Item | `{"Toggle":"Loop"}` | Unassigned | Unassigned |
| `player.toggle-fullscreen` | `player.presentation-toggle` | Player / Item | `{"Toggle":"Fullscreen"}` | Unassigned | Unassigned |
| `player.toggle-filmstrip` | `player.presentation-toggle` | Player / Item | `{"Toggle":"Filmstrip"}` | Unassigned | Unassigned |
| `review.panel-inspector` | `review.show-panel` | Home / Item | `{"Surface":"Inspector"}` | Unassigned | Unassigned |
| `review.panel-jobs` | `review.show-panel` | Home / Item | `{"Surface":"Jobs"}` | Unassigned | Unassigned |
| `review.panel-subclips` | `review.show-panel` | Home / Item | `{"Surface":"Subclips"}` | Unassigned | Unassigned |
| `review.panel-visualindex` | `review.show-panel` | Home / Item | `{"Surface":"VisualIndex"}` | Unassigned | Unassigned |
| `export.browservideos` | `export.open` | Browser / Item | `{"Entry":"BrowserVideos"}` | Unassigned | Unassigned |
| `export.browsersubclips` | `export.open` | Browser / Item | `{"Entry":"BrowserSubclips"}` | Unassigned | Unassigned |
| `export.playervideo` | `export.open` | Player / Item | `{"Entry":"PlayerVideo"}` | Unassigned | Unassigned |
| `export.playerselectedsubclips` | `export.open` | Player / Item | `{"Entry":"PlayerSelectedSubclips"}` | Unassigned | Unassigned |
| `export.playerallsubclips` | `export.open` | Player / Item | `{"Entry":"PlayerAllSubclips"}` | Unassigned | Unassigned |
