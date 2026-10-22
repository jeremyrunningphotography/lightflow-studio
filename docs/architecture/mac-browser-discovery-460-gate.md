# Mac Browser discovery — preimplementation gate

LF-MAC-DEV-027, October 22, 2026. Source baseline:
`6d5aee56cb84786633d416bfce4b1d8c1e87ebbd` (independently fetched main).

## Superseding owner authorization — October 22, 2026

Jeremy approved bounded extraction within #460 in
[the owner decision](https://github.com/jeremyrunningphotography/lightflow-studio/issues/460#issuecomment-6346316550).
The historical stop report below is preserved. Implementation and qualification are
recorded in [the current handoff](../qualification/mac-browser-460.md).

## Historical disposition

**Stop for owner architecture decision.** #460 requires: “If it needs material
extraction, schema changes, new writer/lifecycle authority or unsafe storage
admission, stop for owner decision.” Existing shared contracts support read-only
Browser composition and Catalog repositories, but do not expose the Windows
discovery/reconciliation application workflow. This is a material extraction,
not merely Mac host wiring. No production changes have been made.

## Verified source boundary

- `Lightflow.Application/MediaDiscovery.cs`: shared media registry, contained
  nonrecursive enumeration and concurrency limit (default 2).
- `Lightflow.Application/MediaAssetContracts.cs` and
  `Lightflow.Catalog/CatalogMediaAssets.cs`: shared asset records/repository and
  persistence. Shared repositories are not a substitute for reconciliation.
- `LightflowStudio/MediaAssets.cs`: **Windows-project internal** asset service,
  sampled fingerprint service and observation/creation/missing/relocation logic.
- `LightflowStudio/CatalogReconciliation.cs`: **Windows-project internal**
  reconciliation contracts and sole reconciliation algorithm, participating in
  the existing Catalog mutation lifecycle.
- `LightflowStudio/DerivedWorkScheduling.cs`: Windows discovery refresh first
  reconciles, then optionally schedules Preview work. Absence of a scheduler
  already permits successful Catalog reconciliation without derived work.
- `LightflowStudio/BrowserRecursiveScope.cs`: **Windows-project internal**
  recursive walker and result aggregation, coupled to discovery/derived contracts.
- `LightflowStudio/BrowserStorage.cs`: Windows location resolver establishes
  volume/share anchors and reuses the most specific existing mapping.
- `LightflowStudio/BrowserNavigation.cs`: provisional known Catalog content,
  effective recursive scope, discovery invocation, cancellation and generation
  protection; not available through current Mac application contracts.
- `LightflowStudio/StorageManagement.cs`: Windows composition of these owners.
- `LightflowStudio/LightflowStudio.csproj`: net8.0-windows / UseWPF. Mac cannot
  consume this assembly as its shared application workflow.
- `Lightflow.Application/BrowserCatalog.cs`: explicitly read-only projection;
  `BrowserCatalogReader` never ingests/reconciles or schedules derived work.
- `Lightflow.Platform.MacOS/MacShellCatalogSession.cs`: readiness-checked reads
  borrow the sole CatalogLifecycle session across the complete request; no
  discovery/write operation is exposed.
- `Lightflow.Platform.MacOS/MacBrowserMediaEffects.cs`: native read-only media
  assessment, containment/link rejection and before/after mapping validation.
  These effects do not yet provide the Windows observation/admission seam.

## Windows versus accepted Mac behavior

| Concern | Shipping Windows source | Accepted Mac baseline |
| --- | --- | --- |
| Uncataloged navigation | Resolve accessible folder; reuse most-specific mapping or create volume/share anchor; reconcile visited folder | Filesystem Locations navigation; uncataloged folders have no ingestion |
| Supported formats | Shared extension registry: still, RAW, video, audio; known classification is not a codec/decode guarantee | Registry available, but no discovery commit |
| Timing | May present known Catalog content first; authoritative direct reconciliation then re-enumeration; Preview scheduling follows reconciliation | Read-only Catalog query and folder enumeration |
| Existing identity | Same root/path key retains AssetId; update source observations, not authored relationships | Existing Catalog identity is read only |
| Source changes | Versioned bounded head/tail fingerprint plus size/time; retry observation once if facts change during read | Availability observation only |
| External rename/move | Reconciliation matches path keys, not content relinking: old path becomes missing, new supported path gets new identity | No reconciliation |
| Lightflow-owned move | Separate RelocateAsync preserves AssetId after owned filesystem operation | Out of this slice |
| Missing sources | Mark direct children missing only after authoritative enumeration and successful supported observations; do not delete records | Existing records retained |
| Offline/errors/links | No missing inference for unavailable/failed enumeration; skipped links refuse reconciliation before mutation | Native availability/path refusal, read-only |
| Cancellation | Observed writes can survive interruption; unseen missing inference is withheld; latest navigation generation suppresses stale presentation | Read cancellation/generation guards, no ingestion |
| Duplicate/colliding paths | Portable identity preflight before mutations; repository duplicate checks and existing mutation owner | Shared identity/repository foundations available |
| Recursive scope | Stored recursive roots determine scope; same discovery pipeline per folder; default 4 concurrent folders, 20,000-folder cap; descendant errors diagnostic, base failure fatal | Existing Catalog recursive query only |
| Preview | Independent optional derived-work lifetime after Catalog commit | Intentionally absent |

Relevant inspected test sources: `LightflowStudio.Tests/CatalogReconciliationTests.cs`,
`MediaDiscoveryTests.cs`, `BrowserRecursiveScopeTests.cs` and `BrowserNavigationTests.cs`.
They cover supported/unsupported files, repeat identity, fingerprints, missing
direct children, unavailable/canceled scans, reopen, bounded enumeration/recursion,
partial descendant failures, automatic anchors and stale generation handling.
This is source evidence; Windows tests were not executed on this Mac.

## Concrete proposal for owner decision

Authorize a bounded extraction of the existing asset observation/fingerprint and
Catalog reconciliation contracts/algorithms into shared Application code, plus
the existing recursive discovery owner and the minimal optional derived-work
contract seam needed to preserve Windows behavior. Preserve Windows composition
and behavior through compatibility adapters; do not copy the algorithm into Mac.
Abstract Windows path effects/diagnostics at their existing boundaries rather
than introducing a second discovery engine. Review automatic Mac volume anchor
admission and native source observation explicitly; do not treat browsable media
storage as eligible active Catalog storage.

The Mac host would then compose these shared owners over its existing
CatalogLifecycle session/repositories/mutation authority, with readiness checks,
bounded cancellation and close/drain semantics. No new schema, writer, lifecycle,
Preview peer, storage support, publication or Move is proposed. Native observation
and anchor safety must be qualified before ingestion is enabled.

The extraction can remain part of #460 only if Jeremy explicitly expands its
authorization; alternatively a separately authorized prerequisite can supply the
shared workflow first. No new issue/assignment is created by this proposal.

## Hygiene completed before production

- 027 was unused in current registry/open PRs (#457/#448/#360), reserved in the
  task clone and on #460. Independent full clone and branch follow naming policy.
- #460 added to Project #4, Browser / unset Priority / Backlog, native parent
  #366. No implementation has begun. #456 is accepted foundation; final issue
  UI shows no native blocked-by edges. An attempted completed-prerequisite
  selection did not persist; no dependency creation is claimed.
- #29 and #73 moved from In Progress to existing Backlog: remaining native
  children are Backlog, with no active assignment or open implementation PR.
  Comments record the evidence; issues, children, dependencies and DoD preserved.
- #345 retained In Progress: #354 has open acceptance PR #360 and owner hardware
  gates. #366 retained In Progress for current Mac Browser work.
- #289 retained In Progress by Jeremy's explicit response during this pass.
  #406 scheduling was ambiguous; no child or dependency was changed.
- #393/#394/#395 remain incomplete. No priority/Area invention, unrelated product
  work, merge, release or next milestone.

## Validation and delivery limits

Only registry/evidence documentation changed. `git diff --check` is the relevant
local check. No implementation PR, CI claim, runnable discovery application or
acceptance launch command is supplied because production work is stopped at the
explicit architecture gate. A fresh branch-built Mac app and separate DARKMATTER
package qualification remain required after authorized implementation.
