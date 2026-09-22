# Non-destructive video rotation (#287)

Rotation is a clockwise quarter-turn adjustment **after** the source's own display orientation.
Zero means preserve source orientation. Rotate Right adds 90 degrees; Rotate Left subtracts 90 degrees;
both normalize modulo 360. Thus a source displayed at 90 degrees plus a 90-degree Lightflow adjustment
is displayed at 180 degrees. The Catalog never rewrites the source or its display matrix.

`VideoRotation` owns normalization, composition, and quarter-turn dimension swapping. Migration 18 adds
`MediaAssetVideoRotation`, keyed by `AssetId`, with constrained degrees, revision, and UTC timestamps.
No row means zero adjustment/revision zero. Returning to zero retains the row/revision so optimistic
concurrency cannot mistake an old selection for current state. `IAssetVideoRotationStore` reads coherent
snapshots and applies a complete selected batch transactionally with expected revisions. Notifications
follow commit. The store does not resolve or open sources, so offline videos remain editable.
Whole-Catalog backup/recovery and existing AssetId-preserving relocation automatically retain rotation.
The established independent-copy operation copies the adjustment into the new AssetId.

## Presentation and derived frames

Browser actions use the existing all-identified-videos selection capability: each selected video turns
relative to its own current adjustment in one transaction. Mixed media selections cannot rotate. Player
uses the same store for its current video. Both expose Rotate Left and Rotate Right in styled context menus.

FFmpeg and Flyleaf remain the source-orientation authorities inside their existing adapters. Their output
is not assumed to start at zero. The pinned Flyleaf source reads the negative FFmpeg display-matrix angle,
then composes `Config.Video.Rotation` with the source angle in its renderer. Lightflow passes only the
authored adjustment into that existing property; it does not precompose and accidentally apply source
rotation twice. Playback source information exposes the source rotation for effective layout dimensions.
Fit/pixel zoom/pan use those dimensions, including fullscreen. Rotation requests redraw the retained frame
without reopening, seeking, changing playback speed, or changing source-relative timestamps.

Flyleaf's default snapshot dimensions are unrotated visible dimensions. The adapter explicitly passes
effective dimensions when capturing, so screengrabs and retained reverse-step frames are not stretched.
An orientation change retires an old retained reverse-step bitmap and reveals the same native retained
frame at its unchanged decoded timestamp.

Reusable Preview files remain **source-oriented, without the Catalog adjustment**. This applies to Browser
automatic/preferred frames, marker thumbnails and Subclip posters. `OrientedPreviewImage` is the shared
WPF presentation boundary: it consumes stable AssetId plus the inherited rotation store, and coerces the
original bound bitmap through the authored adjustment. The original image binding remains intact, so
subsequent turns do not repeatedly rotate already-adjusted pixels. It handles revision-ordered change
notifications and rejects obsolete asynchronous asset reads. Browser grid/Details, Inspector, filmstrip,
marker cards, and Subclip cards all use this boundary. A failed Catalog read cannot display an unverified
orientation as if it were authoritative.

Rotation alone changes no cached pixels, cache identities, preferred-frame positions, Color state, marker
positions, or Subclip In/Out values. No regeneration queue or source decode is needed for existing cached
images, including offline images. Preview cleanup/rebuild follows the same existing source-oriented policy.
Future Visual Index presentation should use this same image boundary for its source-oriented derivatives;
this issue does not implement Visual Index. Native Player snapshots already contain effective orientation
and must not be passed through the Preview adjustment again.

## Export and Premiere

The existing Catalog-to-Export handoff captures a `VideoRotation` per input. Export materialization stores
it in the normal serialized settings snapshot, so output identity, Jobs, History, and Review & Rerun carry
the captured adjustment rather than querying later Catalog state. Older snapshots omit it and default to
zero. The readiness summary identifies outputs that bake Lightflow rotation.

FFmpeg autorotation applies source metadata first. The shared Encoding builder then applies the authored
transpose/flip filters after deinterlacing and before output scaling. Existing decode/process/encode remains
mandatory; no stream-copy or metadata-only orientation shortcut is introduced.

Premiere rotation projection is not implemented. Adobe's documented `FootageInterpretation` surface has no
ordinary rotation getter/setter. `ClipProjectItem.getComponentChain` exists, but that is not evidence of a
reliable, reconcilable source/master orientation property. Sequence Motion is not equivalent to source
interpretation. The existing Send dialog explicitly warns when selected assets have a Lightflow adjustment:
Premiere receives the original source orientation. No duplicate rendered source is silently created, and
the existing NLE identity, ownership, and reconciliation contracts remain unchanged. A future projection
requires empirical supported-API verification and product review under #255.

Sources inspected on 2026-09-21:

- [Pinned Flyleaf source](https://github.com/jeremyrunningphotography/Flyleaf/tree/28f5dd4b3f4c09b6de37524a2e2cd7626f6d844e): `VideoStream.cs`, `Renderer.VP.cs`, `Renderer.Snapshot.cs`.
- [FFmpeg video options](https://ffmpeg.org/ffmpeg.html#Video-Options): display rotation and default autorotation.
- [Adobe FootageInterpretation](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/footageinterpretation).
- [Adobe ClipProjectItem](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/clipprojectitem).

## Validation

`VideoRotationTests` covers normalization, composition/dimensions, JSON persistence, atomic batch conflicts,
offline writes, restart, relocation, Preview deletion, backup/restore, migration from schema 17, copy independence,
preferred-frame timestamp preservation, and Encoding filter order.

`VideoRotationPixelTests` generates asymmetric source fixtures with all four display-matrix rotations.
For each source it checks all four authored adjustments against Preview bitmap presentation, native Flyleaf
capture, and a CPU-encoded output using the production Encoding filter chain. Quadrant colors and effective
dimensions must agree; source hashes and decoded timestamps must remain unchanged. CPU encoding keeps this
contract test independent of NVIDIA hardware; production encoder selection remains in the existing pipeline.

Packaged hands-on acceptance must cover the styled Browser/Player menus, multi-selection, offline cached
images, Color, preferred frames, Subclips/markers, Fit/pixel zoom/pan, fullscreen, paused/playing rotation,
and the Premiere warning. Automated tests do not substitute for that acceptance.
