# Shared classification extraction — R1 / LF-WIN-DEV-001

Issue [#383](https://github.com/jeremyrunningphotography/lightflow-studio/issues/383), native child of Epic #366. Jeremy explicitly accepted architecture, automated validation and packaged hands-on functionality as PASS on 2026-10-08. Accepted head: `63fb05f594c140cdd94b2cd328a0f158df780620`; [PR #385](https://github.com/jeremyrunningphotography/lightflow-studio/pull/385) merged normally as `589beb399208cb574cb6f5e5f5b5ef29815f7f24`. #383 is Completed / Project Done, and this assignment is Done; Epic #366 remains Open / In Progress. Baseline: fetched main `ab4bfa5ee7a02745537ca1d46a832ad63ac5b376`.

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

Local Release evidence (unchanged implementation/test inputs at `3466831`; this follow-up adds only assignment/validation documentation):

- Neutral tests: 19 passed, zero skipped, with pinned locked restore.
- Windows private-desktop classification / Browser semantic / real Browser integration / lifecycle / new shared-Catalog cases: 82 passed, zero skipped; no owned processes remain.
- Full Windows private-desktop suite: 2,738 passed, one existing opt-in Premiere acceptance skip, zero failures; no owned processes remain.
- Premiere companion: 90 passed.
- X2CatalogProof Release build: zero errors/warnings.
- Release portable ZIP generated, checksum/contents verified; packaged startup, workspace/Jobs, Catalog backup/restore, SQLite runtime and dependency checks passed. Final hands-on executable is refreshed with the mandatory PullRequest command after the last commit; its timestamp and process cleanup are recorded in the PR.

New real-Catalog tests exercise whole-batch admission during quiescence (including completion), missing-asset partial failure through the existing dispatcher, fresh concurrent fields/readback and no-op revisions. Real WPF tests exercise changed same-scope presentation/projection, older revision rejection, captured selection/replacement scope, current Player refresh, Folder/static/Smart workflows and Catalog reopen. Existing tests remain authoritative.

Accepted-head [CI run 37852671360](https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/37852671360) succeeded: neutral Windows/macOS tests with .NET 8 and locked restore, required Windows Unit tests and installer/package validation. Tagged release publication was correctly skipped. Local actual portable ZIP generation/checksum/content validation and final-head PullRequest packaging passed startup/workspace/Jobs, graceful shutdown, Catalog backup/restore, icons, pinned SQLite runtime and dependency checks. The accepted executable was refreshed at 2026-10-08 22:19:05.409 UTC, after the final commit at 22:18:44 UTC; SHA-256 `94c02644c25d04ee19a641ce7f085c3939c9a7aa8a3765e4f6b1f1e17c0b572a`. No packaged smoke process remained at handoff. This is historical acceptance evidence for the unchanged implementation, not a claim that the retained executable is rebuilt from every later documentation commit.

An earlier implementation-head CI run encountered a Catalog file-lock error during cleanup of an unchanged workspace-restoration fixture. All 17 fixture cases passed in isolation; final accepted-head CI passed without weakening assertions or altering production shutdown. Accepted logs, TRX files, package metadata and the portable ZIP remain in the task-owned workspace. Merge/status documentation is handled in a separate post-merge PR rather than modifying the accepted implementation head.

Workspace: `C:\Git\Agents\LF-WIN-DEV-001-Classification`.

Required fresh executable: `C:\Git\Agents\LF-WIN-DEV-001-Classification\artifacts\release\LightflowStudio\LightflowStudio.exe`.

Isolated owner acceptance root: `C:\Git\Agents\LF-WIN-DEV-001-Classification\.cache\acceptance-r1`.

```powershell
& "C:\Git\Agents\LF-WIN-DEV-001-Classification\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-WIN-DEV-001-Classification\.cache\acceptance-r1"
```

Hands-on: exercise multi-selection rating assignment/menu toggle, flag assignment/clamped stepping, all Color labels/clear and keyword preservation in Folder/static/Smart scopes, Grid/Details and Browser/Player round trips. Reopen the isolated profile to verify durable values; compare existing visuals/interactions. Jeremy completed this packaged acceptance and authorized the normal merge; no remaining R1 acceptance gate is open.

This slice establishes neutral shared classification execution and Windows compatibility. It does not establish a Mac application, shared UI, Catalog portability qualification or authorization of later migration work.
