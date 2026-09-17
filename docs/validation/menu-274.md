# Shared submenu CI isolation (#274)

Starting main: `572390c91a0daf5ccc2b264fefda0ba6e77b3926`.
The accepted #269 behavior and production implementation are unchanged.

## Evidence and root cause

The original release-main run [35274862499](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/35274862499) failed in both attempts. Current-main run [35282794767](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/35282794767) failed identically. The resize test opens the real Browser context menu at one-third of the desktop width, relocates it, and then opens/grows/shrinks Camera LUT.

Diagnostic run [35284163981](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/35284163981) captures the immediate close stack:

```
MenuItem.SetTimerToOpenHierarchy
  -> MenuItem.FocusOrSelect
  -> MenuBase.OnIsSelectedChanged
  -> tested MenuItem.IsSubmenuOpen = false
```

The hosted desktop has a 1024x720 work area, stationary cursor at (512,384), and 400ms MenuShowDelay. The initial root menu intersects that cursor, creating pending sibling-selection work. The local desktop has a 5120x1392 work area and its cursor is outside the fixture. WPF closes the previously selected submenu when that timer selects a sibling. This is legitimate menu behavior, not an error in Lightflow's right-first placement candidates.

The trace closes the submenu **before** the expanded-left assertion. Fade-out briefly leaves the child attached and measurable, so that assertion passes. The shrink assertion then reads the destroyed popup child's screen coordinates and throws. Item removal did not cause the close. Increasing the fixed delay would not establish ownership of native pointer input.

The local original scenario passes on both .NET 8.0.29 and an isolated 8.0.31 runtime matching CI; runtime patch alone is not causal. A local full baseline passed 2,030 tests with one opt-in live Premiere skip. The STA collection already serializes all dispatcher tests. Temporary diagnostic event handlers initially demonstrated another fixture hazard: delayed close events can outlive the active test; those diagnostics are removed from the final change.

Primary framework reference: [WPF MenuItem at v8.0.31](https://github.com/dotnet/wpf/blob/v8.0.31/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/MenuItem.cs), especially MouseEnterInMenuMode, SetTimerToOpenHierarchy, and FocusOrSelect. The architecture decision is recorded in #274 before implementation.

## Correction and retained coverage

Only tests change. Loaded fixture menus disable pointer hit testing because these tests programmatically drive menu opening, resize and keyboard events; they do not test pointer navigation. No cursor movement, Windows setting changes, production overrides, skips or relaxed geometry assertions are used.

The original assertions remain: shared 150-DIP minimum, content growth, all six Browser submenus, right preference, right/bottom work-area fallback, live growth and shrink, chevron direction, no horizontal clipping, nested keyboard opening, Left and Escape. Direct callback tests additionally pin right-first candidates and vertical/left alternatives across short, wide and tall content without creating a native window.

Cleanup closes each root and waits, with a bounded failure, until root and instantiated submenu visuals lose their presentation sources. This completes asynchronous native destruction before the next STA test.

## Acceptance

Use the task's packaged executable with its explicit isolated data root. Compare gear and Browser menu widths; exercise Send To, Rating, Flag, Export and LUT submenus in open space and near right/bottom screen edges. Check dynamic long LUT labels, chevron direction, nested Right/Left/Escape navigation, and normal pointer hover/dismissal. Production mouse behavior remains hands-on acceptance, separate from the pointer-isolated geometry fixture.

This task does not merge, publish a release, or move/recreate the existing v0.40.0 tag.
