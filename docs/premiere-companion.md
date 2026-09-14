# Premiere source-media handoff (#257)

Implementation of the accepted [#256 architecture](research/premiere-uxp/README.md).
Premiere Pro 26.5 or later and the bundled Lightflow Studio Companion 1.x are required.
This implementation sends source media only. Native Subclips (#258), marker projection
(#259), proxy attachment (#260), color and timeline assembly remain outside its scope.

## User workflow

1. Open Settings → General → Premiere Pro Integration in Lightflow.
2. Install the bundled CCX through Creative Cloud Desktop. Restart Premiere after updating
   a running companion; the installed version and the loaded version can differ.
3. Open the Lightflow Studio panel in Premiere. Select the pairing folder displayed by
   Lightflow. Keep the panel open during handoff.
4. Select compatible Catalog source assets in the Browser and choose **Send to Premiere Pro…**.
   Review the connected project and destination bin; optionally create/use a child bin.
5. Follow progress and per-source results in Jobs. Save the Premiere project after import.

Installation inventory is read through Adobe UPIA. Installed software yields Ready, never
Connected. Connected requires a compatible authenticated heartbeat received within ten
seconds and an unexpired token. The running host supplies its version, active project,
and available bins. A healthy companion with no project can be Connected, but cannot
receive a handoff. Missing/disabled components, incompatible versions and connection
failures have separate guidance. Refresh installation after installing/updating a CCX.

Lightflow starts the listener when the integration is opened. Pairing expires after
eight hours and rotates on listener start or explicit reconnection. The companion asks
for the pairing folder each session; no hidden/background availability is promised.

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

## Packaging and automated validation

`scripts/Build-PremiereCompanion.ps1` builds a flat deterministic CCX containing only the
four production files. `Build-Release.ps1` includes it at
`PremiereCompanion/LightflowStudio.ccx`; package validation requires it.

Run the .NET suite and `node --test PremiereCompanion/handoff.test.cjs`. Bridge tests use
the actual HTTP listener, an isolated SQLite Catalog and Windows ACLs. The JavaScript
suite tests reconciliation and crash gaps with injected native adapters.

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

Still unverified and blocking completion: source import and repeat receipts in the real
host; save/reopen/Save As identity persistence; item/bin rename/move and undo/redo;
multiple-project/project-switch behavior in the real host; interrupted import recovery;
reconnection of the updated companion after cold start. Reopening the disposable project
twice produced a partially drawn workspace and an unresponsive Premiere instance before
the updated companion could be reopened. The cause has not been established. No source
media was imported or user project content changed during this checkpoint.

Keep #257 In Progress and its PR Draft until these production acceptance gates pass.
