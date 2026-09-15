# Premiere source-media handoff (#257)

Implementation of the accepted [#256 architecture](research/premiere-uxp/README.md).
Premiere Pro 26.5 or later and the bundled Lightflow Studio Companion 1.x are required.
This implementation sends source media only. Native Subclips (#258), marker projection
(#259), proxy attachment (#260), color and timeline assembly remain outside its scope.

## User workflow

1. Open Settings → General → Premiere Pro Integration in Lightflow.
2. Install the bundled CCX through Creative Cloud Desktop. Restart Premiere after updating
   a running companion; the installed version and the loaded version can differ.
3. For first-time setup only, expand **First-time setup** in integration Settings and
   choose **Copy Setup Location**. In Premiere, choose Window > UXP Plugins > Lightflow
   Studio Companion, then **Allow Connection Access**. Paste the copied location into
   Adobe's folder-picker address bar, press Enter and choose Select Folder. The companion
   remembers Adobe's folder grant and connects automatically on subsequent launches.
   Keep the panel open during handoff. Reset/forget controls are under Troubleshooting.
4. Select compatible Catalog source assets in the Browser and choose **Send to Premiere Pro…**.
   If setup is needed this opens integration Settings. If disconnected, it explains the
   state and offers Settings. When connected it opens a separate Send dialog: review the
   current project and select a destination bin; optionally create/use a child bin.
5. Follow progress and per-source results in Jobs. Save the Premiere project after import.

Installation inventory is read through Adobe UPIA. Installed software yields Ready, never
Connected. Connected requires a compatible authenticated heartbeat received within ten
seconds and an unexpired token. The running host supplies its version, active project,
and available bins. A healthy companion with no project can be Connected, but cannot
receive a handoff. Missing/disabled components, incompatible versions and connection
failures have separate guidance. Refresh installation after installing/updating a CCX.

Lightflow starts the listener when integration is first opened, then resumes a previously
configured listener during later workspace initialization without opening Settings. It
does not block presentation readiness on the bridge. Credentials expire after eight
hours; a 15-second maintenance check renews expired credentials, and listener restart or
explicit reset rotates them. The companion rereads the protected file through its saved
folder grant on each heartbeat. Closing the panel still removes its availability.

### Supported automatic setup boundary (companion 1.0.6)

Adobe's [Premiere UXP filesystem documentation](https://developer.adobe.com/premiere-pro/uxp/resources/recipes/filesystem-operations/)
distinguishes sandbox access, user-selected `request` access and arbitrary-path
`fullAccess`. It also documents persistent folder tokens across sessions. Same-user
Windows execution and a deterministic path do not bypass that sandbox. The supported
implementation retains one initial folder grant rather than broadening permissions.
There is no unauthenticated credential-discovery endpoint and no dependency on writing
credentials into undocumented Adobe plugin-data directory layouts.

Only Adobe's opaque folder token and the explicit pause choice are stored in plugin
localStorage; Lightflow bearer credentials remain in its ACL-protected file and companion
memory. A folder token is deliberate: a token for the credential file could break when
Lightflow atomically replaces that file. Reconnect reads the latest file and validates
the exact endpoint, protocol, credential format and expiry before any HTTP request.
Malformed, missing or expired settings never authorize a request. An expired file waits
for Lightflow renewal; an invalid Adobe grant requires the explained access step again.
Disconnect persists a pause until Resume Connection. Forget Connection removes the grant
and requires first-time setup again. Neither action deletes handoff reconciliation data.

Settings shows installation/live status and clear next-action guidance; setup details
are expandable, with no raw path field. The disconnected Send action reuses the shared
Lightflow NoticeDialog, using a primary Open Integration Settings button and Not now.

## Production boundary

`PremiereBridge` hosts Kestrel on IPv4 loopback, port 47857, HTTP/1. The UXP client uses
the exact endpoint `http://localhost:47857`, following the hostname permission constraint
observed in #256. External endpoint configuration is discarded. Requests require the
exact Host and port, a constant-time-checked 256-bit bearer credential, loopback peer,
POST, JSON, an allowlisted route, no query, and no browser Origin/Sec-Fetch headers.
There is no CORS, redirect, shell, arbitrary file, or general command endpoint.

The credential is written atomically into an ACL-protected current-user folder. It is
never included in receipts or logs. The companion has requested-folder filesystem
access and only the localhost network domain. Body, header, connection, request-rate
and processing-time limits bound the HTTP boundary. This is a same-user desktop trust
boundary, not a defense against arbitrary code already running as that Windows user.

Heartbeat negotiates host/companion/protocol compatibility and binds the running client
instance. Poll and receipt require that instance. Receipts also require the operation
and fresh dispatch IDs, so a late response from an earlier attempt cannot finish a new
attempt. Only one source command is in flight. Switching projects prevents subsequent
dispatch; the companion checks the accepted project again before mutations and readback.

## Catalog identities and reconciliation

Catalog schema 14 adds `PremiereHandoffs` inside the existing migration, backup and
relocation boundary. CatalogId and AssetId remain authoritative. Each destination
registration is scoped to the observed project GUID and normalized saved project path;
Save As cannot silently retarget an accepted command. This path is a destination guard,
not the identity of a source asset. An existing registration retains its accepted bin
and operation ID on retry, even if the user selects another bin.

Before dispatch, Lightflow durably records the intent and dispatch state. The companion
records intent before import, reads native item IDs afterward, and records the resulting
mapping before returning a receipt. A retry locates the mapped item by ID and corroborates
its media path. Names and bin moves remain editor-owned. Missing/relinked items, editor
undo, changed source facts, ambiguous readback and unmapped matching media stop with an
explicit conflict or uncertain outcome; a matching path never authorizes adoption.

Both the Catalog receipt and the companion journal can recover a known item mapping.
There is no metadata recovery stamp and no deliberate source-file metadata write.
The gap between import and durable mapping cannot be made atomic across applications.
When a dispatched attempt has no recoverable item ID, automatic reimport is blocked,
including after a failure receipt without an established mapping. This conservative
case requires inspection of the original project; there is no destructive reset button.

Cancellation stops waiting/queued work. An import already executing in Premiere may
finish and remain there. Resending the same Catalog selection reconciles known results;
it does not blindly replay an uncertain command. Batch progress uses the existing Jobs
surfaces, and source receipts remain in the Catalog across Lightflow restarts.

## Send workflow and source In/Out

The Send dialog separates the connected Premiere project, selected Catalog sources, and
the active-project destination. Refresh reads the latest authenticated companion
heartbeat and clears a selected bin/child name when the project destination identity
changes. It does not infer a project from installed software.

For video sources with a saved Lightflow review range, the per-send option projects the
source item's In/Out points after import. It is not native Subclip creation; #258 owns
that separate operation. Premiere's supported
[ClipProjectItem source In/Out actions](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/clipprojectitem/)
run in an undoable project transaction. Lightflow's 100-nanosecond `TimeSpan` boundaries
are converted to Premiere `TickTime` values with integer arithmetic. When a boundary
falls between Premiere ticks, Lightflow deterministically uses the nearest representable
tick (ties round up) and explains that adjustment in the Send dialog. A range is omitted
only when it remains invalid after projection, such as a range too short to retain an
ordered In/Out pair.

The companion records the source-range projection after its imported-item mapping and
before it returns the authenticated receipt. A missing receipt then reconciles that
known phase without applying the range again. This preserves an editor's later item
changes while retaining duplicate-free recovery.

A mere authenticated connection does not protect application shutdown. The close warning
appears only after the bridge has delivered a command to the companion and before the
corresponding receipt resolves it. A queued or idle Connected state closes normally.
Premiere UXP does not expose a reliable foreground-activation contract, and Windows
foreground rules do not make process/window activation dependable, so Lightflow does not
use a process/window activation workaround after Send. Jobs continues to record results.

## Packaging and automated validation

`scripts/Build-PremiereCompanion.ps1` builds a flat deterministic CCX containing the
seven production code/manifest files and unchanged approved icon/header PNG assets.
`Build-Release.ps1` includes it at
`PremiereCompanion/LightflowStudio.ccx`; package validation requires it.

Run the .NET suite and `node --test PremiereCompanion/*.test.cjs`. Bridge tests use
the actual HTTP listener, an isolated SQLite Catalog and Windows ACLs. The JavaScript
suite tests reconciliation and crash gaps with injected native adapters.

### Reviewed Settings/Send separation (companion 1.0.3)

Settings has no bin picker, child-bin field or Send action. Its status comes from actual
authenticated heartbeat health, with installation inventory used only when no live
connection is available. The Send dialog clears destination/child-bin choices on project
changes or disconnect and rechecks the current project and bin at submission. It never
uses installation alone to authorize a send. A healthy no-project connection shows an
instruction to open/save a project. Missing bin data has explicit waiting/error guidance.

Bin enumeration was already implemented and historical production evidence contains
root/child bins. The former combined Settings dialog nevertheless exposed an empty picker
without a healthy project. Static investigation also found a publication defect: busy
heartbeats could pair a new active project with cached bins from its predecessor. The
cache is now scoped by project GUID and saved-path guard; enumeration rechecks the active
project before publication, includes project root, and reports nesting/item/bin limits.
Nested labels show hierarchy and retain native IDs; names are never reconciliation keys.
The exact cause of Jeremy's observed empty dropdown cannot be established from code
alone. Current-project population and visual rendering remain acceptance checks.

The CCX uses the approved #241 PNG bytes for both panel header and manifest icon entries,
following Adobe's [UXP manifest icon contract](https://developer.adobe.com/uxp/guides/explanation/concepts/manifest/).
Creative Cloud/host rendering remains unverified this iteration. No computer control or
real Premiere/UI testing was performed for 1.0.3. General startup performance is tracked
separately in [#265](https://github.com/jeremyrunningphotography/lightflow-studio/issues/265).

`PremiereLiveAcceptanceTests` is an explicitly opted-in driver, not a substitute for
hands-on acceptance. Set `LIGHTFLOW_PREMIERE_ACCEPTANCE` to an isolated artifact directory
containing `media/source.mov`, and `LIGHTFLOW_PREMIERE_PROJECT` to the exact disposable
project path. Run only `InstalledCompanion_ProductionBridgeAcceptance`. Pair the installed
companion with `%TEMP%/Lightflow-Premiere-acceptance-pairing`. Write `send`, `snapshot`,
`rotate` or `stop` to `action.txt` in the artifact directory. It uses the production
bridge/Catalog code and writes connection state and received handoff evidence without
credentials. Stopping the driver successfully does not itself prove an import passed.

## Acceptance checkpoint — 2026-09-14

Verified locally:

- Production stable-ID CCX 1.0.0 installed/enabled through Adobe UPIA, with the research
  plugin not loaded in UDT.
- Installed companion authenticated to the production Kestrel bridge on Premiere 26.5.0
  / UXP `uxp-9.3.0-local`; readback identified `Lightflow Test.prproj`, its project GUID
  and root-bin ID. No prototype server was used.
- CCX update to 1.0.1 accepted by UPIA. The already-loaded client continued reporting
  1.0.0, demonstrating the installed-versus-running version distinction.
- Closing Premiere expired the authenticated connection and removed Connected status.
- After the startup stall cleared, installed 1.0.1 reopened and authenticated after
  manual pairing. With no project open, the healthy heartbeat reported a null project
  and empty bins.
- The production bridge imported the synthetic `source.mov` into the requested new
  `Lightflow 257 acceptance` bin. A repeat send verified the same native item ID without
  reimport. Saving, closing and reopening the disposable project preserved the project,
  bin and source-item IDs; a third send again verified the original item.
  Raw credential-free receipts and lifecycle readback are in
  [the production evidence directory](evidence/premiere-257/).

Still unverified and blocking completion: Save As identity behavior; item/bin
rename/move and undo/redo;
multiple-project/project-switch behavior in the real host; interrupted import recovery;
and the complete packaged Lightflow selection-to-Jobs interaction. Native window and
keyboard actions timed out again after the successful save/reopen/API checks, while the
authenticated API heartbeat remained healthy. The cause has not been established.
The disposable project now contains the synthetic source and acceptance bin; unrelated
projects were not modified. Companion 1.0.2 increases the minimum/floating panel height
and makes pairing single-flight after 1.0.1 exposed clipped status and duplicate folder
pickers. The regression test passes; the updated panel still needs real-host validation.

The required local package command passed startup presentation, Jobs activation and
dependency validation. The smoke timeout now permits up to 15 minutes for bounded
Catalog integrity/migration work, while retaining readiness and early-exit checks.
The initial short deadline failed against the existing approximately 1.4 GB Catalog;
a diagnostic launch completed migration and presentation readiness.

Keep #257 In Progress and its PR Draft until these production acceptance gates pass.
