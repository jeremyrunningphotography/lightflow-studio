# Shutdown measurement record — #267

Specification: [issue #267](https://github.com/jeremyrunningphotography/lightflow-studio/issues/267),
discovered during acceptance of startup PR #266. The original #265 measurements remain
in `startup-265.md`.

On 2026-09-16, the user's activity log recorded storage disposal beginning at 09:56:55
and completing normally at 09:58:54 America/Los_Angeles. No exceptions, recovery
failures or extra backup operations were logged during that launch. The process had
closed its window but was still draining storage work.

An instrumented Release single-file executable reproduced this on the existing local
installation. The harness launched it with the existing startup-smoke and presentation
report switches, waited for presentation readiness plus two seconds, called
`Process.CloseMainWindow()`, and measured until natural process exit. It did not force
termination. No computer-control tooling or destructive data experiments were used.

| Run | Close request to natural exit | Storage shutdown |
| --- | ---: | ---: |
| Instrumentation only | 100.395 s | 100.068 s |
| Cancellation fix, first launch | 0.175 s | See stage log |
| Cancellation fix, second launch | 0.166 s | 0.049 s |

Before the fix, monitoring and derived workers stopped by 39.9 ms, but the storage gate
was not acquired until 100,057.3 ms. Closing Catalog and Previews then took about 11 ms.
The startup Settings usage calculation held that gate while reading Preview records
and enumerating the cache. Cancellation of the read-only calculation releases the gate;
Catalog/Preview closure, worker draining, and maintenance leases still complete normally.

The local before/after stage excerpts are in ignored
`artifacts/startup-profile/shutdown-before-after.log`. These are individual warm-machine
observations, not bounds on shutdown during legitimate writes or slow external I/O.

Focused validation: 67 tests passed. Deterministic regressions cover queued and active
usage cancellation, storage and Preview operations that shutdown must still wait for,
usage lease release, artifact preservation, and cancellation during cache enumeration.

The prior package script silently forced its smoke process to exit after five seconds.
It now fails validation on that timeout or a nonzero exit code; forced cleanup of its
own failed test process remains in `finally`. A successful package build therefore
requires a natural process exit as well as startup presentation readiness.
