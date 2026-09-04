# Feature: Metadata Browser

## Goal

Provide a clear, searchable view of image, video, audio, and file-system metadata.

## Requirements

- Friendly summary and raw detail views
- Multi-file comparison
- Search fields
- Copy field values
- Export CSV and JSON
- Distinguish embedded metadata from file-system properties
- Highlight inconsistent creator, date, camera, and copyright fields

## Catalog classification architecture

Ratings, review flags, color labels, and keywords are durable Catalog facts keyed by stable `AssetId`.
They are independent of source paths and Preview artifacts, so remapping a Media Root or rebuilding
Previews cannot discard them. Browser tiles and the Player consume the same Catalog projection.

Classification filters are ordinary `BrowserFilterPredicate` values within `BrowserQuery`: values of the
same facet OR together and different facets AND together. This plain query representation is deliberately
serializable for future Smart Collections. The Browser query lock snapshots complete presentation intent
(search, media type and advanced predicates, sort, and direction), never navigation scope. Manual ordering
is Collection-only and temporarily materializes as Name ascending in folder scope while the locked snapshot
continues to retain Manual for a later Collection.
