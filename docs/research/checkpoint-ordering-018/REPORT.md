# LF-WIN-DEV-018: checkpoint ordering and bounded Catalog diagnostics

Disposition: **BLOCK at owner review gate**. Jobs correction has deterministic focused proof. The historical Catalog sharing-refusal holder remains unresolved after the bounded investigation. No merge, package readiness, release or tag is claimed.

Workspace: `C:\Git\Agents\LF-WIN-DEV-018-CheckpointOrdering`; branch `codex/431-jobs-checkpoint-ordering`; fetched main `38bb1ed8fe65a4bb6232a542e79a5d2288fea90f`. Canonical checkout and other agent workspaces were not modified. Root AGENTS.md, assignment registry, #431, independent 016 REVIEW.txt, #423 discussion/failed CI job 114631144672, accepted #412/#422, runtime/store, journal/native ownership and CI/private-desktop infrastructure were inspected. #423 remains unchanged and unmerged; #412 stays completed.

## Administration

#431 is standalone, with no native parent or dependency relationships. Project #4 membership was added; Area uses existing `Export & Jobs`; Priority remains unset. Status was `Next` during orientation, then `In Progress` when implementation began, and is `Review` at blocked Draft PR #433 handoff. No affected Epic or Definition of Done advances. Assignment 018 was absent from fetched main/remote branches; owner allocations 015/016/017 and pending 012 closeout are preserved. The task registry reserves 018 and points to this owner gate.

## Jobs correction and deterministic proof

`ApplicationJobsRuntime.Publish` captured its snapshot before the application serialization lock. A publisher could pause after capture, allow newer lifecycle checkpoints to save, then overwrite them with obsolete state. This source defect is proven; its occurrence in the historical CI interleaving remains unproven.

The regression uses a controlled publisher boundary and a controlled executor. A progress publisher is suspended immediately before entering the publication lock. While it is delayed, the executor completes and a terminal checkpoint with CompletedAt saves synchronously. Releasing the publisher must preserve that terminal checkpoint. On the old ordering, it instead persists Running with no completion timestamp; the assertion fails deterministically. With capture inside the existing publication lock, it rereads current terminal state and cannot regress the file.

The internal nullable synchronization seam is unused by production callers. No scheduler, store, lifecycle, History boundary, persistence schema or mutation ownership was added. Capture and synchronous checkpoint save use the existing lock; executors and async waits remain outside it. Runtime mutations release their runtime lock before observer notification. Aggregate Changed callbacks remain outside the application lock. The existing concurrent 24-job lock-inversion regression and original three-item lifecycle/fractional-progress assertions are retained.

Private-desktop results:

| Run | Result | Retained local evidence |
| --- | --- | --- |
| `lf018-stale-baseline.trx` | Expected FAIL: Completed → Running after delayed publication | `artifacts/validation/6879c79bd9c74089917b1eb600574712/output.log` |
| `lf018-jobs-focused.trx` | PASS 23/23, including deterministic proof, concurrency, cancellation, terminal results and recovery | `artifacts/validation/8db3df4c40c04eb9b23bd1252ec2a3c2/output.log` |

Both used `scripts/Test-Noninteractive.ps1` with focused filters; no desktop bypass or arbitrary retry/timeout increase. Baseline and corrected test deadline remain 10 seconds. The successful Jobs run reported no owned processes remaining.

## Catalog bounded tracing

One instrumented focused execution of the existing two interruption theory cases passed 2/2. Original assertions remain intact: PendingRecovery/no writer, mutation/restore refusal, both database files retained, source/destination ownership release, valid intended durable boundary, receiving-profile recovery and competing-owner refusal. No production Catalog recovery, sharing mode, SQLite, journal policy or lease behavior was changed.

Temporary test-build journal probes recorded UTC open/read/disposal timing, journal path identity, writer/reader native handles and HRESULT/native sharing-code on exception. They were scoped by process-local environment variables to the disposable fixture root. Their source is archived in `TEMPORARY_JOURNAL_PROBE.cs.txt`; the production journal file was restored byte-for-byte before handoff. The temporary test environment setup was also removed. Committed test-only instrumentation records process identity/start/exit, kill/wait/first startup timing and read-only Restart Manager holder censuses. It never calls Restart Manager shutdown/restart.

| Boundary | Child PID / writer handle | Kill request / child exit / wait completion (UTC) | First journal read / outcome |
| --- | --- | --- | --- |
| Activating | 30420 / 0x7A8 | 14:18:10.8097734 / 14:18:10.8519598 / 14:18:10.8521936 | 14:18:10.8886250; acquired reader 0x6E4; verified interruption |
| ActivatedPendingConfiguration | 61220 / 0x744 | 14:18:12.3850791 / 14:18:12.4080957 / 14:18:12.4257431 | 14:18:12.4396916; acquired reader 0x870; verified interruption |

Parent PID 86984 started 14:18:08.7386941 UTC. Child start times and unique journal IDs are in sanitized `PROCESS_TRACE.txt` and `JOURNAL_TRACE.txt`. Before kill, Restart Manager identified exactly the paused child for each journal; after the first startup read it reported no holder. Those snapshots do not identify the historical post-exit conflicting handle. Forced child termination runs no managed writer-disposal path; reader disposal and subsequent successful lease reacquisition establish release in these successful cases, without claiming exact kernel close timestamps. Cleanup order remained kill → wait → startup refusal → blocked authoring/restore → coordinator disposal → source/destination lease probes → receiving-profile open/ownership check → owner disposal. The private-desktop launcher reported no owned processes remaining.

No sharing error occurred in this run, so no failing-case native code or holder was measured. Historical review reports sharing refusal; the instrumented exception path will report HRESULT/native code if encountered. Restart Manager sees registered user-process resource owners, not every kernel/filter/inherited handle. Census before kill adds about 300 ms while the child is paused; instrumentation changes timing and may perturb the race. Passing instrumented cases are not proof that the historical failure is harmless or corrected. Unverifiable evidence must continue to refuse authoring; no assertion wording relaxation or startup retry is proposed.

Local evidence: `LightflowStudio.Tests/TestResults/lf018-catalog-trace.trx`, `artifacts/validation/e6b7dad971ef490caf0fe613a98ef79d/output.log`; fixtures remain under this clone's `artifacts/lf012-process-interruption`. Published paths use `[fixture]`.

## Required owner decision

The kickoff explicitly says to stop when journal lock ownership remains ambiguous after bounded investigation. That condition is reached. No Catalog production or fixture-cleanup defect was identified; no behavioral correction is proposed. If Jeremy authorizes another diagnostic phase, use a narrowly filtered native file-I/O/handle trace spanning process termination and the first failing read, with capture that does not delay that read, and retain a deliberate locked-evidence fail-closed regression. This is a proposal only, not authorization to expand this task.

Full Release validation, required exact-head CI, installer/portable packaging and final packaged executable refresh are **pending** at this stop gate. The final removal of temporary probes has not received another execution. Existing bin output is an instrumented diagnostic build, not an acceptance executable. No startup command is supplied for it. The Draft PR must stay unmerged and is not ready for hands-on acceptance. Resume through focused verification of the final tree, full validation, exact-head required CI, packaging, executable timestamp/provenance and task-process checks only after the owner resolves this gate.
