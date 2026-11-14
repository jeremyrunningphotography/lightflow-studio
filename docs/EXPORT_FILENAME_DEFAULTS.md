# Export filename defaults contract — #500 I02 / future #503

The owner accepted #500 I01 and I02, each 10/10 PASS with zero observations, on
2026-11-13. #503 must use this established
contract after #500 is merged; it must not redefine defaults while adding
preference persistence or Reset Defaults.

| Setting | Default / established behavior |
| --- | --- |
| Filename recipe | Original Name only, for ordinary and Subclip export entry points |
| Custom token when manually added | Actual value is the empty string |
| Custom placeholder | Custom, display only when empty and unfocused; absent on focus |
| Separator | Hyphen, unchanged; empty Custom components add no separators |
| Destination mode | Specific Folder, unchanged |
| Initial destination | Prospective handoff InputFolder, or empty if unavailable; unchanged |
| Optional subfolder | Enabled with name Exports, unchanged |
| Codec / container | Same as Source, unchanged |
| Other export defaults | Unchanged; inventory the actual model/default store in #503 |

An empty/null Custom component emits neither text nor a separator. The configured
Custom token stays in the recipe and can be edited; it is not silently removed.
A Custom-only empty recipe produces an empty stem and remains blocked by normal
Windows filename validation. Whitespace and all other token expansion rules are
unchanged. Deliberately typed `Custom` is ordinary user text, not filtered out.
The UI placeholder is not a NamePart value and must never be serialized as input.

Owner clarification: preserve the existing Original Name token meaning. For saved
Subclips it resolves to the saved Subclip name (NamingOriginalName); for ordinary
videos and no-Subclip active/full-source fallbacks it resolves to the source name.
I02 changes the initial recipe, not this accepted #496/#497 naming mapping.

All tokens remain manually available. Explicit restored recipes remain
authoritative; I02 adds no preference persistence, settings store or Reset action.
No sequence numbers are silently appended to avoid collisions. Duplicate planned
outputs remain blocking, even for repeated Subclip names; the user can add a
sequence or another token explicitly. Selected-source/partial-source protection,
existing-output skip/overwrite warnings and actual container extensions remain.
Destination resolution, directory creation and subfolder rules are unchanged.

This document supplies defaults, not #503 implementation authority. #503 still
requires its own setting inventory and owner scope review. Do not persist source
selection, active marks, Subclip ranges, transient plans or one-shot overwrite
permissions. Use the existing settings infrastructure when separately authorized.
