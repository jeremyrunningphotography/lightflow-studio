> Historical NAS checkpoint. Superseded for current M2 scope by [completed P2 final recommendation](local-portability/P2_FINAL_RECOMMENDATION.md): G2 PASS recommended, owner acceptance pending. Historical findings below are retained.

# M2 final disposition checklist — 2026-10-05

Outcome D; G2 open/unpassed. Detailed evidence and reasoning: [LIVE_NAS_QUALIFICATION.md](LIVE_NAS_QUALIFICATION.md).

| Requested item | Result |
|---|---|
| 1. authoritative main | aed6c2906637c8a5a9d71c1c18ac24bed48a8724 |
| 2. X2 branch/commit | codex/x2-nas-qualification-20261005; final exact commit in work/evidence/RESUME_HANDOFF_CHECKPOINT.md |
| 3. Mac/NAS environment | M2 Pro arm64 macOS26.6.2 build25G83, SMB3.1.1 JRVault Development; raw NAS_ENVIRONMENT and smb capabilities retained |
| 4. actual SQLite/runtime/options | SQLite3.53.3, PCL2.1.13/Microsoft8.0.29; source/compile options/native hashes retained |
| 5. Catalog schema | 19 |
| 6. Preview schema | 4 |
| 7. current/default journal | Catalog demands WAL/FULL/NORMAL/5000ms; local Preview WAL/NORMAL sync1; NAS DELETE/FULL proof-only |
| 8. network conclusion | No generic remote SQLite safety guarantee; sync/locks/cache must be truthful |
| 9. WAL on SMB | Request returns delete; production create MigrationFailed/open Unreadable |
| 10. alternative | Local active WAL + closed central snapshots recommended; DELETE is credible experiment, unqualified |
| 11. live NAS create/open/read/write | Current production create/open fail; raw pinned DELETE reads/transaction writes succeed |
| 12. authored state | 23 tables compared,32assets; exact Notes/Revision/UpdatedUtc delta only |
| 13. integrity/reopen | DELETE integrityok; production open still fails policy |
| 14. disconnect/reconnect | Only closed directory rename away/back; actual disconnect/remount/sleep not tested |
| 15. stale lock/recovery | same-host SQLBUSY5/reacquire; ownedSIGKILL hot17920-byte journal recovery passed; stale SMB/application owner unqualified |
| 16. backup/restore | NAS/local both directions SQLite-aware backup equality; closed protected restore/rollback passed |
| 17. case-sensitive | Distinct APFS case-only names collide under existing folded asset key |
| 18. Unicode | NFC/NFD filesystem aliases can create duplicate Catalog IDs; SMB aliases observed |
| 19. root remapping | Existing RootId/per-MachineId mapping adequate concept; exact keys/containment need fail-closed enforcement |
| 20. Catalog-on-NAS | Current production unsupported; no NAS support acceptance |
| 21. Preview location | Recommend independently machine-local rebuildable Preview; measured local store4 |
| 22. concurrent policy | One active Lightflow owner; no multi-writer requirement, implementation or takeover guarantee |
| 23. performance | Exploratory32asset warm timings retained, no threshold/performance pass |
| 24. Windows roundtrip | pending; no SSH/simulation |
| 25. handoff | WINDOWS_NAS_FIXTURE_HANDOFF.md, exact origin ZIP/hash, defined singlemapping+Notes edit and local Mac return |
| 26. identity changes | Initial rejection/preflight plus operation guards; future versioned root-key policy/migration for general exact-name support; none implemented |
| 27. initial support | Local active Catalog+Preview; closed NAS snapshots/media; reject ambiguous roots pending qualification |
| 28. Outcome | D, proposed B/C direction |
| 29. gate | open/unpassed, architecture decision required |
| 30. artifacts | docs/research/mac-catalog-proof and preserved work/data +work/evidence |
| 31. issue/Project/PR | #369/#366 open; parent366/prerequisite367 verified; Project-v2 inaccessible; no PR/merge |
| 32. production changes | none, empty production/schema/package diff |
| 33. X1/X3 | remained paused; not messaged/resumed |
| 34. process/clean state | owned build servers shut down, no X2processes; NAS mounts/fixtures/evidence preserved |
| 35. unexpected findings | SMB WAL request returns DELETE/real production policyfailure; invalid Previewfixture fingerprint corrected; same-host DELETE successful does not qualify network durability |
