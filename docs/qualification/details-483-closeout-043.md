# Details and presentation persistence — final qualification

LF-WIN-ADM-043, DARKMATTER, 2026-11-08. This record prepares the single
implementation PR for #483 under #480 / #395 / #366. Actual merge and post-merge
results belong in issue/PR discussion and the final portable evidence package.
No release or tag is authorized.

## Acceptance and source provenance

- LF-MAC-DEV-040 accepted I04: `b5694012ec7ad15d35fa8de6d640d478222447de`,
  tree `741442a96ca3fb9fcba4fe73e75004ca9d3ad849`. Owner final 4/4 PASS
  preserves earlier accepted Details, three Grid modes, column identity/configuration,
  selection/navigation, persistence, restored Locations, unavailable-volume fallback,
  dimensions and cached-first Preview activity. Historical Mac Release 421 and
  safety 16 PASS are reported evidence, not a fresh 043 Mac runtime execution.
- LF-WIN-TST-041 independently qualified that Mac source: 2,880 Windows Release
  passed, zero failed, two existing skips; focused 670, portable packaging and
  Catalog/backup/recovery checks PASS. Original and v2 reports/evidence remain in
  its retained independent workspace; no later-source qualification is attributed to it.
- LF-WIN-DEV-042 combined source: `ce35e0367977ed31293466b36659509c269bf558`,
  tree `c700cb3d810ca16a7d10bc33c328e8d94f47545b`. Historical full Release
  2,884 passed, zero failed, two skips; focused Details 14, Application 141,
  Catalog 68, UI 146, package/dependency/startup/Catalog safeguards PASS.
- Owner final Windows acceptance: **4/4 PASS**, observations None: Details Grid
  controls disabled, remembered Grid mode restored, orange insertion marker, and
  Details regression. The 043 assignment explicitly supplies this acceptance and
  authorizes normal merge after qualification and CI; no repeat physical acceptance
  is required without material change. Earlier 042 reports correctly predate it.

Both accepted commit/tree identities were verified against local Git objects.
The complete Mac-to-combined diff contains exactly four WPF production files,
two Windows test files and registry documentation. Mac/shared production files
are byte-identical. Native WPF still owns header dragging, destination and Move;
the adapter changes separator paint/viewport visibility and cancellation cleanup.
Grid mode disablement derives from existing layout state and guards routed clicks.
There is no separate query, sort, selection or column persistence engine.

Current main `cc64f725aefb461c1b1a798cf2dfffa04658befd` is an ancestor of the
accepted implementation. Integration in the independent full clone
`C:\Git\Agents\LF-WIN-ADM-043-DetailsCloseout` required no conflict resolution.
Before 043 documentation, its tree differs from combined acceptance only by the
043 registry reservation. All subsequent changes are closeout documentation.
No accepted production or validation input is changed by 043.

## Final qualification

Fresh 043 shared Release: Application **141/0/0**, Catalog **68/0/0**, UI
**146/0/0** (passed/failed/skipped); Premiere companion **90/0/0**.
Fresh full Windows Release: **2,884 passed / 0 failed / 2 skipped**, 7m52s.
Fresh focused Details: **14 passed / 0 failed / 0 skipped**, 8s. Both completed
through the required private-desktop boundary with no owned processes remaining.
The two skips are native file-symlink coverage without Windows privilege/developer
mode (Win32 1314), and explicitly opt-in installed Premiere companion acceptance.
Full-suite native playback, Catalog protection and backup/recovery tests passed.
Required exact-head and post-merge CI are recorded separately.
Fresh PullRequest/SkipInstaller portable staging **PASS**: executable icon,
coherent SQLite payload, Browser startup, splash/workspace handoff, Jobs activation,
graceful shutdown, packaged Catalog backup/restore protection, companion packaging,
verified encoding/playback dependencies and staged package contents. The first
package completed in 45.1s with no owned processes remaining. Installer creation
and portable release archive are intentionally excluded locally; full configured
CI packaging remains mandatory. The final committed PR head is packaged again
before readiness, with exact timestamp/hashes and smoke evidence in the final ZIP.
Configured CI includes neutral Windows/macOS suites, native Mac storage/Catalog
lifecycle, a self-contained Mac shell build, Windows tests and package validation.
Windows execution is not evidence of macOS runtime behavior; prior Mac owner
acceptance, source preservation and mandatory macOS CI provide that disposition.

The first 043 full-suite attempt was stopped after concrete missing-input failures:
the fresh clone lacked pinned FFmpeg encoding and playback distributions.
Original private-desktop log is retained, not reclassified as PASS. The owned
boundary was interrupted and no task testhost/app remained. Both dependency
archives were then downloaded into this clone and independently SHA-256 verified
by the repository provisioning scripts before rerun. No assertion was weakened,
test removed, production fix made or retained acceptance Catalog touched.

## Safety and deferred work

Restoration remains Catalog/root/path bound, contained, cancellable and cached-first;
unavailable mappings preserve Catalog records. Column settings remain presentation
preferences outside the Catalog. Existing admission, cloud residency, protected
roots, single-writer, backup and recovery behavior remains in the accepted source.
Disposable test/package roots are task-owned; 040/041/042 acceptance data and
historical evidence remain retained and unchanged.

#489 unified six-level sizing, #484 search/filter/sort, #485 metadata/status and
Mac frame-rate semantics, and separate NAS reconciliation performance remain
deferred. No new feature or investigation is included. #480 has six children and
will remain open after three complete; #395 and #366 also remain open.

Historical maintenance: **Updated** existing Chronicle and Source Index milestone
records to distinguish accepted #483 delivery from earlier local checkpoints.
Development History is unchanged: the accepted Grid milestone already establishes
the broader Browser narrative; detailed Details qualification belongs here.
