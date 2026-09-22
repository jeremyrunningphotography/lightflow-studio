# Settings authority audit — #291

Baseline: `af303cc3cd89c3933ae3c85d61790b906569d2d1` (fresh origin/main, 2026-09-22).
Inspected MainWindow.xaml, MainWindow.xaml.cs, MainWindow.Premiere.cs, AppSettingsStore,
StorageManagement, ExportDialogModel, BrowserTree/BrowserStorage, workspace continuation,
PreviewMaintenance, and architecture/design/profile documentation.

## Control inventory and decisions

| Current section / control | Persisted authority / actual consumer | Decision and compatibility |
| --- | --- | --- |
| General / Default Video Folder + Browse | AppSettings.DefaultVideoFolder → ApplySettingsToBatch; initial directory for old root pickers | Remove. Hidden compatibility review is restored from AppState/History, Browser resolves its own location. Old JSON fields remain readable as unknown fields, omitted on new saves. |
| General / Screengrab Folder + Browse | AppSettings.ScreengrabDirectory → FrameScreengrabService via live storage.Settings | Keep staged global capture preference; isolated default remains profile-owned. |
| General / Premiere Pro Integration | OpenPremiereAsync → production connection/setup dialog and profile-owned bridge/pairing | Keep immediate administration entry; do not change pairing or installation ownership. |
| Color / Camera LUT Folder + Browse, Include subfolders | CameraLutFolder/CameraLutIncludeSubfolders → ApplicationLutLibraryCache, RefreshLutsAsync, Player refresh | Keep staged administration, separate peer card. Refresh only changed stage after save. |
| Color / Creative LUT Folder + Browse, Include subfolders | CreativeLutFolder/CreativeLutIncludeSubfolders → same cache, independently scoped stage | Keep staged administration, separate peer card. Existing LutFolder read migration retained. No per-asset assignment changes. |
| Export / Resolution, Recovery Mode | DefaultResolution/DefaultRecovery → ApplySettingsToBatch only | Remove global controls/state. Modern modal explicitly starts Source resolution and normal recovery; do not change that. Legacy History retains its typed recipe. |
| Export / Include subfolders, hidden Preserve source folder structure, Overwrite existing | IncludeSubfolders/PreserveFolderStructure/OverwriteExistingFiles → ApplySettingsToBatch only | Remove global controls/state. Browser recursive scope and modern Export overwrite already have independent owners. Preserve AppState/History compatibility. |
| Export / Named preset + Apply Preset | EncodingPreset → Settings-only presentation; expands EncodingPresetCatalog into Encoding | Remove global selector; preset catalog remains shared typed recipe API. No preset interchange work. |
| Export / disabled Encoder, Video Codec, Container, Audio | Encoding.Backend/Codec/Container/AudioMode → modern modal defaults and legacy job planning | Remove global controls. Preserve existing advanced encoding values by migrating the Encoding payload once into profile-local Export-owned defaults before Settings can rewrite the file. Modern modal keeps its existing Source codec/container and copy-audio initialization. |
| Export / NVENC preset, Tuning, Rate Control, Multipass | Encoding.EncoderPreset/Tune/RateControl/Multipass → ExportDialogModel and EncodingJobOptions | Same Export-owned migration; existing Export advanced controls remain the point of use. |
| Export / Quality, Target Bitrate, Max Bitrate, AQ strength | Encoding.Quality/TargetBitrateMbps/MaxBitrateMbps/AqStrength → same | Same migration; no silent reset of effective encoding choices. |
| Export / Pixel Format, Frame Rate, AAC Bitrate, Audio Sample Rate, Audio Channels | Encoding.PixelFormat/FrameRate/AudioBitrateKbps/AudioSampleRate/AudioChannels → same (modern modal already resets frame rate) | Same migration; no new global preferences. |
| Export / Deinterlace, Spatial AQ, Temporal AQ, Fast start | Encoding.Deinterlace/SpatialAq/TemporalAq/FastStart → same | Same migration. |
| Storage / Catalog location + Change | resolved storage locations; CatalogDirectory/CatalogId → protected RelocateCatalogAsync | Keep immediate existing administration, with explicit action confirmation. No #271 setup redesign. |
| Storage / Previews location + Change | PreviewsDirectory → RelocatePreviewsAsync | Keep existing immediate move/rebuild modes. |
| Storage / Usage + Refresh Usage | PreviewMaintenance.GetUsageAsync; no preference write | Keep read-only/refresh administration. |
| Storage / Cache limit | PreviewCacheQuotaGb → PreviewRetentionPolicy.FromSettings | Keep staged preference, 1–1024 GB. |
| Storage / Clean Up, Clear, Rebuild, Cancel, progress | existing Preview maintenance and coordination services | Keep immediate commands/progress/cancellation; preserve destructive confirmation. |
| Storage / Backup selection, Back Up Now, Restore Selected | Catalog recovery service and backup list, not global preference | Keep immediate manual administration in Backup & Recovery card. No #272 exit backup. |
| Storage / Add Media Root, Rename, Reconnect | IMediaRootService.CreateAsync/RenameAsync/RemapAsync | Remove Settings card; small Locations-owned Add Location / Rename Location / Reconnect commands preserve existing RootId services, including offline roots. No identity/schema changes. |
| Tools / Check Again, dependency rows/help | LocateTools, RefreshDependencyHealthAsync | Keep under Advanced. Check saved configuration; do not apply an unsaved override globally. Preserve version/detail/status. |
| Tools / FFmpeg executable + Browse | FfmpegPath → ExecutableLocator/LocateTools (also FFprobe sibling) | Keep staged override; bundled/PATH fallback unchanged. |
| Footer / Save Settings, Restore Defaults | currently reconstructs AppSettings, applies legacy Batch defaults; reset touches hidden details checkbox | Save only edited retained preferences onto latest settings. Restore only staged preferences; never reset storage identity, queue policy, pairing, Export defaults, or perform maintenance. |

## Other persisted state checked

- DetailedActivityLogging is not a global diagnostic preference: only the hidden legacy
  `ShowEncodingDetails` checkbox consumes it; AppendDetailedLog always writes the file.
  Its checkbox immediately saves and History rerun changes it. Remove this false global
  authority; retain the contextual checkbox and typed History DetailedOutput recipe.
- MaxSimultaneousExports and IsExportQueuePaused remain owned by existing Jobs commands;
  preserve them when saving preferences. No #297 behavior changes.
- CatalogDirectory, PreviewsDirectory and CatalogId remain storage-owned. Mere category
  navigation/Restore Defaults cannot mutate them.
- Browser layout, filters, location, panels, Player review, and Visual Index density remain
  owned by workspace/Catalog continuation. No Settings copies.

## Layout and validation plan

General: Captures / Integrations. Color: Camera LUTs / Creative LUTs. Storage: Catalog /
Previews / Backup & Recovery. Advanced: Requirements / FFmpeg. Retain rail and fixed
Save/Restore footer. Accepted hands-on review supersedes the initial paired-card proposal:
keep a centered category rail beside one vertical card stack at every width, with header
and footer spanning both columns and no visible Categories label. Bounded text/path controls, existing dark styles, ordinary keyboard controls.
No shared Jobs style changes.

Automated validation stays on a noninteractive desktop; no computer control. Test retained
preference round trips, old-profile migration and repeat loading, Export ownership, category
contracts, responsive layout/DPI in DIPs, Locations eligibility, storage/identity preservation,
LUT/dependency/profile behavior. Packaged visual acceptance remains Jeremy's decision.
