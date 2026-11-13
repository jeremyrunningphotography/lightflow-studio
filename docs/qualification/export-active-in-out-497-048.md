# Export Active In/Out — LF-WIN-DEV-048 / #497

## Accepted source and scope

Owner accepted I01 `b26812893314f9045b761a3d952049af696e5ff3` with **8/8 PASS,
zero observations**: complete marks; partial marks; no marks/persistence; per-video
batch isolation; saved-Subclip precedence; interior Out/EOF/audio/preflight;
overwrite/skip/source protection; and timeline/dark-theme UI.

Accepted application tree: `cc54c2b448849d5de93a362226320b47e3097c8d`.
Accepted test tree: `9b6d7e7fef40bc61d7ec0671ff6862961ed6477b`.
Final qualification preserves both trees and all dependency/build/workflow inputs.
Only the authorized 047/048 registry reconciliation and this record change after
acceptance. No new owner acceptance cycle or production correction is required.

Target `release/1.0` remains at `ca929e586b929d721656a5ca5c5c5fb8fff5f905`:
#496 / PR #501 merged before #497 implementation; post-merge CI 43496848475
SUCCESS, publication skipped. No intervening release changes at qualification.
The accepted source and original evidence ZIP are retained in the task workspace.

## Architecture and preserved behavior

Browser Export Subclips retains each selected asset's persisted active marks when
it has no saved Subclips; no marks means full source. Saved Subclips retain
precedence, ordering, identity and revision. In-only reaches source end; Out-only
starts at zero, following existing Player/MediaRange semantics. No global current
Player range is borrowed. Catalog restoration/storage policy is unchanged.

The capability handoff retains the per-asset snapshot and appends
`NoSubclipActiveRange` provenance without changing existing enum values. The dialog
uses fixed active ranges consistently in preflight, timeline, plan and submission.
Timeline tooltips distinguish saved Subclip, active marks and full source; naming
bases/formats are preserved. Jobs/history/rerun retain provenance. Final submission
rejects changed per-asset marks, Subclip state or source identity; materialized
invalid ranges block without silently switching to full source.

No Player stepping/backend/global keyboard routing, Browser selection, range
resolver/tolerance/EOF, metadata probing, NVENC, Jobs-style activity, FFmpeg
execution, destination or collision implementation changed. Accepted #496
inclusive interior Out and verified exclusive source EOF remain authoritative.

## Windows qualification

Independent task clone: `C:\Git\Agents\LF-WIN-DEV-048-ExportActiveInOut`.
All WPF/native tests and package checks use required private-desktop wrappers,
never switch desktops, and keep media/storage task-owned. 049/050 confirmed no
conflicting native jobs; 048 runs serially and announces resource release.
No production Catalog or another agent's workspace/process is used.

Fresh qualification on the accepted application/test source:

| Gate | Result |
| --- | --- |
| Focused range, Catalog/Player integration, Subclips, preflight, planning/history and isolated Jobs startup | 263 PASS / 0 FAIL / 1 existing opt-in timing SKIP |
| Unfiltered Windows Release | 2568 PASS / 0 FAIL / 8 opt-in SKIP, 4m51s |
| Actual packaged-tools NVENC export, frame/audio, EOF, overwrite/skip and source protection | 12 PASS / 0 FAIL / 0 SKIP |
| Required local package | PASS: startup/presentation/Jobs, graceful shutdown, Catalog runtime/backup/restore protection and dependencies |

No new qualification failure occurred. No failing test was excluded or changed.
Package-mediated export verification combines accepted owner 8/8 hands-on results,
fresh actual executable startup/Jobs smoke, and same-source live ExportDialog plus
real export execution with freshly packaged tools. Synthetic output evidence
verifies decoded frame counts/timestamps and audio endpoints. Final PR-head local
package is refreshed after the documentation commit; timestamps, executable/tool
hashes and owned-process cleanup are independently checked in the final ZIP.

Historical I01: focused 100 PASS; full 2568 PASS / 0 FAIL / 8 opt-in SKIP;
packaged-tools 12 PASS and package PASS. Those results are retained, not substituted
for the fresh qualification above. Earlier I01 full 2566 PASS / 1 Jobs startup
FAIL / 8 SKIP is preserved: UnauthorizedAccessException in unchanged
ExportQueueStore.Save during MaxSimultaneousExports startup. Isolated and later
I01 full runs passed unchanged; this was not claimed fixed or proven baseline.
Any new failure receives retained logs, bounded diagnosis and unchanged-release
comparison; unresolved #497 behavior failures block readiness.

The synthetic offset/AAC explicit-EOF fixture intentionally remains blocked when
captured video duration conflicts with container EOF evidence. Separate verified
EOF succeeds. No tolerance relaxation, test weakening or exclusions are applied.
Original private DJI inputs are not used; #496 acceptance is preserved, with fresh
synthetic frame/audio, source-safety, Same as Source and actual NVENC coverage.

## Package, PR and remaining gate

Required local package command:
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller`.
Final-head package provenance and manifests are in the final qualification ZIP.
Executable: `C:\Git\Agents\LF-WIN-DEV-048-ExportActiveInOut\artifacts\release\LightflowStudio\LightflowStudio.exe`.
Isolated persistent-root command:

```powershell
& "C:\Git\Agents\LF-WIN-DEV-048-ExportActiveInOut\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-DEV-048-ExportActiveInOut\artifacts\acceptance\persistent"
```

The single implementation PR targets `release/1.0`, closes #497 and tracks #491.
Final PR SHA, exact-head full/companion/installer/package CI and publication-skipped
results are recorded in [the issue/PR discussion](https://github.com/jeremyrunningphotography/lightflow-studio/issues/497)
and final ZIP; earlier-head CI never substitutes for final-head CI. This avoids
self-referential documentation commits that would invalidate the recorded head.
047's stale merged registry status is corrected here only by explicit owner
authorization; unrelated records remain unchanged. 048 is prepared closeout,
not merged/Done. **Explicit owner merge approval remains required.** No auto-merge,
release/tag, main forward-port or production Catalog operation. #500 / 051 stays
gated until #497 merges. Historical maintenance: **Not warranted** for this bounded
bug correction and routine qualification/registry work.
