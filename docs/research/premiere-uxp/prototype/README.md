# Disposable Premiere UXP proof — #256

This is an experiment, not a production companion. It never reads the Lightflow Catalog.
Fixture UUIDs demonstrate contract shape only; production must consume existing identities.
See [the research report](../README.md) for observed versus documented results.

## Reproduce

1. From the repository root, generate new synthetic fixtures:
   `powershell.exe -NoProfile -ExecutionPolicy Bypass -File docs/research/premiere-uxp/prototype/New-Fixtures.ps1`.
   This prints a unique directory under `artifacts/research/premiere-256/`. It creates six seconds
   of 30 fps test video plus stereo tone, a half-size proxy, hashes, and FFprobe metadata.
   Pass `-FfmpegPath` and `-FfprobePath` to use a different verified local build.
2. Optional network proof: `node docs/research/premiere-uxp/prototype/bridge.cjs <fixture-directory>`.
   It binds only 127.0.0.1:47856, writes a random token to that fixture's `pairing.json`,
   exposes only authenticated GET `/health`, and expires after 20 minutes. Port collision fails
   visibly. No production command execution or media serving exists.
3. In Premiere 26.5, close real projects. Enable plugin developer mode if needed and restart
   Premiere. Use Adobe UXP Developer Tool 2.2 or newer with development mode enabled.
4. Add this folder's `manifest.json` to UDT and Load. Open the Lightflow 256 Research panel.
5. Click **Select fixtures and run proof** and select the generated folder. The plugin creates
   or opens only `Lightflow-256-disposable.prproj` there. A different active project causes a stop.
6. Inspect `evidence-*.json` and UDT console output. Each optional API attempt records its error
   without converting failure into success. A project mutation's return value alone is insufficient:
   inspect the destination item/marker/proxy readback.
7. Run again against the same project and fixture folder. Compare IDs and counts, then save,
   close/reopen the disposable project and repeat. `mapping.json` is an experimental destination
   map. Missing mapped items and ambiguous unmapped objects stop retries; there is no automatic repair.
8. On the disposable project only, exercise undo/redo for named transactions, import undo, renames,
   bin moves, hard-boundary trimming, and audio/video-only subclip variants. Record observations.
   The automated proof requests both audio and video and hard boundaries; it does not itself prove
   trim enforcement or all inclusion combinations.
9. Compare fixture hashes before/after marker operations and inspect any newly created XMP sidecars.
   Premiere metadata preferences can affect disk writes; do not extrapolate fixture results to real media.

Do not commit generated media, project files, tokens, or UDT personal logs. Publish only reviewed
fixture evidence. Stop the bridge after the test. Unload the research plugin in UDT when finished.

## Local validation

`node --check docs/research/premiere-uxp/prototype/index.js`

`node --test docs/research/premiere-uxp/prototype/bridge.test.cjs`

These checks validate JavaScript syntax and the narrow Node HTTP boundary, **not** Premiere behavior.
No application build is needed because this folder does not affect production paths.

## Experimental limitations

- Mapping writes are not crash-atomic and do not establish a production durable journal.
- A mutation followed by a crash before mapping persistence is deliberately an uncertain outcome.
- A 100-nanosecond Lightflow tick does not convert to an integer number of Premiere ticks in every case.
  Fixed exact fixture times here avoid hiding that issue; see the report for conversion policy.
- No lifecycle/background claim follows from a successful visible-panel run.
- No custom metadata identity stamp is written until source-XMP side effects are understood.
- Independent CCX production installation, multi-project switching, save-as GUID behavior and
  plugin reload/reinstall durability need separate runtime checks.
