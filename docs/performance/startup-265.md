# Startup measurement record — #265

Specification: [issue #265](https://github.com/jeremyrunningphotography/lightflow-studio/issues/265).
Starting `main`: `772b14a8c343d691368f6a38172815a202e768ce` (verified against GitHub).

## Controlled storage measurements, 2026-09-16

The Release measurement runner called `LightflowStorageCoordinator.StartAsync` with an
isolated local application-data root, inside a `StartupDiagnostics` scope writing to a
flushed log. Each measurement used a new process and disposed the coordinator afterward.
SQLite's backup API, with read-only source connections, made consistent copies of the
local Catalog (1,542,787,072 bytes) and Preview database (1,414,451,200 bytes). The test
root had minimal settings preserving CatalogId and a new machine identity; it therefore
does **not** represent the user's live media-root availability or workspace restoration.
No user databases or retained backups were changed by these experiments.

A same-day Migration anchor was seeded from the copied Catalog using SQLite backup and
normal backup naming/metadata. Both baseline processes retained that anchor and deleted
their newly created Automatic backup. The retention-only run used the same databases,
then the optimized runs reused its retained Automatic backup. No OS cache flush was
performed: these are warm-cache sequential measurements on one machine, not cold-start
claims or package-build timings. No tests or builds ran during the measurements.

| Implementation / launch | Storage initialization | Catalog backup |
| --- | ---: | ---: |
| Instrumentation only, baseline 1 | 87.099 s | 76.950 s |
| Instrumentation only, baseline 2 | 92.954 s | 82.722 s |
| Retention fix only, first valid Automatic snapshot | 97.526 s | 86.825 s |
| Retention fix only, repeated launch | 11.556 s | 0.012 s |
| Retention plus unchanged-open scan removal, launch 1 | 3.484 s | 0.010 s |
| Retention plus unchanged-open scan removal, launch 2 | 3.590 s | 0.012 s |
| Retention plus unchanged-open scan removal, launch 3 | 3.486 s | 0.010 s |

Baseline 1 breakdown: Catalog quick checks 1.731 + 2.788 s; Preview quick checks
1.647 + 3.760 s; backup source full check 36.473 s; backup copy 2.904 s; copy full
check 37.447 s; retention 0.102 s. Media-root startup in this isolated profile was
0.007 s. Optimized launch 1 retained one Catalog scan (1.696 s) and one Preview
scan (1.652 s), with no backup scans/copy. The retained same-day snapshot explains
the largest improvement; eliminating duplicate scans explains the remaining reduction.

The local runner and raw logs are in ignored `artifacts/startup-profile/`. Its core is:

```csharp
using var diagnostics = new StartupDiagnostics(writeLine);
var result = await LightflowStorageCoordinator.StartAsync(isolatedLocalApplicationData);
// Assert Ready, PreviewAvailable, and no RecoveryDiagnostic before accepting a measurement.
await result.Coordinator!.DisposeAsync();
```

For an ordinary packaged launch, `activity.log` now records process age on entry,
per-stage begin/end and elapsed durations before storage opens, through workspace
construction/restoration and presentation. Compare entries from the same PID. Nested
stage durations overlap; do not sum a parent stage with its children. First backup of
a UTC day and migration launches still include the required safety work and can be slow.

## Safety evidence

Deterministic regressions cover both creation orders for Automatic plus Migration or
Recovery, fresh-service repeated launches while the source is exclusively locked, UTC
rollover, and same-second filename collisions at 23:59:59 UTC. Existing retention bounds,
foreign-file protection, backup failure, invalid restore, and rollback tests remain active.
New validation tests cover one scan on each unchanged reopen, later corruption rejection,
pre/post Preview migration scans, and Catalog migration with a real validated pre-migration
backup followed by restore and re-migration with preserved identity.

The remaining Catalog pre-migration full check, backup source/copy full checks,
post-migration checks, restore validation/rollback, identity/history checks and verified
WAL/FULL/foreign-key policy are retained. No persistent scan cache, deferred safety work,
or change to workspace readiness was introduced. Focused Release validation: 93 passed.

Packaged visual acceptance remains Jeremy's responsibility: verify progress text and
splash styling, coherent workspace reveal, and two consecutive same-day launches.
