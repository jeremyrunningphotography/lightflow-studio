# Catalog Settings/startup orientation: writer-ownership decision gate

LF-WIN-DEV-012, 2026-10-10. **BLOCK pending owner architecture decision.** This is orientation evidence, not production implementation or owner acceptance of #412.

Workspace: `C:\Git\Agents\LF-WIN-DEV-012-CatalogSettings`. Branch: `codex/412-catalog-settings-startup`. Independently fetched main: `9e9f3509f63f821fcaa91367eaa7cb8797935613`, including accepted #392 merge `a0a04da07dde9c8c7609f61ab3e21656c8c8b4dd`. Canonical checkout and other agents' workspaces were not changed. Jeremy explicitly authorized the bounded #412 implementation in the attached kickoff; its stop conditions require a gate when single-writer ownership cannot be demonstrated or current source materially conflicts with accepted architecture.

## Demonstrated finding

Ordinary startup acquires `StartupStoreEvidence.Begin` before SQLite opening. Its exclusive read/write journal lease beside the Catalog prevents a second coordinator from starting against that Catalog: the first diagnostic was refused with a sharing violation on `LightflowCatalog.db.startup-state`. This existing owner resolves the initial suspected startup gap.

However, `StorageManagement.Transfer.cs` disposes `_catalogStartup` in the quiescence callback (line 53), then activates and publishes the destination without a corresponding destination `StartupStoreEvidence.Begin`. Application instance ownership is scoped to the interactive session/user, and isolated profiles have different identities derived from their data roots (`ApplicationDataProfile.InstanceIdentity`). Consequently, application ownership alone cannot preserve the Catalog owner across this transition.

The second diagnostic ran the real native NTFS admission, production Catalog creation, accepted transfer engine, coordinator startup, instance mutex/pipe ownership and Collection repositories. It:

1. Obtained Primary status for two distinct isolated profile instance identities.
2. Created a synthetic Catalog through the first coordinator's existing lifecycle.
3. Moved it successfully through `RelocateCatalogAsync` while retaining that coordinator.
4. Configured a second task-owned profile to the moved Catalog's standard directory and unchanged CatalogId.
5. Started a second coordinator successfully against the same resolved database.
6. Committed a Collection through each live coordinator and read each other's committed Collection.

This demonstrates two authoring-capable lifecycle owners, not simultaneous SQLite transactions or database corruption. SQLite still serializes SQL transactions; that does not establish the required exclusive Catalog lifecycle owner. No separate OS child-app smoke was launched; both real instance identities and lifecycle owners were exercised in one test process. Native cross-process ownership and future Mac behavior remain unqualified.

## Evidence and scope

- Ordinary-startup probe: `artifacts\validation\daed91a4b60748caad059bc3afad0fd0\output.log`; `LightflowStudio.Tests\TestResults\lf012-writer-ownership-probe.trx`. Failed at the existing ownership lease, before the second SQLite open. This is counterevidence to the initial hypothesis, preserved transparently.
- Post-move probe: `artifacts\validation\f6cc9da0469c48b980fa0ceef48d2d4a\output.log`; `LightflowStudio.Tests\TestResults\lf012-post-move-writer-probe.trx`. **1/1 diagnostic passed**, confirming the unsafe baseline. A passing diagnostic is not a passing safety requirement.
- Synthetic data: `artifacts\lf012-writer-probe\617abc7516df4fc6bdfeadb86dad76b1`; CatalogId `04165754-6ccb-4a08-b534-3236f76e7856`. Both Collections persisted. Both coordinators were disposed; clean-shutdown publication was disabled for this intentionally conflicting diagnostic.
- The shared private-desktop runner reported zero owned processes after completion. The initial sandbox attempt could not create the desktop; the permitted native runner invocation was used, without bypassing isolation or switching desktops.
- Probe source is retained as `WriterOwnershipProbe.cs.txt`, excluded from compilation. Its assertions deliberately establish the unsafe baseline and must not become production regression acceptance assertions.

All data and outputs belong to this assignment. Nested isolated profile roots permit both profiles to reference the same synthetic Catalog without violating the existing `--data-root` containment policy. No owner Catalog, JRPhoto4T, external drive, actual LUT, or other task's data was used. No production files were edited. No fresh package, implementation PR, exact-head CI, full-suite result, LUT/media qualification or Mac product acceptance is claimed.

## Concrete proposed resolution for owner decision

Authorize correction of the **existing** lifecycle/`StartupStoreEvidence` ownership handoff as a bounded prerequisite within #412 before adding Settings/startup controls. Retain the source ownership lease while it remains the recoverable owner; acquire and retain the destination lease before its first SQLite open/activation, transfer its responsibility to the coordinator only after activation/configuration commit, and dispose the correct provisional lease on proven rollback. A failed destination lease must leave source/configuration recoverable. Unproven rollback remains RecoveryRequired. Do not add a second writer lifecycle, schema/provider change, mutex keyed only by path spelling, or filesystem policy exception.

Required qualification: a second profile/process is refused after move, before/during activation and after commit; rollback retains/reacquires the proven source owner; activation/save/rollback failure and restart each preserve one owner; native object/path changes do not authorize replacement or lease release through a revoked path. Apply the same existing ownership contract to explicit Open Existing and document the future Mac adapter requirement.

If Jeremy accepts this prerequisite, record that decision first on #412, replace the diagnostic with meaningful refusal regressions, then resume the originally authorized implementation and full validation/Draft PR/package handoff. The controlled-restart approach remains the proposed Open Existing shell transition so Player, Browser, Inspector, Jobs and stale callbacks retire through existing shutdown; it is not implemented or qualified here.

## Reproduce the diagnostic

In this task clone only, copy the retained source to `LightflowStudio.Tests\CatalogWriterOwnershipProbeTests.cs`, then run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -Filter 'FullyQualifiedName~CatalogWriterOwnershipProbeTests' -ResultsName lf012-post-move-writer-probe.trx
```

Remove only that temporary source file afterward. The fixture chooses a new UUID data root beneath this clone's artifacts and disposes both coordinators and instance owners. It retains synthetic data as evidence. Do not launch an owner executable or alter normal user storage to reproduce this finding.

## Durable roadmap disposition

Reserve assignment 012 without recycling its number. #412 remains Open/Backlog because the stop occurred during orientation before production implementation; it is not Review or Done. Preserve native parent #271, Catalog Area, unset Priority, completed blocker relationships #391/#392 and blocking relationship to #393. #271/#393 stay Open/Backlog; #289/#366 stay Open/In Progress. No parent Definition-of-Done checkbox advances. No merge, release/tag, accepted ADR rewrite, filesystem support expansion or assignment 013/014 experiment occurs.
