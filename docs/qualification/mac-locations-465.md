# LF-MAC-DEV-030 — I05 cloud availability / current qualification

I04 owner acceptance passed sidebar/navigation, cloud-only protection, unsupported
folders, unrelated offline mappings, Catalog preservation and mount lifecycle.
Desktop/Documents warnings prompted the bounded I05 investigation/correction.

## Verified diagnosis at I04 source

- One bounded non-recursive check returned Documents: zero assets, one unavailable
  source; Desktop: eight assets with expected cloud exclusions; Downloads: 36 assets.
  Native metadata established the Documents top-level supported source was dataless,
  not downloaded and had zero allocated blocks. Desktop resident files were actually
  fingerprinted and returned. No evidence justified hydrating sources or loosening
  identity/storage checks. This does not establish that every owner-reported missing
  file was reproduced; a specific missing resident source remains an owner follow-up.
- Expected exclusions were encoded as a generic reconciliation Diagnostic, causing
  the Browser to say Scan incomplete even when direct discovery completed safely.
- A separate bounded recursive Documents run exposed a genuine rejected filesystem
  link in an application-presets descendant. That refusal must remain visible.
  The misleading direct warning does not require recursive traversal to reproduce.

## I05 correction

- Typed UnavailableSourceCount travels through reconciliation, recursive aggregation
  and Browser snapshots. Expected cloud-only/unknown-residency exclusions receive an
  availability/action notice, not an incomplete-scan label. True refusals still carry
  detailed diagnostics and show a concise Scan incomplete warning.
- Mac metadata refusal of one source is recorded for that entry, leaving unrelated
  safely identified resident siblings available. Mac composition explicitly opts into
  partial source reconciliation; other callers retain strict linked-entry refusal.
  Refused/link scopes disable missing-state inference. Unsafe sources are never
  opened or merged, and existing identities/authored data are preserved.
- Recursive aggregation retains genuine reconciliation diagnostics and availability
  counts. The Mac projection includes only successfully reconciled eligible sources,
  excluding unsafe and stale records while preserving them in the Catalog.
- Native no-materialization reads, cloud residency rules, storage admission, I04
  offline-root handling, I03 mount lifecycle and accepted visuals are retained.
  No timeouts, progress, concurrency or spinner changes.

## I05 validation

Release Application 141, Catalog 52, shared UI 59 and Mac native 59 pass. Regression
coverage includes mixed resident/cloud sources, direct and recursive paths, alias/link
refusals alongside safe siblings, zero-resident availability, strict default behavior,
missing-state protection, authored ratings/collections and identity across reopen.
Two old native tests expected whole-folder exceptions for links; updated assertions
verify explicit partial warnings, rejected links and preserved records instead.

A final bounded real-folder check returned Documents 0 resident/1 unavailable,
Desktop 8 resident/9 unavailable, Downloads 36 resident/0 unavailable; all direct
operations completed with no genuine Diagnostic. No automatic cloud download or
personal-file change occurred. Private filenames/native metadata evidence stays local.
Native test cloud metadata is simulated only for registered task fixtures.

Fresh isolated I05 packaging, real owner acceptance, independent Windows local
qualification, exact-head CI and explicit merge authorization remain gates. No
routine Computer Use/screenshots/ZIP. No Chronicle entry warranted. Project/native
parent/dependency tools remain unavailable; no roadmap state is invented.

---

## Historical I04 qualification

# LF-MAC-DEV-030 — I04 acceptance corrections / current qualification

I01/I02 visuals and I03 automatic mount lifecycle are owner-accepted baselines.
I04 addresses only resident iCloud media and unrelated offline mapping conflicts;
recursive-spinner timeouts/progress/concurrency remain explicitly deferred.

## I04 implementation and boundaries

- A MediaSource-read-only native probe accepts resolved cloud-managed directory
  identity, retaining exact spelling, alias/symlink and protected-root containment
  checks. The ordinary Catalog/cache probe and shared storage admission policy
  retain their cloud restrictions.
- Supported files receive metadata-only residency checks. Dataless, not-downloaded
  or unknown cloud residency is not opened; resident media in the same folder can
  still reconcile. Unavailable entries remain visible to reconciliation's seen-set,
  preserving existing AssetId/source state instead of inferring missing records.
  The current media projection omits unavailable sources with actionable feedback.
- Native fingerprint sampling preserves the existing v1 algorithm. The entire
  synchronous metadata/open/read operation disables dataless materialization using
  Darwin's thread-scoped I/O policy, restored in finally. The source is opened with
  no-symlink traversal and checked against native identity before/after sampling.
  No cloud download API or automatic hydration is used. Failed policy setup refuses
  reading. Cancellation is checked around the bounded native operation.
- An unavailable root can be excluded from mapping-conflict checks only when native
  evidence establishes an absent top-level /Volumes boundary and a distinct live
  candidate mount, resolved containment/spelling and stable native object/mount
  identity. Same-volume missing folders, overlap, aliases, ambiguous/unknown facts
  and genuine conflicting identities remain refused. No offline mapping is removed
  or rewritten; RootId/AssetId and authored data survive remount/revisit.
- Shared changes add an optional source-availability fact (default true), skip
  observation of unavailable files while retaining their seen identities, and show
  actionable navigation refusals. Windows providers keep their existing defaults.

## I04 validation

Release tests: Application 141, Catalog 51, shared UI 57, Mac native 58; all pass.
Native tests simulate cloud resource metadata only for registered task fixtures;
no iCloud account, permissions, production media or cloud flags are changed.
A disposable task-owned APFS disk image exercises actual mount/remount, offline
mapping coexistence, genuine conflict rejection, identities and authored retention.
The native test helper is excluded from the shipping bundle.

Intermediate test compile/API mismatches were corrected. A subsequent full native
run caught changed missing-folder classification; preserving the existing native
ENOENT/access-denied statuses restored the full passing suite. Logs remain local.
Existing analyzer/headless-key warnings remain. No routine Computer Use, screenshots
or ZIP is required or performed. Actual owner iCloud/provider behavior and physical
storage remain hands-on gates. A fresh isolated arm64 package, independent Windows
qualification, exact-head CI and explicit merge authorization remain required.
No Chronicle entry is warranted for these bounded corrections.

---

## Historical I03 qualification (superseded by I04 above)

# LF-MAC-DEV-030 — I03 automatic volume lifecycle / current qualification

Jeremy accepted I01 (9/9) and I02 (6/6) as the functional/visual baseline. I03 adds
only automatic macOS volume lifecycle reconciliation; accepted layout is retained.
The owner-approved I03 workflow revision removes the routine screenshot/ZIP gate.
No Computer Use or actual mount/eject operation is performed by this implementation.

## I03 implementation

- `MacVolumeChangeSource` owns native NSWorkspace mount, unmount and volume-rename
  subscriptions. It registers with the workspace notification center, not the default
  center. Native callback/disposal synchronization keeps the managed delegate alive
  until callbacks stop. No network scanning, mounting or credentials.
- `BrowserLocationMonitor` owns the subscription, a 250 ms event debounce and cancellable
  serial reconciliation. Notifications are hints; fresh native snapshots determine
  the current list, so duplicate/out-of-order payloads cannot invent stale mounts.
  There is no recurring timer or continuous polling. Late results are rejected.
- The Mac host dispatches reconciliation through Avalonia's UI dispatcher. A ready
  Catalog also requests reconciliation once per Browser epoch, closing the initial
  open/subscription race without turning the existing Catalog readiness timer into
  volume polling.
  Shutdown unsubscribes and cancels/drains the monitor before closing the Catalog.
  Failed native observer setup leaves manual Refresh and actionable feedback available.
- `BrowserViewModel.ReconcileLocationsAsync` updates only transient location presentation
  using cached Catalog mappings. It does not invoke navigation, discovery, source
  probing, recursive media scans, Catalog readers or mutations. Existing rows/expanded
  branches are reconciled without re-enumerating retained folder trees.
- Known removed local/external/network mounts are excluded from the live list, including
  Catalog shortcut fallbacks. Current path, RootId/AssetId, history, cached assets and
  selection remain; ongoing navigation is cancelled and actionable unavailable text
  replaces stale availability. Reconnection restores entries and offers explicit retry
  or manual Refresh, without automatically rescanning media.
- Native display-name changes update labels while preserving path/Catalog identity.
  If macOS also changes the mount path, the new native path gets its own entry and the
  old active path stays unavailable; no automatic Catalog mapping rewrite occurs.
- Native metadata reads are limited to one actual snapshot per provider. Cancellation
  cannot forcibly interrupt a blocked OS metadata call, but cancelled queued hints do
  not accumulate native snapshot threads. The existing presentation timeout remains.

Official notification contracts: [mount](https://developer.apple.com/documentation/appkit/nsworkspace/didmountnotification)
and [volume rename](https://developer.apple.com/documentation/appkit/nsworkspace/didrenamevolumenotification).
The notification center choice follows these contracts; actual mount delivery still
requires owner hands-on qualification with real storage.

## I03 validation

- Application Release: 141 passed, including event burst coalescing, in-flight supersession,
  cancellation/disposal and an uncooperative native read.
- Shared UI Release: 56 passed, including local/external/network removal/reconnection,
  duplicate snapshots, rename identity, current path/history/selected asset preservation,
  manual Refresh, late/suspended snapshots and background-event UI dispatch. Counter
  assertions verify automatic events do not call Catalog reads/discovery/source probes
  or re-enumerate retained folder trees.
- Mac native Release: 52 passed, including existing classification/Catalog safety tests
  and real native subscription/disposal driven by synthetic process-local workspace
  mount/unmount/rename notifications. No actual volumes were mounted or ejected.
- Mac host Release compilation passed with zero errors. Final self-contained package,
  exact source/build hashes and CI status are recorded in the I03 completion report.

Original failures are retained in task logs: the old disconnected-share assertion
expected generic missing-folder text; it now expects precise unmounted-volume feedback.
An initial dispatcher overload compilation error was corrected. Final focused suites
pass; existing obsolete headless-key and native platform-analysis warnings remain.
No persistent Catalog schema, admission policy, writer or Windows WPF shipping change.
Windows regression/local package qualification remains independent.

## Workflow documentation and I03 handoff

Separate narrow documentation commit `50cb7a3459cb947ea84a6eb299787cda52280bff`
adds the owner-approved implementation handoff convention to root `AGENTS.md`.
Routine implementations use a self-contained Markdown completion report; significant
visual redesign/uncertainty warrants screenshots/evidence packaging. Explicit permission
before desktop control and all acceptance/test/Windows/CI/merge gates remain unchanged.

Task-relative paths in the existing clone:
- Runnable bundle: `artifacts/lf030-i03-app/Lightflow Studio.app`.
- New isolated data root: `artifacts/lf030-i03-acceptance`.
- Disposable local media: `artifacts/lf030-i03-navigation`.
- Report/provenance/test evidence: `artifacts/lf030-i03-handoff`.

The final single fenced Markdown report supplies absolute executable/data-root paths,
a complete Terminal launch command, exact provenance, validation and owner checklist.
No routine screenshot/Computer Use/ZIP gate. Owner tests actual JRP and local/external
mount/unmount, reconnection and supported rename. Never force-eject user storage as
an agent test. General network outage/reachability, network scanning/automatic mounting,
credential management and user-managed Favorites remain out of scope.

Keep Draft PR #467 unmerged and #465 open. Required: Jeremy's I03 hands-on acceptance,
independent Windows qualification, exact-head CI and explicit merge authorization.
Project/native-parent/dependency tooling remains unavailable; no field changes claimed.
No release/tag, broader Epic completion or historical rewrite. A new Chronicle entry
is not warranted for this bounded correction; accepted milestone evidence is preserved.

---

The following I02/I01 material is historical and does not reinstate a screenshot gate.

# LF-MAC-DEV-030 — I02 correction / current handoff

Owner-approved I02 in #465 supersedes I01 section-collapse behavior. Jeremy passed
all nine I01 hands-on checks; that functionality is the accepted baseline, not final
issue/merge acceptance. Continue `codex/030-mac-locations` and Draft PR #467 in the
existing independent clone. No new assignment or implementation branch.

## I02 bounded implementation

- Favorites, Devices & Volumes and Network use static text headings in the existing
  compact section layout. No section chevron, button, event handler or collapse state.
  Existing separators, muted type and 32-point heading rhythm are retained.
- Tree artwork uses shared theme resources:12 DIP and1.25 stroke. Folder/volume/share
  targets and reserved disclosure columns remain32 DIP; icon and label columns remain
  unchanged. Navigation toolbar icons remain16 DIP. Verified server organizational
  parents still expand/collapse; section permanence does not change tree policy.
- I01 complete row construction, retained icon/label controls, keyed reconciliation,
  leaf geometry, focus and async generation checks are unchanged. Native mount adapters,
  BrowserViewModel, Catalog/discovery and Windows shipping inputs are unchanged in I02.

Changed implementation files: `Lightflow.UI/BrowserPane.cs`, `BrowserPresentation.cs`,
`Themes/Lightflow.axaml`; focused tests in `Lightflow.UI.Tests/LocationsTests.cs`.

## I02 Stage A evidence and exact paths

Release locked-restore tests:51shared UI /50Mac native PASS. UI coverage replaces
superseded section-collapse tests with permanent visible headings/top-level locations
through address/history/Refresh. It verifies small artwork vs32-DIP targets, retained
icon/label/disclosure objects and focus, fixed leaf/button-column alignment, and
preserved verified-server collapse across Refresh. Two existing obsolete headless-key
warnings remain. An initial test-only compilation error (StyledElement vs Visual)
was corrected; its original log remains under `work/i02-ui.log`.

Fresh acceptance fixture uses only public native console lifecycle/discovery, a new
Catalog, one task PNG and authored collection `LF030 I02 acceptance notes`. No GUI
launch or visible desktop control during I02 Stage A. Existing I01 artifacts stay intact.

App: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/lf030-i02-app/Lightflow Studio.app`.
Data root: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/lf030-i02-acceptance`.
Task folders: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/lf030-i02-navigation`.
Staging: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/handoffs/LF-MAC-DEV-030-I02`.
Proposed ZIP after permission/capture: `LF-MAC-DEV-030-I02-handoff.zip` in its parent.
The staged manifest and fresh bundle provenance record exact published source/tree,
build identity, every file hash, timestamp/dependency/icon checks and actual CI state.

After separate explicit I02 desktop permission, Terminal launch:

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/lf030-i02-app/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/lf030-i02-acceptance"
```

Equivalent PowerShell on this Mac:

```powershell
& "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/lf030-i02-app/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/lf030-i02-acceptance"
```

Request5–7minutes of I02 Computer Use: capture all three permanent headings and
12-DIP tree artwork; expand built-in/home and task Trip/Day2; inspect leaf/expandable
alignment, navigate/select task media and Refresh; quit normally and release desktop.
Owner-assisted exact-command launch may be needed because Computer Use blocks Terminal.
No unrelated resize retest or user-volume unmount. No I02 visual acceptance from tests.

I01 source `deae87b2c1582d04c2fa5b467bb35ac1c93d7d14` CI41380067750 is verified
SUCCESS; it is historical evidence, not I02 exact-head CI. Fresh I02 CI, Jeremy's I02
hands-on approval, independent Windows qualification and explicit merge remain required.
Project/native-parent/dependency operations remain unavailable; no field changes claimed.
Keep #467 Draft/unmerged and #465 open. No release/tag or broader Epic completion.
This bounded correction needs no new Chronicle entry. The remainder is historical I01.

---

# LF-MAC-DEV-030 — Mac Locations Redesign

Issue #465 is authoritative. Session: **LF-MAC-DEV-030 — Mac Locations Redesign**.
Independent full clone `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations`,
branch `codex/030-mac-locations`, baseline main `1ea4da350371c277eb95c6208310a5d355cc5085`.
Canonical checkout and earlier agents' outputs are untouched. 030 was available in
current main/open PR inventory; reservation is in the assignment registry and issue.

## Implementation and source investigation

The approved embedded mockup was downloaded and visually inspected before UI edits.
The shared BrowserPane previously created a plain-text button, returned before icon
construction for non-prefetch locations, replaced the entire icon/label content on
model updates, and cleared/re-added children after enumeration. These code paths
explain incomplete initial rows and unnecessary layout work; native visual acceptance
of the correction is still required. Every folder now starts with a complete reserved
32-point disclosure / 18-point icon-column / flexible ellipsized-label row. Updates
retain icon, label and content controls. Leaf resolution hides only the disclosure
inside the reserved column; keyed child reconciliation retains surviving controls.
Generations reject stale native reads; removed branches are retired. Collapse hides
retained children immediately and does not initiate another native read.

Native read-only `getfsstat(MNT_NOWAIT)` snapshots and Foundation volume resource keys
supply mount paths, localized names, volume UUIDs, locality, internal/external and
browsability facts. No path/name-based storage classification or server scanning.
Only matching native volume UUID/source suppresses an alias of the startup volume;
there is no `- Data` name heuristic. Non-browsable/hidden native volumes and mount
namespaces outside the visible `/Volumes`/startup surface are not sidebar entries.
This does not claim general APFS volume-group discovery or storage admission.
System navigation identity remains `/`; its actual native name is displayed, or
truthful `System Volume` fallback when unavailable. Home uses the resolved account
folder name. Unknown internal/external facts remain generic local-volume presentation.

Three independently collapsible sections default expanded in each fresh pane session.
Their state survives navigation/address/history/Refresh, without navigation side effects.
Built-in Favorites are lazy expandable locations, never user-managed Favorites.
Server parents are non-navigable organizational buttons. Only a validated kernel SMB
mount source establishes a server; unknown formats remain direct Network shares.
No credentials, DNS scans, mounting or Connect to Server functionality is added.
Existing Catalog folder shortcuts remain above the grouped native locations; they
are existing Catalog references, not user-managed Favorites. A previously observed
network mount cannot be reinserted by a disconnected Catalog mapping on Refresh.
Its current path/history/selected Catalog asset remains, with existing actionable
unavailable text. Reconnection restores the live entry without deleting records.
Display metadata is separate from path/RootId/AssetId identity.

## Owner-confirmed discovery boundary

Accepted #460 invokes automatic supported-media discovery on visited folders through
`IBrowserDiscoverySession`. #465 also says navigation must not silently ingest media.
This patch preserves the accepted owner and behavior and adds no discovery/ingestion
path, writer, schema or Catalog-storage eligibility change. Jeremy explicitly confirmed during Stage A: **Preserve accepted #460 discovery;
#465 adds no ingestion behavior.** This clarification is recorded on #465. No accepted
discovery policy is changed by this sidebar implementation.

## Automated evidence and limitations

Release locked restores, task-owned SDK 8.0.415 and dependency cache; headless Avalonia
only, no visible application launch or Computer Use during Stage A.

- Shared Application: 137 passed; Catalog: 50 passed.
- Shared UI: 51 passed, including complete initial rows, retained icon/label controls
  and leaf text geometry, section defaults/session collapse, address/history/Refresh,
  server/fallback, disconnect/reconnect selection/history, stale network expansion.
- Mac native: 50 passed, including classification, identity-only deduplication,
  duplicate names, SMB-source validation/fallback, real native snapshot/cancellation,
  existing permission/link rejection and real Catalog safety regressions.
- Independent native Catalog process qualification: 23 checks passed; original JSONL
  and summary are retained in task-owned evidence.

Initial sandbox MSBuild IPC failure, initial disconnect/prefetch failures and native
JSON boolean failure are retained under `work/`; later passing runs supersede them
without relabeling historical evidence. Two existing obsolete headless-key warnings
remain. TRX files live in each project TestResults directory.
Read-only snapshot on this Mac observed internal APFS `Macintosh HD` at `/` and SMB
`JRP` at `/Volumes/JRP`; this is not permission/read/navigation or visual acceptance.
No physical external drive, mount interruption, network fault injection, VoiceOver,
native focus/scroll or scale qualification is claimed. Native calls cannot safely
interrupt blocked OS metadata work; model timeout/cancellation rejects late results.
Windows shipping sources/build inputs remain unchanged; independent Windows Release
and fresh local installer/portable/package qualification remain required.

## Exact Stage A handoff paths

Package: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/lf030-i01-app/Lightflow Studio.app`.
Isolated data root: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/lf030-i01-acceptance`.
Staging: `/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/handoffs/LF-MAC-DEV-030-I01`.
Proposed final ZIP: `LF-MAC-DEV-030-I01-handoff.zip` in its parent directory.
Exact source/build identity and file hashes belong to the staged manifest and bundle
provenance, produced after the source commit. No final screenshot ZIP before Stage B.

After explicit owner desktop permission only, launch from Terminal:

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/lf030-i01-app/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/lf030-i01-acceptance"
```

Equivalent ready-to-copy PowerShell on this Mac:

```powershell
& "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/lf030-i01-app/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-030-MacLocations/artifacts/lf030-i01-acceptance"
```

Request 10–12 minutes of exclusive Computer Use after Stage A:
actual initial sidebar, three collapsed/re-expanded sections, built-in Favorite and
nested task folders, native volume/share labels and verified grouping/fallback,
folder-row stability during expansion, selection/history/Refresh, narrow/long-label
presentation and truthful missing task-folder state. Do not unmount user shares,
access credentials, invent fault coverage or repeat accepted unrelated resize checks.
Quit the task app normally, release desktop, then finalize actual screenshot/evidence
ZIP. Screenshots and automation are evidence; Jeremy's acceptance remains separate.

Keep Draft PR unmerged and #465 open. Required: final exact-head CI, Mac owner visual/
functional acceptance, independent Windows regression/fresh packaged acceptance and
explicit merge authorization. Project fields/native parent/dependencies are unavailable
through current connector; no successful reconciliation is claimed or values invented.
#366/#395 and broader migration milestones remain incomplete. No release/tag.
Chronicle assessment: bounded sidebar/row correction needs no separate entry yet;
retain evidence for a later shared Browser milestone account after owner acceptance.
