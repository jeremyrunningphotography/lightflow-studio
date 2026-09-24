# Smart filter editor inventory — latest #217 acceptance iteration

All 18 exposed field descriptors were rechecked. One field/category remains one row; alternatives OR within it, All/Any combines rows. Aspect Ratio uses effective presentation geometry.

| Field | Operator / editor | Cardinality and origin |
|---|---|---|
| Media Type | is / is any of; dark checklist | supported categories; multiple values |
| Aspect Ratio | is / is any of; existing dark checklist | exact rational 16:9, 9:16, 4:3, 3:2, 1:1, 21:9; multiple values |
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

## Aspect Ratio — effective orientation

`DerivedVideoMetadata.SourceDisplayAspectRatio` now retains a reduced integer rational computed from source FFprobe display-matrix rotation using the existing `VideoRotation.Dimensions` primitive. The Browser composes the #287 Catalog adjustment with that source-oriented ratio. Rotation reads must finish before video membership is known; rotation changes refresh membership and revision guards reject stale reads. Catalog restore invalidates those guards and reloads current-scope adjustments. Old Preview records are projected from their persisted raw FFprobe payload through the same normalizer without accessing or reprobeing media. Encoded Resolution remains unchanged.

Still-image projection obtains dimensions from the same WIC EXIF transform used to render images, including mirrored orientations. Ratio identity uses integer GCD reduction, not formatted doubles. 21:9 equals 7:3; a marketing “21:9” 2560×1080 raster is actually 64:27 and does not match the exact 21:9 choice.

Non-square-pixel sources, conflicting DAR, and non-quarter-turn rotation remain unknown: Lightflow's current playback/query boundary does not establish a shared display-aspect contract for these cases. That portion is deferred for architecture/product clarification; the filter never substitutes encoded dimensions. Legacy metadata without either normalized geometry or raw probe data is likewise unknown. Missing/unspecified SAR uses the ordinary square-pixel default.
