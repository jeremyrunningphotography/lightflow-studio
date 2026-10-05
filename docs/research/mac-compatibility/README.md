# Mac compatibility research and accepted planning

The nine reports below are the unchanged Agent X research snapshot of 2026-10-05,
against main `1119a907b02ef5cc97c9c62b4f2fddb196e540df`. Their historical statements
about uncreated issues, uncommitted artifacts and unresolved support choices describe
that research phase, not the subsequent planning state. Current execution specifications
live in GitHub issues and the [Roadmap Project](https://github.com/users/jeremyrunningphotography/projects/4).

The research was accepted for planning. [Owner decisions and proof contracts](PLANNING_DECISIONS.md)
record the subsequent decisions separately; they take precedence where historical
recommendations differ. Acceptance is not evidence of Mac runtime capability.

- [Main research](MAC_COMPATIBILITY_RESEARCH.md)
- [Platform dependency inventory](PLATFORM_DEPENDENCY_INVENTORY.md)
- [Capability parity matrix](CAPABILITY_PARITY_MATRIX.md)
- [Framework comparison](FRAMEWORK_COMPARISON.md)
- [Cost matrix](COST_MATRIX.md)
- [Risk matrix](RISK_MATRIX.md)
- [Proposed M0–M16 roadmap](PROPOSED_MAC_ROADMAP.md)
- [Sources](SOURCES.md)
- [Historical research validation](RESEARCH_VALIDATION.md)

Compact supporting evidence: [SQLite native asset metadata](SQLITE_MAC_ASSET_EVIDENCE.json),
[strategy scores](STRATEGY_SCORES.csv), [source portability screen](SOURCE_PORTABILITY_SCREEN.csv).
The screen is a heuristic inventory, not a measured code reuse percentage. Raw scans,
downloaded packages and build caches are intentionally excluded.

Repository observations and static package metadata are distinguished from architectural
inference and unproved runtime claims in the reports. SQLite's arm64 asset was inspected,
not executed. Mac playback, Avalonia UI, image fidelity, packaging/signing and native
test isolation have not been qualified on Mac hardware. Historical prices and source
versions require rechecking when execution is authorized.

Only the Epic and M0–M3 are planned now. M1–M3 are unstarted proofs. The later roadmap
remains a proposal: no M4+ implementation or issues are authorized by this planning work.

Current specifications:

- [Mac Compatibility Epic #366](https://github.com/jeremyrunningphotography/lightflow-studio/issues/366)
- [M0 decision record #367](https://github.com/jeremyrunningphotography/lightflow-studio/issues/367)
- [M1 Player proof #368](https://github.com/jeremyrunningphotography/lightflow-studio/issues/368)
- [M2 Catalog proof #369](https://github.com/jeremyrunningphotography/lightflow-studio/issues/369)
- [M3 UI/image proof #370](https://github.com/jeremyrunningphotography/lightflow-studio/issues/370)

See [planning preservation validation](PLANNING_VALIDATION.md) for checks on this documentation change.
