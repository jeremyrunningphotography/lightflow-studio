# Local accurate-seek refinement for #111

`3.11.2-lightflow.4` applies `flyleaf-fast-seek.patch` to the published base
commit pinned in `flyleaf.json`. The package and patch are repository-local;
the base commit alone does not contain this change. Jeremy accepted the resulting
functionality hands-on on September 10, 2026.
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

Final acceptance rebuilt the package from public source plus this patch and
matched SHA-256 `d0eb69e7c6edc3abe4d5027c5e94342ceb683b142a10d32a11cf8c2d497d3ca1`.
Real-engine tests compare decoded seek PTS against FFprobe for long-GOP B-frame
and VFR sources, including near-start/end, backward and rapidly replaced seeks.
Existing cadence, audio restart, frame-step and resource-release regressions run
in the affected/full suites. Tests do not force every demuxer-specific fallback
condition or assert a hardware-dependent seek latency. Measured UI improvement
is covered by Jeremy's hands-on acceptance.
