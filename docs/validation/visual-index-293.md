# Visual Index #293 review validation

The authoritative hands-on revision adds assigned Camera → Creative Color, Browser preparation Jobs for all
12/24/48 sets, shared foreground-priority frame demand, generation progress and contextual Right Panel surfaces.
Both entry points use the same planner, frame/cache service and renderer. The application owns the shared Jobs
runtime and frame service; hiding Jobs in Player does not affect work.

Automated coverage includes Color stage order/identity/stale rejection, union planning, per-video mixed outcomes,
retry/cancellation, shared in-flight promotion, bounded priority, immediate cached progress, terminal failure,
lazy visibility, stale asset/density/Color completion, exact seeking, Browser eligibility and panel transitions.
Real FFmpeg coverage retains the existing source/orientation boundary. No computer control is used.
Full tests and packaged smoke use a private Windows desktop that is never switched to the input desktop.

Hands-on checklist (all with the packaged executable and task-owned acceptance-data root):

1. In Browser select one video, then several: Create Visual Index should create one Job per video without opening
   Player. Mixed/non-video selections disable the action. Inspect progress, cancel one, and retry a failed Job.
2. While preparation runs, open a video in Player and view Visual Index. Foreground frames should arrive without
   waiting for the background backlog. Jobs continue; only Inspector/Subclips/Visual Index appear in Player.
3. Confirm Camera-only, Creative-only and both assigned stages affect frames. Change/remove Color with VI visible;
   old colored frames must disappear and the current presentation must replace them. No Original color label.
4. Watch `Generating x of y…`, including cached hits. Completion removes it; a missing source/LUT must end in a
   sanitized unavailable state. Reopen to retry after repairing the source/resource.
5. After a preparation Job completes, switch among 12/24/48: all sets should be cached. Rapidly navigate videos
   and densities while generating; no stale frames, extra tab clicks or scrolling jumps.
6. Seek with pointer and keyboard; the existing Player/review context stays intact. Check short/long clips,
   narrow/default/wide panels and portrait sources. The nearest Current indication follows playback.
7. Verify lazy behavior with VI hidden and on startup. Clear Previews and reopen VI to rebuild. Retained offline
   frames remain useful. Return to Browser: Inspector and Jobs remain available with current Job results.

#287 rotation and the separate polish issues remain outside this PR. Reconcile and validate combined presentation
if either lands first. Draft PR #301 remains unmerged; #293 stays open pending architecture and hands-on acceptance.
