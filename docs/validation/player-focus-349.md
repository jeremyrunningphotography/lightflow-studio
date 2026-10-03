# Player / Inspector focus revision (#349, Draft PR #355)

Owner hands-on acceptance of the first #345 action slice failed. The RCA below was performed against unchanged
head `0b2905a105ce6ac7696754c66def5573fb7c2c80` before production changes. The durable correction was recorded in
[#349](https://github.com/jeremyrunningphotography/lightflow-studio/issues/349#issuecomment-5972862115) and
[#345](https://github.com/jeremyrunningphotography/lightflow-studio/issues/345#issuecomment-5972862285).

## Verified causes

| Interaction | Actual unchanged-build keyboard focus | Route / cause |
| --- | --- | --- |
| Zoom click, open/close unchanged, select/close | ZoomChoice ComboBox; popup sequence can retain ComboBoxItem | MainWindow/Player preview receives Space/Arrows, blanket ownership rejects them; semantic play/pause is eligible. Active open ComboBox navigation is legitimate. Completed mouse interaction retains focus. |
| Inspector typing | TextBox | Editor ownership correctly rejects Space/Arrows/I/O/S/M/C. |
| Inspector successful Apply | ScrollViewer inside Inspector after disabled Apply Button loses focus | MainWindow blanket Right Panel gate blocks all six tested review keys, even though Player is eligible and draft is cleared. |
| Set In / Set Out mouse class-focus equivalent | Corresponding Button | Space rejected by Button ownership and normal WPF Space invokes that Button. Verified Set In causes an additional range write, with no playback toggle. |
| S creates/reveals Subclip from focused Set In, no popup history | SetInButton remains focused | Creation does not assign Set In focus; it fails to end the earlier incidental focus. Subsequent Space is Button activation. |
| Other transport/action Buttons | Corresponding Button | Same retention and Space ownership mechanism. Arrows remain review input on ordinary Buttons. |
| I/O/S | TextBox in editing; nonediting Inspector chrome after Apply | Legacy switches are not semantic eligibility failures. Text editing correctly owns letters; Inspector shell gate incorrectly blocks completed interaction. |

These are deterministic WPF diagnostics, not a claim of new physical desktop reproduction or owner acceptance.
They use real focus/dropdown state, routed Click, normal WPF Space down/up and MainWindow preview routes with a
fake playback backend. Evidence/source/TRX and the explicit focus audit are preserved in `artifacts/349/rca`.

The focus audit found Player focus on Open, Subclip rename commit/cancel, fullscreen entry/exit and Visual Index
seek completion; editor focus on rename initiation; surface focus on native/retained video interaction; shell
focus on Right Panel toggle and Browser tree/grid/search navigation. No explicit Inspector Apply or Subclip
creation assignment to Set In exists. Visual Index has a seek-completion focus return, not a separate close
restoration. No relevant FocusManager restoration was found. Primary causes are inappropriate focus retention,
blanket key ownership and shell routing, not Player semantic eligibility.

## Correction and review evidence

See [the shared policy](../PLAYER_ACTIONS.md#focus-and-local-keyboard-ownership). Mouse-completed Button/dropdown
interactions return to review; Tab/assistive keyboard focus preserves local activation/navigation. Per-key
ownership admits nonconflicting Player shortcuts from ordinary chrome. Successful same-context Inspector Apply
returns focus only when still owned by Inspector; pending/failure/cancellation/replacement retain their guards.
Legacy I/O/S return to review without migrating or redesigning range semantics.

New deterministic tests cover Zoom close with and without selection (waiting for actual popup-fade completion),
Space and both frame directions afterward, actual Tab traversal and WPF Button Space activation, closed/open
ComboBox keyboard operation, Set In/Out and other transport mouse completion, Subclip creation/reveal, Inspector
typing/success/failure/cancel/moved focus/replaced context, and I/O/S saved ranges/Subclip arguments after these
interactions, folder-tree focus and fullscreen. Existing suites retain rename, #344/Browser-only tree navigation,
Ctrl/Alt traversal, Color lifecycle, semantic transport/keyboard/controller equivalence and bounded repeat checks.

Final validation and the exact new-head package/CI record are supplied in the Draft PR handoff. Artifacts from this
revision live in `artifacts/349`; the established owner acceptance data root remains `artifacts/345/acceptance-data`.
The independent near-source-start backward presentation observation remains outside this work.
Architecture and packaged hands-on acceptance are still required. Do not merge Draft PR #355.
