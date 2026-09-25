# README screenshot provenance

These images show the packaged application at main baseline
`09ba6b243af1a8ef063ecc108753286fabf91e96` (2026-09-25).
They are actual Windows application captures, not generated mockups.

## Media credit

*Big Buck Bunny*, © 2008 Blender Foundation / www.bigbuckbunny.org.
Licensed under [Creative Commons Attribution 3.0](https://creativecommons.org/licenses/by/3.0/).
See the [film's license information](https://peach.blender.org/about/) and
[official downloads](https://peach.blender.org/download/).
Source file: `big_buck_bunny_720p_h264.mov.zip` from Blender's official
[download directory](https://download.blender.org/peach/bigbuckbunny_movies/).

For demonstration, the film was trimmed into short, renamed H.264 clips without
audio. The demo Catalog adds illustrative ratings, labels, Collections, a Smart
Collection, descriptions, a saved range, two Subclips, and a point marker. These
annotations are staged examples, not metadata asserted about the original film.

## Capture set

| Image | Purpose |
| --- | --- |
| browser-overview.jpg | Real demo filesystem navigation: Forest Story, Camera A / Camera B, recursive browsing, and Hybrid media tiles |
| browser-collections.jpg | Collection hierarchy, Smart Collection, Hybrid tiles, Catalog indicators, and Inspector descriptions |
| player-color-subclips.jpg | Player filmstrip, saved range, point marker, Color controls, and reusable Subclips |
| player-visual-index.jpg | Twelve timestamped sample frames alongside the paused Player |
| export-workflow.jpg | Valid output plan, saved range, Color policy, confirmed NVENC availability, and enabled Export action |

The Browser, Player, and Visual Index share a 1600 × 1000 DIP window configuration
(window-only capture is 1586 × 993 pixels). Export uses its own 1106 × 893 pixel modal
capture. Images were encoded as JPEG without changing controls, text, or product pixels.
Only the returned application/modal bounds are retained; unrelated desktop content
is excluded. The retired `jobs-file-operations-detail.jpg` showed stale layout and
failed operations and has been removed.

## Isolation and privacy

All media copies, annotations, exports, settings, Catalog, and Previews belong to the
independent #309 clone and its explicit `--data-root` profile. No normal Lightflow
profile or personal media was used. Representative Catalog annotations and workspace
preferences were prepared offline in the disposable profile, then loaded and visually
checked through the production UI. The application generated its own Previews/Subclip
posters/Visual Index, and the Export example was executed successfully.

The lead Browser image shows a real task-owned `Demo Media` Location, with media copied
into `Forest Story/Camera A` and `Forest Story/Camera B`. `Nature Selects`, `Exports`,
and illustrative `Past Projects/Session 01–24` folders complete the demo tree. The tree
is scrolled to Demo Media so unrelated machine drives are outside the capture; Include
Subfolders combines the two camera folders in the media grid. The Right Panel is closed
to give the filesystem workflow prominence. These are real directories and production
navigation, not a composited tree or a change to the application.

The separate Collections screenshot retains Collection breadcrumbs and Inspector.
Locations is collapsed in that image and the Player images. The filesystem Browser and
Export images show only task-owned paths under `C:\Git\Agents\issue-309-readme-refresh`.
The retained images were
reviewed for personal information, usernames, secrets, private media, unrelated
application content, warning/loading states, and disabled primary actions.
