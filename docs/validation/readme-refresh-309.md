# README refresh: review and release input (#309)

## Scope and baseline

Documentation-only changes against origin/main
`09ba6b243af1a8ef063ecc108753286fabf91e96`, fetched on 2026-09-25.
This is input for documentation/visual acceptance and the Release Agent, not final
1.0 release notes or authorization to publish.

## Major 1.0 release-note themes

- Browse real folders, include subfolders, search/filter, and choose Grid or Details.
- Organize across folders with Collections, Collection Sets, and source-backed Smart Collections.
- Review through the Player filmstrip, frame/transport controls, saved ranges, point markers,
  Subclips, non-destructive rotation, and timestamped Visual Index.
- Apply Camera then Creative LUTs consistently through review, Previews, and encoded output.
- Inspect technical media information and save descriptive metadata in the Catalog.
- Export selected Browser videos/Subclips or Player media with explicit naming, destinations,
  Color, and source-aware delivery settings; follow background work through Jobs.
- Continue work across sessions with separate durable Catalog and rebuildable Preview storage,
  backup/recovery, storage administration, and the redesigned Settings.
- Hand sources, native Subclips, saved source In/Out, and point markers to Premiere Pro
  through the installed companion; explain setup and the explicit handoff boundary.
- Deliver a self-contained Windows installer and portable package with pinned dependencies.

Before publication, reconcile these themes with the exact accepted release source.
Do not add #299 camera-profile discovery, reconsidered #149 behavior, proxies, publishing,
AI, or other roadmap work solely because older capability documents mention it.

## Claim verification

| README scope | Current evidence |
| --- | --- |
| Browser navigation, recursion, search, views | MainWindow.xaml / MainWindow.xaml.cs; BrowserQuery.cs; Architecture Browser Grid and Details |
| Collections / Smart Collections | docs/SMART_COLLECTIONS.md; BrowserQueryIntent.cs; merged PR #310 |
| Player filmstrip and transport | PlayerViewerHost.xaml and workspace/review code |
| Rotation | docs/VIDEO_ROTATION.md; merged PRs #303 and #313 |
| Visual Index | VisualIndex.cs; VisualIndexView.xaml; Architecture Visual Index; merged PR #301 |
| Descriptive metadata / Inspector | MediaInspectorView; Architecture Creator-authored descriptions |
| Browser export selection / fallback | BrowserSelectionActions.cs; MainWindow.xaml.cs; SubclipExport handoff; merged PR #316 |
| Jobs queue and cleanup | JobsAdmission.cs; Architecture Jobs command eligibility; merged PR #305 |
| Settings | docs/validation/settings-291.md; Architecture Settings authority; merged PR #304 |
| Premiere requirement/version | PremiereCompanion/manifest.json: 1.2.2, minimum host 26.5.0 |
| Earlier published companion | v0.40.0:PremiereCompanion/manifest.json: 1.1.5 |
| Markers shipped on main | PR #300 merged; #259 closed and accepted merge recorded in issue comments |
| Install / package | Build-Release.ps1; installer/LightflowStudio.iss; package validation; latest release API |
| Version banner | Directory.Build.props and set-version.ps1 |

Older architecture sections and capability lists contain historical/future claims.
The README uses the later merged implementation where those descriptions conflict.
The linked Premiere guide's introduction and setup routing were corrected; its
implementation history is otherwise retained.

## Corrections and boundaries

Removed old Settings export-default claims, legacy branding/screenshots, an Export
warning/disabled-action image, the failure-heavy Jobs image, implementation-heavy
per-Job descriptions, and speculative future publishing wording. Added current
organization, review, Inspector, continuation, Settings, and Premiere coverage.
Clarified that Same as Source still re-encodes, NVENC needs supported NVIDIA hardware,
portable distribution does not imply a shared/portable Catalog, file operations can
change originals, and Premiere has its own media/sidecar behavior.

No product version metadata was changed. The README banner remains **0.40.0**, in the
exact form recognized by set-version.ps1. The old versioned set-version command example
was removed, so there is no second README version command to substitute for 1.0.
The `release/0.40` example is an actual maintenance-line example, not a release-version
banner. Companion 1.2.2 must track the final shipped companion, independently of the
Lightflow product version. The historical 1.1.5 comparison in the setup guide stays historical.
Reassess the current-main versus published-release sentence when 1.0 is published.

## Screenshot audit and staging

See [asset provenance and capture notes](../assets/readme/README.md).
All four original images were audited visually. Three filenames now contain current
captures; the old Jobs image is removed and Visual Index is added. Following Jeremy's
documentation review, the lead Browser capture now emphasizes actual filesystem
navigation and recursive browsing through a task-owned Demo Media tree. The earlier
Collection capture is retained as browser-collections.jpg beside the dedicated
Organize across folders subsection. The resulting set contains five images. No manual capture
is required for this set. Actual Color selectors are visible, but no LUT is applied to
the already rendered sample film; the images do not claim a camera-log transform.

Task workspace: `C:\Git\Agents\issue-309-readme-refresh`.
Packaged executable: `artifacts\release\LightflowStudio\LightflowStudio.exe`.
Isolated profile: `.cache\readme-profile`.
Original public-safe media: `.cache\readme-media\Forest`.
Filesystem demo: `.cache\Demo Media\Forest Story\Camera A` and `Camera B`.
Raw captures and downloaded source are ignored under `.cache\readme-source`.

The package was built from the baseline using PullRequest / SkipInstaller. Packaged
startup/presentation, graceful shutdown, icon and dependency validation passed.
No full application test suite is required for these documentation-only changes.
The Export capture shows three videos with distinct saved In/Out bars (1–5 seconds,
2–9.5 seconds, and 6–11 seconds). All three NVENC exports completed locally after
the enabled Export setup was captured.
Local validation parsed four Markdown files, resolved all 22 relative links (including
five image references), and rendered the README in a local GitHub-like layout. All five
images decoded and loaded at their expected dimensions, with no horizontal overflow.
All ten external links in the changed documentation returned HTTP 200; releases/latest
resolved to v0.40.0 with installer, portable ZIP, and SHA256SUMS.txt assets verified.
The version-banner pattern still matches set-version.ps1, and git diff --check passed.
Evidence and the local HTML preview remain under ignored .cache; no content was uploaded
for rendering. These checks do not claim exact GitHub renderer parity or user acceptance.

## Acceptance gate

Keep this work local until Jeremy accepts the README and screenshots. Do not push,
open a PR, close #309, or merge before that acceptance. A later PR-ready handoff must
refresh the packaged executable after its final commit per AGENTS.md.

#309 was absent from the roadmap and has been added. It has no native parent or recorded
blocking dependencies. Priority and Area were not preassigned; do not infer a parent
Epic or product priority from this documentation task.
