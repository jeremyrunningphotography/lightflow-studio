# Export Active In/Out — LF-WIN-DEV-048 / #497

Target: release/1.0. Dependency PR #501 / #496 merged as
ca929e586b929d721656a5ca5c5c5fb8fff5f905 and incorporated before implementation.
Independent workspace: C:\Git\Agents\LF-WIN-DEV-048-ExportActiveInOut.

## Behavior and architecture

For Browser Export Subclips, saved Subclips retain precedence. With no saved
Subclips, the existing include-selected-videos option uses that asset's persisted
active range, or the full source if no marks exist. In-only means through source
end; Out-only means from zero, following existing Player/MediaRange semantics.
No global current-Player marks are read. Source and naming bases remain unchanged.

The Catalog-to-capability handoff retains the per-asset mark snapshot and uses an
appended NoSubclipActiveRange provenance value (existing enum values preserved).
The dialog consistently uses fixed active ranges in preflight, timeline, planning
and submission, with distinct timeline tooltips and unchanged output naming.
Saved Subclip, active range and full-source provenance persist in Jobs/history.
Final submission compares current Catalog range/provenance/source identity with
the dialog snapshot and refuses changes, requiring the dialog to be reopened.

No Player/backend stepping, resolver/tolerance/EOF, metadata probing, NVENC,
Jobs-style activity, output destination or source-collision implementation changed.
Existing malformed primary records continue to restore as absent; no new Catalog
corruption-recovery policy is introduced. A materialized invalid active range is
blocked without silently switching to full source.

## Validation and acceptance gates

Initial focused Release: 98 PASS. Native export/safety/history: 50 PASS, including
seven real NVENC successful exports and strict ambiguous EOF rejection. The first
new native test run had a missing test-only WPF import; corrected. Its subsequent
run had 48 PASS / 1 FAIL because an offset/AAC fixture had source EOF evidence that
legitimately disagreed with captured video duration. The test now explicitly
asserts the accepted strict rejection and a separate zero-offset EOF success;
production validation was unchanged. Original failed output/TRX are retained.

A mixed-batch final-plan/inclusion test was added afterward; final full and focused
counts, package provenance, executable/hash and owner checklist are recorded in
the complete task-owned evidence ZIP. No acceptance, PR readiness, merge or release
is claimed by this checkpoint. Tests run only through Test-Noninteractive.ps1;
packaging uses Build-Release.ps1 -Mode PullRequest -SkipInstaller.

Owner acceptance of the packaged app is required before one implementation PR.
Independent qualification where applicable, exact-head CI and explicit merge
authorization remain later gates. No production Catalog operation, release/tag or
main forward-port. Historical maintenance: Not warranted for a bounded bug fix.
