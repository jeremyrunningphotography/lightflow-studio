# Premiere UXP edit-handoff research — #256

Research date: 2026-09-11. Status: **in progress; runtime proof pending**.
Starting main: `7d37f893f680fff69a2c8d6b2ec6cb0f221f5939` (fetched and reconfirmed).
Branch: `codex/256-premiere-uxp-research`. No production application files changed.

This document distinguishes **local observation**, **Adobe documentation**, and **proposed design**.
Documentation and Node tests do not satisfy #256's Premiere runtime Definition of Done.
Do not begin #257 on the strength of this report alone.

## Roadmap reconciliation

All six issues are in **Lightflow Studio Roadmap**, Project 4, Area **NLE Integration**.
Existing Area option IDs were preserved when adding the approved generic NLE Area.

| Issue | Priority | Status | Native parent | Native blockers |
| --- | --- | --- | --- | --- |
| #255 | P2 — Normal | In Progress | None | None |
| #256 | P1 — High | In Progress | #255 | None |
| #257 | P2 — Normal | Backlog | #255 | #256 |
| #258 | P2 — Normal | Backlog | #255 | #256, #257 |
| #259 | P2 — Normal | Backlog | #255 | #256, #257, #226 |
| #260 | P2 — Normal | Backlog | #255 | #256, #257, #39 |

Readback verified all relationships. #255 has five open native children and all DoD items
remain unchecked. Its execution summary now distinguishes active research from planned implementation.
#226 remains Player / P2 / Backlog / parent #72. #39 remains Video Processing / P2 / Backlog /
parent #27. Completed #157 remains Subclips / P2 / Done, with all six children closed.
No release targets, milestones, extra blockers, or new implementation issues were introduced.

## Versions and evidence ledger

- **Observed:** installed executable at `C:\Program Files\Adobe\Adobe Premiere Pro 2026\Adobe Premiere Pro.exe`
  reports ProductVersion `26.5.0`, FileVersion `26.5.0.99`.
- **Documented current release:** Adobe's September 9 release notes identify 26.5.
  [Release notes](https://helpx.adobe.com/premiere/desktop/whats-new/release-notes.html).
- **Target:** Premiere 26.5, manifest v5; actual UXP runtime version must come from the loaded plugin.
- **Observed outside Premiere:** synthetic 6-second 30/1 fps fixtures, 180 video frames, stereo audio,
  640×360 source and 320×180 proxy generated and FFprobe-checked; JavaScript syntax and authenticated
  Node HTTP boundary checks passed.
- **Not yet observed:** plugin load, API mutations, destination identifiers, UXP network request,
  save/reopen identity, undo, hard-boundary enforcement, source-file side effects, production installation.

Prototype and reproduction instructions: [prototype/README.md](prototype/README.md).
Generated data lives under ignored `artifacts/research/premiere-256/`, not the Catalog.

### Host setup checkpoint

UXP Developer Tool 2.3.0.5 is installed and the research manifest is registered as
`lightflow-research-256`. Load and Load & Watch returned **No applications are connected to
the service**; UDT itself reported a connection to its service on port 14001.
Premiere's Plugins preference initially showed developer mode unchecked. It was enabled,
and, with Jeremy's explicit permission, the open `Lightflow Test.prproj` was saved and
Premiere restarted. A new empty project was then created at:

`artifacts/research/premiere-256/1c976eac-c3bd-4bce-a4e6-b45f28b4ec27/Lightflow-256-disposable.prproj`.

The title bar verified the disposable destination. No source media has been imported and
no prototype API mutation has run. After restart and project creation, UDT still displayed
no connected host. Subsequent UDT input attempts failed with `coordinate input geometry is
unavailable`, then `computer-use request timed out: activate_window` and, after window
recovery, `computer-use request timed out: click`. Read-only process inspection reported
Premiere and UDT responsive. The host connection and UI input failure remain unresolved;
this is tooling evidence, not evidence that Premiere's UXP APIs lack the required features.

The published Adobe CLI 1.2.0 fallback was attempted only under ignored
`artifacts/research/uxp-cli`. Setup failed successively on missing `tar`, the published
`@adobe/uxp-devtools-app` dependency, and finally `yarn`. It did not produce a usable CLI.
Adobe's [CLI source instructions](https://github.com/adobe-uxp/devtools-cli/blob/main/packages/uxp-devtools-cli/README.md)
describe manual Yarn setup and warn that npm installation is unsupported. Prefer restoring
the current UDT host connection rather than treating this older package as a tested Premiere tool.
No loopback bridge was started and no UXP evidence or mapping file has been generated.

## Existing Lightflow ownership boundaries

Repository inspection: `CatalogDatabaseContracts.cs` exposes existing `CatalogId`;
`CatalogReconciliation.cs` and the media-asset services resolve stable `AssetId` through roots and
relative paths. Catalog identity must survive relocation; resolved absolute paths are transport inputs.
See [Catalog ADR](../../decisions/0001-lightflow-catalog-persistence.md).

`CatalogSubclips.cs` already owns `SubclipId`, `AssetId`, name, In/Out `TimeSpan`, source duration,
revision, and timestamps. Identical ranges deduplicate at creation. Current ordering is In then
SubclipId, not an integration-specific ordinal. SQL stores 100 ns ticks. `JobModel.cs` defines
exclusive-Out resolved ranges and typed per-item issues, progress, and provenance. Preserve these
semantics; do not round-trip through formatted timecode or decimal seconds.

`IndependentExportJobs.cs` is currently an Encoding-specific queue. Reuse Jobs presentation/state
concepts for handoff but do not pretend a Premiere mutation is an FFmpeg output or add it directly
to an encoder scheduler. Define a typed handoff executor if accepted batch work warrants Jobs.
Cancellation stops between safe mutations; it does not undo completed destination changes.

The legacy `PremiereHelper/Export-V1-Clips.jsx` renders V1 ranges through AME. It neither hands off
Catalog identities nor supplies reusable modern bridge code. Leave it separate.
`FfmpegCommandBuilder.Proxy` currently supplies a fixed NVENC/AAC scale-to-1080 command.
That command is not a durable proxy artifact model or a demonstrated Premiere-compatible policy.
#39 owns that evolution; this research does not create a competing encoder.

## Documented capability matrix

Every row below awaits runtime confirmation unless explicitly stated elsewhere.

| Capability | Documented surface / minimum | Evidence needed |
| --- | --- | --- |
| Active destination | `Project.getActiveProject`, `guid`, `path`, `getProject` / 25.6 | No-project result, project switching, save/reopen/Save As identity |
| Bin | `getRootItem`, `FolderItem.getItems`, `createBinAction` / 25.6 | One created bin, ID readback, rename/move persistence |
| Import | `Project.importFiles(paths, suppressUI, targetBin, asNumberedStills)` / 25.6 | Compare item IDs before/after; boolean alone is insufficient |
| Item identity | `ProjectItem.getId`, `getParentBin` / 25.6 | Project scope and persistence across reopen/rename/move |
| Source lookup | `findItemsMatchingMediaPath` / 25.6 | Returns substring candidates; never treat as authoritative identity |
| Native subclip | `createSubClipAction` / 26.3 | Read back exact In/Out, name, native media reference |
| Source clip markers | `Markers.getMarkers(ClipProjectItem)` and add Action / 25.6 | Point and range readback, not sequence markers |
| Marker identity | `Marker.guid` / 26.3 | Persist and re-resolve after save/reopen |
| Proxy | `canProxy`, `hasProxy`, `getProxyPath`, `attachProxy` / 25.6 | Positive and incompatible-fixture tests |
| Project metadata | `Metadata` schema and project-metadata Actions / 25.6 | Durable Lightflow stamp without source-file writes |

Sources: [Project](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/project),
[FolderItem](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/folderitem),
[ProjectItem](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/projectitem),
[ClipProjectItem](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/clipprojectitem),
[Markers](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/markers),
[Marker](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/marker),
[Metadata](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/metadata).

Subclip creation takes a name, TickTime start/end, a hard-boundary boolean, and `takeVideo` /
`takeAudio` options (documented defaults true). Hard boundaries are documented to prevent extending
past the range. Proposed default: both available media streams and hard boundaries; product review
must accept that policy after an actual trim test. Do not mutate the source clip's review In/Out
as a substitute for a native subclip.

## Actions, undo, and side effects

Adobe's 26.3 changelog requires Action creation inside `project.lockedAccess`; the callback is
synchronous. Create and consume Actions inside the same lock/transaction. Perform asynchronous
discovery beforehand, then revalidate destination and affected state. Do not hold locks over HTTP,
filesystem operations, or awaits. [Changelog](https://developer.adobe.com/premiere-pro/uxp/changelog/).

Bin, subclip, marker, rename, move, and metadata Actions can be grouped in named transactions.
Dependent discovery may force multiple groups: create object, discover its ID, then decorate it.
`attachProxy` and `changeMediaFilePath` are explicitly **not undoable**. Import is an asynchronous
operation outside the Action interface; its actual undo behavior remains to be tested. Project saves
and external journal writes are not part of an editor undo group. No all-or-nothing handoff promise.

Proposed order: import/reconcile source → transaction(s) for subclips/markers → verified proxy
attachment → result reconciliation. Keep proxy changes separately visible in results. Undo can
invalidate a previous success receipt; re-read destination state on retry. Never compensate a failed
marker by deleting a successfully imported source that an editor may already be using.

Source-marker/XMP preferences may cause on-disk metadata writes. The prototype uses only disposable
media; production requires explicit investigation of project-only marker/metadata behavior. Do not
write identity into media XMP or silently change the user's global metadata preferences.

## Candidate IPC and availability design

**Recommendation pending runtime proof:** Lightflow hosts a small authenticated loopback service;
the UXP companion is its client. Start with HTTP polling/long polling for commands and per-operation
acknowledgements. WebSocket is an optional latency optimization with the same protocol and journal,
not an exactly-once guarantee. Adobe documents fetch and client WebSockets with declared domains;
UXP cannot host a WebSocket server. [Network recipe](https://developer.adobe.com/premiere-pro/uxp/resources/recipes/network/).

Alternative: atomic versioned manifest files in one user-granted folder, periodically enumerated.
This avoids a listener but adds folder permission, acknowledgement, stale-file and latency concerns.
UXP's documented `fs` reference does not expose a watcher API; do not assume Node `fs.watch` exists.
Polling is the supported candidate until runtime evidence says otherwise.
[UXP fs reference](https://developer.adobe.com/premiere-pro/uxp/uxp-api/reference-js/modules/fs/fs).

Use `localFileSystem: request` for bootstrap folder selection and persistent tokens, with plugin
data storage for operational cache. Token invalidation and uninstall must be recoverable. Test
whether Premiere's native import/attach path parameters need broader permissions separately from
UXP file reads. Do not request `fullAccess` merely for convenience.
[Filesystem recipe](https://developer.adobe.com/premiere-pro/uxp/resources/recipes/filesystem-operations/).

Adobe documents `hostUIContext.hideFromMenu` for an invisible plugin invoked at application launch.
That is promising for near-one-click availability but not a proven guarantee in this experiment.
[Plugin tutorial](https://developer.adobe.com/premiere-pro/uxp/plugins/).
Panel hide/destroy hooks have documented Premiere limitations; heartbeat expiry must determine
liveness rather than trusting teardown callbacks.
[Lifecycle hooks](https://developer.adobe.com/premiere-pro/uxp/plugins/tutorials/add-lifecycle-hooks/).

| Destination state | Proposed behavior |
| --- | --- |
| Running, one project | Show destination summary; pin GUID/path/session at acceptance |
| Multiple projects | Use active project at acceptance, never first enumerated project; recheck each phase |
| No project | Report `NoActiveProject`; do not silently create a production project |
| Premiere closed | Report unavailable; optional user launch followed by handshake, never UI automation |
| Missing plugin / asleep | No heartbeat means unavailable, not proof of which cause; provide installation/open guidance |
| Incompatible plugin | Handshake rejects protocol/host/capability mismatch before mutation |
| Project switches mid-send | Stop, retain partial results, require destination reconciliation |

## Candidate handoff contract and reconciliation

All fields here are **proposed**, not a new production persistence schema:

```json
{
  "schemaVersion": 1,
  "handoffId": "existing-or-new-operation-uuid",
  "catalogId": "existing-CatalogId",
  "destination": {
    "sessionId": "authenticated-companion-session",
    "projectGuid": "observed-premiere-guid",
    "projectPathAtAcceptance": "absolute-project-path",
    "binId": "observed-item-id",
    "binPathHint": ["Lightflow"]
  },
  "assets": [{
    "assetId": "existing-AssetId",
    "resolvedSourcePath": "absolute-source-path",
    "sourceFingerprint": { "sizeBytes": "decimal", "lastWriteUtcTicks": "decimal" },
    "subclips": [{
      "subclipId": "existing-SubclipId", "revision": "decimal", "name": "selected-name",
      "inTicks": "10000000", "exclusiveOutTicks": "30000000", "ticksPerSecond": "10000000"
    }],
    "markers": [],
    "proxy": null
  }]
}
```

Future marker fields reference MarkerId, AssetId, revision and exact source-relative time.
Future proxy fields reference #39's artifact ID, AssetId, generation recipe/version, readiness,
verified path and source/stream fingerprint. Do not invent a separate durable proxy identity in #260.
Media paths, bin paths, item names, and filesystem timestamps are corroborating facts, not IDs.

Proposed durable Lightflow mapping key: `(CatalogId, destination registration, AssetId)` for a source,
extended with `SubclipId`, future MarkerId, or proxy artifact ID. Values include Premiere project
GUID, item ID / marker GUID, source and projected revisions, last observed destination properties,
operation IDs, and per-phase outcomes. This is destination projection state, not another Catalog.
If necessary to prevent duplicates after plugin uninstall, store durable mappings through the
existing Catalog service/migration/backup boundary; plugin storage alone is insufficient.

Project GUID and item ID stability are still unproven. A destination registration distinguishes
Save As/copies if GUIDs are preserved. Re-registration must never silently retarget a retry.
Project-only custom metadata may allow recovery of Lightflow IDs after journal loss. Prove scope,
copy behavior and persistence before relying on it; marker GUID mapping has no assumed custom property.

Use a persisted operation intent before dispatch and a result journal with `notStarted`, `applied`,
`verified`, `failed`, `conflict`, or `unknownOutcome` per phase. Retried requests reuse the same handoff
and operation IDs; changed payload/revision under an existing ID is rejected. A lost response means
reconciliation, not automatic resubmission. Import-readback/journal commits cannot be atomic across
two applications, so exactly-once execution is not guaranteed.

| Retry situation | Proposed response |
| --- | --- |
| Source imported, subclip failed | Keep source mapping; retry only subclip after verifying source |
| Source already exists without mapping | Offer explicit adoption only after identity evidence; ambiguous candidates stop |
| Source renamed/moved | Find by project-scoped ID; verify media; retain editor organization |
| Subclip renamed | Preserve destination edit by default; conflict if a newer Lightflow rename must project |
| Marker edited in Premiere | Compare last projected properties/revision; flag conflict, do not overwrite silently |
| Identical handoff twice | Return verified receipts / no-op; rescan state to detect editor undo |
| Project changed | Stop; never replay into the newly active project |
| Disconnect or Premiere closes | Pending mutations become unknown; reconcile on authenticated reconnect |
| Different proxy already attached | Conflict; require explicit replacement intent |

This is one-way handoff with conflict detection, not bidirectional synchronization.

## Time and marker-model feedback candidate for #226

Keep the existing stable MarkerId/AssetId/revision and point-marker scope. Premiere supports a
zero-duration marker, optional name/comments, a type string, color index, TickTime start/duration,
and a GUID. That does not require #226 to add ranges or expand its UI now. Optional future notes and
duration should be independently extensible; never encode them into a name. Keep NLE-specific type
and palette mappings in the adapter, defaulting ordinary annotations to Comment. Color/type are not
demonstrated to be portable enough to make Premiere enums the Lightflow domain.

Keep exact integer Lightflow ticks as decimal strings in JSON. Premiere exposes string ticks and
frame-aware constructors. [TickTime](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/ticktime).
Candidate conversion uses 254016000000 Premiere ticks/second: `LF ticks × 127008 / 5`.
Adobe's [Dynamic Media XMP guide](https://www.adobe.com/content/dam/cc/us/en/acom/products/xmp/Pdfs/DynamicMediaXMPPartnerGuide.pdf)
describes that DVA timebase; the prototype also verifies `createWithSeconds(1).ticks` before mutations.
Use integer/rational arithmetic, define rounding, report any quantization, and retain the original
Lightflow time. At 100 ns resolution, not every value converts exactly. Do not confuse source-relative
positions with starting timecode (the fixture deliberately starts timecode at 01:00:00:00).
Validate 30000/1001 and VFR using actual source PTS; avoid changing frame interpretation to make a
handoff appear aligned. Equal-time ordering can use start then MarkerId, matching Subclip conventions.

No marker issue scope expansion is accepted by this document. Apply surgical guidance after runtime
readback establishes the relevant behavior.

## Proxy feedback candidate for #39

Adobe supports attaching externally generated proxies through its normal workflow.
[Attach proxies](https://helpx.adobe.com/premiere/desktop/organize-media/ingest-proxy-workflow/attach-proxies-to-full-resolution-media.html).
Adobe's reference describes matching frame rate, duration, fielding and audio channelization, with
compatible size/pixel-aspect changes; it warns that some mismatches may not be rejected.
[Adobe reference, proxy compatibility](https://helpx.adobe.com/pdf/premiere_pro_reference.pdf).
That older reference is a compatibility hypothesis to test against 26.5, not a complete current
`attachProxy` validation contract.

Candidate #39 requirements: keep source frame-rate/timebase, duration, timecode/start and stream
layout in the artifact manifest; measure codec/container, dimensions/PAR, field order, sample rate,
channels and channel mapping. Preserve timecode when available. Generic omit/remap-audio policies
must not claim Premiere eligibility by default. No required filename suffix or directory structure
has been established for explicit API attachment. A successful boolean does not establish temporal
compatibility; verify state and playback. No codec whitelist, mandatory ProRes encoder, or metadata
stamp requirement has been proven. Synthetic MPEG-4/PCM MOV fixtures are experiments, not a chosen
production proxy preset. Test matching and mismatching channels, rate, duration and dimensions.

## Installation and security recommendation

Use a JavaScript-only `.ccx` companion, packaged by UDT, independently distributed with Lightflow
releases initially. Adobe permits independent distribution without Marketplace submission.
[Independent distribution](https://developer.adobe.com/uxp/guides/how-to/distribution/independent-distribution/).
Adobe's packaging guide says CCX does not need the CEP digital-signature/timestamp workflow; hybrid
native code has additional requirements, which this design avoids.
[Packaging](https://developer.adobe.com/premiere-pro/uxp/plugins/distribution/package/).
Creative Cloud Desktop or UPIA installs CCX; UDT/developer mode is the development loading path.
The installation page's note about developer-mode *connections* needs clean-machine testing before
promising no developer setup to production users.
[Installation](https://developer.adobe.com/premiere-pro/uxp/plugins/distribution/install/),
[UDT setup](https://developer.adobe.com/premiere-pro/uxp/introduction/essentials/dev-tools/).

Keep a stable distribution plugin ID, increment plugin version, and version the protocol separately.
Recommended first supported host floor: 26.5; do not mistake the 26.3 API floor for a tested product
support promise. Handshake includes companion version, protocol range, host version, actual UXP
version, required capabilities and destination session. No heartbeat alone can distinguish missing,
disabled or closed Premiere. Marketplace can be a later distribution channel, not an architectural
dependency. Verify upgrade, uninstall and incompatible-version behavior on a clean profile.
[Manifest](https://developer.adobe.com/uxp/guides/explanation/concepts/manifest/).

Proposed production security boundary:

- Bind explicit loopback only; validate Host and reject browser Origin/fetch-context requests.
- Authenticate every request with a high-entropy session token, scoped to a short-lived paired
  instance. Pair through an explicit user-granted bootstrap location; secure it with current-user
  filesystem ACLs. Rotate credentials, expire stale sessions, never put tokens in URLs/logs.
- Reject unsolicited CORS, redirects, arbitrary commands, executable paths and file writes. Allow
  only typed import/subclip/marker/proxy operations referencing an accepted immutable manifest.
- Canonicalize and verify media paths against accepted source facts; bound payload size, item counts,
  request duration and polling rate. Serialize mutations per destination and fail port collisions
  visibly. An authenticated client is not permission to mutate arbitrary paths.
- Stop/reconcile on stale heartbeat or destination mismatch. Do not broaden permissions to work
  around failures. Local malware with the user's privileges is outside the pairing-token boundary.

The prototype tests only the HTTP health/authentication boundary. It does not implement production
pairing ACLs, command transport, persistent journals, or background lifecycle.

## Remaining acceptance gates

1. Load companion and record actual host/UXP versions and every core API result.
2. Prove exact native subclip bounds, hard-boundary trimming, and audio/video inclusion variants.
3. Prove marker properties/GUID persistence and source-file/sidecar effects.
4. Prove proxy positive/negative compatibility and explicitly non-undoable behavior.
5. Prove rename/move/save/reopen/Save As reconciliation and unknown-outcome recovery.
6. Prove loopback permissions and hidden/background availability, multiple/no projects and reconnect.
7. Verify independent production installation/update without development tooling.

Only then finalize architecture decisions in #256, surgically refine #226/#39 and #257–#260 where
evidence changes their contracts, reconcile the Epic, and stop for product/architecture review.
