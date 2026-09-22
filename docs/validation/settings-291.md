# Settings redesign hands-on review

Use the task-owned package and an isolated profile:

```powershell
& 'C:\Git\Agents\issue-291-settings-redesign\artifacts\release\LightflowStudio\LightflowStudio.exe' --data-root 'C:\Git\Agents\issue-291-settings-redesign\artifacts\settings-review-profile'
```

## Review checklist

- Open Settings from the application menu. General, Color, Storage, and Advanced should
  be easy to scan. Back to Browser restores the live workspace. Use the category rail
  with the arrow keys and Tab through controls; focus should remain visible.
- Resize between the 1120 × 720 minimum and a wide/maximized window. Cards should stack
  in one column at every width, beside the category rail. The combined group is centered,
  with the heading and footer aligned to the cards and no Categories label or horizontal clipping.
  Check the same behavior at the display's normal DPI. The footer remains reachable
  while Storage or Advanced scrolls.
- General: choose a capture folder and save. Take a Player screengrab and verify its
  destination. Restart with the same isolated root and verify the preference remains.
- Open Premiere Pro Integration. Verify current connection/setup guidance, especially
  if another Lightflow profile owns the bridge. Pairing remains profile-specific; do not
  grant this test profile access unless intentionally testing it in Premiere.
- Color: configure different Camera and Creative folders and independently toggle
  subfolders. Save; only the changed collection should refresh. Existing per-asset
  assignments remain Catalog-owned. Restart and verify both preferences.
- Storage: check Catalog/Previews paths and usage. Quota is staged until Save. Existing
  location changes, Clean Up, Clear/Rebuild, cancellation and manual backup/restore
  remain immediate operations with explicit action dialogs. Use only disposable data
  for maintenance/restore checks.
- Advanced: inspect dependency status/details, Check Again, and optional FFmpeg override.
  An unsaved override must not change runtime tool selection. Invalid paths should be
  explained through a Lightflow dialog when saving.
- Restore Defaults should stage preference defaults, leaving storage locations, Catalog
  identity, integrations, Export configuration and Jobs policy intact until/unless the
  relevant explicit command is used. Save and restart to verify retained preferences.
- Confirm there is no Settings Export page, default input folder, global resolution,
  global encoder options or Media Roots card. Current Export still starts from Source
  choices and exposes its own advanced controls.
- Browser Locations: Add Location, then right-click its top-level row to Rename Location
  or Reconnect Location. Unavailable/unmapped locations must remain reconnectable. These
  commands retain the existing Catalog root identity; ordinary Rename still renames a
  filesystem folder.

## Automated evidence

`SettingsAuthorityTests` covers old-profile migration, migration failure, repeat-load safety,
preference reset isolation and offline Location targeting.
`SettingsLayoutTests` renders the actual Settings markup without a Window at narrow/wide
sizes and 100/150/200% DPI, checks horizontal containment and keyboard-reachable controls.
Settings source contracts, persistence, existing Export, storage, LUT, dependency and profile
tests supply regression coverage. Automated renders are not Jeremy's visual acceptance.

The task's private-desktop validation wrapper lives under ignored `artifacts/validation`;
it never switches the interactive desktop. General #284 infrastructure is outside this change.
Premiere tests requiring fixed localhost port 47857 cannot run while another Lightflow owns it.

Implementation remains local until Jeremy accepts it for final PR review or requests a
remote checkpoint. No PR acceptance or merge is implied by a review package.
