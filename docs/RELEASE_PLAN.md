# Release Planning

## Versioning

Lightflow Studio follows semantic versioning.

- Patch releases: fixes, compatibility updates, and polish
- Minor releases: backward-compatible capabilities and meaningful workflow expansion
- Major releases: breaking changes or major stable product milestones

## Release readiness

A release candidate should satisfy:

- All automated tests pass
- Version values are synchronized
- Installer and portable builds complete
- Packaged dependency hashes are verified
- Third-party notices are current
- Upgrade from the previous supported release is tested
- Representative video fixtures pass smoke tests
- Recovery modes are tested against known damaged samples
- Settings migration is tested
- Release notes identify user-visible changes and known limitations

## Local Windows package validation

Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode Release`
to validate the self-contained application, pinned dependencies, portable ZIP, and Inno Setup installer.
`Test-ApplicationIcon.ps1` reads PE resources as data and compares every icon frame byte-for-byte against
the approved v4 ICO; it runs for both application and installer. `Test-PackageContents.ps1` also verifies
the staged ICO hash. Installer shortcuts/uninstall identity refer to the packaged application executable.
WPF window/taskbar identity uses the approved v4 PNG fallback; see the runtime Branding README.

For the final PR functional-test handoff, rebuild from the final commit with
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller`.
Jeremy tests `artifacts\release\LightflowStudio\LightflowStudio.exe`. Verify its timestamp is newer than the
commit and that no smoke-test process remains. The startup smoke check verifies restored workspace
presentation/splash closure as well as process survival. It does not simulate subjective visual acceptance.
Windows Explorer may cache old icons; embedded-resource verification distinguishes that cache from an
incorrect binary, without changing approved artwork to defeat caching.

## Release channels

Recommended future channels:

- Stable
- Preview

Preview builds may contain experimental capabilities but must use a separate update channel
and visibly identify themselves in the application.

## Definition of done for a feature

A feature is not complete until:

- Product behavior is documented
- Acceptance criteria pass
- Validation and error states exist
- Logging is adequate for troubleshooting
- Cancellation and cleanup behavior are defined
- Settings persistence is tested where relevant
- Accessibility is reviewed
- User documentation is updated
- Release notes are drafted
