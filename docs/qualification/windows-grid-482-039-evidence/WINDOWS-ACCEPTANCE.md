# LF-WIN-TST-039 Windows owner acceptance (fresh accepted-source package verified)

Persistent issue-scoped acceptance root:

```powershell
& "C:\Git\Agents\LF-WIN-TST-039-MacGridWindowsQualification\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-TST-039-MacGridWindowsQualification\acceptance\persistent"
```

Independent fresh-start root:

```powershell
& "C:\Git\Agents\LF-WIN-TST-039-MacGridWindowsQualification\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-TST-039-MacGridWindowsQualification\acceptance\fresh"
```

Both roots are new to #482 and separate from automated tests/package smoke. Use supported application initialization to create an isolated Catalog. Normal owner Catalog/profile and #481 acceptance roots must not be selected. No owner acceptance has occurred in this assignment. Retain issue acceptance data after testing.

1. Windows Browser starts normally; isolated Catalog initializes without unexpected repair/migration.
2. Preview, Info and Hybrid modes switch correctly and retain existing status-bar controls.
3. Grid/Details switching and thumbnail sizing retain their existing presentation and behavior.
4. Mouse selection, Arrow, Shift+Arrow and Windows Ctrl selection/focus remain correct; orange borders and focus distinction persist.
5. Mixed still/RAW/video thumbnails, truthful placeholders, available metadata/status, cached revisits and existing filter/sort behavior remain intact.
6. Navigation, resize/right-panel changes, scrolling, Refresh and normal shutdown/reopen work.

Record PASS/FAIL and observations for all six checks. Automated and static results do not replace owner hands-on acceptance. No visible desktop automation or Mac reacceptance was performed.
