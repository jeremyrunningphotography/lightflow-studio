# LF-WIN-DEV-047 — Export Subclips Reliability

## I03 preflight UX and performance

I02 owner acceptance remains pending; no PR is authorized. I03 adds explicit
Pending/Checking/Ready/Warning/Error row states, a single authoritative footer /
Export eligibility calculation, per-file completion and generation guards.
Source facts are independent of export settings; later probe results cannot
overwrite codec/container choices or reintroduce excluded fallback rows. Final
acceptance checks source identity and rejects a settings/selection change during
asynchronous materialization. Source identity is no longer reread on every UI
refresh. Progress callbacks do not rebuild the filename composer.

The existing JobsRadialProgress component is used in the existing status column,
with Compact Jobs' 21-pixel dimensions and the existing 1.2-second Jobs activity
rotation pattern. Ready uses its completed state; terminal errors retain the
existing red issue button. No extra status column or new spinner geometry/colors.
Live private-desktop tests cover animation, UI heartbeat, partial/all completion,
errors, changing settings during a pending result and screenshots.

Verified exclusive EOF is normal Ready: its technical reconciliation is absent
from banners/tooltips/file warnings. Complete requested times, stream time base,
frame duration/end, tail and tolerance remain in preflight/execution diagnostics
and actual-source regression evidence. Meaningful interior corrections remain
allowed, visible warnings. The I02 range resolver and approved tolerance are
unchanged.

Measurements on the two original DJI sources found I02's wide decoded windows
dominated (roughly 15.5 and 18 seconds); metadata took tens of milliseconds after
initial access and two identity reads about 1 ms. I03 probes actual EOF from a
targeted seek, verifies its terminal frame/end using the unchanged resolver, and
independently decodes the saved In neighborhood when it is not already in that
decoded segment. Insufficient evidence falls back to the original wide decoded
probe. Both boundaries retain decoded proof. Source work is bounded to two
concurrent pipelines, hardware probes overlap, and file/process work stays off
the UI thread. No persistent/speculative cache or new dependency.

Three measured I02 runs: 35.071 / 34.092 / 34.092 seconds for both sources.
Final decoded-both-boundaries runs: 2.271 / 2.122 / 2.119 seconds. These are local
measurements, not a latency guarantee. The faster packet-In experiment is retained
as research evidence but is not the final implementation. Per-dialog verified
facts are reused across settings changes; source/range snapshots and generations
govern validity, with fresh identity checks at submission.

Focused qualification: 168 PASS. Full Release, refreshed package, original-source
exports and final provenance are recorded in the complete I03 evidence ZIP. Any
unrelated baseline-native failure must be retained without claiming clean full
qualification. Historical maintenance remains not warranted for this bounded fix.

## Earlier iteration records

## I02 correction — owner-approved EOF policy

I01 owner acceptance is **NEEDS WORK**. The owner approved the separate exclusive
source-EOF rule in #496 on 2026-11-12. This section supersedes I01's inferred
terminal-cadence behavior and the earlier Same as Source exclusion.

Actual read-only DJI probes show HEVC/hvc1, 3840×2160 10-bit, 60000/1001 fps,
video time base 1/60000, MP4, two AAC tracks and a later MJPEG stream. Saved Outs
8.256000 / 15.786667 seconds equal source/container/audio EOF. Last video starts
8.224883333 / 15.765750 and ends 8.241566667 / 15.782433333. Their video-to-EOF tails
14.433333 / 4.233667 ms fit the actual 16.683333 ms local duration. Comparing EOF
to frame start instead rejected both at more than one frame interval.

I02 requires decoded terminal frame duration, stream time base, current source and
video durations, and stable source identity. It checks terminal end against the
video stream end and the saved EOF against current source EOF, with stream-tick
precision. Safe EOF retains its original exclusive endpoint, final video and audio;
unsafe/stale/ambiguous evidence fails. Interior inclusive Out-frame rules remain.
Verified EOF MP4/MOV uses microsecond movie-timescale precision so edit-list
rounding does not lengthen the reported audio endpoint by a fraction of a
millisecond. Original-source tests check audio to one sample plus 2 microseconds.

Same as Source's initial model had null metadata and immediately materialized
`unknown` compatibility errors. The view delayed metadata refresh until range
probing ended. Source probes also swallowed failures. I02 separates pending,
failed and authoritative metadata, publishes completion before range probing,
retains process diagnostics, drains both pipes and bounds probe runtime. Current
HEVC/MP4 metadata is obtained from the media, not guessed from file extensions or
GPU availability. Global option validation now uses each materialized source codec
so supported Same as Source HEVC/P010 is not rejected against a H.264 placeholder.
Actual H.264/P010 and unsupported source combinations remain blocked.

The shared ExportSourcePreflight is exercised by the real dialog and focused
regressions. Opt-in original-source acceptance tests exercise the styled WPF dialog,
materialize a Same as Source plan, run the production executor and compare source /
output decoded PTS/counts, HEVC/MP4 identity and both AAC endpoints. Real media and
the private source-path manifest are excluded from portable evidence.

Retain I01's correct independent NVENC execution-probe diagnostics. No hardware
workaround, dependency, production Catalog change, release, main forward-port or PR.
New private-desktop validation/package results and exact source provenance are in
the I02 implementation evidence ZIP. Owner hands-on acceptance remains outstanding.

## I01 implementation record (not owner accepted)

Issue #496; tracking #491; target `release/1.0`. Owner hands-on acceptance is
required before opening the single implementation PR. No merge, tag, release or
main forward-port is authorized.

## Root cause and correction

`SubclipCreationEligibility.Materialize` turns an absent working Out into source
duration. `CatalogSubclips` stores that value as OutTicks, and capability handoff
restores the fixed range unchanged. Source duration is an exclusive EOF endpoint,
not necessarily a decoded frame start. The original `EncodingRangeResolver`
rejected differences over 2 ms. Separately, the outer preflight catch in
`ExportDialog` replaced a successful independent NVENC capability probe with an
unavailable result when range resolution threw. This confirms the historical
045 chain directly in current source; it does not establish that every reported
NVENC failure has that cause.

The existing exact/2 ms rounding path remains. Additional Out correction requires
decoded frames (packet PTS cannot authorize it), a unique nearest earlier frame,
and a difference within the adjacent local interval capped at 50 ms. At EOF the
preceding observed interval supplies the bound and the requested endpoint must
equal source duration. Insufficient evidence, ties, later-frame candidates and
out-of-bound differences fail with requested/nearest times and recovery guidance.
In-point validation stays strict. Saved Subclip values are never rewritten.

Out remains inclusive: its actual next frame timestamp is the exclusive FFmpeg
trim end; EOF uses source duration. No later frame is selected as a correction.
The successor lookup no longer skips real frames closer than 2 ms. Endpoints are
capped at source duration. `ResolvedMediaRange.BoundaryDiagnostic` records the
requested/resolved timestamp and local cadence, appears in the existing dialog
warning surface, survives job serialization and is emitted to execution diagnostics.

Generic preflight failures now have their own blocking issue and preserve the
encoder execution-probe result. Actual NVENC initialization failures still report
unavailable. No hardware workaround, fallback codec or dependency change is added.
Range-probe stderr is drained concurrently and retained on failure.

## Validation and scope

Use `scripts/Test-Noninteractive.ps1` for all Release tests and the prescribed
`Build-Release.ps1 -Mode PullRequest -SkipInstaller` for packaging. Private desktops
must remain hidden. All data and outputs belong to the independent 047 clone.

Focused unit/model/materialization plus opt-in real NVENC tests: **86 PASS**.
Real exports cover CFR, nonzero stream start and a VFR input; output decoded
timestamps/counts are compared to the selected source frames. Evidence is retained
under task-owned `artifacts/export-047`. Enable with
`LIGHTFLOW_EXPORT_NVENC_ACCEPTANCE=1`; optionally point
`LIGHTFLOW_EXPORT_TEST_TOOLS` at this clone's packaged `ffmpeg` directory. GPU-free
CI skips this opt-in hardware test, retaining the ordinary boundary/model tests.

Initial full validation had absent fresh-clone media dependencies and was aborted
after its native testhost stalled; the attempted overlapping focused build also
failed on that testhost's locked DLL. Both attempts are preserved, not PASS.
Initial 64x64 hardware fixtures produced a real NVENC minimum-dimension failure;
256x256 fixtures pass without production encoder changes. Later full-suite,
package and packaged-tools results belong in the complete acceptance ZIP report.

No UI layout/resources are changed; the existing styled warning/error presentation
is reused. No original private media or production Catalog is accessed. The exact
owner's failing source is not available; representative synthetic fixtures do not
substitute for owner acceptance. General output cadence, codec discovery, unsaved
ranges, filename editing and Player behavior are outside this correction.

Roadmap: #496 added to Project 4, In Progress / P1 / Export & Jobs. Native parent
and blockers were absent and remain absent; #491 stays open. No Epic was invented.
Historical maintenance: **Not warranted** for this bounded reliability fix.
