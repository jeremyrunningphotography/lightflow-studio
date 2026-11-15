# Export preferences — #503 / LF-WIN-DEV-052 I01

Local implementation, pending packaged owner acceptance. This current contract
supersedes only the Specific Folder destination default in the historical
#500 filename-defaults document. Original Name-only, empty Custom values and
display-only Custom hint remain unchanged. No PR, merge or release is implied.

## Defaults and preference inventory

Both ordinary and Subclip Export default to **Same as Source**. Each item's
actual source parent determines its output root, including Collections and
mixed-folder selections. Create subfolder is independently enabled with name
`Exports`. Specific Folder initially suggests the first source's parent, without
committing a path/history entry. The mode itself is remembered. Editing the
native ComboBox, Browse or selecting history makes the path explicit; the last
explicit path is retained independently of mode until Reset.

| Reusable preference | Established default |
| --- | --- |
| Destination mode | Same as Source |
| Explicit Specific Folder | None; contextual suggestion only |
| Create subfolder / name | Enabled / Exports |
| Ordered filename parts and Custom values | OriginalName only; new Custom value empty |
| Filename separator | Hyphen |
| Format / codec / resolution | Same as Source / Same as Source / Source |
| Frame rate / audio intent | Source (0) / Copy |
| Camera / Creative LUT policy | As selected in Lightflow |
| Quality mode / CQ | ConstantQuality / 18* |
| Target bitrate (also CBR) / maximum | 40 / 80 Mbps* |
| NVENC preset / tune / multipass | P7 / HighQuality / FullResolution* |
| Pixel format / AQ strength | Yuv420p / 8* |
| Spatial AQ / temporal AQ | true / true* |
| Deinterlace / Fast Start | false / true* |

*Existing migrated encoding recipe remains the profile baseline for these
fields. Constructor-established Source frame rate and Copy audio stay defaults
even when historical recipe fields differ. CBR and VBR target intentionally
share the same underlying value. Inactive values are retained. There are 32
inventoried Export control entries, 28 reusable entries/26 independent stored
values (Custom values are nested in parts; CBR shares target). Fixed Encoder
display, overwrite, current range controls and no-Subclip inclusion are excluded.
Advanced expansion, selection/focus and scroll are dialog-local.

## Storage and migration

`ExportPreferenceSession` uses existing `export-defaults.json` beside the active
data root's settings.json; it never accesses the Catalog. `--data-root` isolates
the file. The JSON envelope uses `SchemaVersion: 1`, `LegacyEncodingBaseline`,
`Overrides`, `RecentFolders`. String enum identifiers are independent of UI
labels/indices; JSON names use the C# contract's PascalCase. Nested typed
Encoding values are diffed against the resolved profile baseline. No rendered
source values are persisted. Null/absent SpecificFolder means contextual, while
empty naming parts/Custom values remain explicit intent and can fail validation.

Unversioned legacy EncodingOptions (numeric enums) are read without rewriting;
the first preference/history save writes the versioned envelope. Old
Settings.Encoding migration remains one-time and cannot overwrite an existing
Export file. General Settings never reads/edits Export preferences after migration.
Returning a field to its baseline omits its override. Normal Reset clears the
override layer while preserving legacy baseline and MRU. Unknown supported-schema
fields round-trip during ordinary saves; Reset clears unknown override fields
as well. Invalid JSON/values, incomplete documents or unsupported schema are not
silently normalized/overwritten: a Lightflow-styled explicit recovery action
preserves the original as a uniquely named recovery file before resetting.

Recent folders are up to ten distinct explicit absolute paths; canonicalization
uses Windows GetFullPath, removes redundant trailing separators and compares
case-insensitively. Explicit commits move a path to the front. Drive/share
availability never removes history. Suggested paths and incomplete typing are
not MRU entries. Typed absolute paths enter history on Enter, focus departure or
close; Browse/history selections commit immediately.

One coalescing background writer snapshots current memory every 250 ms while
dirty. Disk reads/serialization/writes run off dispatcher in the pump; sparse
in-memory comparison is small and synchronous. Unique same-directory temporary
files are replaced/moved atomically using existing app conventions; previous
valid data is retained as .backup. A fingerprint check detects externally
changed files rather than overwriting them. The app profile owns normal
single-process access; this is not a general cross-process transaction framework.
Close waits for pending saves independently of cancelled preflight, displaying
save failure truthfully. No export submission or successful completion is needed
to remember preferences; Cancel closes the export, not preference history.

## Validation and Reset

Every output-relevant change invalidates old analysis immediately, disables
Export and uses the existing row progress circle/footer messaging. Control reads
and restoration are batched; initial interactive models defer synchronous
planning. Detached snapshots, existing 200 ms idle planning, one planning gate,
cancellation/generation/revision guards and final submission revalidation are
preserved. Custom typing never runs disk planning synchronously or rebuilds its
editor. Destination changes recompute every file path and its actual source,
duplicate, existing-file and overwrite/skip conditions. No auto-renaming is added.

Worker destination checks require an available explicit base folder and test
write access at the nearest existing output ancestor using a unique DeleteOnClose
file. Planning creates no output directories. Existing execution creates the
optional subfolder and rechecks safety. Disconnected/denied paths remain visible
and block Export; no fallback directory is chosen. A syntactically valid but
nonexistent Specific Folder must be created or selected before Export. Empty
enabled subfolder names, traversal, reserved device names and invalid filesystem
components are blocked; disabling subfolder leaves each source parent/chosen root.
Permissions and network availability can still change after preflight; execution
failure remains authoritative. Noncancellable OS filesystem calls can delay
completion, while current UI edits supersede their results without blocking.

Missing LUT identities/hashes remain visibly unavailable; no silent NoLut is
substituted. Codec/container/P010 and hardware errors block using existing
materialization/preflight. Inactive CQ/bitrate/AQ/FastStart preferences remain
stored without forcing an incompatible mode. Existing Copy-to-AAC audio fallback
is unchanged. Restoring intent never restores old validation or overwrite rights.

Reset Defaults is an always-visible secondary footer action, disabled at defaults.
It applies immediately without confirmation, clears explicit folder/overrides,
restores the matrix above, preserves MRU/current source/range/inclusion context
and disclosure state, clears current overwrite authorization, refreshes controls
and triggers complete new analysis. It does not change queued Jobs or files.

No Browser selection, asset/Subclip/range objects, active marks, source-specific
Color/rotation, pending jobs, progress, validation/plans, collisions, confirmation
or overwrite authorization is written to the preference document.

## Platform and qualification boundaries

Typed string policies, ordered tokens, numeric units and intent/effective semantics
are platform-neutral. This implementation uses WPF, Windows path/case/reserved-name
rules, local paths and existing NVENC support. Future Mac parity must adapt UI,
path validation and safe replacement/resource resolution; it must not silently
reinterpret Windows drive/UNC paths. No Mac code or new dependencies are added.

The verified launcher guard is preserved. Its historical classification trigger
remains unconfirmed; owner explicitly accepted that uncertainty for continued
development. Actual desktop verification, nested rejection, child exit codes and
cleanup are required for all native/Release/package validation. See the separate
launcher recovery qualification record and I01 evidence for exact results.
