# Keyboard Shortcuts hands-on acceptance (#353)

Use the fresh task package and isolated acceptance profile:

```powershell
& "C:\Users\jerem\Documents\Codex\2026-10-03\name\work\lightflow-studio\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Users\jerem\Documents\Codex\2026-10-03\name\work\lightflow-studio\artifacts\shortcut-acceptance"
```

Add disposable media through Browser Locations for Player/Browser checks. The profile is
independent of ordinary Lightflow storage. Do not merge or clean up before owner acceptance.

1. Before editing, verify Space, arrows, I/O/S/M, Ctrl+Left/Right, Alt+Left/Right, C hold,
   Browser navigation/Shift extension, Enter, 0–5, Ctrl+Up/Down and Ctrl+I.
2. Open Settings → Keyboard Shortcuts. Search “Play / Pause”, Edit, press P, Use Shortcut.
   Before Save, verify the old shortcut still works. Save Settings; P now works and Space
   no longer does. Restart and verify the binding remains.
3. Rebind Next Frame to N and Set In to B; save and verify exact behavior on ready video.
   Held N follows bounded frame stepping. Held B cannot repeatedly write the boundary.
4. Rebind Set Rating 5 to R or Step Flag Up to G; save and verify Browser selected assets
   and the Player current asset, retaining existing classification semantics.
5. Unassign a default, save and verify it stops firing. Reset that row, save and verify
   the default returns. Reset All Shortcuts, save, restart and verify all defaults return.
6. Try assigning Play/Pause to Right: clear feedback must name Next Frame. Try Ctrl+I:
   it conflicts with the overlapping Home panel command. Ctrl+Shift+I is distinct.
7. Try Alt+F4, Windows-key chords, Delete or Ctrl+Alt+letter: the gesture cannot be committed.
   Avoid pressing OS chords on the owner's main desktop if the OS may intercept them;
   Escape cancels capture even when the OS cannot deliver the attempted chord.
8. Rebind Compare Original (hold) to Shift+B after clearing/remapping Set In. Verify press
   bypasses, release restores current Color, releasing Shift before B still ends the hold,
   and repeat never toggles or rearms after cancellation. Test window deactivation, local
   editor focus, modal entry and source replacement. No persistent Color intent changes.
9. Assign Play/Pause to Enter (Browser Open remains Enter). Verify each presentation resolves
   its own command, while deliberate keyboard Button activation and editor Enter stay local.
10. Verify text/multiline/Inspector fields, Subclip rename, editable/open ComboBoxes, sliders,
    lists/trees and menus own their keys. Check #343 Collection/Smart Delete semantics and
    #344 folder-tree arrows while Player is shown; Browser tree arrows remain local.
11. Capture with keyboard only: Edit, modifier-only presses, Escape cancel, Tab cancel,
    valid candidate, Tab to confirmation and Enter/Space activation. Capturing a gesture
    must not trigger the previous shortcut or automatically activate Use Shortcut.
12. Change Settings category/destination or deactivate during capture; capture ends. Changes
    remain staged until Save; navigation/capture must not create keyboard-shortcuts.json.
13. Search by label/category/variant ID/context. Review category groups and current/default/
    customized/unassigned state. Reset-one must explain a collision with another customization.
14. Check the existing centered rail/card layout at minimum and wide window sizes and the
    owner's DPI. Verify dark theme, readable text, focus cues, scrolling and fixed Save footer.
15. Verify newly assignable presentation/Export commands retain their accepted action services
    and local/modal ownership. No new default shortcut, TourBox preset or transport is implied.

Automated coverage includes pure defaults/configuration/persistence/conflicts; remapped
Player/native-surface and Browser/shell dispatch; Color lifecycle/no-rearm/recovery; Settings
staging/capture/cancel/reset/search; actual Settings markup containment at narrow/wide sizes
and 100/150/200% DPI; and the accepted semantic/local-input regression suites. Exact counts,
package freshness and final-head CI are recorded in the PR handoff after verification.

Contract and default inventory: [KEYBOARD_SHORTCUTS.md](../KEYBOARD_SHORTCUTS.md).
