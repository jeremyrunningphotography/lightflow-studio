# Windows v1.1.0 RC — OWNER ACCEPTED

LF-WIN-REL-053; November16,2026. Release issue [#509](https://github.com/jeremyrunningphotography/lightflow-studio/issues/509).

## Exact source and acceptance

Owner explicitly accepts RC **2b48891c5bcb6cb9d4d680d777fe460231b2bd4c**, tree
**464e3abd74a454a52e9f97e050e6a89889dc73ab**, production subtree
**800b6b018badab9754edec40ab9f50aaf6edca39**.

Owner results: application functionality PASS; fresh installation PASS; upgrade
from v1.0.2 with Catalog and preferences preserved PASS; uninstall/reinstall
without unexpected user-data loss PASS. These actual owner results supersede the
earlier pending installer-target/acceptance status. This is complete RC-level
acceptance, not a claim that every18-item checklist case was individually recorded.

The source begins at accepted Windows release/1.0 commit6b488064cfa24fbb2b6476b03e5bf15ab5a5083b.
Owner approved release/1.1 as a bounded exception to normal main-first minor policy.
Main's wider shared Catalog/macOS work is excluded. Only the approved Jobs ordering,
publication context/tagged notes, version/splash and release docs are introduced.
After acceptance, finalization changes only documentation/registry. Accepted
production, tests, workflow, launcher and dependencies must remain unchanged;
verify exact blob/tree equality before opening/merging the one preparation PR.

## Qualification and explicit local exception

- Focused release-preparation:43PASS/0FAIL/0SKIP.
- Original full local Release: **2693PASS/12FAIL/8existing opt-in SKIP**. All twelve
  BrowserFolderContextSafetyTests.MenuCommandsKeepSelectionAndUseInvocationFolder
  variants failed at setup Clipboard.Clear(), line37, COMException0x800401D0
  (CLIPBRD_E_CANT_OPEN), before Browser command assertions. Cases: Cut, Copy,
  Keyboard Copy, Paste into folder, Rename, Cancel Rename, Stale During Rename,
  New Folder, ReadOnly New Folder, Unavailable, Delete, Delete Confirm. Test blob
  a0fb8a94f210d0c08f68190dbed0cf8c0da7b0cb matches the unchanged baseline and #504
  category. Root cause/harmlessness remain unconfirmed. Owner authorizes a LOCAL
  exception, not a suite pass or CI waiver. No full retry solely for green,
  clipboard workaround, test exclusion or weakened assertion.
- Companion90/90PASS; GPU Export12/12PASS with no skips, pinned task tools,
  active ranges/saved EOF/audio/overwrite/source protection. Do not infer all
  LUT combinations or private original DJI footage from those12tests.
- Full Release packaging PASS: installer metadata/icon/branding; actual portable
  extraction matches all25staged files; SHA256SUMS, startup/Jobs/presentation/splash
  handoff, graceful shutdown, SQLite runtime/backup boundaries and dependencies.
- Published1.0.2 portable hash verified. Isolated stable-created Catalog, seeded
  through app services and reopened by stable before candidate: CatalogId,
  4AssetIds, Collection4members, Subclip and active marks preserved. Legacy encoding,
  schema1save/reopen, explicit folder/subfolder/MRU and Reset service checks PASS.
- Packaged UI observed prepopulated media, Ctrl+A3selection, saved/active/full
  Export rowsReady, NVENC available and migratedCQ23. Modal-input tool binding
  limited further automation; original limitation/own-probe cleanup retained.
  A later clean persistent startup/graceful shutdown PASS. Owner RC acceptance
  is actual owner evidence, not a retroactive automated-modal-interaction claim.

Required exact-head full Windows/Companion/package CI must pass before normal
merge. A red gate stops without a blind retry; local exceptions never waive it.
Post-merge push CI must pass too. Preserve historical failed/passing raw evidence.
Actual final PR/head/run/merge provenance belongs in #509/PR metadata, not another
administrative PR or a premature statement of merge here.

## Preserved packages, profiles and handoff

Workspace: `C:\Git\Agents\LF-WIN-REL-053-ReleaseReadiness`.
Accepted RC setup/portable/staged app are preserved in
`implementation\closeout\accepted-rc-2b48891` before mandated PR-head refresh.
Original setup SHA2564b9d56c6f5a35be950b3dd6c26d9e64fd8aed8fcf9c616640a35eca6505ddc9e;
portable SHA2560ea10ca088d74d2c0109e50868f5fd94fd1becdc28b1453b198bf43b0294ba70.
Those are accepted RC assets, not official tag-built publication assets.

Research ZIP SHA25637b149f7b32408f88fa17721f1397ace6db9f2781f28d5136d82c1b16286ecdd;
initial stop ZIP7cf8b9e92dfb3729ead9ebe121d45d5ecdf4de1e36f70b65d833493dd0ecae36;
recovery ZIPcb8ed761dd069049aae1a62468e0ba99eeeeda8956a2c1222a67e62600cae7cc.
All original archives, Catalog/media and fixture state remain retained. Live
databases, media and Preview caches stay out of evidence ZIPs.

The mandated final PR-head command is:
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller`.
Record real output, timestamp newer than PR commit and no owned smoke processes.
PR mode validates staged files but skips a new portable archive; unchanged-input
Release-mode portable evidence is retained, never mislabeled as a new CI ZIP.

```powershell
& "C:\Git\Agents\LF-WIN-REL-053-ReleaseReadiness\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-REL-053-ReleaseReadiness\acceptance\data-persistent"
& "C:\Git\Agents\LF-WIN-REL-053-ReleaseReadiness\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-REL-053-ReleaseReadiness\acceptance\data-fresh"
```

Persistent root already browses generated acceptance/media/A; mixed Collection,
saved Subclip and active marks retained. Fresh root is independently empty.
Never reset persistent acceptance state or access production Catalog storage.

## Preparation-stage closeout and publication boundary

One implementation PR targets release/1.1. Owner authorizes normal merge only on
unchanged accepted implementation and all green required exact-head checks;
no squash/rebase/force-push/auto-merge. Keep #509 open because publication is still
outstanding; #491 and #504 stay open, with #510/#511 cosmetic work deferred.
New [#512](https://github.com/jeremyrunningphotography/lightflow-studio/issues/512)
tracks installer existing-version/incoming-version replacement notification,
accurate data-preservation explanation and Continue/Cancel with silent-install
behavior preserved. It is deferred and not implemented in1.1.0.

Publication requires new explicit owner authorization: exact accepted release-line
merge, proposed v1.1.0 tag, green full tag workflow, freshly rebuilt official
installer/portable/checksums, approved notes, downloaded checksum/smoke verification
and latest-stable check. Do not assume tag-built assets equal local RC bytes.
Published tags remain immutable; use reviewed corrective patches if needed.
Main reconciliation remains separate reviewed applicability/forward-port work,
not a merge of main here or a reset/rebase of main. No release/tag/publication is
authorized by this prepared closeout. Historical chronicle: no shipped milestone
is claimed; this release qualification record documents the preparation milestone.
