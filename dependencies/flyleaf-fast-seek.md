# Local accurate-seek refinement for #111

`3.11.2-lightflow.4` applies `flyleaf-fast-seek.patch` to the published base
commit pinned in `flyleaf.json`. The new package and patch are repository-local
pending hands-on acceptance; the base commit alone does not contain this change.
`Build-FlyleafPackage.ps1` applies the patch and builds the manifest version.

The opt-in `SeekAccurateFromKeyframe` retains Flyleaf's accurate decoded-frame
selection and native seek serialization. It first asks the demuxer to seek
backward at the requested timestamp, then decodes to the target using the
existing half-frame tolerance. If no decoded frame is available or it overshoots
the target by more than that tolerance, it retries the established 3000ms
preroll before presenting. The existing `SeekAccurate` entry point is unchanged.

Evidence from the September 10 recording and Activity Log: native seek/cadence
work took 876–1068ms during playback and about 400ms paused; stopping took
0–70ms. The prior timestamp-publication change did not remove this native cost.

No tests were run for this refinement, per the rapid-iteration instruction.
Hands-on latency improvement and fallback behavior remain to be accepted.
Final regression coverage must include long-GOP media, VFR/B-frames, near-start
and end seeks, backward-seek fallback, rapid cancellation, cadence, and audio sync.
