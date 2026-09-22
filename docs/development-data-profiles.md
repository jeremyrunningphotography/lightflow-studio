# Development data profiles

`LightflowStudio.exe --data-root "C:\Git\Agents\issue-276-data-root\test-data\acceptance"`

The exact, case-sensitive switch takes one separate, fully qualified local directory. Quote paths containing spaces. Relative, drive-relative, UNC/device paths, duplicate switches, missing/empty values, and ambiguous Windows trailing-dot/space paths are rejected. Resolution normalizes `.` / `..` and trailing separators before deriving paths and instance identity. The selected directory is the application-data directory itself; no vendor subdirectories are appended.

Selection occurs before logging, activation, or persisted settings are opened. No argument retains the existing `%LOCALAPPDATA%\Jeremy Running Photography\Lightflow Studio` profile and its configured Catalog/Preview locations. An explicit root cannot overlap that normal application-data directory. Invalid arguments or an unusable profile fail startup with exit code 2, without activation or normal-profile fallback. Early errors go to stderr/debug trace and a best-effort `%TEMP%\LightflowStudio-startup-<pid>.log`; this unique bootstrap diagnostic does not load a profile.

Existing links/junctions in the profile or its ancestors are rejected before opening state. Directory creation and write/flush probes check initialization. This is task-owned profile isolation, not a security sandbox against another process replacing files/links during execution. Do not copy/link a production profile into an agent root; start with empty state and deliberately add test media. User-requested media reads and output operations are still real filesystem operations.

## State ownership inventory

| State | Isolated behavior |
| --- | --- |
| Durable Catalog, SQLite sidecars and locks | `Catalog\LightflowCatalog.db`; existing migration/locking/durability services remain in use |
| Catalog recovery and backups | `Catalog\Backups`; recovery uses the selected Catalog locations |
| Generated Preview database, thumbnails, standard previews, maintenance staging | `Previews` and `Temporary` |
| Settings, legacy application state, workspace continuation | `settings.json`, `export-defaults.json` (legacy encoding migration only), `state.json`, `workspace-state.json`; no production settings are loaded |
| Jobs, export queue, durable operation/trim history, output identity cache | Profile-relative existing filenames, including `jobs-runtime.json`, `export-jobs.v2.json`, history files and `output-identities` |
| Encoding LUT resources | `encoding-resources\luts` |
| Activity log and rotation, machine identity | `activity.log` and rotated siblings, `machine-id` |
| Premiere pairing/session and handoff journal | Profile-owned pairing directory, fresh credentials and Catalog journal; isolated profiles can configure and use the production companion. Only one bridge can own localhost:47857; a collision opens connection guidance without publishing credentials or attaching to another profile. |
| Default LUT folders and screengrabs | Empty LUT preferences instead of the legacy `J:` default; screengrabs default beneath this profile |
| Single-instance activation | Mutex and pipe use a stable hash of the normalized root; same root retains activation semantics, distinct roots can run concurrently |

Persisted Catalog/Preview overrides and storage relocation destinations must stay beneath an isolated root. Configuration errors retain the existing unavailable/invalid storage handling and never retry the production profile. Explicit user-selected media, exports and integration installers are not automatically relocated. Packaged binaries/resources, FFmpeg, Windows volume discovery, GPU/audio devices and Adobe installation metadata remain machine resources. Runtime dependency verification creates its own disposable SQLite fixture; .NET native extraction remains runtime-managed executable code, not profile state.

### Premiere profile switching

The production UXP companion reads only the pairing folder explicitly granted through Adobe's folder picker. Its Adobe-managed persistent folder grant selects one profile; it never scans profiles or adopts whichever server answers. The fixed endpoint authenticates with a newly generated token written under that profile's `premiere-pairing` directory only after successfully binding the listener. Credentials from a previously running or different profile fail authentication. Catalog/handoff access is supplied by the owning window's storage coordinator, with no normal-profile fallback.

Close the other Lightflow bridge owner, open Integration Settings and choose **Refresh Connection**. In the companion choose **Forget Connection**, then **Allow Connection Access** using the current profile's **Copy Setup Location**. Reset Connection rotates only the current profile's credential; it cannot change the companion's folder grant or erase another profile's state. Switching back requires granting the original profile's folder again. This explicit grant change affects the shared Adobe companion preference, not either Lightflow Catalog. Concurrent Lightflow windows may use separate profiles, but only one can use Premiere at a time.

## Validation and future storage configuration

`scripts/Build-Release.ps1` passes an isolated root to both packaged verification launches. Browser smoke verifies presentation readiness, isolated Catalog/Preview/settings/log creation, and graceful shutdown, then removes the disposable root after the process exits. The packaged executable contains no embedded task path. Use a separate persistent task-owned root for hands-on acceptance; report both executable and data-root paths.

`LightflowStorageLocations` remains the shared location abstraction. `ApplicationDataProfile` adds process selection, initialization checks and activation identity; `LightflowStorageCoordinator` resolves persisted storage through the selected locations. #271 still owns user-facing setup, independently configured production Catalog/Preview locations, defaults and relocation UX. #276 adds none of that UX.
