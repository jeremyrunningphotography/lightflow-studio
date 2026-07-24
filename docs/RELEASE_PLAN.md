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
