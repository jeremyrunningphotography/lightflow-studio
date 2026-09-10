# Player filmstrip final acceptance (#111)

Jeremy accepted the filmstrip UX and seek refinement hands-on on September 10, 2026.
Final acceptance started from main `78ef59fbca9969e51542e96e5becfb2317bd8b46`
and accepted branch commit `e6ef435cfa0a720289be0b9cc1a073464ff55df7`.

The existing Browser result/selection projection supplies a captured, ordered
AssetId review set to the shared PlayerViewerHost. Single-open uses compatible
results; Enter, double-click, and context-menu Open support selected subsets.
All asset navigation uses one source-switch path, paused at the destination's
own In, with cancellation and generation guards. Browser return preserves the
opening subset or selects/reveals the traversed result. Hidden strip visibility
is workspace layout state; no per-asset playhead history is stored.

Final review found one compatibility edge case: a multi-selection with only one
compatible asset expanded to all results. Subset intent now uses the original
selection count before excluding unsupported members. Accepted layout and
interaction semantics are otherwise unchanged.

Deterministic coverage includes ordered mixed results and filtered queries;
actual folder/Collection restoration; restored hidden review sets; all three
Open entry points and Browser return selection/reveal; Ctrl+Arrow, ordinary frame
stepping, button/click navigation and boundaries; repeated per-source In/Out
restoration without writes or playhead leakage; delayed path/range cancellation;
missing members; teardown; and a 10,000-item virtualized strip with fewer than
100 realized containers. Existing Player input-isolation, fullscreen, lifecycle,
cadence/audio, and workspace suites remain part of affected/full validation.

The seek refinement uses the same native decoder and serialization. Real-engine
regressions compare seek results with FFprobe decoded PTS on VFR and long-GOP
B-frame media, including near-start/end, backwards, and rapid replacement seeks.
These are correctness checks, not machine-specific latency thresholds or proof
that every demuxer's fallback case has been triggered. See
`dependencies/flyleaf-fast-seek.md` for the measured motivation and source recipe.

The repository-local Flyleaf package reproduced byte-for-byte from its published
base commit plus committed patch during final acceptance. Documentation and
packaging tests verify that the corresponding patch ships with the manifest.
Computer-control tooling was not used. Final local Release validation passed:

- 24 focused filmstrip/Browser-return/workspace/seek tests.
- 899 affected Player, playback, Browser, workspace, range, Trim and packaging tests.
- 1,865 tests in the complete suite, with no failures or skips.

The first affected pass exposed stale context-menu and dependency-version
expectations; those and the corresponding packaging metadata were corrected
before the clean runs above. Required packaged executable checks and CI results
are recorded in the Draft PR handoff.
