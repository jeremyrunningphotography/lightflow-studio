# #340: durable clean-startup contract

Implemented locally following Jeremy's explicit architecture approval, superseding the earlier conservative-Catalog proposal. The accepted census fix from f9fe0ef is preserved. No push/PR/release is authorized before hands-on acceptance.

## Normal and exceptional startup

A successful prior shutdown is evidence for bounded readiness on **both** stores. An eligible clean current-schema startup executes no Catalog quick_check/integrity_check, no Preview quick_check/integrity_check, no full asset/Preview enumeration and no unsolicited usage census. This is a lifecycle-based fast path, not a claim that an undetected offline bit flip is impossible.

The SQLite durability policy remains Catalog WAL/FULL/foreign_keys ON and the existing Preview WAL/NORMAL (DELETE on UNC) policy. Authored mutation admission, backup quiescence, required migration backup/validation and restore protection are unchanged. Global startup corruption detection is now conditional, as approved. Lightweight reads detect touched-page and compatibility problems; they cannot prove every untouched page healthy. Normal SQLite errors still fail through existing safety paths. No untrusted Catalog scan is moved into the background while allowing normal writes.

## Durable evidence and ordering

`StartupStoreEvidence` owns an exclusive read/write lease on `<database>.startup-state` beside each store. A checksummed versioned certificate records store/path identity, a session nonce, Windows physical file identity, final last-write metadata/length and a bounded 100-byte SQLite header hash. WAL or rollback-journal content disqualifies the fast path. No database schema is added. Metadata is captured after write handles close, under a read-only handle excluding new writers.

`StartupSessionCompletion` owns `startup-session.state` in the isolated/profile root. It contains a 32-character completion nonce only when both store certificates completed that exact session. Startup consumes the previous completion record by overwriting it with Dirty and flushing to stable storage **before any SQLite open**. Each store certificate is likewise durably overwritten with Dirty before use. Failure to consume evidence aborts startup rather than admitting writes with old Clean evidence. Missing, malformed, partial, foreign-session, changed-file, missing-store or recovery-file evidence selects deep validation. Both stores must qualify; otherwise neither is given the clean shortcut.

Per-store certificates are written only after providers have drained/closed, a dedicated non-pooled connection successfully checkpoints (including checking SQLite's busy result), recovery sidecars are empty/absent, and the database is flushed. The final session nonce is published and flushed **last**, after both certificates succeed. A crash between those publications cannot grant either fast path: the session record remains Dirty. A partial session nonce cannot match both new certificates unless the complete nonce was already written, which occurs only after both stores reached the safe boundary. There is no fallback to a previous Clean record.

The protocol relies on the same OS/device durable-flush guarantees as the storage underneath SQLite. Missing creation metadata is conservative; a failed flush never authorizes later writes during startup. It does not authenticate files against deliberate external manipulation or certify arbitrary untouched pages. Filesystems that cannot provide stable identity/durable close evidence remain on the deep path.

The per-store lease permits deletion, preserving existing rebuildable Preview-folder deletion behavior, but excludes concurrent readers/writers of the certificate. A removed certificate cannot grant later fast-path eligibility. SQLite continues to own database transaction locks. Relocation releases the old evidence lease; a changed storage location does not receive a clean certificate that session and validates on its next launch. Profile/store leases prevent competing Lightflow coordinators from trusting the same evidence simultaneously.

## Shutdown and backup boundary

Existing MainWindow job/export/bridge shutdown and playback disposal precede storage disposal. Storage cancels accounting, stops monitoring and derived work, drains complete Catalog mutations, closes admission, acquires Preview maintenance exclusion, closes providers and clears Catalog pools. Only then can certificates and final session completion publish. A storage exception leaves Dirty. Unexpected UI/task failure and nonzero application exit explicitly prohibit clean publication even if disposal later succeeds.

Successful backup-on-exit retains the existing quiescent Catalog boundary into exit. A cancelled/failed backup resumes existing user-choice behavior and does not publish completion. If the user subsequently chooses a permitted normal exit without a backup, successful live-store shutdown can still publish Clean; backup creation is not a prerequisite for SQLite live-store cleanliness. Snapshot validation and live-file startup eligibility remain independent. Long-path backup handling, restore protection, snapshot validation and rollback are untouched.

## Five-state startup model

| State | Catalog | Previews |
| --- | --- | --- |
| Clean current schema, both certificates and final nonce match | Bounded readiness, then current durability policy and session activation | Bounded readiness, retain all records/artifacts/retry state |
| Interrupted or uncertain completion, invalid/missing evidence | Blocking quick_check before normal use, then identity/schema/history checks | Blocking quick_check and compatibility checks before Preview activation |
| Migration required | Existing preflight quick_check, full pre-migration integrity_check, mandatory migration backup, transactional ledger/schema upgrade, post-migration quick_check | Existing pre-migration quick_check, transactional migration including failure deadlines, post-migration quick_check |
| Restore/recovery | Existing candidate/protective/staged validation and publication/rollback; reopened Catalog always deep-checks with Restore reason | Identity-bound existing Preview state remains; no blanket invalidation; uncertain evidence cannot qualify |
| Readiness/open anomaly or detected corruption | Leave fast path, attempt deep inspection where SQLite can open, refuse activation if readiness remains invalid; preserve Catalog for existing explicit recovery | Leave fast path, attempt deep inspection where possible, disable unavailable provider on failure; preserve DB/artifacts/source evidence |

A valid scan cannot repair a missing required schema object, so a readiness anomaly still refuses activation. Open failures can be too early for SQL validation; those fail safely through the existing unavailable/corrupt classification. Corruption never triggers silent Catalog recreation.

This change does not introduce automatic Preview deletion/quarantine. Current startup rejects the unavailable provider while Catalog remains available; existing relocation with SwitchAndRebuild to a new empty destination is the explicit recovery route. No uncertain or healthy startup resets failure deadlines, sweeps artifacts or eagerly rebuilds the Catalog. A future automatic quarantine workflow would require a separate recoverable file-publication contract and is not silently invented here.

## Lightweight checks

Catalog opens read-only, reads user_version, verifies application ID, complete expected migration history and CatalogInfo identity, prepares/executes a LIMIT 1 asset readiness read and checks its required root/status index. It then verifies the existing connection durability/FK/busy policy and final identity/history/version through the normal session constructor. Bounded readiness is constant in asset count; ledger/schema work scales with schema size. A version change cannot bypass migration checks even if clean evidence was presented.

Preview opens read-only, checks version and application/store identity, reads at most one row's work/retry identity columns, verifies required Preview indexes, and applies existing connection policy. Metadata JSON is not deserialized and no PreviewRecord is created by readiness. Generator/source/retry decisions remain the existing per-record demand logic. Unsupported versions fail, never get treated as empty stores. FK enforcement is not a replacement for foreign_key_check; no global FK scan is introduced into the clean path.

A structured `StartupValidationReason` carries CleanShutdown, UnexpectedShutdown, Migration, Restore, DatabaseAnomaly or ExplicitValidation. Standalone service callers without consumed lifecycle evidence remain deep by default. There is no time/day-based trigger.

## Presentation and diagnostics

The normal splash keeps brief single-line phases such as Opening storage, Checking Catalog, Checking Previews and Restoring workspace. It has no permanent explanation or estimate.

Exceptional validation uses the original 440-by-approximately-311 footprint and artwork, one primary line and one supporting line. Its footer masks the artwork tagline only while those two lines are present. Exact examples:

- Catalog interrupted: **Verifying Catalog after an interrupted shutdown…** / **Protecting your saved Lightflow work**
- Catalog migration: **Verifying upgraded Catalog…** / same support
- Catalog restore: **Verifying restored Catalog…** / same support
- Catalog anomaly: **Verifying Catalog before opening…** / same support
- Preview interrupted: **Verifying Preview storage after an interrupted shutdown…** / **Making sure cached media is ready to use**
- Other Preview validation: **Verifying Preview storage…** / same support

SQLite exposes no defensible progress units here, and the accepted cold/warm difference makes an unqualified historical countdown misleading. No time estimate is shown. Unknown-duration work uses a small pulsing indeterminate segment. The shared template now hides the determinate indicator while indeterminate, preventing a false full/100% bar; measured determinate progress is unchanged. No new buttons, disclosure panels or modal dialogs.

Activity Log records fast/deep decisions, structured reason, readiness outcome, deep-validation begin/end and durations, recovery-to-unavailable transitions and the final clean-shutdown result. Existing phase timing records Catalog, Preview and presentation elapsed time. No source paths or metadata payloads are added to these diagnostics. Enumeration/usage invocation notes support deterministic regression assertions. `LIGHTFLOW_STARTUP_CAPTURE` is an opt-in isolated-profile-only capture of the real splash for packaged validation; no capture runs during ordinary startup.

## Census contract retained

Loaded never requests usage. Explicit Refresh Usage runs one SQL COUNT scalar, materializes zero PreviewRecords and streams file sizes/counts. Orphans remain unknown on ordinary refresh; explicit Cleanup owns reconciliation. No schema changes, invalidation, retry resets or regeneration are introduced by accounting. The 20,000-row regression and accepted 2,121,692-record read-only count evidence remain applicable.

## Validation and acceptance

Focused tests cover consecutive clean starts with zero scan/whole-store enumeration/census invocations; missing/torn/Dirty evidence; cross-store publication interruption; failed checkpoint/write; physical replacement; active mutation drain; backups enabled/disabled/cancelled/failed; Catalog migration, Preview migration, restore/recovery; readiness anomalies; Catalog preservation on corruption and Preview disablement without Catalog/source mutation. Existing Preview retry/regeneration/persistence and Settings/startup tests remain in scope. WPF rendering checks confirm two-line fit at 100%, 150% and 200% DPI and correct indeterminate/determinate behavior. No historical production migration acceptance or full release suite is repeated.

Packaged timing and acceptance paths are recorded in the task's combined handoff after rebuilding the exact local executable from the final commit. Tests prove path selection; timings supplement rather than replace the no-scan contract.
