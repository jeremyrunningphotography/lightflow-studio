# Export filename builder — #500 / LF-WIN-RES-051

## I01 scope and baseline

Owner-authorized local implementation, target `release/1.0`, baseline
`293cd4b9ec43c0a7207d6cb271b08131c5d85f98`. PR #501 and #502 normal merges
were verified, including both merge parents. Original research ZIP/source are
preserved in the task workspace; this implementation does not use the historical
research base as its current foundation.

The dialog now opts its existing model into 200 ms debounced background planning.
Each worker owns a detached snapshot of naming, destination, codec/container,
LUT policies, selection and preflight facts. All mutable collections/arrays are
copied on the dispatcher; publication stays on the dispatcher. A per-dialog gate
bounds planning concurrency to one worker. Cancelling old generations stops delay
and waiting work; generation checks reject stale results even when an OS disk
operation cannot be interrupted. Closing invalidates pending/final submission.

Every current edit immediately invalidates the authoritative plan and disables
Export. Lightweight updates retain existing review rows with checking indicators,
so typing does not regenerate all WPF rows. The representative preview updates
immediately; current per-file names, classifications and eligibility publish only
after full validation. Final submission uses a detached current snapshot, freshly
validates source identities, resource snapshots and output safety off-thread,
and checks revision/preflight/planning generations before returning a queueable
plan. Duplicate submission is guarded. Existing execution protections remain.

Shared LUT validation is deduplicated only within one plan. Nothing is cached
across edits, final submission or execution. No change to range resolution,
encoder execution, codec/container selection, token semantics, selected-source or
duplicate-output protection. Merged #496/#497 behavior remains the foundation.

The single-line Custom text editor retains its existing width, height, padding,
dark brushes and focus behavior. Horizontal scrollbar is Hidden, preserving
caret scrolling; vertical scrollbar is Disabled and text remains NoWrap.
The stale immutable-chip equality guard is replaced by current model equality;
chip drag gestures exclude text/child-button interaction.

## Validation

Focused automation includes native text commands, caret/selection/scrolling,
slow filesystem and LUT gates, rapid edits/debounce, stale completion publication,
settings/token/destination/LUT changes, mixed saved/active/full-source inputs and
selection filtering, final-submission races, close and error recovery. Existing
Export/preflight/collision/LUT/range suites are also required. Source-contract and
live overwrite tests now assert Hidden single-line scrolling and pending-then-
validated readiness, retaining all existing warning/button/icon assertions.

Native measurements use WPF Text/SelectedText, routed editing commands and
dispatcher idle. Clipboard transport, physical input-to-photon latency and manual
owner acceptance are distinct gates; no synthetic PASS claim covers them.
All application/test data and outputs remain task-owned; automated GUI work uses
the required private-desktop launcher, never the input desktop.

Qualification details, exact source/package hashes, test results, before/after
CSV observations, launch commands and the numbered owner checklist belong in the
portable I01 evidence ZIP. Initial failures are preserved: test namespace import;
legacy Auto-scrollbar and immediate-overwrite expectations; unprepared task-local
playback dependencies. No assertion was dropped to obtain a pass.

## Owner gate

I01 is not owner accepted, PR-ready, merged or released. Provide the freshly built
`artifacts\release\LightflowStudio\LightflowStudio.exe` and ready-to-copy isolated
startup command in the evidence report. Do not open a PR before hands-on acceptance.
Historical maintenance: **Not warranted** — bounded defect correction below the
milestone threshold. No Chronicle/History update, release/tag or main forward-port.

I01 local qualification: focused Export/native/planning tests 160 PASS / 4 existing
opt-in SKIP; full Windows Release 2,584 PASS / 0 FAIL / 8 existing opt-in SKIP.
Premiere companion 90/90 PASS. Before/after timing evidence is retained in the
I01 ZIP. Packaged media/installer gates and owner acceptance remain separate.
