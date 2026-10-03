# First Player action slice validation (#349)

This slice proves the platform-neutral Player action boundary for #345 with play/pause, bounded presented-frame
stepping and a momentary Color gesture. See [policy and the ten acceptance answers](../PLAYER_ACTIONS.md).

Automated checks must include the final Release suite, focused Player/ownership/decoded-frame/session checks,
Companion tests and the separate net8.0 contracts build. Required local packaging uses:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller
```

The packaging script validates the real SQLite runtime, isolated startup/presentation/Jobs, controlled shutdown,
FFmpeg dependency hashes/versions/licenses and portable staging. Package freshness must be checked against the
actual PR commit timestamp, with no leftover packaging smoke process.

## Hands-on review

1. Open a video, test transport and Space, including held Space (one toggle). Verify saved working/Subclip ranges,
   loop and fullscreen retain current behavior.
2. With Player active, click the already-selected displayed folder, then use Left/Right. Verify #344 remains fixed;
   genuine sliders/selectors/editors and menus retain their own input. Check Ctrl+Arrow review and Alt+Arrow markers.
3. Assign Camera/Creative LUTs, hold C and release. Repeat after source changes, switching workspace, deactivation
   and entering a modal dialog. Verify current Color returns without altering Catalog assignment or leaving bypass.
4. Verify still-image/unavailable-source contexts do not run playback actions. Inspect the small shared contract,
   Windows adapter boundary and fake-controller tests before accepting the architecture.

The independent near-source-start backward presentation observation documented with #344 is outside this slice.
No TourBox adapter, shortcut Settings editor, Browser action migration or unrelated bug fix is included.
The Draft PR must remain unmerged until Jeremy's explicit architecture and hands-on acceptance.

## Previous-head automated evidence (`0b2905a`)

- Final focused Release: 151 passed, including semantic/controller, keyboard/native repeat and ownership,
  #344 folder-tree/modifier/fullscreen, decoded-frame, modal and Color lifecycle checks.
- Final full Release: 2,562 passed, zero failed, one expected installed-Companion acceptance skip (2,563 total).
- Premiere Companion: 90 passed, zero failed.
- Separate neutral net8.0 build: zero warnings/errors; assembly-reference test rejects Windows/WPF dependencies.
- Initial diagnostic full run exposed three obsolete source-shape assertions and two Windows checks under sandbox
  restrictions. Assertions were updated to retain presentation/ownership coverage; both Windows checks passed in
  final focused and full unrestricted validation. No unrelated production fix was made.

Final TRX/logs and task-owned package verification are preserved under `artifacts/345`. The Draft PR handoff records
the exact commit, fresh executable, package checks, process verification and isolated acceptance startup command.

## Focus revision evidence

The [unchanged-build RCA and revision policy](player-focus-349.md) extend the hands-on checklist: test mouse Zoom
open/close both unchanged and selected, Inspector editing/Apply, Set In/Out, Subclip creation and ordinary Buttons.
Verify review Space/Arrows/I/O/S afterward; deliberately Tab-focused Buttons/ComboBoxes, slider navigation, menus
and editors must retain their local input. Failed/cancelled Apply and replacement editing contexts keep drafts.

- Final focused Release: 164 passed; targeted cleanup and previously affected UI/native checks: 20 passed.
- Final full Release: 2,578 passed, zero failed, one expected installed-Companion skip (2,579 total).
- Companion: 90 passed. Independent neutral net8.0 build: zero warnings/errors.
- The initial full run exposed a test-teardown error: cancelled/failed draft transitions left test MainWindows open.
  Teardown now explicitly consents to closing after assertions and verifies removal. Later UI checks and a native
  presentation-capture check pass in the targeted recheck and final full run. No unrelated production fix was made.

Revision TRX/logs and new-head package/CI verification are preserved under `artifacts/349`. The established owner
acceptance root `artifacts/345/acceptance-data` remains intact. Architecture and owner hands-on acceptance remain pending.
