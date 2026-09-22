# Visual Index #293 review validation

The authoritative hands-on revision adds assigned Camera → Creative Color, Browser preparation Jobs for all
12/24/48 sets, shared foreground-priority frame demand, generation progress and contextual Right Panel surfaces.
Both entry points use the same planner, frame/cache service and renderer. The application owns the shared Jobs
runtime and frame service; hiding Jobs in Player does not affect work.

Automated coverage includes Color stage order/identity/stale rejection, union planning, per-video mixed outcomes,
retry/cancellation, shared in-flight promotion, bounded priority, immediate cached progress, terminal failure,
lazy visibility, stale asset/density/Color completion, exact seeking, Browser eligibility and panel transitions.
Real FFmpeg coverage retains the shared source/orientation boundary. VI template tests verify 90/270-degree pixel orientation and dimensions, live adjustment, and unchanged cached pixels through OrientedPreviewImage. No computer control is used.
Full tests and packaged smoke use a private Windows desktop that is never switched to the input desktop.

Hands-on checklist (all with the packaged executable and task-owned acceptance-data root):

1. In Browser select one video, then several: Create Visual Index should create one Job per video without opening
   Player. Mixed/non-video selections disable the action. Inspect progress, cancel one, and retry a failed Job.
2. While preparation runs, open a video in Player and view Visual Index. Foreground frames should arrive without
   waiting for the background backlog. Jobs continue; only Inspector/Subclips/Visual Index appear in Player.
3. Confirm Camera-only, Creative-only and both assigned stages affect frames. Change/remove Color with VI visible;
   old colored frames must disappear and the current presentation must replace them. No Original color label.
4. Watch `Generating x of y…`, including cached hits. Completion removes it; a missing source/LUT must end in a
   sanitized unavailable state. Use the regenerate icon beside Frames to retry after repairing the source/resource.
5. After a preparation Job completes, switch among 12/24/48: all sets should be cached. Rapidly navigate videos
   and densities while generating; no stale frames, extra tab clicks or scrolling jumps.
6. Seek with pointer and keyboard, then press Space without clicking Player; the existing Player/review context stays intact. Check short/long clips,
   narrow/default/wide panels and portrait sources. The nearest Current indication follows playback.
7. Verify lazy behavior with VI hidden and on startup. Clear Previews and reopen VI to rebuild. Retained offline
   frames remain useful. Return to Browser: Inspector and Jobs remain available with current Job results.

Current main (8f9816c, merged #287) is reconciled locally. Check Rotate Left/Right with VI visible: display should
update immediately, including portrait geometry, while Color remains applied and cached densities remain reusable.
The accepted Browser polish changes from main are included in the next local package.

Local iteration only: commit and package locally, then wait for Jeremy's hands-on feedback. The existing draft PR
is a remote checkpoint and must not be pushed/updated again until explicitly authorized. #293 remains open.
The Frames regenerate icon invalidates the current video's complete Visual Index generation across all densities
and color variants, then queues all three sets. Persistent generation identities reject stale in-flight results
without deleting pixels shared with markers. Ordinary LUT switching continues to reuse matching cached frames.
Fractional-rate sampling calculates each timestamp independently and truncates to FFmpeg microsecond precision.
Regression coverage includes final-frame extraction, generation invalidation, Space focus, and Generating job arcs.

