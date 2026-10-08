# Browser semantic actions (#351)

`Lightflow.Actions` adds a sibling `BrowserActions` dispatcher and `IBrowserActionPort`; Browser targets never
pretend to identify a Player session. Existing Player contracts and services retain their ownership.

| ID | Typed arguments | Repeat / execution |
| --- | --- | --- |
| `browser.navigate-selection` | `NavigateSelectionArguments(Previous/Next/First/Last, Extend, Distance)` | Per-action single flight; relative repeat allowed; no pending queue |
| `browser.open-current` | `NoActionArguments` for existing Enter/Open ordering, or `OpenBrowserArguments(AssetId)` for clicked media | Suppress repeat; single flight |
| `asset.set-rating` | `SetRatingArguments(0..5, ToggleCurrent=false)` | Suppress repeat; Catalog serialization |
| `asset.set-flag` | `SetFlagArguments(Rejected/Unflagged/Picked)` | Suppress repeat; Catalog serialization |
| `asset.step-flag` | `StepFlagArguments(Previous/Next)` | Per-action single flight; relative repeat allowed; clamp at Reject/Pick |
| `asset.set-color-label` | `SetColorLabelArguments(Red/Yellow/Green/Blue/Purple/null)` | Suppress repeat; Catalog serialization |

`ToggleCurrent` preserves existing rating-menu toggles. Keyboard/controller rating assignment remains direct set.
Relative distance is bounded to 1..10,000 projected items per invocation. Ordinary controller Previous/Next uses
one item regardless of layout. Windows translates vertical/page gestures into a bounded item distance from its
existing columns and viewport geometry; Home/End translate to First/Last. Shift requests the existing range
extension. There are no WPF keys, controls, handles, OS codes or TourBox identifiers in this assembly.

## Browser target and scope

`BrowserActionTarget` contains the shell Browser session Guid, UI navigation generation, typed scope identity,
ordered projection generation (changed ordered identities, not unrelated refreshes), and presentation generation. Folder scope identifies RootId, relative folder and Include Subfolders;
static and Smart Collection scopes identify their own CollectionId and distinct kind. Equal displayed assets
do not make these scopes interchangeable. Collection Sets are organizational targets, never asset scopes.

Targets come from committed Browser scope authorities, not focused controls. A pending replacement generation
has no semantic target until its scope is committed. Context projects presentation/interaction readiness, stable
selected AssetIds, current-item AssetId and item availability. Selection is resolved and copied at admission,
before any await; changing selection afterwards cannot retarget an admitted durable mutation. Retained hidden
selection follows existing Browser rules; Smart defining-query membership can prune selection as before.

Navigation and Open require the exact captured projection/context. Old invocations return Superseded without
acting on replacement content. Navigation uses the shared BrowserGridModel, existing dirty-Inspector transition
guard, stable current identity, shared Shift anchor and reveal/status path. Grid/Details remain presentations of
one model. A current Asset absent from the new projection falls back to the first selected/first projected item.

Open uses the original resolution, captured compatible review-set and OpenResolvedBrowserPlayerAsync path.
Enter/menu Open retains first-selected order; double-click and Inspector Open retain the explicitly clicked
AssetId. An explicit Open identity must exist in the captured projection. Dirty drafts can cancel the transition.
Resolution completion rechecks target and projection before entering Player; repeat cannot launch multiple opens. Browser/Player/Back cycles advance presentation generation. Open single-flight ownership belongs to the captured target and invocation, so a retiring Open cannot block a fresh Browser presentation or release its newer owner; completion checks the expected Player destination.

## Classification authority and correctness

R1 ([#383](https://github.com/jeremyrunningphotography/lightflow-studio/issues/383)) moves classification values to Lightflow.Domain and command policy/store contracts/execution to Lightflow.Application (net8.0). The Windows port delegates its captured mutation loop and argument mapping to AssetClassificationService; its completion projection keeps the existing scope checks/publication inside one outer Catalog lifecycle admission. SQL/session serialization and lifecycle implementation remain in Windows. See [boundary and validation](validation/shared-classification-r1.md); explicit architecture and hands-on acceptance are still required before merge.

CatalogAssetClassificationStore remains the only durable classification store. UpdateAsync serializes fresh-value
read/mutate/save/readback operations through a gate owned by the Catalog session (shared across store instances),
inside the existing Catalog mutation lifecycle. A Browser batch retains one outer lifecycle admission across all of its captured Assets; unchanged values do not generate redundant durable writes. The lifecycle counts and drains operations; it was not a writer
mutex. All production Browser, keyword and Player classification writers now mutate freshly read values rather
than building whole-row writes from hydrated UI snapshots. SaveAsync remains an explicitly whole-value operation
for initialization and also uses the session gate. No schema, backup, source-file or keyword normalization changes.
Read-only test adapters reject mutation by default rather than providing an unsafe read/save fallback.

The stable selected AssetIds are captured once for the complete logical Browser action. Already submitted writes
may complete for those IDs after a selection/scope change. Publication requires the same Browser session, committed
navigation generation and scope. Projection/presentation-only changes in the same scope do not suppress fresh classifications;
otherwise rapid independently admitted field updates would leave the view stale. Catalog revisions reject older
classification publication. A superseded operation cannot write selection, Inspector or Browser state into a new
scope. Player writers separately check their captured source generation and revision before publication. A pending Browser commit also refreshes the same Asset already opened into Player, with revision guards on both hydration and publication.

Classification publication reuses ApplyClassification, ReapplyQuery, Inspector invalidation and existing state
revision hydration protection. Smart membership is recomputed by the existing defining-query evaluator over
source candidates; no Smart membership row is written, no Source/rule is changed and no second query engine exists.
Per-asset durability remains the established store behavior; a partial failure returns Failed, never complete success.

## Input and UI adapters

Windows MainWindow.BrowserActions.cs implements the port, input translation and presentation publication.
Keyboard classification admission consumes the accepted PlayerKeyboardOwnership per-key policy, with Browser
scope trees and Inspector/Right Panel protected. Numeric type-ahead in pickers/lists remains local. Browser canvas
and selected shell-tab content remain input boundaries; tree focus never becomes the Browser semantic target.
Search/text/editable ComboBoxes, open dropdowns, lists, sliders, menus and modal dialogs retain their local input.
Controller invocation needs an explicit Browser target, not keyboard focus. Modal/application lifecycle readiness
still applies to controllers. No global key capture is added.

Migrated Windows routes: Browser arrows/Home/End/Page plus Shift; 0..5 ratings; Ctrl+Up/Down flags; Enter/Open;
rating/flag/Color-label context menus; double-click and Inspector Open. Ctrl+A stays the existing local selection
operation. Player semantic shortcuts and #343 Delete scope policy are unchanged. Source-file delete, static membership
remove and Smart no-destructive-fallback remain distinct, as covered by existing regression tests.

Keywords remain a text-entry workflow outside the action catalog; their existing writer uses UpdateAsync to prevent
stale fields from undoing semantic classification. Settings/mappings, #352 presentation/Export, file operations,
Favorites, Inspector editing, Smart/Collection redesign and TourBox transport are excluded.

A macOS presentation adapter can implement IBrowserActionPort against the same actions, scope/target records,
arguments and results. It must supply native input ownership, viewport distance/reveal and dirty-edit guards.
The current BrowserGridModel/query/Catalog services remain in the Windows application assembly; this slice does
not claim that the complete Browser implementation has been ported.

## Controller proof and acceptance

BrowserSemanticActionTests invoke the dispatcher directly with Controller input and no WPF key events.
BrowserActionIntegrationTests exercise the real MainWindow port and Catalog in Folder/static/Smart scopes and
Grid/Details: previous/next, stale Open, 0..5 ratings, direct/clamped flags, all labels/clear, multi-selection,
Catalog reopen, concurrent keyword/field updates, captured IDs through mutation quiescence and replacement context,
keyboard translation/local ownership, and dirty Inspector Cancel/Apply followed by captured-review-set Open.
Existing Details, Smart Collection, Inspector, Player action/ownership, #343 and #344 tests remain acceptance gates.

Exact validation and the packaged acceptance command are recorded in [the validation record](validation/browser-actions-351.md).
Owner architecture and packaged hands-on acceptance remain required before merge.
