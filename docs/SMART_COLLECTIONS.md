# Smart Collections

Issue #217 adds **required Source + durable defining Browser query** to the existing Collections hierarchy.

## Catalog ownership

Migration 19 adds `Collections.IsSmartCollection` and `SmartCollectionDefinitions`. The Collection hierarchy row owns the stable identity (exposed as `SmartCollectionId` by the definition), name, optional parent Set, mixed sibling ordinal, revision, and timestamps. The definition row owns the required Source and versioned query document. There are no materialized matching `CollectionAssets` rows.

Sources are either a logical `RootId` and normalized relative folder with explicit Include Subfolders, or an ordinary static `CollectionId`. Smart Sources are rejected by the service and database. Source references are restrictive: change/delete dependent Smart Collections before deleting a static Source Collection. Deleting a Smart Collection removes only its organization and definition, never source media. Whole-Catalog migration safety, backup/recovery, relocation, and root remapping preserve this data. Mutations use the existing immediate transaction, optimistic revision, mixed ordering, and Catalog mutation-admission lifecycle.

## Shared query contracts

`BrowserQueryIntent` version 1 stores Search, Match mode, and the existing `BrowserFilterPredicate` data with named enum values. Sort is presentation and is omitted. Missing/unsupported versions, fields, and document properties fail visibly rather than falling back to a different meaning.

`BrowserQueryEngine.Filter` owns all predicate execution. Match All preserves normal Browser faceting: alternatives within one field, intersection between fields. Match Any accepts any rule. Filename/relative-path Search narrows either mode. Empty rules and Search accept the Source universe.

`BrowserGridModel` retains candidate tiles for metadata/state hydration, caches the defining-query membership projection, and applies the independent transient view query afterward. A view-filter change cannot write a Catalog definition. Metadata and authored-state updates reapply both stages. Selection is removed when an asset leaves defined membership; normal view-filter selection behavior remains intact.

`BrowserPredicateEditor` converts rule input into shared predicates; it does not evaluate them. The dialog enumerates `BrowserFilterField`. Future text predicates, including #299 Camera Profile/Gamut, use the existing text value path and shared matching implementation. A new value type extends the shared Browser editor, never a Smart-specific evaluator. Query persistence does not contain a second field list. Verify the final #299 integration again if it merges before publication.

## Source loading

Opening a Folder-backed Smart Collection first reads known Catalog candidates and hydrates their existing Preview and authored state. A dedicated instance of the existing `BrowserNavigationSession` then uses `NavigateSourceAsync` to reconcile the logical Source with explicit saved recursion. This override does not change ordinary Folder recursive-root configuration.

The existing bounded direct/recursive discovery services remain authoritative. Recursive progress coalesces intermediate Catalog candidate publication without an additional filesystem traversal. One active Browser generation and cancellation token reject stale results. Completion retires intermediate publication before attaching the final Preview batch. The existing Browser working spinner and “Updating Smart Collection…” status remain nonblocking. Offline Sources retain known candidates with unavailable state and an explanatory diagnostic.

Static Sources use `BrowserCollectionScopeService` without filesystem enumeration. Catalog membership notifications refresh an active dependent Smart Collection. Relevant Folder monitoring events refresh current Catalog candidates. Creating definitions never scans all saved Sources; only opening/restoring the selected Smart Collection triggers its discovery.

Editing rules/name/Location with an unchanged Source reprojects current candidates without scanning. Source or recursion changes reload and reconcile. The existing workspace Collection identity restores Smart selections through the same load dispatch.

## Interaction

- Browser toolbar (beside Lock Filters) and display-area context menu save current eligible Source, recursion, Search, and filters.
- Collections header and Collection Set context menu start with blank rules and current eligible Source. The Set context supplies organizational Location.
- Current Smart scopes cannot supply a Source; the required Source remains blank.
- Collection Sets receive focus and context targeting without becoming Browser asset scopes. Their caret, keyboard navigation, hierarchy operations, and three creation actions remain available.
- Smart Collections have a distinct icon and share hierarchy rename/move/reorder/delete. They are absent from manual membership destinations, reject media drops, and expose no manual removal action.

## Local validation and acceptance

See [the #217 acceptance checklist](validation/smart-collections-217.md). Automated UI checks construct and render the definition dialog offscreen without showing desktop windows. The packaged application remains subject to Jeremy's hands-on visual and functional acceptance before publication.
