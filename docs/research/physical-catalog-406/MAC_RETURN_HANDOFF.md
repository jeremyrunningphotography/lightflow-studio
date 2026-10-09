# Return to LF-MAC-RES-007 — assignment 007

Windows bounded phase completed CONDITIONAL, 30/30 fixture checks and 68/68 neutral tests. Normal Windows removal succeeded at 2026-10-09 20:16:31 UTC, no veto, E: absent. Jeremy may disconnect JRPhoto4T from DARKMATTER and connect it to Mac. Windows claims no Mac return verification.

Continue the existing Mac session, one assignment #406 / Draft #407. Do not allocate another number/issue. Preserve historical LF-BOTH-RES-007 evidence; record new Mac findings under LF-MAC-RES-007. Fetch the documentation branch, retain historical head `4acdf3c80243bc1188b01f48c768e2265e7ed6a7`, and read [Windows phase](WINDOWS_PHYSICAL_PHASE.md).

1. Read-only native reidentify: original physical SanDisk Extreme Pro 55AF, USB, GPT partition UUID and Mac volume UUID from the private original handoff, ExFAT, local mount. Do not assume `/Volumes/JRPhoto4T` without verifying. Confirm Jeremy's reconnect and the existing root/ownership; no new test root or replacement Catalog. Stop on identity/root/hash mismatch.
2. Confirm root and descendants are not aliases/symlinks and remain on that physical volume. Independently measure allocation; Windows final footprint was 37 MiB including directories, 340,050 logical bytes, 38 files. Do not open any owner files.
3. Use the exact existing private root, then run the read-only verifier below against `windows-file-audit.json` and `windows-summary.json`. It hashes **all 24 original and 14 Windows files** before any SQLite access. It reads the closed Mac and Windows copies with immutable/read-only SQLite URIs, preventing sidecar changes, and compares every synthetic row/identity. No production Catalog open or admission bypass.

```sh
python3 docs/research/physical-catalog-406/verify_mac_return.py \
  --root "<verified exact existing physical task root>" \
  --output "<Mac task-owned local evidence directory>/mac-return.json"
```

The existing root's `LF-WIN-RES-007` subdirectory contains Windows artifacts; `expected-state.json` records full baseline/final rows and the single note delta. Main closed hash: `a1e7a33dba9dde85b0d60c4f6baf3b5d90ee4f615a586d421c5c4adb2e634e30`. Original Mac closed hash remains `d9a9010a5733d8342c57cece8a75896ade106265b3c205d250e7f35f88352008`. Windows backup/restore hash `98d608943dd5d22bb76939afdacf1b20507ce698a9abc9a47e1fb216c15b3b30`. Physical expected-state hash `e811a545b501a5eeb32f66fcd27856a4b75d072a4df133780a93feb250fe64f2`.

4. CatalogId `d5a5cf99-1a76-4347-9fe6-d0e8c9d5c086`, RootId `3f01e9f4-8fe6-4fdb-9188-825da780085b` and all 32 AssetIds must remain equal. Only asset `015db1c9-1b7f-41d9-8d07-8e3a73fbee9c` note changes from `Mac physical authored delta` to `Windows physical authored delta LF-WIN-RES-007` in the Windows derivative; the original remains unchanged. Compare all other fields including rating, keywords, collection, markers and subclip strings.
5. Rehash after immutable reads; close all connections, streams and any native assessor/mount anchors. Capture zero task holders with the existing Mac methods. No automatic physical writes or another eject required by this return verification; ordinary removal only if Jeremy requests it.
6. Add Mac findings to Issue #406 and Draft #407 without changing historical evidence. Keep Open/In Progress, parent #77, blocker #393, Catalog/Priority unset. Do not claim product round trip, atomic publication or power-loss durability from compatible research fixtures. Independent review and Jeremy's support-matrix acceptance remain outstanding.

Private Windows evidence: `C:\Git\Agents\LF-BOTH-RES-007-ExternalCatalog\.cache\windows-406`; public source/tool hashes and sanitized test results are alongside this handoff. Windows source packages match production versions, but Mac system SQLite is 3.51.0 versus Windows pinned-native 3.53.3, and neither phase exercised the Lightflow production schema/service or Settings relocation.
