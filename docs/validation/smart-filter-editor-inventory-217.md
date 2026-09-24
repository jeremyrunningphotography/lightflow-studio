# Smart filter editor inventory — latest #217 acceptance iteration

All 17 exposed field descriptors were rechecked. One field/category remains one row; alternatives OR within it, All/Any combines rows. Aspect Ratio is deliberately not exposed pending authoritative display-geometry metadata.

| Field | Operator / editor | Cardinality and origin |
|---|---|---|
| Media Type | is / is any of; dark checklist | supported categories; multiple values |
| Camera | is / is any of; checklist | observed/saved values; multiple; hidden from new authoring when vocabulary is empty |
| Lens | is / is any of; checklist | observed/saved values; multiple; hidden from new authoring when vocabulary is empty |
| Capture Date | in range / in any range; date pickers | inclusive ranges; optional endpoints; multiple alternatives |
| Duration | is at least / is at most; time entry | inclusive numeric seconds; accepts seconds, mm:ss, hh:mm:ss; each alternative has its own operator |
| Resolution | is / is any of; preset selector with Custom | numeric width/height; 1280×720, 1920×1080, 2560×1440, 3840×2160, 4096×2160 plus observed/saved sizes; Custom positive integer pixels |
| Frame Rate | is / is any of; preset selector with Custom | existing rational canonical table (23.976, 24, 25, 29.97, 30, 50, 59.94, 60), observed/saved values; Custom positive fps normalized by BrowserFrameRate |
| Color | is applied / is original | binary state; no combined either choice |
| Camera LUT | is assigned / is not assigned | binary state; no combined either choice |
| Creative LUT | is assigned / is not assigned | binary state; no combined either choice |
| In/Out Range | is set / is not set | saved primary range state; no combined either choice |
| Subclips | exist / do not exist | binary state; no combined either choice |
| Rating | is / is at least / is at most / is greater than / is less than | one supported rating comparison and 0–5 threshold |
| Flag | is / is any of; checklist | Picked, Unflagged, Rejected; multiple alternatives |
| Color Label | is / is any of; checklist | Not set plus unchanged Red, Yellow, Green, Blue, Purple; multiple alternatives |
| Keywords | is / is any of; checklist | observed/saved exact keywords; multiple; hidden from new authoring when vocabulary is empty |
| File or path | contains; ordinary styled TextBox | arbitrary literal substring |

The resolution presets are the exact common sizes expressly accepted in this latest product review, not aspect-preserving export-height presets. Both preset editors show only one value experience at a time: selector or Custom input. Their identities remain numeric dimensions/rates, never formatted strings.

Binary selectors now offer exactly two actual states. An already-captured Browser view containing both state alternatives is shown as two explicit alternatives with an `or` label and removal actions, preserving save-view fidelity without offering a combined either-state value. New state rows do not offer an add-alternative action.

Color Label unset uses `BrowserFilterPredicate.MatchUnset` and matches a hydrated asset's null ColorLabel. `Not set` is presentation only. It does not match unknown/unhydrated authored state. Existing label identities are unchanged.

Duration's shared evaluator now supports inclusive >= and <=. The default remains >= for existing documents and ordinary Browser minimum choices. No exactly or between operator is exposed. Mixed duration alternatives retain their individual comparisons; a bounded interval would require a separately designed two-bound operation rather than ORing a minimum and maximum.

Query document version 3 makes the duration comparison and semantic unset extension explicit. Versions 1/2 are read and normalized into the current fixed composition; no recursive Boolean model was added.

## Aspect Ratio — stopped on architecture gate

`DerivedVideoMetadata` persists encoded Width/Height and FrameRate but no source display rotation, sample aspect ratio or display aspect ratio. `BrowserTechnicalMetadata` likewise lacks display geometry. `VideoRotation` from #287 is the user-authored adjustment. Live playback composes stream rotation with that adjustment and uses renderer visible dimensions (`FlyleafPlaybackBackend.CaptureOrientedBitmap`). Using only saved adjustment plus encoded dimensions in the Browser query would misclassify rotated-source and potentially non-square-pixel media.

Still-image metadata retains EXIF orientation and WIC has a shared display transform, but that does not fill the missing video contract. A correct follow-up must establish authoritative normalized displayed dimensions/rational aspect (including source rotation and pixel aspect), backfill/hydrate those values into Browser query tiles, compose the existing rotation adjustment, and refresh membership on rotation changes. No encoded-dimension approximation or Aspect Ratio field was added in this iteration.
