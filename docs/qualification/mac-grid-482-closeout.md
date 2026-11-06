# Mac Browser Grid Cards — accepted completion and single-PR integration

LF-MAC-DEV-038 / LF-WIN-TST-039, November 6, 2026. This document records accepted
implementation/qualification and prepares final integration; it does not claim CI,
merge, issue closure or release before those operations complete.

## Accepted identities and source safety

- Accepted implementation: `e941fabefde207878d88690000a624bab8853cae`.
- Accepted overall tree: `ef16701a7522a215962657716d047251128a6f9b`.
- Parent: `8b3cb49df858daa0966b333e46e0c337f43a5e1f`.
- Baseline/current fetched main at closeout orientation:
  `1b37d62fe1bb153e6988661ce65453648d937551` (no intervening commits).
- Mac clone `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-038-MacBrowserGridCards`,
  branch `codex/038-mac-browser-grid-cards`; full independent workspace preserved.
- Windows clone `C:\Git\Agents\LF-WIN-TST-039-MacGridWindowsQualification`,
  branch `codex/039-mac-grid-windows-qualification`, exact accepted source during
  qualification/package. The baseline comparison used its separately owned clone.

Final integration may change `docs/**` only relative to accepted source. Verify
all other tracked paths (production, tests, locks, scripts, CI and root AGENTS) are
byte-identical. Final PR head/tree and merge/main identities must be reported
separately; the complete post-documentation tree is not the accepted tree.

## Owner acceptance and authority

Owner reports nine I01 behaviors accepted, with a bounded keyboard-border correction
needed; Arrow/Shift+Arrow/Command+Arrow semantics were confirmed correct. Final Mac
I02 **8/8 PASS**, including orange selection/focus and permanent bottom-right mode
controls, preserved all previously accepted functionality. No renewed Mac hands-on
cycle is required for documentation-only integration.

Owner Windows hands-on **6/6 PASS** on accepted source/package: normal Browser startup;
Preview/Info/Hybrid; Grid/Details and size controls; mouse/keyboard selection;
thumbnail/metadata/status presentation; navigation/scroll/Refresh/normal shutdown.
The owner explicitly approves qualification with the documented inconclusive Catalog
exception below. These are subsequent owner decisions, not findings invented by 038
or retroactively attributed to the independent Windows report.

The owner's final-closeout instruction explicitly authorizes one implementation PR,
full exact-head CI, normal merge once required checks pass, post-merge CI, issue/
Project/Epic administration and safe cleanup without another routine approval round.
It does not waive failed CI, permit changes to accepted behavior, or authorize #483.

## Independent qualification: preserve the exception

The [original LF-WIN-TST-039 report](windows-grid-482-039.md) is integrated with its
historical INCONCLUSIVE/pending-owner statements intact. The later owner acceptance
above supersedes only those owner gates. Engineering observations remain unchanged:

| Run | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Valid full Windows Release | 2,879 | 1 | 2 |
| Accepted-source focused Windows | 606 | 0 | 0 |
| Shared Application | 141 | 0 | 0 |
| Shared Catalog | 67 | 0 | 0 |
| Shared Avalonia UI | 102 | 0 | 0 |
| Pre-#482 baseline transfer tests | 14 | 0 | 0 |
| Pre-#482 baseline broader Catalog | 406 | 0 | 1 |

These runs overlap and are not summed. Original invalid-prerequisite run (2,764 pass,
116 fail, 2 skipped), recursive-runner interruption and desktop-access investigations
remain in the original ZIP. They are not qualified clean full-suite passes.

`ClosedCatalogTransferIntegrationTests.BackupCopybackRestoresThroughExistingRecoveryOwner`
failed in the valid full run: storage locality admission could not be established,
with Windows access denied (CreateFileW/MetadataNoFollow Win32 5) on a disposable
Catalog restore leaf. It later passed in accepted-source focused validation; neither
bounded baseline comparison reproduced it. Windows Catalog code is unchanged by
#482, and separate packaged Catalog safety checks passed. Unique cause is unresolved;
no baseline reproduction, confirmed #482 regression, correction or clean full Release
PASS is asserted. **Disposition: owner-approved qualification with documented
inconclusive exception.** This approval does not waive a new required CI failure.

Windows package/dependencies/Browser startup/Jobs/normal shutdown/backup/restore
protection checks PASS; executable SHA-256
`8e725c89d9707089fb77fa3a0b601357b6e6ff42bacb9472a9ef6e9a60865ee5`,
28 staged files, timestamp `2026-11-06T20:21:06.8792691Z`. PullRequest SkipInstaller
packaging did not qualify an installer; full implementation CI must run its installer
job. No release/tag or installer execution is inferred.

## Evidence integration and integrity

The existing ChatGPT handoff attachment supplied the original
`LF-WIN-TST-039-Windows-Qualification(1).zip`, SHA-256
`cdc67f28e749b306c657e848be268c86dbbeb6ca7cb8afc272c6a68cb1c9ec2f`.
All 151 entries pass ZIP CRC; all 150 entries in EVIDENCE-SHA256SUMS.txt match.
Both source-transfer manifest entries match. Source bundle SHA-256
`e9caddd10e8778c9a6381a364c56d3a9515a995190d601ada67b8b860fcf28b8`
matches the original Mac transfer. Accepted commit/tree and package-provenance.json
agree exactly. Mac independently parsed original TRX counters rather than trusting
the report's convenience JSON (whose Counters fields are empty).

Git contains the original findings report, selected text evidence/manifests and
[integration receipt](windows-grid-482-039-evidence/integration-receipt.json),
[verified TRX counters](windows-grid-482-039-evidence/verified-trx-counters.json),
[original failure](windows-grid-482-039-evidence/original-release-failure.json),
[source transfer provenance](windows-grid-482-039-evidence/PROVENANCE.md) and
[package manifest](windows-grid-482-039-evidence/package-manifest.json). Git text is
normalized to UTF-8/LF; the original ZIP is retained byte-for-byte in task artifacts
and will accompany the final portable closeout ZIP. No binary app, Catalog, cache or
private media is added to Git. Raw logs/failure attempts remain in the original ZIP.

## Workflow, history and pending final gates

Root AGENTS remains exactly accepted: local-first implementation; owner acceptance
and applicable independent qualification before one PR; ZIP evidence; protected
issue-scoped retained Catalogs; required exact-head and post-merge CI; explicit merge
authority. Current owner instructions satisfy that authority conditionally on checks.
Root AGENTS in the PR requires full CI, never a documentation-only substitute.

Historical disposition: **Updated** Chronicle, Development History and Source Index
for the accepted Mac Grid capability and local-first single-PR milestone, preserving
prior historical checkpoints. Routine qualification failures/registry administration
remain in qualification evidence rather than becoming separate historical milestones.

Before merge: finish docs/registry integration; prove accepted production/build/test
inputs unchanged; publish the exact accepted ancestry; open one PR; verify full CI on
its exact head, normal merge eligibility and no unresolved blocker. After merge:
verify merge/main source and post-merge CI; confirm #482 Completed/Project Done while
preserving Browser/unset Priority; update #480 to 2/5, keeping it open and #483–#485
Backlog/unstarted, #395/#366 open. Record final immutable provenance in GitHub metadata/
comments and portable evidence without a registry-closeout PR. Safely remove only
verified completed branches. Both agent evidence/workspaces and retained acceptance
Catalogs remain; no task cleanup may reset/delete Catalogs, Preview caches or media.
