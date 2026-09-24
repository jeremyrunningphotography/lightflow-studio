# Smart Collections

Issue #217 adds **required Source + durable defining Browser query** to the existing Collections hierarchy.

## Catalog ownership

Migration 19 adds `Collections.IsSmartCollection` and `SmartCollectionDefinitions`. The Collection hierarchy row owns the stable identity (exposed as `SmartCollectionId` by the definition), name, optional parent Set, mixed sibling ordinal, revision, and timestamps. The definition row owns the required Source and versioned query document. There are no materialized matching `CollectionAssets` rows.

Sources are either a logical `RootId` and normalized relative folder with explicit Include Subfolders, or an ordinary static `CollectionId`. Smart Sources are rejected by the service and database. Source references are restrictive: change/delete dependent Smart Collections before deleting a static Source Collection. Deleting a Smart Collection removes only its organization and definition, never source media. Whole-Catalog migration safety, backup/recovery, relocation, and root remapping preserve this data. Mutations use the existing immediate transaction, optimistic revision, mixed ordering, and Catalog mutation-admission lifecycle.

## Shared query contracts

`BrowserQueryIntent` version 3 stores Match mode and shared `BrowserFilterPredicate` alternatives with named enum values. Each distinct Field is one visible row; alternatives OR within that row, and Match All/Any combines rows with AND/OR. This is a fixed composition model, not a recursive Boolean tree. The editor prevents duplicate field rows. Sort remains presentation and is omitted.

File or path is an ordinary `FileOrPath` predicate with literal case-insensitive substring matching against filename or relative path. It participates normally in All/Any. Save current eligible Browser view starts at All, groups same-field alternatives into a single row, and projects Browser search into FileOrPath, preserving `(Images OR RAW) AND Picked AND search`. The ordinary Browser search box still ANDs search with its facets; it is normalized only when captured as a definition. Version 1 development documents are read and normalize their SearchText into the ordinary field. This intentionally changes old development-era Any+search behavior; no hidden legacy conjunction is retained. Writes emit version 3 without SearchText. Version 2 documents also remain readable. Version 3 adds explicit unset ColorLabel intent and inclusive maximum Duration semantics. Missing/unsupported versions, fields, and document properties fail visibly.

`BrowserQueryEngine.Filter` remains the sole predicate executor. Empty definitions accept the Source universe.

`BrowserGridModel` retains candidate tiles for metadata/state hydration, caches the defining-query membership projection, and applies the independent transient view query afterward. A view-filter change cannot write a Catalog definition. Metadata and authored-state updates reapply both stages. Selection is removed when an asset leaves defined membership; normal view-filter selection behavior remains intact.

`BrowserFilterDescriptors` shares field labels, editor kinds and value sources between Browser faceting and `BrowserFilterRowEditor`. Known multi-value choices use supported media types, classification states and source metadata/keywords; selected saved values remain editable even if absent from the current known Source. Rating uses its shared operator/threshold predicate; date rows preserve multiple structured ranges. FileOrPath uses arbitrary text. Source value discovery reads Catalog/Preview/authored state asynchronously without navigating or triggering discovery. A source change rejects stale value results.

Future #299 Camera Profile/Gamut fields register their value source in this shared descriptor registry and their evaluator in the existing predicate contract, not in a Smart-specific list or query engine. Query persistence has no separate field list. Verify #299 integration again if it merges before publication.

Authoring metadata distinguishes finite choices, observed facets, preset/custom dimensions and fps, time, dates, rating, state and arbitrary text. Resolution and Frame Rate use one selector with Custom switching the same value area into structured input. Resolution presets are the exact raster sizes approved in the latest review; frame-rate suggestions reuse `MediaFrameRate.Canonical` plus observed values. Duration accepts inclusive is at least/is at most, preserving numeric seconds. Color Label adds a semantic `MatchUnset` predicate displayed as Not set. Binary selectors expose only their two actual states; already-captured dual-state alternatives remain explicit to preserve Browser fidelity. Camera/Lens/Keywords are unavailable for new rows when their known vocabulary is empty, while existing selections stay editable. See the [complete current editor inventory and Aspect Ratio architecture stop](validation/smart-filter-editor-inventory-217.md). Aspect Ratio is not exposed because authoritative video display geometry is not available to the persisted Browser query pipeline.
The Match sentence and embedded ComboBox live in one replaceable WPF DataTemplate, with semantic enum values independent of lowercase English labels. This follows #233's standard WPF resource direction without attempting the application-wide localization migration. The row editor replaces the former generic string parser. Browser and Smart date inputs share Lightflow chrome.

## Source loading

Opening a Folder-backed Smart Collection first reads known Catalog candidates and hydrates their existing Preview and authored state. A dedicated instance of the existing `BrowserNavigationSession` then uses `NavigateSourceAsync` to reconcile the logical Source with explicit saved recursion. This override does not change ordinary Folder recursive-root configuration.

The existing bounded direct/recursive discovery services remain authoritative. Recursive progress coalesces intermediate Catalog candidate publication without an additional filesystem traversal. One active Browser generation and cancellation token reject stale results. Completion retires intermediate publication before attaching the final Preview batch. The existing Browser working spinner and “Updating Smart Collection…” status remain nonblocking. Offline Sources retain known candidates with unavailable state and an explanatory diagnostic.

Static Sources use `BrowserCollectionScopeService` without filesystem enumeration. Catalog membership notifications refresh an active dependent Smart Collection. Relevant Folder monitoring events refresh current Catalog candidates. Creating definitions never scans all saved Sources; only opening/restoring the selected Smart Collection triggers its discovery.

Editing filters/name/Location with an unchanged Source reprojects current candidates without scanning. Source or recursion changes reload and reconcile. The existing workspace Collection identity restores Smart selections through the same load dispatch.

## Interaction

- Browser toolbar (beside Lock Filters) and display-area context menu save current eligible Source, recursion, Search, and filters.
- Collections header and Collection Set context menu start with no filters and current eligible Source. The Set context supplies organizational Location.
- Folder context → New Smart Collection uses that Folder, no filters, and top-level organizational Location. Recursion is copied only when that Folder is the currently displayed ordinary Folder; otherwise it starts off. Right-click does not navigate.
- Current Smart scopes cannot supply a Source; the required Source remains blank.
- Collection Sets receive focus and context targeting without becoming Browser asset scopes. Their caret, keyboard navigation, hierarchy operations, and three creation actions remain available.
- Collection breadcrumbs resolve all organizational parent Sets, including after rename/reparent. The previous omitted hierarchy was presentation-only; parent identities were already persisted correctly.
- Smart Collections have a distinct icon and share hierarchy rename/move/reorder/delete. They are absent from manual membership destinations, reject media drops, and expose no manual removal action.

## Local validation and acceptance

See [the #217 acceptance checklist](validation/smart-collections-217.md). Automated UI checks construct and render the definition dialog offscreen without showing desktop windows. The packaged application remains subject to Jeremy's hands-on visual and functional acceptance before publication.
