# #354 Console preset preparation validation

## Source and scope

- Fresh clone/branch from verified origin/main `a827470ab0338790c5697d9fcfaebe9231a70670`.
- Root AGENTS.md, issues #354/#345/#346, accepted action/shortcut docs and current contracts inspected.
- Independent Console research and source distinctions: [capability record](../tourbox/RESEARCH.md).
- No changes to Lightflow.Actions, LightflowStudio, application tests, Companion, dependencies or UI.
- Added documentation, a mapping specification and a separate neutral developer validation tool only.
- #354 is active work; #345 and independent #346 remain open. No deeper integration implementation.

## Focused evidence

```powershell
dotnet build tools/TourBoxPresetValidation -c Release --no-restore
dotnet run --project tools/TourBoxPresetValidation -c Release -- --self-test --write-guides
```

Build: **zero warnings/errors**, including the neutral net8.0 Lightflow.Actions project reference.
Compiled inventory: **23 descriptors / 77 curated variants**.
Proposed layout: **33 physical inputs / 37 semantic bindings / 13 additional assignments**.
All proposed gestures resolve correctly in Browser/Player; all 77 effective bindings were checked
against context conflicts and Windows reservations. Directions match curated typed arguments.
The Color candidate targets the existing Begin/End/Cancel session, not a toggle or separate keys.

**12 self-tests passed:** stale ID; reserved AltGr chord; Home overlap; Player overlap; rotary toggle;
reversed rotary; non-session hold; unqualified hold; REP creation; duplicate physical input;
recommended profile compatibility; customized-frame detection with no profile mutation.

Two end-to-end read-only CLI checks additionally passed: a schema-1 defaults-only fixture returned
exit 2 with all 13 additions missing, and a future-schema fixture returned exit 2 with its diagnostic.
Both fixture SHA-256 hashes remained unchanged. The default invocation explicitly does not claim
compatibility with an uninspected user profile. Generated control/inventory tables were inspected.

The disposable acceptance profile uses the existing ShortcutProfile/KeyboardShortcutStore APIs to
seed only the 13 unassigned additions, without overwriting an existing file or adding a new store.
Its compatibility result, exact file/executable/data-root paths and fresh package qualification are
recorded in the PR handoff after the final commit. This task artifact is not a distributed profile import.

## Application qualification and remaining gate

The application source remains identical to accepted merged main. Existing #353 owner acceptance
and accepted-head CI remain applicable; no full-suite rerun is needed for this preset specification.
The required PullRequest package command supplies a fresh unchanged binary and its normal startup,
graceful shutdown, Catalog runtime/backup and dependency checks for hardware acceptance. It does
not run the full unit suite. Exact packaging evidence is supplied in the machine-specific PR handoff.

No Console file was generated/imported, keyboard/hardware event injected, vendor protocol inspected
or physical behavior marked passed. Real application association, combinations, repeats, Color hold,
haptics/speed storage, reconnect and owner export/import must pass [the acceptance sheet](../tourbox/ACCEPTANCE.md).
The Draft PR must remain unmerged, and #345 must remain open, until explicit owner acceptance and
merge authorization. Windows/macOS preset parity and deeper routes are not claimed.
