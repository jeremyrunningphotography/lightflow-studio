# LF-WIN-DEV-052 — #503 I01 qualification stop

2026-11-15, release/1.0. **Local implementation complete enough for focused
validation; full Release gate failed. Not an owner acceptance build.** No PR,
merge, release/tag or main forward-port. Owner disposition is required before
GPU/package qualification continues.

## Source and scope

Implementation commit: `43941ec8afb80c475b6d11b76dca8f371ce5e121`.
Implementation tree: `583c3d683c6f555191af66c770ed99657d44b917`.
Verified release baseline: `4cf5e61dbd7ada0019b23e62ff1dd9292d7651b0` (#507).
Preserved WIP: `6935a0f`; verified launcher guard: `bbcf8b0`.
Subsequent documentation commits do not change tested application/test source.

Workspace: `C:\Git\Agents\LF-WIN-RES-052-ExportSettingsPersistence`, branch
`codex/503-export-settings-i01`. Existing RES-prefixed workspace was explicitly
retained by the owner after identity revision to LF-WIN-DEV-052. No other agent
was started/contacted and no other agent workspace was used.

Versioned sparse Export-owned preferences, legacy baseline, asynchronous save,
Same as Source per-item resolution, explicit Specific Folder + ten-entry MRU,
independent custom subfolder, immediate Reset/history preservation, safe recovery,
conditional intent and current-generation asynchronous validation are implemented.
See [schema/defaults/behavior](../EXPORT_PREFERENCES.md) for the complete matrix,
transient exclusions, corruption/migration and platform contract. Both ordinary
and Subclip entry points use the same root-scoped store. Jobs persistence, Player,
Browser behavior, clipboard/OLE and production desktop detection are unchanged.

## Validation history

| Stage | Result |
| --- | --- |
| Guarded initial compilation + destination tests | 6 PASS / 0 FAIL |
| New preference/native filename group, first iteration | 57 PASS / 2 FAIL |
| Expanded model/preflight iteration | 151 PASS / 4 FAIL / 3 existing opt-in SKIP |
| Broad Export-name selection before native dependency setup | 266 PASS / 3 FAIL / 7 existing opt-in SKIP |
| Corrected focused Export group | 204 PASS / 0 FAIL / 6 existing opt-in SKIP |
| Full Windows Release at 43941ec | **2699 PASS / 2 FAIL / 8 existing opt-in SKIP**, 5m22s |
| GPU Export / installer / portable / packaged startup | Not run after full gate failed |

All tests used scripts/Test-Noninteractive.ps1 and the accepted private-desktop
guard. The historical root cause remains unconfirmed; no recurrence, marker
mismatch or recursive wrapping occurred in this continuation. Original recovery
guard 4/4 and independent release-baseline 6/6 evidence is preserved separately.

Initial failures were retained and corrected within scope: editable-template
initialization accidentally committed a contextual destination; input detection
now requires actual editor focus (Browse/MRU commit explicitly). Tests relying
on the superseded Specific Folder default now explicitly choose that mode; old
collision/race/range assertions remain intact. Source-contract tests now account
for close/save flushing and approved Same as Source wording. The broad Export
name filter also selected an unrelated rotation/export native test before pinned
FFmpeg was prepared; the full suite uses verified task-owned dependencies and
that test passes. No retries without substantive setup/code/test-contract
correction, assertion weakening or test exclusions were used.

Focused coverage includes every reusable field in a complete store round trip,
individual default removal, fresh/isolate profiles, legacy migration/reset,
reopen/restart-equivalent reload, MRU canonicalization/limit/offline retention,
Reset during rapid writes, corruption/newer schema recovery, unknown-field
preservation, external-write conflict protection, real WPF ComboBox editor input,
close flush, pending row/footer/button state, multi-folder paths, missing
destinations/LUTs/hardware, P010 validation, inactive AQ, reserved/traversal/empty
subfolders, source/duplicate/existing-output safeguards, and existing filename/
range/EOF/preflight regressions. Actual GPU/owner packaged execution is not claimed.

## Exact full-suite failures

1. `FlyleafPlaybackIntegrationTests.NonDivisorCadence_SelectsRealSourceFramesWithoutChangingClock_AndPauseRestoresInspection`:
   two of 38 observed cadence intervals were `0.11699999999999999` seconds,
   outside `[0.032, 0.101]`. Assertion at PlayerReviewBackendTests.cs:88.
   Test blob equals release: `6d9051364df9a78b8aefdf58e5ba97989059f0e8`.
2. `BrowserFolderContextSafetyTests.MenuCommandsKeepSelectionAndUseInvocationFolder(command: "Cut")`:
   `System.Runtime.InteropServices.COMException: OpenClipboard Failed`, HRESULT
   `0x800401D0 (CLIPBRD_E_CANT_OPEN)` at Clipboard.Clear / fixture line 201.
   Test blob equals release: `a0fb8a94f210d0c08f68190dbed0cf8c0da7b0cb`.

These failed cases and their production areas were not changed by this branch.
That establishes source provenance, **not baseline reproduction or harmlessness**.
The clipboard signature is consistent with #504's documented contention family;
the cause of this run's failure and the cadence failure remain unclassified.
No independent rerun/comparison, Player/clipboard workaround or waiver was made.
No Export/ActiveRange-named test failed in the full TRX. Required full gate remains
failed regardless of focused Export success. Owner section-8 stop condition applies.

## Packaging and acceptance status

No freshly packaged executable exists for this source. No installer, portable
archive or GPU result is claimed. There is **no valid I01 acceptance launch
command**; do not run an older artifact as if it represented this change.
Reserved unused roots are `implementation\data-persistent` and
`implementation\data-fresh` under the workspace. Generated test media and outputs
are task-owned; no production Catalog was accessed. A future actual handoff must
include a freshly packaged binary, exact persistent/fresh-root commands and the
18-item owner checklist included in the evidence ZIP.

Historical milestone documentation is deferred until qualified acceptance; no
completed product milestone is claimed at this failed gate. Issue #503 remains
open/In Progress, not Review/Done. #491 remains incomplete; #504 stays separate.
No parent, priority or dependencies were invented. The ZIP contains raw TRXs/logs,
source/patch provenance, schema/defaults, acceptance checklist and manifest/hashes.

Owner disposition requested: classify or explicitly disposition the cadence and
clipboard full-suite failures before resuming GPU/package qualification. Do not
weaken assertions, bypass isolation or relabel the failed full run as passed.
