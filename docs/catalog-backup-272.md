# Catalog exit backup — writer inventory and lifecycle

Investigation baseline: `af303cc3cd89c3933ae3c85d61790b906569d2d1`.
Specification: #272; Jeremy approved operation-level mutation admission/drain within this issue.

## Normal-operation writers

| Owner | Catalog state | Complete operation boundary |
| --- | --- | --- |
| CatalogClassifications / Browser and Player commands | ratings, flags, labels, keywords | Entire accepted bulk command, including each awaited asset save; individual saves also participate |
| CatalogCollections | Sets, Collections, membership and mixed order | Public logical mutation, including its existing per-service queue; caller loops over Collections must retain an outer admission |
| CatalogDescriptions / InspectorDescriptionEditor | authored descriptions | Complete Apply operation |
| CatalogMediaRanges | working ranges | Save operation |
| CatalogSubclips | Subclips, names and order | Public mutation, including queue wait |
| CatalogMarkers | markers and names | Create/rename/delete operation |
| ColorManagement | LUT library identity and Color assignments | Library refresh or complete assignment operation; bulk UI commands retain outer admission |
| CatalogPreviewFrames | preferred frame intent | Set/reset operation; generated pixels remain Preview state |
| VideoOrientation | authored rotation | Complete rotation operation |
| BrowserRecursiveRoots | recursive Browser scope | Enable/disable operation |
| MediaRoots | root identity, names and machine mappings | Create/rename/remap; Browser resolution can create an anchor and therefore is not necessarily read-only |
| MediaAssets | asset identity, observations, fingerprints, availability and relocation | Whole service mutation, particularly relocate followed by observation |
| CatalogReconciliation | folder discovery, changed and missing observations | Whole accepted reconciliation; all per-asset transactions plus final missing-state update |
| FileOperationExecutor / AssetCopying | moved identities, new copy identity and cloned authored state | Complete accepted file operation through Catalog reconciliation; taking a lease only after the filesystem move/copy is too late |
| DerivedMediaMetadata, ThumbnailGeneration, MarkerThumbnails, SubclipPosters | source observations via MediaAssets | Catalog observation is covered by MediaAssets; normalized probe metadata, generated thumbnails and Preview publication use the separate Preview database |
| CatalogPremiereHandoffs / PremiereMarkers | prepared/dispatched intents, receipts and marker/Subclip projection state | Logical handoff spans job execution and HTTP request callbacks; dispatch/receipt callbacks run in a separate execution context and must explicitly share the accepted operation's authority |

CatalogAssetState is a read projection. Workspace/settings, file-operation history, export history and runtime queues are profile JSON, not Catalog tables. They require ordinary persistence ordering but not SQLite mutation admission. Catalog repositories currently share a session accessor, not a mutation-operation coordinator. The storage coordinator's `_mutationGate` covers storage maintenance only.

## Lifecycle-exclusive writers

CatalogDatabaseService creation and migrations run before normal workspace activation. Pre-migration backup remains mandatory. StorageManagement restore and relocation switch sessions and must be exclusive with exit backup; they cannot bypass the mutation drain while normal writers are admitted. CatalogRecovery creates staged copies and protects the live database before restore. These operations are distinct from normal runtime mutations.

## Existing backup inventory

* StartAsync creates at most one Automatic backup per UTC day after opening storage: retire this routine trigger.
* Settings Back Up Now currently creates an Automatic-kind copy: user-requested behavior must no longer enter automatic retention.
* Migration backup and pre-restore Recovery backup remain safety requirements.
* Relocation's staged SQLite transfer and retained original directory remain safety mechanisms, not routine backup history.
* No periodic or ordinary exit backup trigger was found on this baseline.

## Required boundary

Close operation admission atomically, drain previously accepted logical operations, then snapshot with SQLite online backup, validate the result, and publish it. Reads do not establish or substitute for mutation quiescence. Connection disposal and pool clearing do not prove a logical operation has completed. An outer operation must retain admission across all its transactions and asynchronous continuations. Separate-context continuations (notably Premiere HTTP receipts) require explicit ownership; an ambient context alone is insufficient.

No new mutation may start during snapshot. A failed/cancelled exit that returns to Lightflow must reopen admission without allowing stale leases to grant authority. Success-to-shutdown retains the closed boundary. A full operation failure or cancellation releases admission, but must not be interpreted as proof that all of the user's intended edits succeeded.

## Implementation and ownership

`CatalogMutationLifecycle` belongs to the storage coordinator and is attached to each activated session. Admission and quiescence use one synchronization boundary. Normal mutations remain concurrent; admission counts them rather than serializing them. Repository mutation entry points acquire admission before scheduling work or waiting in existing service queues. The Browser bulk command, reconciliation, asset service, recursive-root service, file-operation job/executor, and Premiere job scopes retain outer admission through their entire logical operation. Player preview-frame selection and Browser/Player rotation retain admission across their preliminary async work. Reads retain their existing paths.

Nested operations inherit admission only while their owner is live, and are independently counted. Premiere HTTP dispatch/receipt persistence uses a captured continuation capability tied to the live sending operation. It is not an unrestricted bypass flag. Released/stale capabilities are rejected. Lifecycle disposal wakes pending admissions and drains with an explicit disposed result. The concrete production composition must expose its owning lifecycle; missing writer ownership fails closed rather than silently allocating an unrelated gate.

Catalog restore/relocation participate as complete operations, so exit backup waits for them. Backup does not acquire the shared Preview maintenance lock: a Preview worker may be waiting to publish a new Catalog observation once admission reopens. Waiting on that lock while admission is closed would invert the dependency. The drained Catalog lifecycle alone keeps the session stable during the snapshot. Normal skipped/disabled exit does not invoke the expensive drain/backup/validation path.

## Preferences, destination, retention, and recovery

Both new and existing profiles deterministically default to enabled backup-on-close, with `Catalog Backups` beneath the selected application-data profile. Successful startup persists that default; a missing/corrupt/identity-mismatched Catalog never causes a storage fallback or replacement. Old managed backups remain in `Catalog/Backups`. Both durable preferences live in profile `settings.json`. Isolated backup destinations must remain beneath that profile root, matching #276. This is independent of #271's future Catalog/Preview relocation UX.

Destinations must be absolute and separate from Catalog, Preview, and temporary directories (including ancestor/descendant overlaps). Linked/junction paths are rejected so aliases cannot defeat this check. Normal profiles can select network/removable folders; every attempt probes actual write/flush access and reports failure instead of selecting a fallback. Missing folders are created only at the explicitly selected destination. A destination changed with the close dialog's folder picker is persisted even if the user subsequently skips that backup. Settings changes follow the existing Save Settings action.

User copies use `LightflowCatalog-User-v{schema}-{yyyyMMddTHHmmssZ}[_sequence].db`. They are ordinary standalone SQLite Catalog databases, with Catalog identity/schema inside the database and supplementary adjacent metadata. **All user-requested copies are retained until the user deletes them**; this is visible in Settings and the backup dialog. Their separate naming convention prevents old automatic retention, including older app versions, from recognizing them as cleanup candidates. Migration/pre-restore safety copies retain their existing managed convention and retention. Settings recovery lists current configured user backups alongside managed safety copies, and restore uses the existing identity/protection/validation path.

The copy is made with the existing SQLite online backup primitive, without a source full scan. Only source identity/schema are checked before copying; the staged copy receives the existing full recovery integrity check plus identity/schema comparison. After validation and durable flush, same-directory rename publishes the final database. Supplemental metadata failure does not invalidate an already committed, self-describing database; diagnostics retain that warning. Staging names end in `.incomplete`; cleanup failure cannot leave a final-looking partial backup.

Progress is deliberately indeterminate. The pinned provider's existing `BackupDatabase` and integrity command are synchronous calls running off the UI thread. Cancellation waits for a supported boundary after the current SQLite operation, cleans/isolate staging, and returns to retry/change destination/skip/keep using Lightflow. It does not interrupt accepted Catalog mutations. Once atomic publication commits, the operation has succeeded. The single-instance lifetime and connection/session cleanup remain owned by the normal application shutdown path after success. Backup failure/cancellation releases quiescence; successful exit holds it until `Closed`. Back Up Now uses the same domain operation but reopens admission after success.

## Acceptance paths

Workspace: `C:\Git\Agents\issue-272-catalog-backup`.
Executable: `artifacts\release\LightflowStudio\LightflowStudio.exe` beneath that workspace.
Launch with `--data-root "C:\Git\Agents\issue-272-catalog-backup\artifacts\acceptance-data"`.
Default acceptance destination: `C:\Git\Agents\issue-272-catalog-backup\artifacts\acceptance-data\Catalog Backups`.

Check enabled/disabled close, Skip This Time without disabling the preference, destination persistence, successful backup/exit, cancellation while accepted work drains or SQLite is running, failure/retry/change folder/explicit skip, manual backup without exit, and Settings/dialog visual consistency. Network/removable physical device behavior requires an available user-selected device/share; automated coverage uses task-owned disappearing/write-failure fixtures. No normal-profile data is needed.

Settings backup/recovery choices display local date/time and the backup database file size; schema and internal backup-kind names remain implementation metadata. Back Up Now takes the current Settings destination edit without requiring Save Settings. The dialog validates and persists that destination when backing up, while cancelling without saving a destination preserves the pending Settings edit.

Restore confirmation uses Lightflow dialogs and describes restoring the one Catalog from a selected backup, with a safety backup of current state first. History enumerates only the configured user destination and managed safety copies. Previous user destinations are not remembered; a backup's absolute path remains usable if the file is accessible. Restoring it does not change profile settings or the configured backup destination. Settings expanders visually group header and content; expansion changes the disclosure arrow, while the accent outline is reserved for keyboard focus.
