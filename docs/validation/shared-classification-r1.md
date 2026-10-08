# Shared classification extraction — R1 / LF-WIN-DEV-001

Issue [#383](https://github.com/jeremyrunningphotography/lightflow-studio/issues/383), native child of Epic #366. Owner-authorized bounded implementation; architecture and packaged hands-on acceptance remain required before merge. Baseline: fetched main `ab4bfa5ee7a02745537ca1d46a832ad63ac5b376`.

## Implemented boundary

- `Lightflow.Domain` (net8.0): public AssetFlag, AssetColorLabel and AssetClassification declarations, preserving values/defaults.
- `Lightflow.Application` (net8.0): existing command policy and IAssetClassificationStore, plus AssetClassificationService and minimal IClassificationMutationAdmission.
- Application references only Domain and existing Actions. Domain has no project/package references. Actions is unchanged and has no Domain dependency. No Windows targeting, WPF, WinForms, media/native, SQLite package or reverse application reference enters the new assemblies.
- The real WPF Browser port calls the shared executor. Its typed argument mapping and sequential mutation loop are removed locally. Declaration consumers and XAML enum references change mechanically; styles and visual behavior are unchanged.
- CatalogAssetClassificationStore retains its unchanged SQL/session gate/read-mutate-save-readback/no-op logic. CatalogMutationLifecycle implements the admission interface using its existing generic RunAsync method; no lifecycle algorithm changes.

The service snapshots ordered IDs before awaiting the one outer admission. Sequential UpdateAsync calls preserve fresh values, keywords, revisions and per-asset commits. Store/admission exceptions propagate to the unchanged Actions dispatcher, so a partial commit never becomes complete success. There is no fallback Get/Save mutation, additional gate/lock/transaction or cancellation policy.

The synchronous completion projection receives read-only committed results inside the outer admission, preserving publication-before-release ordering. WPF supplies its existing same-scope validation and publication; the service captures no controls, dispatcher, target or context type. Await continuations preserve the caller's interaction context. WPF continues Inspector invalidation, Browser/Player revision guards and query/Smart membership reapplication. Admitted writes finish for captured IDs after scope replacement, and stale publication is rejected. Same-scope presentation/projection changes still allow publication.

The source-linked X2CatalogProof now references the shared contracts rather than retaining copied declarations; its evidence and research behavior are unchanged.

## Validation and acceptance

Initial focused Release evidence:

- Neutral tests: 19 passed, zero skipped, with pinned locked restore.
- Windows private-desktop classification / Browser semantic / real Browser integration / lifecycle / new shared-Catalog cases: 82 passed, zero skipped; no owned processes remain.
- X2CatalogProof Release build: zero errors/warnings.

New real-Catalog tests exercise whole-batch admission during quiescence (including completion), missing-asset partial failure through the existing dispatcher, fresh concurrent fields/readback and no-op revisions. Real WPF tests exercise changed same-scope presentation/projection, older revision rejection, captured selection/replacement scope, current Player refresh, Folder/static/Smart workflows and Catalog reopen. Existing tests remain authoritative.

The PR CI runs the same neutral project on Windows and macOS with .NET 8 and locked restore, independently of the unchanged required Windows test/package jobs. Final-head full Windows tests, installer/portable CI, local package startup/dependency checks and timestamps are recorded in the PR handoff after execution; this document does not predeclare them passed.

Workspace: `C:\Git\Agents\LF-WIN-DEV-001-Classification`.

Required fresh executable: `C:\Git\Agents\LF-WIN-DEV-001-Classification\artifacts\release\LightflowStudio\LightflowStudio.exe`.

Isolated owner acceptance root: `C:\Git\Agents\LF-WIN-DEV-001-Classification\.cache\acceptance-r1`.

```powershell
& "C:\Git\Agents\LF-WIN-DEV-001-Classification\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-DEV-001-Classification\.cache\acceptance-r1"
```

Hands-on: exercise multi-selection rating assignment/menu toggle, flag assignment/clamped stepping, all Color labels/clear and keyword preservation in Folder/static/Smart scopes, Grid/Details and Browser/Player round trips. Reopen the isolated profile to verify durable values; compare existing visuals/interactions. Explicit owner acceptance remains the merge gate.

This slice establishes neutral shared classification execution and Windows compatibility. It does not establish a Mac application, shared UI, Catalog portability qualification or authorization of later migration work.
