# Console mapping validator

Small neutral .NET 8 developer tool for #354. It references `Lightflow.Actions` without modifying it.
Run from the repository root:

```powershell
dotnet run --project tools/TourBoxPresetValidation -c Release -- --self-test
dotnet run --project tools/TourBoxPresetValidation -c Release -- --profile "C:\path\to\keyboard-shortcuts.json"
dotnet run --project tools/TourBoxPresetValidation -c Release -- --write-guides
```

Default input: `docs/tourbox/elite-plus-windows.mapping.json`; `--manifest <path>` selects another
specification for validation. `--write-guides` regenerates CONTROL-MAP.md and COMMAND-INVENTORY.md
beside the chosen manifest. It never creates a Console preset or saves a Lightflow profile.

Validation checks current 23/77 inventory, real command IDs/defaults, reserved gestures, all effective
binding conflicts, Browser/Player resolution (including absent-context behavior), typed signed relative
operations, suppressed discrete buttons and a qualified Begin/End/Cancel hold probe. Twelve self-tests
reject stale IDs, AltGr/system collisions, Home/Player overlap, a rotary toggle, reversed direction,
non-session/unqualified holds, REP creation and duplicate physical inputs, then check the recommended
profile and read-only detection of an owner customization.

Exit codes: 0 valid proposal/static profile compatibility; 1 invalid input/specification;
2 saved profile differs or has unsafe/unknown-schema diagnostics. Static compatibility does not prove
Console emission, actual hold lifetime, hardware behavior, application association or haptic scope.
The [physical checklist](../../docs/tourbox/ACCEPTANCE.md) supplies those acceptance gates.
