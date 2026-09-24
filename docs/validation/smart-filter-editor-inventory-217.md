# Smart filter editor inventory and authoring decisions

The field inventory was completed against BrowserFilterPredicate.Matches, BrowserFilterDescriptors, Browser toolbar handlers, MediaFrameRate.Canonical and Player/Browser state labels before editing controls.

| Field | Category / operator | Cardinality and alternatives | Value origin / editor | Existing Browser behavior |
|---|---|---|---|---|
| Media Type | finite enum; is / is any of | one or more OR alternatives | supported presentable categories; checklist | independent media buttons OR |
| Camera | observed metadata; is / is any of | one or more OR alternatives | known Source/saved camera values; checklist; unavailable as new row when empty | contextual observed facet |
| Lens | observed metadata; is / is any of | one or more OR alternatives | known Source/saved lens values; checklist; unavailable as new row when empty | contextual observed facet |
| Capture Date | inclusive date range; in range / in any range | one or more ranges OR | user-entered date pairs; either endpoint optional | added ranges OR within CaptureDate |
| Duration | numeric time minimum; is at least | one or more thresholds OR | user-entered nonnegative time; structured time input | NumberValue means seconds >= threshold; toolbar offers contextual 10/30/60/300 second minima |
| Resolution | structured exact dimensions; is / is any of | one or more width/height pairs OR | positive integer pixels, optionally observed suggestions | exact PixelWidth AND PixelHeight per alternative |
| Frame Rate | normalized numeric metadata; is / is any of | one or more rates OR | existing MediaFrameRate.Canonical + observed values + positive custom numeric fps | canonicalize then exact equality, same normalization for values and assets |
| Color | boolean/state; is applied / is original | true, false, or explicit either | globally known state selector; no third control | HasColorState, requires hydrated authored state |
| Camera LUT | boolean/state; is assigned / is not assigned | true, false, or explicit either | globally known state selector | HasCameraLut; Browser currently offers assigned |
| Creative LUT | boolean/state; is assigned / is not assigned | true, false, or explicit either | globally known state selector | HasCreativeLut; Browser currently offers assigned |
| In/Out Range (previously Saved Range) | boolean/state; is set / is not set | true, false, or explicit either | globally known state selector; no third control | HasReviewRange is saved In/Out state, consistent with Browser/Player terminology |
| Subclips | boolean/state; exist / do not exist | true, false, or explicit either | globally known state selector | HasSubclips |
| Rating | numeric ordinal; is / is at least / is at most / is greater than / is less than | one operator and 0–5 threshold | known rating choices | Browser replaces its one active rating predicate |
| Flag | finite enum; is / is any of | one or more OR alternatives | Picked, Unflagged, Rejected; checklist | classification enum facet |
| Color Label | finite enum; is / is any of | one or more OR alternatives | AssetColorLabel enum; checklist | classification enum facet |
| Keywords | observed authored vocabulary; is / is any of | one or more OR alternatives | known Source/saved keywords; checklist; unavailable as new row when empty | exact case-insensitive keyword membership, alternatives OR |
| File or path | arbitrary text; contains | one string | ordinary styled TextBox and aligned placeholder | filename OR relative path substring; ordinary saved row |

No arbitrary resolution preset list is introduced: output height presets preserve aspect ratio and are not exact width/height classifications. Structured dimensions can author 3840 × 2160 without any current matching asset. Frame-rate suggestions use the existing rational canonical table (23.976, 24, 25, 29.97, 30, 50, 59.94, 60), not a new Smart-only rate vocabulary.

Duration comparison/range extension is outside this iteration: existing evaluator ignores Comparison for Duration and only implements >= NumberValue. The editor exposes precisely that operation and does not suggest unsupported operators.

Camera/Lens/Keywords are conditionally excluded from new-row authoring when there are no known choices; saved selections remain editable. No other field is removed. Fixed composition and ordinary Browser behavior remain unchanged.
