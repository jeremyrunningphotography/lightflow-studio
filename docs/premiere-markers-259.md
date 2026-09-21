# Premiere point-marker handoff (#259)

Status: implementation for review; real Premiere 26.5+ acceptance is **not yet performed**.

Videos send all current Catalog point markers to the reconciled source ProjectItem.
Subclips send markers in `[In, Out)` to each durable native Subclip, using exact
`sourcePositionTicks - InTicks`. Complete-video fallback uses source timing. Temporary
source prerequisites receive no markers. There is no additional Send mode or checkbox.

Catalog schema 17 extends the existing handoff journal with `PremiereMarkerHandoffs`,
keyed by destination, MarkerId and source AssetId / SubclipId target. Intent retains
AssetId, revision, optional name, exact source-relative and projected 100ns positions.
Receipts retain target item ID, marker GUID and last verified destination properties.
Old source/Subclip receipts deserialize without marker fields; schema 16 Catalogs
retain their marker data and acquire an empty projection table through normal backup/migration.
Clearing a Catalog marker does not delete an already-sent Premiere marker; subsequent
sends project current Catalog markers only. No source XMP identity stamp is written.

The production adapter uses Comment type, zero duration, the actual optional name,
and empty comments. It leaves Premiere's initial color choice alone and snapshots the
result. Rename changes only the name Action and preserves GUID/color. All six observed
properties (name, start, duration, type, comments, color index) participate in conflict
detection. Marker time uses the established nearest-tick integer conversion
`(LF * 127008 + 2) / 5`, never floating seconds/timecode/frame approximations.

Adobe's current [Markers API](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/markers)
and [Marker API](https://developer.adobe.com/premiere-pro/uxp/ppro-reference/classes/marker)
were checked against the accepted #256 prototype. They document ProjectItem marker
collections, synchronous enumeration/property reads, GUID and rename Actions. This is
API evidence, **not empirical proof of native Subclip timing or save/reopen persistence**.

Discovery and journal I/O happen outside `lockedAccess`. Inside it, the adapter rechecks
the marker inventory and creates/consumes Actions within `executeTransaction`. Afterwards
it enumerates again and verifies the exact changed GUID/properties and unchanged unrelated
markers. A successful transaction boolean alone never verifies a marker.

Retry rules:

- Existing GUID plus unchanged last projection: verify or rename in place.
- Editor property changes: Conflict, preserving the editor marker.
- Deleted mapped GUID: recreate only when the remaining inventory has no new unknown
  GUIDs. GUIDs of other durably mapped Lightflow markers are allowed, including those
  created later in the same batch. Similar names/times never establish ownership.
- Recreated target: recreate a marker only after its old target disappears and its
  new reconciled target has no markers other than other durably mapped Lightflow GUIDs. Otherwise conflict.
- Lost create/readback/journal outcome: UnknownOutcome blocks automatic duplication.
  An interrupted update of a known GUID can recover only its exact durably recorded
  intended post-state. Never adopt an unowned lookalike.
- Marker failures/conflicts remain independent Jobs item results. A verified video or
  Subclip cannot make the overall Job successful when a requested marker failed.

## Packaged isolated acceptance (preferred)

The superseding isolation decision permits Premiere from an isolated packaged profile. Launch:

```powershell
& 'C:\Git\Agents\issue-259-premiere-markers\artifacts\release\LightflowStudio\LightflowStudio.exe' --data-root 'C:\Git\Agents\issue-259-premiere-markers\.cache\packaged-acceptance'
```

Prerequisite acceptance: Jeremy checks that an unconfigured **Send To → Premiere Pro**
opens Integration Settings, pairs this profile with the production companion, and reaches
the normal Send workflow after Done or window close. A profile with a previous successful
authenticated pairing opens Send directly, even while awaiting reconnection; Send remains
disabled until connection/project/destination requirements are met. Directly opening
Integration Settings does not open Send afterward. Close the other Lightflow bridge owner first. Copy Setup
Location from this build; choose Forget Connection in the companion's Troubleshooting
section, then Allow Connection Access with that location. Each profile owns its Catalog,
handoff journal and pairing credentials. The companion remembers one explicit folder
grant; stale or other-profile credentials cannot authenticate. A port collision offers
Refresh Connection in Integration Settings and never takes over another bridge.

No marker-specific work resumes until Jeremy accepts this prerequisite. Real Premiere
behavior and visual consistency still require Jeremy's hands-on acceptance; automated
checks do not claim that acceptance. Use task-owned disposable media and a new disposable
Premiere project for subsequent marker testing.

## Optional deterministic live harness

The live harness uses production Catalog services, planning, `PremiereJobs`, journal,
authenticated bridge and the same packaged UXP companion. The harness supplies fixtures
and orchestration only; it contains no marker reconciliation implementation.

All fixture paths must be in a subdirectory of this checkout's
`.cache/premiere-acceptance`. Before Catalog/bridge startup the harness validates the
configured project/media, profile exclusions, canonical containment and reparse tree.
Fixture files with multiple hard links are rejected. Each invocation creates a new
run directory and Catalog, so no environment path can select an existing Catalog.
Pairing is under that run and its credential file is removed on orderly shutdown;
evidence/Catalog remain for review. This follows #276's task-owned isolation model,
not protection against a hostile same-user process changing paths concurrently.

The companion's existing Adobe-managed private storage still contains its normal
operation journal and folder grant; those are the established production companion
mechanism, keyed by unique run Catalog/operation IDs. No normal Lightflow profile or
pairing state is consumed. Do not load a second companion or run a normal Lightflow
bridge concurrently: the production endpoint remains fixed at localhost:47857.

## Jeremy's live steps (no computer control)

1. Install the task package's `PremiereCompanion/LightflowStudio.ccx` (1.2.0) through
   the existing documented install workflow. Confirm the running panel reports 1.2.0.
   Close any normal Lightflow connection yourself; the harness will fail a port collision.
2. Use the prepared synthetic 30-second MOV at
   `.cache/premiere-acceptance/manual-259/media/source.mov`. Create a **new empty**
   Premiere project at `.cache/premiere-acceptance/manual-259/disposable.prproj`.
   Do not copy a production project or use production media. Record current metadata
   preferences manually without silently changing them.
3. In a terminal in this task clone, start:

   ```powershell
   $env:LIGHTFLOW_PREMIERE_ACCEPTANCE = "$PWD\.cache\premiere-acceptance\manual-259"
   $env:LIGHTFLOW_PREMIERE_PROJECT = "$env:LIGHTFLOW_PREMIERE_ACCEPTANCE\disposable.prproj"
   dotnet test .\LightflowStudio.Tests\LightflowStudio.Tests.csproj -c Release --no-build --filter FullyQualifiedName~InstalledCompanion_ProductionBridgeAcceptance --logger 'console;verbosity=detailed'
   ```

4. In another terminal select the newly created run, and grant the companion access
   to its `premiere-pairing` folder. Never grant the normal profile's pairing folder.

   ```powershell
   $run = (Get-ChildItem .\.cache\premiere-acceptance\manual-259\runs -Directory | Sort-Object CreationTimeUtc -Descending | Select-Object -First 1).FullName
   # Pair with "$run\premiere-pairing"; inspect "$run\connection.json".
   Set-Content -LiteralPath "$run\action.txt" -Value 'send'
   ```

5. Wait for `after-send` in `evidence.jsonl`. Inspect the source's five point markers:
   9.9999999s, 10s, 12.0000001s (unnamed), 19.9999999s and 20s. Readbacks record exact
   Premiere ticks/GUIDs, independent of rounded Premiere UI displays. Repeat `send`:
   expect the same five GUIDs and no duplicates.
6. Send `send-subclips`. The first native Subclip `[10s,20s)` must contain three markers
   at 0s, 2.0000001s and 9.9999999s. The second `[11s,21s)` must contain markers at
   1.0000001s, 8.9999999s and 9s. The same MarkerId has distinct target mappings/GUIDs.
   Verify actual Source Monitor timing independently of the API readback. If Premiere
   instead applies source-relative timing or shares marker ownership across targets,
   **stop and report evidence; do not adjust the accepted product behavior silently**.
7. Send `rename`, then `send` and `send-subclips`: the 12.0000001s marker becomes
   `Renamed in Lightflow` everywhere while retaining each mapped GUID. Repeat both sends.
8. Add an unrelated Premiere marker. Rename or move one mapped marker in Premiere.
   Resend: that item must Conflict, preserve the edit, leave unrelated markers alone,
   and show a mixed Job. Restore the exact prior properties to continue independently.
9. Delete a mapped marker; resend and inspect safe recreation/new GUID. Separately
   delete it and create an unmapped replacement: resend must conflict, not duplicate.
   Interrupted/lost-create cases remain UnknownOutcome when ownership cannot be proven.
10. Save, close and reopen the disposable project **while keeping the harness running**.
    Resend both modes and compare all GUID mappings. Perform marker undo/redo and
    record actual Action behavior; do not assume whole-batch atomic rollback.
11. Send `snapshot` after relevant operations. `evidence.jsonl` records source SHA256,
    adjacent XMP filenames, production handoffs/receipts and Jobs. Inspect XMP contents
    manually if created. Record observations by media/preference combination; an unchanged
    MOV hash is not a universal no-write claim. If source facts change, the production
    source guard may stop subsequent sends; preserve the evidence instead of bypassing it.
12. Send `stop` before the 20-minute harness deadline. Clear the two acceptance environment
    variables before ordinary test runs. Keep evidence for review. Use a fresh disposable
    project/fixture area for another harness invocation (it has a fresh Catalog identity).

To verify temporary-source cleanup independently, start a fresh fixture/project and send
`send-subclips` **before** `send`. Only the two requested native Subclips should remain;
no marker should have been projected onto a removed prerequisite source.

## Evidence status

No real Premiere #259 interaction, source/XMP side-effect observation, undo/redo result,
Subclip timing proof or save/reopen GUID proof is claimed by this implementation.
Automated results and packaged build provenance are reported in the Draft PR/handoff.
