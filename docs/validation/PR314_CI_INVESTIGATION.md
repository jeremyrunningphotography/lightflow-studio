# PR #314 required-CI investigation

Baseline main: `1434713edf17b77258d03f5c808f77a7c9be1213`.
Accepted PR head: `3b277edd37b1625a2ed0714bd5e4e03f19bd55c8`.
Required run: https://github.com/jeremyrunningphotography/lightflow-studio/actions/runs/36158390921 (attempts 1 and 2).

## Evidence and classification

- **Application instance:** both CI attempts failed in the test's finally/join, not in a reported production assertion. The simulated owner is a background thread, not a child process. Its synchronous `WaitForConnection()` cannot be released by the test's `release` event, so a client that never connects leaves cleanup blocked and can mask the original assertion. Classification: confirmed test cleanup defect, with the CI trigger not locally reproduced. The fix uses a cancellable asynchronous pipe wait on the same owning thread, cancels and joins it in finally, propagates server faults, and adds a connection assertion so a pre-connection timeout cannot falsely pass the no-ack test. The 100 ms client deadline and five-second elapsed/join checks remain unchanged.
- **File operation:** CI attempt 2 found `history.json.active.tmp` open during directory deletion. `FileOperationJobs` publishes terminal presentation state before calling synchronous `FileOperationHistoryStore.Complete`, which writes terminal history and the active ledger under one lock. The test used terminal presentation as a persistence barrier. Classification: test teardown/persistence race. It now also waits for the matching history record through the existing synchronized History API; observing the record cannot overtake the active-ledger write. No delete retry, stream suppression or production ordering change.
- **Video rotation:** CI attempt 2 failed `backup.Succeeded` immediately after the first Catalog rotation write. This is before relocation, Preview-store creation/deletion, restart or restore. The boolean-only assertion discarded the backup service diagnostic. Classification remains **unresolved intermittent backup failure**, not an established rotation or Preview defect. Include `backup.Diagnostic` in the assertion to make any recurrence actionable. No rotation or recovery semantics changed.

## Comparison and shared effects

A task-owned full main clone ran the same tests (with only diagnostic assertion text added for backup). Each of the three tests passed 200 invocations on main and 200 on the accepted PR. Another 200 each on the PR while a background task repeatedly called `SqliteConnection.ClearAllPools()` also passed; process-wide pool cleanup is therefore an unconfirmed hypothesis, not a demonstrated cause. Diagnostic harnesses and logs are local ignored artifacts, not production code.

None of these failing paths calls the changed FFprobe normalizer, metadata probe-version selector, derived scheduler, or Browser publication. PreviewPersistence, Catalog recovery/rotation storage, application-instance code and file-operation production code are unchanged from main. #312's detached bitmap/decoder fix is not exercised before the backup failure. Suite scheduling can expose timing races, so absence of a direct call path does not prove all possible timing effects impossible.

After the test-only corrections, each affected test passed 100 additional invocations; all 48 tests in the three directly related classes passed. Full Release/companion and subsequent CI results are recorded on PR #314. These corrections do not change accepted #311 application behavior or require new behavioral hands-on acceptance. Merge remains prohibited by the investigation request; preserve the branch.

Local validation after corrections: Release application suite 2,389 passed, one expected live-Premiere skip, zero failures; companion suite 90 passed. A final affected-class run after moving the readiness assertion inside cleanup passed all 48 tests. No production code changed.
