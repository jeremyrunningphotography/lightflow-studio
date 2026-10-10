# Conservative path and Catalog identity safety — #391

LF-WIN-DEV-009. Owner-authorized bounded implementation from current main
`0d7405af8402615b0f03a2df48df82c0fd8ca316`. #403 is merged at accepted head
`b73d72d9bfdac26051236106ca9441bac3c75c42`; #389 is Completed. Architecture,
required validation and packaged owner acceptance remain review gates. No merge is authorized.

## Authoritative owner

`Lightflow.Application/PortablePathIdentityValidator.cs` is the single neutral, read-only
path/identity validation owner. It references the accepted Domain storage facts and sole
`StorageLocationPolicy`, without native APIs, SQL, UI or filesystem calls. Comparison keys
are temporary diagnostics/preflight values, never replacements for persisted keys.

| Existing owner | Treatment in this slice |
| --- | --- |
| `CatalogDatabaseService.ReadCatalogIdentity`, `CatalogInfo` | Retain CatalogId creation/read, application identity, schema/integrity and migration ownership. |
| `LightflowStorageCoordinator.StartAsync`, `RestoreCatalogAsync`, `RelocateCatalogCoreAsync` | Replace direct expected/actual CatalogId comparisons with `ValidateCatalogIdentity`; retain all storage admission, recovery, configuration, activation and rollback behavior. Absolute mount spelling is never compared as Catalog identity. |
| `MediaRootService.CreateCoreAsync`, `RemapAsync` | Retain RootId and per-MachineId mapping ownership. Wrap admission with operation-owned Windows storage facts and shared path validation. Explicit remap changes the selected existing RootId's mapping only. Native object equality catches aliases mapping one physical folder to different logical roots. |
| `MediaRootService.ResolveAsync` | Validate raw relative spelling, current root facts and sibling/link observations before returning a physical path. Refusal has a separate identity status, so it cannot become a missing-file observation. |
| `MediaPathSemantics.NormalizeRelativePath`, `RelativePathKey` | Retain historical Windows lookup/serialization compatibility only. These methods are not portable admission authority. Existing uppercase keys remain byte-for-byte unchanged. The adapter explicitly translates Windows separators; neutral validation refuses a literal backslash. |
| `CatalogMediaAssetRepository.CreateAsync`, `RelocateAsync` | Retain AssetId and SQL/transaction ownership. Validate the root's candidate corpus inside the existing write transaction before insert/location update. Duplicate requests remain refused as `AlreadyExists` where existing callers expect that result. |
| `MediaAssetService.CreateAsync`, `ObserveAsync`, `RelocateAsync` | Retain observation/fingerprint and existing Lightflow-owned operation integration. Unsafe resolution fails before observation/missing-state or relocation mutation. No automatic relink or fingerprint match is introduced. |
| `MediaFolderEnumerator.EnumerateAsync` | Validate raw filesystem-relative names before the legacy helper could trim/fold them. Browser's nonrecursive enumeration and skip-link behavior remain owned here. |
| `CatalogReconciliationService.ReconcileAsync` | Validate historical and observed candidates before any writes. Skipped links refuse reconciliation rather than infer missing assets from an incomplete identity inventory. Enumeration may still display safe entries. |
| `CatalogBrowserAssetStateStore` / description, classification, collection, marker and subclip stores | Retain authored relationships by AssetId. No authored values are transformed or migrated. |
| `OwnedStoragePaths.Contains`, central `LightflowStorageLocations` | Retain dynamic Browser exclusions/configured storage routing. No new cleanup path or deletion is introduced. Existing Catalog admission owns native protected-root checks. |
| File-operation services / Catalog recovery | Retain physical operation, drain, SQLite backup, restore, relocation, staging and cleanup ownership. No second lifecycle, schema/provider/durability policy or identity store. |

No legacy helper is deleted in this bounded slice. Its retirement gate is migration of its
remaining Windows lookup/folder/operation consumers with separately approved key compatibility,
not a second normalization implementation or an implicit schema migration.

## Portable comparison and refusal

Relative candidates use slash-separated, nonempty components. Validation never trims,
renames, removes dots or substitutes absolute paths. Empty/dot/traversal components fail
containment. Absolute/drive-relative forms, colon, literal backslash, controls, Windows
unsupported characters, reserved device stems (including extensions and superscript device
digits), leading/trailing whitespace and trailing periods are refused. Malformed Unicode fails.

NFC plus invariant uppercase is a conservative in-memory comparison key. Each path component
prefix is also checked, so `Folder/a.mp4` and `folder/b.mp4` cannot create two folded directory
identities. Duplicate candidates, case-only names and NFC/NFD equivalents fail even on a
case-sensitive source: destination portability cannot assume the source's filename semantics.
This does not promise that every arbitrary Unicode/filesystem collation is equivalent.

Before path-based reconciliation, historical uppercase Windows keys are checked against their original stored path, without
rewriting either. Individually valid NFC or NFD paths remain authored exactly as stored.
An observed case/normalization equivalent with different spelling requires review instead of
attaching the historical AssetId. Missing media supplies no identity reassignment evidence.

Results distinguish Safe, UnsupportedPortableName, AmbiguousMapping, UnsafeContainment,
NativeEvidenceUnavailable and Cancelled. Existing operation result conventions carry actionable
diagnostics; cancellation propagates through the existing operation lifecycle where appropriate.
Refusal is not a Catalog repair, migration or grant of writer ownership.

## Native boundaries and lifetime

`MediaPathIdentityAdmission` is a thin Windows compatibility adapter consuming the accepted
`WindowsStorageLocationAssessor`, using MediaSource/Read, so network media remains eligible.
Fresh assessment/revalidation occurs within one operation; disposal releases its native root
and ancestor pins. Current resolved target, mount/volume, filesystem, availability and capabilities
must agree. Unknown facts fail closed. Native root containment also refuses a media root resolving inside the active Catalog directory, including a junction alias; an ancestor Browser volume anchor remains allowed. Existing same-machine mappings are assessed by that
same provider before SQL mapping writes; natural Browser anchors retain their overlap exception.
Unavailable existing mappings may conservatively prevent new mapping admission until their
native identity can be observed; their historical mappings are preserved.

The adapter enumerates sibling spellings only for referenced path components, checks native
reparse attributes and accepted Windows native file-information link counts beneath the root and refuses links there. Unknown native leaf evidence fails closed. It does not recursively scan an
entire drive when Browser admits a natural volume anchor. Remap preflight reads the historical
asset corpus outside a write transaction; the final transaction verifies that corpus has not
changed. Native root assessments stay owned until the operation completes. Child name/link
observations are snapshots, not a continuous hostile-namespace lease. Ordinary media writes
remain possible. Race-proof arbitrary media open/rename operations and physical fault durability
are not claimed or newly implemented.

macOS consumers use unchanged `MacStorageLocationAssessor` facts and the same shared
validator. macOS native tests observe real case/Unicode names and symlink attributes, then
exercise the accepted adapter's mount facts. Actual native name counts, rather than assumed
APFS case behavior, govern test expectations. This slice does not create a Mac application or
duplicate MediaRootService/SQLite implementation. Directory case sensitivity is deliberately
not inferred from OS or volume spelling; conservative comparison rejects collisions regardless.
Native namespaces are operation-local evidence, never proof that a Windows volume identifier
equals a different macOS identifier.

## Future #392 consumption and limitations

Closed-transfer/open preparation may pass a read-only Catalog identity, persisted root/asset
candidate corpus and freshly assessed destination facts to `ValidateCatalogIdentity`,
`ValidateCandidates`, `ValidateReconciliation`, `ValidateContainment`, `ValidateRootMapping`
and `ValidateNativeMapping`. It must retain the existing snapshot/finalization/integrity/activation
owners and required staging/hash verification. Safe path validation alone cannot authorize
transport or activation. New asset/location writes conservatively read the root corpus inside their transaction; repeated large imports can incur repeated root scans and are not newly scale-qualified. No #392 transfer code or Settings UI is implemented here.

Storage policy and filesystem qualification are unchanged: no active network Catalog, no ExFAT
admission, no concurrent writer support. Physical portability #406 remains unqualified for
production. There is no permissive historical key migration, automatic rename/relink, lossy
identity transformation, export/import/merge or replacement empty Catalog. Unsupported historical
names remain stored for explicit review.

## Qualification and reproduction

| Corpus | Evidence boundary |
| --- | --- |
| `PortablePathIdentityTests` on Windows/macOS neutral CI | Deterministic names, duplicate/prefix collisions, traversal, Unicode, CatalogId, cancellation and changed/absent storage facts; synthetic facts are not native qualification. |
| `PathIdentityPersistenceTests` on native Windows | Production-pinned SQLite, all-table logical snapshots with ratings/flags/labels/keywords/notes/collection membership/markers/subclips; safe remap/reopen, missing media, cancellation, NFC/NFD collisions, real case rename, hard-link and junction alias/child refusal. |
| `NativePathIdentityTests` in macOS adapter CI | Task-owned actual case/Unicode filesystem observations, symlink metadata, accepted native storage assessment/revalidation and unchanged fixture bytes. No disk-image or physical-drive certification claim. |
| Existing root/asset/reconciliation/Browser/storage/recovery suites | Existing safe Windows behavior and G2 negative fixtures remain unchanged. |
| Required Windows full Release and package/CI | Private desktop, pinned dependencies, startup/runtime/backup/restore/shutdown and installer checks. Exact results are in the Draft PR handoff. |

From `C:\Git\Agents\LF-WIN-DEV-009-PathIdentity`:

```powershell
dotnet test .\Lightflow.Application.Tests\Lightflow.Application.Tests.csproj -c Release --disable-build-servers -p:RestoreLockedMode=true
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -Filter "FullyQualifiedName~PathIdentityPersistenceTests|FullyQualifiedName~MediaRootTests|FullyQualifiedName~MediaAssetTests|FullyQualifiedName~CatalogReconciliationTests" -ResultsName path-identity-persistence.trx
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -ResultsName path-identity-full.trx
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller
```

Native Mac, from an isolated clone or hosted runner:

```sh
dotnet test Lightflow.Application.Tests/Lightflow.Application.Tests.csproj -c Release --disable-build-servers -p:RestoreLockedMode=true
dotnet test Lightflow.Platform.MacOS.Tests/Lightflow.Platform.MacOS.Tests.csproj -c Release --disable-build-servers -p:RestoreLockedMode=true
```

Owner acceptance uses only the freshly packaged task build and task-owned root:

```powershell
& "C:\Git\Agents\LF-WIN-DEV-009-PathIdentity\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-DEV-009-PathIdentity\.cache\acceptance-391"
```

Check ordinary Catalog open, media roots/safe Browser paths, Collection persistence, Settings,
normal close and reopen. Collision attacks belong to automated qualification. Draft PR remains
unmerged pending architecture and applicable hands-on acceptance; #391 and R2 remain open.
