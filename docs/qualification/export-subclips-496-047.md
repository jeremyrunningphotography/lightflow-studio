# LF-WIN-DEV-047 — Export Subclips Reliability

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
