# Release Planning

## Versioning

Lightflow Studio follows semantic versioning.

- Patch releases: fixes, compatibility updates, and polish
- Minor releases: backward-compatible capabilities and meaningful workflow expansion
- Major releases: breaking changes or major stable product milestones

## Development and maintenance lines

`main` is the development trunk. Normal issues and new features branch from and target
`main`; there is no permanent `develop` branch and no full GitFlow workflow.

Each released minor family uses `release/<major>.<minor>`. Create that branch at the
exact accepted source for its initial release. For v0.40.x the branch is `release/0.40`,
not `release/0.40.0`. Creating a maintenance line never resets or rebases `main`.

Patch/hotfix work (for example v0.40.1) starts from `release/0.40`, and its PR targets
that branch. Use the same issue, validation, Draft PR, explicit acceptance, and merge
process as other changes. Reconcile each accepted release-line fix into `main` in a
reviewed PR, or record evidence that the fix is already present or is not applicable.
Reference that reconciliation in the issue. Do not backport ordinary new features.

Only the current released minor line is actively maintained unless Jeremy explicitly
decides otherwise. Older release branches remain historical references, not promises
of ongoing support. A new minor release is selected from accepted development work
on `main` and establishes its own maintenance line.

## Selecting and publishing an exact source

1. Fetch the applicable remote branch and record its exact accepted commit. For an
   initial minor release, create its maintenance branch at that accepted source.
   For a patch release, use the maintained release branch, not future `main` work.
2. Verify a clean checkout, synchronized version metadata, accepted scope, and green
   required CI for that exact source. Run all release-readiness checks below. Use an
   independent task clone and isolated `--data-root` profiles for validation launches.
3. Build fresh Release-mode assets from that source. Keep source SHA, validation logs,
   artifact hashes, and workflow run together. Do not reuse an abandoned candidate's
   outputs just because their filenames match. Existing validation remains applicable
   only if its source, inputs, and relevant conditions are unchanged.
4. Verify `vX.Y.Z^{}` resolves to the exact accepted commit on the release line. The
   existing tag-triggered `Test and package` workflow runs tests, builds new official
   installer/portable/checksum assets, and publishes only after its gates pass.
   A tag at a maintenance-line commit is the build source; a tag workflow need not
   run with a branch name as its ref. Branch creation alone does not publish.
5. After publication, verify the remote tag, workflow head SHA, public release, asset
   names/versions, and downloaded SHA-256 checksums. Record user-facing notes and any
   required companion setup. A cancelled workflow is not a passed publication gate.

Published tags are immutable. If an **unpublished** tag reserves the intended version
at an abandoned source, first verify that no release exists, cancel any stale workflow,
and record the old tag object/target and newly accepted source. Correct the annotated
tag explicitly only after source validation, using an exact expected-old-ref lease on
the push; verify its remote peeled target afterward. This push can trigger publication,
so do not perform it while publication is paused. If the existing tag is already correct,
leave it unchanged. When explicitly authorized to resume, rerun the cancelled tag workflow
only after verifying its recorded head is still the intended release-line commit.
Never bypass repository protections or invent another version to avoid a conflict.

### v0.40.0 source boundary

The initial `release/0.40` branch and corrected, unpublished `v0.40.0` tag identify
`129b52e190d1d74a539c5410d123168a36d8adb6`. Its accepted main validation is run
`35288924427`. The earlier candidate `21051d016545e823e072ef02203503ec8db294ed`
is not the release source. Run `35291912779` was cancelled before publication.

The policy documentation is a separate change targeting `main`; it does not become part
of v0.40.0 or invalidate the unchanged source's completed checks. Do not move the release
branch or tag merely to include these documents. Any decision to change the release source
requires explicitly re-establishing the source/tag relationship and applicable validation.

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
