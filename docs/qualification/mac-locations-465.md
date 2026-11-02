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
