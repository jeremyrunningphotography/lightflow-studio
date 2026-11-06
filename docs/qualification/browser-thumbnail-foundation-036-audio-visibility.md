# LF-MAC-DEV-036 — Audio Browser visibility parity correction

## A. Assignment and provenance

Same **LF-MAC-DEV-036 — Browser Thumbnail Foundation**, independent full clone
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation`, branch
`codex/036-browser-thumbnail-foundation`, [Draft #486](https://github.com/jeremyrunningphotography/lightflow-studio/pull/486),
[#481](https://github.com/jeremyrunningphotography/lightflow-studio/issues/481), parent #480.
Starting source `6e1312f03e704c7c58e4b09a13153ce6f8a5cb0c`, tree
`7ca9969d2c09f2c3a01ec03c151b07c30167d7ec`. Main independently verified at
`7f755460e36375234281481cea3004f60344325b`. Corrected immutable SHA/tree and package
receipts are appended to the generated handoff after publication.

Current AGENTS.md, #481, completed #460 specification, actual Windows predicate/tests,
Mac authoritative/recursive/cached projections and accepted performance/UI code were
reviewed. LF-WIN-TST-037's owner-supplied Gate A findings were independently confirmed
from source. That agent stopped before qualification; this report does not promote
its Gate A or Windows qualification to PASS. No other agent/workspace is allocated
or modified. All earlier accepted source/evidence is preserved.

## B. Executive summary

Windows presents StillImage, RawImage and Video only. Mac used IsKnown, which also
includes Audio, exposing MP3/WAV/FLAC in both authoritative and cached-first views.
The thumbnail renderer's truthful unsupported-audio state could not fix visibility.

The only production change is a category predicate at Mac Browser projection
boundaries. Audio remains recognized, reconciled and stored, but does not become a
Browser asset or transient cached row. No extension blacklist, global classification
change, Catalog rewrite, schema migration, cache identity or Windows behavior change.
Owner focused Mac acceptance and independent Gate A revalidation remain pending.

## C. Visibility architecture

Windows `LightflowStudio/BrowserGrid.cs`, `BrowserGridModel.PresentableCategories`
and `IsPresentable`, admits exactly StillImage/RawImage/Video and rejects directories.
`BrowserGridTests.Populate_StandaloneAudioDoesNotAppear` and unknown/category/order
checks are the source reference; existing MP3 fixtures exercise exclusion.

The Windows predicate lives inside its grid presentation implementation; there is
no suitable existing neutral predicate in Application/Catalog/UI. Reusing that WPF
owner would require unnecessary extraction/coupling. A single narrow Mac
`IsBrowserPresentable(MediaTypeClassification)` uses the exact category semantics
against the existing MediaTypeRegistry; it does not duplicate extension classification.

Before: Mac IsKnown admitted Audio. After: all Mac Browser asset projections use
that three-category presentation policy. Global `MediaTypeClassification.IsKnown`,
MediaTypeRegistry and CatalogReconciliationService remain unchanged. Audio remains
legitimately ingested with stable identities/authored state; raw repository reads
still return it. No existing audio row or Preview record is removed to hide it.

## D. Implementation

Production: `Lightflow.Platform.MacOS/MacShellCatalogSession.cs` only.

- Existing Catalog Browser snapshot and read-only folder snapshot filter projected assets.
- Cached scope filters before BrowserViewModel can publish any provisional collection.
- Authoritative direct/recursive visible filesystem candidates and final Catalog projection
  apply the same predicate after unchanged reconciliation.
- Refresh, switching and reopen reuse those paths, including legacy audio Catalog rows.

Filtering happens before the UI receives Assets. Its existing selection/index mapping
therefore operates over a dense visible list, not sparse filesystem positions.
Hidden audio IDs cannot be selected; surviving visible IDs remain stable. Existing
epoch/generation cancellation rejects obsolete results. No UI selection, viewport,
scroll, thumbnail label, status bar or source-verification code was changed.

Tests: `Lightflow.Platform.MacOS.Tests/AudioBrowserVisibilityTests.cs`. The new
`tools/AudioVisibilityQualification` uses production classification, Mac session,
BrowserViewModel, thumbnail/cache and Catalog repository paths on disposable fixtures.
Registry and this report are updated in the same implementation PR.

## E. Validation

A mixed direct folder contains valid JPEG, PNG, previously qualified public Canon CR2,
synthetic MP4 and MOV, a deliberately corrupt MP4, MP3, WAV, FLAC and nonmedia TXT;
the same mix is under Child. The Mac adapter unit fixture separately uses tiny
classification-only payloads and does not assert decoder support from extensions.

Native focused test passes across all four projection APIs, recursive discovery,
legacy reopen and repeated reconciliation. The production integration receipt passes:

- Direct view: 6 visual rows; recursive: 12. Audio and unknown are absent throughout.
- All 114 observed Browser publications remain visual-only, including provisional,
  refresh/reopen and rapid replacement after actual fingerprint work begins.
- Every visible array position selects the corresponding AssetId; selecting stored
  hidden audio IDs clears selection. Refresh retains a surviving selected visual ID.
- All 6 legitimately stored audio records retain AssetIds, rating 4 and Collection
  memberships. The recursive Catalog still contains 18 known records, not 12.
- Supported image/RAW/video previews are Ready; corrupt videos remain Unsupported.
- All 10 thumbnail hashes and mtimes are unchanged across reuse/reopen; zero final
  native readers. No Preview schema, fingerprint or renderer alteration.

Three warm direct visits: first asset 0.35–3.77 ms, first cached projection
2.28–7.18 ms (median 2.45), complete reconciliation 153–161 ms. Recursive cached
projection 3.62 ms; legacy reopen 4.94 ms. These are small local-fixture model/cache
measurements, not compositor or cold-device timings. OS caches are uncontrolled;
no new personal-media/NAS benchmark or broad performance guarantee is claimed.

Final broader Release suites pass **359 tests, zero failed/skipped**: Application 141,
Catalog 67, UI 87, Mac 64. Windows application/test cross-build completes with zero
errors and three existing analyzer warnings. Existing cached-first/label/failure,
source invalidation, navigation, Catalog-safety and AVFoundation read-amplification/
cancellation regressions pass. Exact packaged GUI results are appended after smoke.
Windows-compatible compilation is not independent Windows package qualification.

## F. Mac package

Fresh exact-head app:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-audio-visibility/package/Lightflow Studio.app`.
Prepared unused acceptance root:
`/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-audio-visibility/owner-acceptance-data`.

```sh
"/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-audio-visibility/package/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-audio-visibility/owner-acceptance-data"
```

PowerShell equivalent:

```powershell
& "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-audio-visibility/package/Lightflow Studio.app/Contents/MacOS/LightflowStudio.Mac" --data-root "/Users/jeremyrunning/Git/agents/LF-MAC-DEV-036-BrowserThumbnailFoundation/artifacts/lf036-audio-visibility/owner-acceptance-data"
```

Create a disposable local Catalog and browse the task-owned `fixtures/Media` folder
under the new artifact root. Expect six direct visual rows including truthful
Unsupported for the corrupt video; no MP3/WAV/FLAC/TXT rows. Enable Subfolders for
12 rows, Refresh, switch rapidly to sibling Other and back, then reopen/manual revisit.
Confirm no transient audio, contiguous keyboard selection, cache reuse, no Cached
thumbnail badge and normal genuine failure states. Prior accepted format/performance/
NAS behavior requires affected-area spot checks, not an unnecessary full repeat.

Entire JRPhoto4T remains read-only. This correction uses only disposable local and
existing public/synthetic task fixtures, not personal media or owner Catalogs. No
source-management, remount, repair or writes to JRPhoto4T occur. All new output is
inside this task's `artifacts/lf036-audio-visibility` and normal task build/test roots.

## G. GitHub and history

#481 records the verified Windows policy, owner-approved correction and qualification
stop before implementation. Existing Draft #486 receives corrected source and evidence.
Project #4/native hierarchy are preserved: #481 open/In Progress/Browser/unset priority;
#480 open/Backlog with incomplete DoD; #482–#485 unstarted. No #460 historical
specification changes, new issue/PR/agent, closure or merge.

Historical maintenance: **Not warranted** — bounded presentation parity bug fix, not
a new feature/architecture milestone. Registry/report updated in this PR; prior
Chronicle/Development History/source-index records remain intact.

## H. Evidence manifest

Complete generated `artifacts/lf036-audio-visibility/REPORT.md`, internal `MANIFEST.json`,
fixture matrix, production integration/stage receipt, focused/broader tests, build and
package provenance/dependencies, genuine GUI receipt/screenshot if captured.
Portable ZIP: `artifacts/LF-MAC-DEV-036-Audio-Visibility-evidence.zip`.
Separate runnable app ZIP: `artifacts/LF-MAC-DEV-036-Audio-Visibility-Mac-app.zip`.
SHA-256 receipts accompany archives; the manifest hashes each included evidence file.
No personal media, private Catalogs, credentials or large caches are included. Fixtures
and public RAW/video inputs themselves are not in the evidence ZIP. Prior evidence
is preserved under its original paths.

## I. Outstanding gates

Owner focused Mac acceptance of this exact corrected package; LF-WIN-TST-037
independent Gate A revalidation; full independent Windows qualification; exact-head
CI/review; explicit merge authorization. No Gate A PASS or Windows qualification is
claimed. The owner and LF-WIN-TST-037 control that resumption, not this agent.

**STOP FOR OWNER REVIEW.** No merge, closure, #482, new agent or self-run independent
Windows qualification.
