# LF-WIN-DEV-052 — I01 GPU qualification stop

2026-11-15. Owner approved a bounded local exception for the prior full Release
result, **2699 PASS / 2 FAIL / 8 SKIP**, solely to prepare I01 acceptance. Those
failures remain failed evidence; Player cadence cause is unconfirmed, and the
Browser Cut clipboard signature matches #504's category without a proven cause.
No full-suite rerun or unrelated investigation was performed. Required future CI
is not waived. Repository policy does not prohibit this explicit owner disposition.

## Preserved source

GPU-tested source HEAD: `55e37c20cddb316249fe08e9659b0b35022b50cc`.
Production implementation: `43941ec8afb80c475b6d11b76dca8f371ce5e121`.
Production subtree at both: `8dfa4fd8fe70fec9bc40240cd657c8abf3d8fa44`.
No application/test/script diff from 43941ec to this stage. Guard bbcf8b0 remains
present; production desktop detection remains unchanged and historical trigger
is not claimed resolved. Current origin/release/1.0 is still #507 / 4cf5e61.
Workspace/branch remain the existing independent 052 clone and
codex/503-export-settings-i01. No production correction was made in this stage.

## Established GPU run

```powershell
$env:LIGHTFLOW_EXPORT_NVENC_ACCEPTANCE = '1'
$env:LIGHTFLOW_EXPORT_TEST_TOOLS = 'C:\Git\Agents\LF-WIN-RES-052-ExportSettingsPersistence\artifacts\ffmpeg\bin'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -Filter 'FullyQualifiedName~ActiveRangeExportIntegrationTests|FullyQualifiedName~ExportBoundaryIntegrationTests|FullyQualifiedName~ExportOverwriteIntegrationTests' -ResultsName 052-gpu.trx
```

**11 PASS / 1 FAIL / 0 SKIP**, 10 seconds. One guarded private-desktop run;
no recursion/mismatch. Original TRX and complete output log are preserved.
No test/production change, retry, exclusion, assertion weakening or waiver.

Coverage actually executed:

- ActiveRangeExportIntegrationTests: both marks, In only, Out only, no marks,
  saved Subclip, ordinary EOF, offset-EOF refusal, and VFR active range cases.
- ExportBoundaryIntegrationTests: three real NVENC saved-EOF decoded-frame cases.
- ExportOverwriteIntegrationTests: actual queue skip/replacement/source safety.

These use generated task-owned media and pinned FFmpeg. This run is not a claim
of all codec/container/LUT combinations or packaged-executable validation. The
tools are the pinned task tools, not a packaged copy, because packaging has not run.

## Exact failure and fixture conflict

`ActiveRangeExportIntegrationTests.Packaged_tools_export_active_ranges_with_expected_frames_and_audio(mode: "eof", vfr: False, offset: 5)`
failed at line 99: `Assert.False(Directory.Exists(model.Destination))`; actual
was true. The preceding negative-case checks passed: CanExport false, Export
button disabled, and `export.range-unresolved` reporting Source EOF could not
be verified. The generated source identity check also passed.

**I introduced a test fixture conflict during the earlier #503 mode adaptation.**
Line 62 now calls `Directory.CreateDirectory(root/output)` before opening Export.
That directly contradicts the later assertion that this same directory does not
exist. Read-only inspection confirms it exists and contains **zero output files**.
No successful export of this rejected case is established; the failed assertion
does not establish a production safety defect. It is still a failed required
GPU test and is not relabeled PASS.

The owner explicitly required stopping if any GPU check fails. Therefore no
test correction or rerun was performed and no packaging was started. Suggested
bounded next correction, requiring owner disposition under that stop rule: use
the existing task root as Specific Folder and a separate `output` subfolder;
assert the resolved output leaf remains absent for the refused case. This keeps
the base destination valid and preserves the no-output safety assertion without
precreating its target. Keep all range/frame/audio/source checks unchanged, then
rerun only the established GPU group. No production correction is indicated by
this fixture conflict alone.

## Remaining gates

Installer/portable packaging and isolated packaged Export/restart smoke are not
run. There is no refreshed acceptance executable or valid I01 launch command.
Inno Setup compiler was found at the standard per-user installation path, but
no packaging result is claimed. Existing acceptance data-root directories remain
reserved/unused, and no production Catalog was accessed.

Original focused 204 PASS and full local exceptions remain preserved in the
earlier full-validation ZIP. No large historical package is duplicated. The GPU
stop ZIP contains this report, authorization, raw results/log, fixture source/diff,
source-preservation identities, output-directory observation, cleanup and hashes.
No PR, merge, release/tag, main forward-port or additional agent. Owner hands-on
acceptance remains outstanding.
