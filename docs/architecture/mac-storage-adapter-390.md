# macOS resolved storage adapter — R2-C / #390

LF-MAC-DEV-006. Owner-authorized bounded implementation on 2026-10-09, consuming accepted #388 / merged #401 without changing Domain, Application, Windows production, schema, provider or historical evidence. This is a native adapter and console fixture, not a Mac application or Catalog writer.

## Architecture and native facts

`Lightflow.Platform.MacOS/MacStorageLocationAssessor.cs` implements the existing `IStorageLocationAssessor`. A narrow Objective-C library owns public Darwin/Foundation APIs; no native types enter neutral assemblies. `tools/MacStorageHost` references this production implementation and calls shared `StorageLocationPolicy.Evaluate` and `Revalidate`. It creates synthetic fixture files explicitly as test setup; assessment itself performs no creation, deletion, write probe or Catalog open.

Public APIs: `lstat`, `stat`, `realpath`, `statfs`, `fstatfs`, `getattrlist(ATTR_VOL_CAPABILITIES)`, `access`, `opendir/readdir/closedir`, read-only `open`, `close`, Foundation URL resource values and JSON serialization. Foundation checks `NSURLVolumeIsLocalKey`, `NSURLVolumeIsInternalKey`, `NSURLIsAliasFileKey` and `NSURLIsUbiquitousItemKey`. No private framework, Disk Arbitration daemon modification, global SDK install, SQLite dependency or security-setting change is introduced.

Locality requires agreement between resolved `statfs/MNT_LOCAL` and Foundation's volume-local value. Disagreement or missing evidence is Unknown. A mount's location under `/Volumes` is not a locality rule. The real mounted SMB probe returned Network, including through a task-owned local-looking symlink. APFS disk images returned Local while their Catalog qualification stayed Unknown. External connectivity therefore cannot accidentally imply network locality or Catalog suitability.

| R2-A field | Adapter binding |
| --- | --- |
| Request | Same caller record, exact path spelling, operation ID, generation, role and operation |
| Assessment ID/version | New GUID every call; `StorageLocationPolicy.Version` |
| Time/expiry | UTC from injected `TimeProvider`; starts before observation; default five-second validity, configurable positive maximum thirty seconds; elapsed/stale results cannot admit |
| Canonical location | Native resolved existing target; for authorized creation/write operations, resolved immediate parent plus the exact intended missing leaf |
| Location identity | Device, inode, generation, birth timestamp, parent identity and intended leaf; no Catalog/Root/Asset ID changes |
| Volume identity | Native filesystem ID, mountpoint, source and private live-mount-anchor epoch |
| Filesystem identity | Native filesystem type and instance ID; no string-only suitability policy |
| Availability | Complete only after target or immediate creation parent can be inspected; unavailable/access-denied/broken paths produce Failed snapshots, never first-run inference |
| Read/write | Process access checks plus directory search access, companion parent access and read-only mount flag; these are metadata eligibility facts, not guarantees that a later operation succeeds |
| Filesystem qualification/durability/companions | Supported only for the bounded internal, case-insensitive local APFS evidence covered by accepted G2; other filesystems, external/unknown bus and case-sensitive Catalog filesystems stay Unknown |
| Locking | Explicit valid `VOL_CAP_INT_ADVLOCK` capability; absent evidence is Unknown |
| Aliases | Linked ancestors, symlinks, Finder aliases, cloud/ubiquitous items, unreadable spelling checks or non-exact case/Unicode lookup fail closed as Ambiguous |
| Containment | Explicit composition-owned protected roots; compare native ancestor/object identities as well as canonical nesting; missing/ambiguous boundaries cannot establish containment |
| Diagnostics | Actionable failure/cancellation text; no fallback location or empty replacement |

The internal APFS qualification maps accepted G2's real production-linked WAL/FULL, locking, companions, preservation and recovery evidence into facts. It does not certify hardware flush honesty, power loss, every APFS volume, physical external media or a Mac product. External local Catalog qualification requires additional accepted evidence; neither bus type nor generic writability supplies it. Accepted exFAT-image research remains unchanged and does not become a support allowlist. Shared policy alone decides which role can use the returned facts.

## Mount and resource lifetime

`f_fsid` alone is not treated as a sufficient reusable mount epoch. Each assessor owns a native context with read-only directory descriptors anchoring observed mounts. Each binding gets a new opaque epoch. Subsequent probes check that the retained descriptor's filesystem/root identity still matches the current resolved mount. A new binding changes VolumeIdentity, so prior suitability cannot be borrowed. A second assessor deliberately supplies a different binding and requires restarted preflight.

The native context is privately owned by a managed `SafeHandle`, with deterministic `Dispose` and finalizer fallback. Results contain no descriptor. Probe JSON is freed in `finally`; directory enumeration and autorelease resources are balanced. The native descriptor counter verifies twenty create/assess/reassess/dispose cycles return to baseline.

**Ordinary unmount may be busy while an assessor retains an anchor.** The task-image experiment observed Resource busy with one anchor, zero descriptors after disposal, then successful ordinary detach. Composition must scope the assessor to the protected admission/operation boundary and release it on completion, refusal and cancellation, before requesting eject. Do not make it an indefinitely retained singleton. Forced unmount, surprise removal and physical eject remain UNQUALIFIED; no force-detach was attempted. A snapshot is not an atomic OS-use lease and does not close every time-of-check/time-of-use race.

Native metadata calls run off the caller thread and cannot safely be interrupted. Cancellation is checked before and after observation and before admission by the consumer. A stalled OS call can delay completion; a late result expires and cannot authorize use. No timeout is presented as cancellation of native I/O.

## Conservative paths and ownership

No request is trimmed, case-folded or Unicode-normalized. Invalid NUL/UTF input fails before native lookup. Exact directory-entry spelling is checked conservatively; filesystem lookup equivalence is not permission to merge identities. On native case-sensitive APFS, `Case.bin` and `case.bin` retained distinct object identities. NFC/NFD lookup aliases retained one physical object identity and distinct original requests; ambiguous spelling was refused. Broader permissive root/key support remains #391, with no historical data migration here.

Missing intended leaf is allowed only for Create, Relocate, RestoreActivation or Write and only with an existing accessible immediate parent. Missing Open/Read and missing parents fail; no recursive location fallback occurs. All symlinks are currently refused for containment, including chains and unrelated targets. A linked network target is still truthfully classified Network before the shared policy rejects active Catalog use. Finder-alias positive-resolution support is deliberately absent.

Protected roots supplied to the assessor represent other storage ownership classes relevant to the requested operation. Composition must supply the actual Catalog/cache/temporary configuration, excluding the assessed role's own allowed root; an empty boundary set cannot establish containment. Existing Windows lexical routing remains unchanged.

## Catalog composition boundary

| Existing owner | Future Mac/shared composition responsibility |
| --- | --- |
| `LightflowStorageCoordinator.StartAsync` | Preserve explicit first creation versus existing/expected/missing state and configuration; request fresh native facts and evaluate policy before dispatching creation/open |
| `CatalogDatabaseService.CreateNewAsync` | Sole explicit creation owner, after independent first-creation authorization; Eligible never grants permission to create |
| `CatalogDatabaseService.OpenExistingAsync` | Existing identity/schema/integrity/durability owner; suitability does not establish valid Catalog bytes |
| `WindowsApplicationInstanceCoordinator.StartOrSignal` | Retain current Windows ownership. Future Mac instance/writer adapter is separately scoped; storage suitability grants no writer lease |
| `CatalogMutationLifecycle.RunAsync/QuiesceAsync` | Retain the sole complete-operation admission/drain owner; no new Mac lifecycle is added |
| `RelocateCatalogAsync`, `RestoreCatalogAsync` and existing recovery services | Preserve staging, identity/schema checks, rollback and prior files; assess source/destination at preflight and freshly revalidate immediately before actual open/create/activation |
| Current central location routing and `OwnedStoragePaths` | Retain routing; future accepted composition couples resolved containment and opened-object identity to actual use, with #391 owning broader identity integration |

Within one operation, fresh facts plus shared Revalidate detect changed mount, target, filesystem, capabilities or cancellation. A changed binding requires explicit restarted preflight; no path substitution is permitted. A new operation/generation needs its own assessment and Evaluate. Closed network backup remains RequiresVerifiedStaging; #392 owns local SQLite-aware finalization, verified closed transfer and verified local copyback before separate active-Catalog restore admission. No lifecycle extraction, Windows adapter change or production Mac hookup occurs in #390.

## Native qualification matrix

All byte fixtures are synthetic and task-owned. Native and controlled evidence are labeled separately in structured host results.

| Requested case | Evidence/result |
| --- | --- |
| 1–3 Existing internal APFS directory/file; missing creation leaf | Native Eligible; intended Catalog leaf never created |
| 4 Missing parent | Native Failed; no fallback or first-run inference |
| 5 Read-only location | Native permissions reject; disposable read-only APFS mounts report Write unsupported |
| 6 Unavailable location | Native missing Open/parent fails; unavailable-volume policy also covered by controlled facts |
| 7 Unknown filesystem capability | Native disk-image Catalog qualification Unknown; controlled policy refuses missing qualification |
| 8 Qualified physical external storage | UNQUALIFIED: no physical local external volume available. Native APFS image is Local, not Network, and is not falsely qualified |
| 9–10 SMB and local-looking network link | Native Network; active Catalog rejected, readable media Eligible, closed backup RequiresVerifiedStaging |
| 11–13 Unrelated local link, chain, broken link | Native refusal; broken target remains Failed |
| 14 Alias/containment ambiguity | Native protected-root overlap and links refused; actual Finder-alias fixture UNQUALIFIED |
| 15 Case-sensitive distinctions | Native disposable case-sensitive APFS distinct object IDs/bytes; no Catalog-volume qualification claim |
| 16 NFC/NFD | Native APFS aliases recorded without request rewriting or byte mutation; conservative refusal |
| 17 Mount changes | Native image detach/readonly-remount after disposal records a new epoch; ordinary live-anchor unmount blocked; shared changed-mount refusal covered by controlled facts; forced removal UNQUALIFIED |
| 18 Replaced symlink | Native new target identity; continuation rejected |
| 19 Expiry | Real native snapshot evaluated at exclusive expiry is refused; deterministic policy clock boundary |
| 20 Cancellation | Adapter pre-cancellation prevents admission; native in-flight interruption UNQUALIFIED and not promised |
| 21 Provider failure | Native missing/inaccessible paths and disposed provider fail closed; controlled Failed snapshot also refused |
| 22 Changed capabilities | Native readonly remount facts change; controlled shared revalidation refuses Write unsupported |
| 23–24 Network media/closed backup | Real mounted SMB eligible/conditional respectively; no transfer or database operation attempted |
| 25–26 No creation or mutation on refusal | Intended leaf absent; independent SHA-256 of existing Catalog-like bytes unchanged |
| 27–28 Resources/repeated cleanup | Native descriptor counter returns to baseline for twenty ownership cycles; host completes one hundred repeated assessments; image cleanup uses ordinary detach only |

Local validation: unchanged shared suite 68/68; adapter suite 8/8; production-host local/SMB/image matrices pass; standalone case-sensitive/readonly/Unicode image checks pass. The first host run failed due to numeric native boolean boxing; the marshalling error was corrected and original failure logs/fixtures retained. Initial sandboxed restores and image setup failed; isolated approved execution succeeded. No accepted historical evidence was edited.

Jeremy explicitly authorized GitHub publication on 2026-10-09. Required exact-head Windows/neutral/native CI and owner acceptance are recorded in the Draft PR handoff; this source document makes no merge or acceptance claim. The prepared workflow adds an isolated macOS adapter job and preserves existing Windows and neutral check names/requirements.

## Reproduction and evidence

Requirements exercised: Apple Silicon macOS 26.6, installed Apple Command Line Tools (`xcrun clang`, Foundation and public Darwin headers), net8.0 SDK 8.0.425/runtime 8.0.31 copied into this task's ignored `work/toolchain/dotnet`. These are tested tool versions, not a new global requirement or selected Mac product minimum. No package versions change. Adapter has no package reference; tests reuse exact repository xUnit/Test SDK versions and locked graphs. Native binaries are built locally and ignored.

From the independent full clone, use the existing task-isolating wrapper (historical source unchanged):

```sh
python3 tools/X2CatalogProof/run.py restore Lightflow.Application.Tests/Lightflow.Application.Tests.csproj --locked-mode
python3 tools/X2CatalogProof/run.py test Lightflow.Application.Tests/Lightflow.Application.Tests.csproj -c Release --no-restore --logger trx
python3 tools/X2CatalogProof/run.py restore Lightflow.Platform.MacOS.Tests/Lightflow.Platform.MacOS.Tests.csproj --locked-mode
python3 tools/X2CatalogProof/run.py test Lightflow.Platform.MacOS.Tests/Lightflow.Platform.MacOS.Tests.csproj -c Release --no-restore --logger trx
python3 tools/X2CatalogProof/run.py build tools/MacStorageHost/MacStorageHost.csproj -c Release -p:RestoreLockedMode=true
python3 tools/X2CatalogProof/run.py run --project tools/MacStorageHost/MacStorageHost.csproj -c Release --no-build -- --data-root "$PWD/work/new-native-fixtures"
python3 tools/MacStorageHost/qualify_images.py
```

For an already-mounted network location, append `--probe '<absolute existing mount location>'`; only metadata is read there, and the host writes exclusively to its new local data root. Never reuse an existing fixture root. Never force eject or run against personal Catalog files.

Raw task-local evidence: `work/native-matrix-01` (retained failed setup), `work/native-matrix-02`, `work/native-smb-03/results.json`, `work/native-image-04/results.json`, `work/lf006-images-*/results.json`, `work/evidence/mount-anchor.json`, wrapper command/log records and both test projects' `TestResults/*.trx`. Durable sanitized summaries are under `mac-storage-adapter-390/evidence`; raw path/host identities remain local. Final source SHA and file-hash provenance belong to the committed handoff, avoiding a self-referential source hash inside this file.
