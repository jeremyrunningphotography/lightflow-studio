# Catalog startup gate and Settings implementation (#412)

LF-WIN-DEV-012 continues in `C:\Git\Agents\LF-WIN-DEV-012-CatalogSettings`, branch `codex/412-catalog-settings-startup`, Draft PR #422. The owner-approved startup-gate section of [#412](https://github.com/jeremyrunningphotography/lightflow-studio/issues/412) supersedes the earlier Settings-based startup recovery UI. Renewed technical and packaged owner acceptance are required before merge.

## Current behavior

No Catalog, no workspace. One existing Lightflow-styled Catalog setup dialog handles first-run and every Catalog opening refusal. App does not construct the normal shell until storage is Ready and the authoritative ownership lease is live. First run offers Create New Catalog, Open Existing Catalog and Exit; remembered Catalog failures additionally offer Retry. Closing the gate closes admission, waits for any active operation to release its resources, and exits. Failed attempts remain in the gate. The splash closes before the gate appears.

Approved headings distinguish missing location, actual competing native owner, unsupported storage, invalid Catalog, established corruption, pending recovery and other failures. The path is separate from the explanation; original diagnostics remain in collapsed Troubleshooting details. Established refusal states confirm no replacement Catalog was created. A failed selection displays the selected path; Retry displays the remembered path. Ordinary Settings has Lightflow Catalog Location, Move Catalog and Open Existing, without startup recovery controls.

Retry reopens only the remembered path/identity with creation disabled. Open Existing runs the existing native admission, lease-before-SQLite, schema/integrity/identity validation and explicit identity confirmation, then activation and configuration readback; a failed selection cannot silently reopen the prior Catalog. Healthy Settings keeps the accepted controlled restart and consumer teardown.

Explicit Create New delegates to the existing coordinator and creation lifecycle, generates a fresh identity, and requires native admission, lease, activation, validation and configuration commit before Ready. It proposes the default only when safe; otherwise an unused Lightflow Catalog child is proposed. Occupied locations, prior configured locations (including missing paths), reserved storage and native aliased overlap are refused. The sole empty Catalog/Backups boundary created by profile initialization is permitted only without files or any other children. Existing transfer journals veto Create and Open in that profile. Failed creation retains its recoverable artifacts, restores and verifies prior configuration where possible, and never implicitly activates prior storage; uncertain rollback remains blocked.

Active readiness reuses StartupStoreEvidence. Loss of ownership or Catalog availability revokes authoring through the existing mutation/session admission, rejects Back to Browser and keyboard navigation, closes existing consumers, and uses the existing guarded restart to return to the gate. Startup callback completion rechecks readiness before revealing the workspace. No second Catalog writer/lifecycle or SQLite/schema/provider/durability/support-policy change is introduced. CatalogId, RootId, AssetId, authored data, mappings, LUT identities, transfer journals and retained copies retain their existing owners.

## Current qualification and handoff

Focused startup-gate, rendered WPF, selection, relocation, independent-process ownership, recovery, startup and shutdown qualification passes. Earlier UI/fixture failures are retained. First-run, all recovery messages (including established corruption), navigation rejection and healthy Settings are rendered on the required private desktop and visually inspected. Original unsafe writer evidence remains unchanged in [OWNERSHIP_GATE](../research/catalog-settings-412/OWNERSHIP_GATE.md) and its raw logs; corrected regressions must refuse the second writer.

Final implementation-head full Release, exact-head CI, installer/portable qualification and refreshed local package provenance are recorded in Draft PR #422 and the issue handoff. Historical results below do not establish current-head acceptance. Required package refresh uses `Build-Release.ps1 -Mode PullRequest -SkipInstaller`; owner commands must name that fresh executable and exact task-owned isolated roots. No owner storage or media is used.

Windows local NTFS qualification only. Active network and ExFAT Catalogs remain unsupported. Process interruption is not physical removal/power-loss qualification. Pending transfer journals deliberately block automatic resolution; artifacts remain retained for review. Mac production/UI and physical round trip remain separate #393/#406 gates. #412 remains Open/Review and PR #422 Draft/unmerged. Parent #271, Catalog Area, unset Priority and existing dependencies are unchanged; #393 remains incomplete. No merge, release, new issue or agent allocation is authorized.

---

# Historical implementation and qualification checkpoints

The following retained record describes earlier heads and superseded presentation. The current startup-gate behavior above is authoritative.

# Catalog Settings and startup implementation (#412)

LF-WIN-DEV-012; task clone `C:\Git\Agents\LF-WIN-DEV-012-CatalogSettings`, branch `codex/412-catalog-settings-startup`. Jeremy authorized the bounded ownership correction on [#412](https://github.com/jeremyrunningphotography/lightflow-studio/issues/412#issuecomment-6104684233). The [original failing diagnostic](../research/catalog-settings-412/OWNERSHIP_GATE.md) remains historical evidence. This implementation requires renewed technical and owner packaged functional/visual acceptance before merge.

## User behavior

Settings / Storage exposes Move Catalog and Open Existing Catalog. Move picks a parent and proposes a new `Lightflow Catalog` child, confirms retained source/stale-copy behavior, then presents cancellable prepublication progress and the actual terminal result. Existing destination folders refuse. Source is retained; later destination edits are not synchronized back.

Open Existing selects a directory with standard `LightflowCatalog.db`, not an arbitrary database or a backup payload. Native admission, exclusive ownership, schema/identity, full integrity and accepted portable Root/Asset path validation run before confirmation. A different valid identity is explicitly confirmed and adopted without rewriting or merging. Missing media and unresolved external LUT files do not prevent opening or remove authored assignments.

Open uses controlled restart through existing shell/Inspector/Browser/Player/Jobs shutdown. Active Jobs and unresolved Premiere handoffs must finish. A cancelled shutdown cancels restart. The fresh process reacquires and revalidates selection, activates under the existing lifecycle, saves and reads back configuration, then publishes authoring. It preserves the exact isolated data root. A refused selected Catalog visibly reports failure and only resumes the previous Catalog after configuration and ownership are proven; otherwise recovery blocks authoring.

Normal first launch offers Create New Catalog, Open Existing Catalog, and Cancel before default creation. Existing missing, unavailable, unsupported, corrupt, mismatched, or interrupted Catalogs use the unavailable surface with Retry / Reconnect and Open Existing. Remembered paths never authorize blank substitution. Packaging's explicit guarded startup smoke remains a test-owned creation scenario.

## Authoritative ownership and receipts

`StartupStoreEvidence` remains the sole Catalog ownership lease. Its native journal handle refuses sharing and delete/rename, opens without following reparse points, requires one regular native link, and validates that the current path still names its owned object. All coordinator session publication and connection opening require that lease. Preview delete-sharing remains its previous disposable contract.

Move retains source ownership throughout quiescence/snapshot/staging. It acquires destination ownership before SQLite, verifies the existing activated session and unchanged identity, commits/readbacks configuration, transfers the lease into the coordinator, publishes that session, and only then releases source ownership. Rollback disposes provisional SQLite/lease resources, verifies prior configuration and the still-owned source, and resumes through the existing mutation lifecycle. Failed proof enters RecoveryRequired, disables authoring, closes sessions and releases leases; Catalogs, journals and recoverable artifacts remain retained.

Recovery restore likewise requires the same lease before replacement/activation. An unavailable coordinator reacquires it after reconnecting storage; another owner refuses recovery before any replacement. Failed restore rollback/reactivation retires the existing mutation admission and session, releases leases, and reports recovery required instead of publishing an unowned or disposed session.

A cold closed-transfer unit verifies complete manifest/readiness/pending metadata and all payload bytes before first SQLite. The exclusively owned startup journal is excluded from payload enumeration, not from native lease validation. After successful validation the existing checksum envelope records `Validated` in the existing certificate version/fields, using the existing durable write primitive. It attests prior mutable-Catalog validation and prevents historical snapshot hashes from rejecting legitimate later edits. It does not attest clean shutdown or grant fast startup. Clean/session/fingerprint rules are unchanged. Dirty, missing, torn, or failed cold validation cannot promote a unit; previously validated evidence survives an interrupted reopen. Pending transfer recovery vetoes remain independent and authoritative.

No second writer coordinator, SQLite/schema/provider/WAL/durability change, new synchronization owner, or filesystem support policy change is introduced. RootId/AssetId and authored Collections/Sets, colors, metadata and LUT IDs remain in the existing Catalog. Machine mappings and LUT search folders retain existing owners. LUT resolution continues to use content identity; filenames/ambiguous roots are not guessed.

## Qualification

The retained two-coordinator diagnostic is now a must-refuse regression. Independent executable processes additionally qualify ordinary startup, moved destination ownership, explicit selection contention, normal shutdown release, and termination at Activating and ActivatedPendingConfiguration durable intents. Restart refuses unresolved journal ownership, retains source/destination, and blocks stale callback authoring; explicit receiving-profile selection validates the recoverable published unit. Fault injection qualifies destination lease contention, activation failure, save failure, and rollback failure. Cold-unit tampering remains refused across repeated attempts; edited moved Catalogs reopen without treating their original snapshot as current bytes.

Focused private-desktop results: `lf012-qualified-focused-final.trx`, **55 passed / 0 failed / 0 skipped**, runner confirmed zero owned processes. Local shared contracts: `lf012-neutral-windows.trx`, **136 passed**. Premiere companion: **90 passed**. Full-suite/package/exact-head CI results are recorded in the PR handoff after completion. Earlier failed results are retained, including the full run before dependency preparation and the Preview delete-sharing regression; no conflict assertions were weakened or retries added.

Expanded recovery/Preview/startup/selection/native qualification: `lf012-recovery-preview-focused.trx`, **88 passed / 0 failed / 1 existing privilege-dependent symlink skip**. This includes reconnected recovery versus a second owner, hard-linked journal refusal before writes, and receiving-profile Root/Asset/LUT identity retention. Explicit remapping retains the origin mapping; byte-identical renamed LUTs recover the same identity, while different bytes and stage folders cannot guess an authored assignment. The existing startup ordering assertion now accepts the required additional arguments and explicitly checks both source positions exist before asserting splash-before-storage ordering.

Final recovery terminal qualification: `lf012-recovery-terminal-final.trx`, **45 passed / 0 failed / 1 existing symlink skip**. Original raw diagnostic logs are preserved as [unsafe post-move baseline](../research/catalog-settings-412/post-move-unsafe-baseline.log) and [ordinary-startup control](../research/catalog-settings-412/ordinary-startup-control.log). The baseline's passing diagnostic demonstrates the unsafe original behavior; the corrected regressions require refusal.

`lf012-integrity-diagnostic.trx`: **47 passed / 0 failed / 0 skipped**, including all measured backup/restore path boundaries, the unchanged cleanup deadline, ownership/process/selection/recovery and startup validation. Validation receipt writes are idempotent within an already validated lease, and clean-completion SQLite work also verifies Catalog ownership. The post-restore verifier retains its full integrity assertion and now includes the actual refusal diagnostic.

The second local full run (`lf012-full-release-qualified.trx`) had 2,834 passes, two failures (measured-path live integrity and the controlled cleanup deadline), and two existing skips. Those failures did not reproduce in the focused diagnostic; the same committed head `8c1217c29921266db89e0468273256353e353074` passed the Windows CI suite, both neutral jobs and Mac adapter job in [38108771864](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/38108771864). The underlying transient causes are not claimed fixed. These results remain retained; final-head full/package validation is recorded separately in the PR handoff, without weakened assertions or writer-conflict retries.

Rendered first-run and unavailable Settings surfaces were visually inspected at `artifacts\lf012-presentation\first-run.png` and `recovery-settings.png`; they reuse shared Lightflow resources, typography, explicit actions and dark dialog patterns. Owner packaged visual/functional acceptance remains pending.

## Limits and acceptance

Windows local NTFS qualification only; active network and ExFAT Catalogs remain unsupported. Process termination is not physical removal/power-loss durability qualification. Full Mac production/UI integration and physical round trip remain separate #393/#406 gates; existing shared and Mac adapter CI do not establish application parity. Future Mac startup/transfer/recovery must supply the same lease-before-SQLite, full-session ownership and rollback-or-recovery contract.

An interrupted transfer journal deliberately blocks automatic writer selection/cleanup. Both artifacts remain for review; a receiving isolated profile may explicitly validate/open the retained published Catalog. This is not a general recovery manager or automatic conflict resolver. No automatic root/LUT reassignment or copy synchronization is provided.

Jeremy's bounded acceptance: launch the freshly packaged branch executable, exercise the first-run choice, create synthetic authored work, Move Catalog, close/reopen, Open Existing with confirmed identity/restart, verify the actual Settings path and retained authored work, and observe Retry/Open Existing on unavailable configured storage. Native races and corruption reproduction are covered automatically, not assigned to owner hands-on testing.

Acceptance startup command (fresh task-owned root):

```powershell
& "C:\Git\Agents\LF-WIN-DEV-012-CatalogSettings\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-DEV-012-CatalogSettings\artifacts\lf012-owner-acceptance"
```

#412 stays Open / Review at Draft PR handoff, #393 stays incomplete, and parent/dependencies/Area/Priority remain unchanged. No merge or release is authorized.

Final validation follow-up: the retained local `lf012-final-head-full-release.trx` had 2,835 passes, one cleanup timing failure, and two existing skips. The cleanup test now completes fresh Catalog fixture startup before its unchanged five-second assertion/disposal deadline and logs both intervals (focused: 429.2 ms setup, 29.1 ms cleanup). No cleanup assertion was removed. `lf012-cleanup-transfer-guard.trx` passed all 65 tests.

Pending transfer journals now veto manual restore before backup preparation or SQLite replacement, matching startup's existing veto; both real-process interruption cases assert this refusal. CI run 38109570472 retained two native sidecar admission failures (Win32 access denied) during measured-path backup. Parallel fixtures call global SQLite pool retirement. Backup now keeps an existing session's lease-validated SQLite connection checked out through native revalidation, preventing retirement of its last source connection during that interval; a regression explicitly retires pools while checking WAL retention and second-writer refusal. No SQLite policy or native admission predicate changes. Final focused/full/CI/package results remain pending.

## Owner recovery-presentation revision

Owner functional acceptance remains incomplete. Settings now prominently explains why Browser could not open the Catalog. The Catalog section separates its reason-specific heading and short explanation from the configured location, keeps Retry / Reconnect and Open Existing primary, and retains original diagnostics in collapsed shared-styled Troubleshooting details. Established Catalog refusal states explicitly state that no replacement Catalog was created; generic configuration/first-run failures do not assert it.

Existing startup branches supply presentation-only reason metadata: actual native sharing violation, pending journal, validation refusal, unavailable location, or storage admission refusal. Unknown ownership acquisition failures do not guess a competing owner. No accepted lease, activation, transfer, rollback or filesystem behavior changes. Restart obtains the fresh startup presentation; rendering tests verify reason replacement, collapsed diagnostics and removal of stale recovery/footer state on Ready.

Focused state/presentation, selection and independent-process ownership tests qualify this revision. All four requested recovery variants were rendered and visually inspected with existing Lightflow resources. The full suite and automatic full-suite CI are deferred for this owner presentation iteration; prior full/CI results apply only to their previous heads. Refreshed package provenance and the revised hands-on command are recorded in Draft PR #422. Owner acceptance remains pending; #412 remains Open/Review, no merge authorized.
