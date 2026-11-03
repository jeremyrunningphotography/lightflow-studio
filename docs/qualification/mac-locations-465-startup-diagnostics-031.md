# LF-WIN-TST-031 — startup-frame diagnostic continuation

Date: 2026-11-03. Required conversation name is exactly **LF-WIN-TST-031 — Mac Locations Windows Qualification**; applied through the chat title tool. Same assignment, no new agent.

## Owner exception approval — 2026-11-03

Jeremy explicitly accepts Windows qualification **with the documented startup-frame exception, pending final implementation merge**. This does not change diagnostic disposition D — Inconclusive, the failed original full-suite record, or the lack of a reproduced baseline failure/root cause. The failure is neither fixed nor proven pre-existing. Windows hands-on 6/6 and earlier Mac I01–I06 acceptance remain accepted. This approval supersedes only the historical pending owner-decision wording below; it grants no merge authorization. See [current qualification disposition](mac-locations-465-windows-031.md#current-owner-disposition--2026-11-03). Publication is separate documentation-only; raw diagnostic evidence remains local.

## Disposition: D — Inconclusive

The accepted implementation's previous dependency-complete full suite failed `StartupRestore_NativeVideoSurvivesTaskbarReveal(seconds: 1)`. Three fresh isolated runs now pass on the accepted source and three pass on the verified pre-#465 baseline. This does not reproduce a baseline failure, prove an accepted-source regression, establish comparable failure frequencies, or justify a clean qualification. Suite-context/timing or transient rendering state remains plausible, not established. Confidence is high in the recorded results and comparison provenance, low in a specific root-cause attribution.

Stop for Jeremy/ChatGPT disposition. Recommend reviewing this as a context-sensitive native startup-frame limitation candidate; accepting that limitation requires an explicit owner decision. If stronger attribution is required, separately authorize a bounded suite-order/native-frame telemetry investigation, without weakening the existing assertion. No production correction is proposed or implemented here.

## Provenance

| | Accepted | Pre-#465 baseline |
| --- | --- | --- |
| SHA | `8643942d8cb5de111e7f1d9ea73d09e32bd84308` | `1ea4da350371c277eb95c6208310a5d355cc5085` |
| Tree | `ba4299d4d8ac79ea687592d65025d66c138a3f66` | `d88f735be83d176227fea0a5332bf50ebb39de03` |
| Full clone | `C:\Git\Agents\LF-WIN-TST-031` | `C:\Git\Agents\LF-WIN-TST-031-Baseline` |
| Branch | `codex/031-windows-qualification` | `codex/031-pre465-diagnostics` |

Baseline is the verified PR merge base, also the live PR's base SHA, not a newer main. It was cloned independently with `git clone --no-hardlinks` from this assignment's Git history and checked out at that exact commit. No worktree or other task's outputs were used. Live PR #467 matched accepted head and was Draft/open/unmerged at orientation.

The complete Windows application/test trees, validation scripts, dependency inputs and Windows project/build configuration have no differences between these sources. The shared Application/Catalog changes were inspected in prior qualification; the isolated startup test directly constructs playback objects and does not exercise Browser discovery or Catalog reconciliation. This narrows a causal path but is not proof that full-suite shared process state cannot matter.

## Equivalent dependency preparation and execution

Both clones ran these exact preparation commands in their own workspace:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Get-Ffmpeg.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Get-PlaybackDependencies.ps1
```

Only this assignment's pinned archive files were copied into the new baseline cache. Both scripts independently verified them and produced separate native outputs. All playback-bin file hashes match across clones, as do the loaded test-output FlyleafLib.dll, local Flyleaf package, test source and backend source.

- Encoding FFmpeg: `8.1.2-34-g9b6c8969e0`; archive SHA-256 `089e4169e93b2b3f3acbfced3c0704d24276a225641bdda04d796d28b07a2a38`.
- Playback FFmpeg: `9.0.1-6-g9d4ca21220`; archive SHA-256 `138fe84aa6e4836920f615704115d7ecf4cfbfc20f9a3d5c60f27c1e604fc12b`.
- FlyleafLib: `3.11.8-lightflow.1`; package SHA-256 `2f4cf11b5231b11bb9b7891001aa3827dae7fcfeffe86609ecf7b593a376a6a0`; FFmpeg bindings `9.0.0`.
- Windows PowerShell child `5.1.26100.9711` for all prescribed test invocations (parent harness PowerShell 7.6.5). Same DARKMATTER host, Windows build `10.0.26300.0`, SDK `8.0.423`, x64 test runtime `.NET 8.0.29`, VSTest `17.11.1`, xUnit adapter `3.1.4`.
- NVIDIA RTX 4080 SUPER driver `32.0.16.2101`; Meta/Virtual Desktop monitor adapters also present. Inventory is not proof of the selected rendering device or a cause.

Three rounds, serially alternating accepted then baseline. Each invocation starts a fresh test process through the repository's private-desktop boundary. Both existing theory cases ran; assertions and timing were unchanged. Execute from the source clone, substituting accepted/baseline and round 1/2/3 in ResultsName:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Noninteractive.ps1 -Filter "FullyQualifiedName~StartupRestore_NativeVideoSurvivesTaskbarReveal" -ResultsName LF031-startup-accepted-1.trx
```

No full-suite rerun, app launch, visible desktop control or private-desktop switching occurred. Fixtures/TEMP remained in each clone's own sibling validation area. No normal user profile was used.

## Results and timing

| Source/case | Round 1 | Round 2 | Round 3 | Failures/skips |
| --- | --- | --- | --- | --- |
| Accepted seconds:1 | PASS 1.422s | PASS 1.446s | PASS 1.477s | 0 / 0 |
| Baseline seconds:1 | PASS 1.467s | PASS 1.463s | PASS 1.454s | 0 / 0 |
| Accepted seconds:0 control | PASS 5.016s | PASS 2.069s | PASS 2.028s | 0 / 0 |
| Baseline seconds:0 control | PASS 4.883s | PASS 2.048s | PASS 2.000s | 0 / 0 |

These are TRX whole-test durations, not measured frame-ready latency. Each source passed 6/6 test cases over three processes; all six launchers returned 0 and reported no owned processes remaining. Baseline first build emitted the same three pre-existing analyzer warnings; other diagnostic builds emitted zero warning lines. Blame's normal no-sequence-needed notice is not a playback warning. All diagnostic test/native output was retained; no additional engine/FFmpeg error or warning was emitted in these successful isolated runs. Internal renderer-frame telemetry was not available from the unchanged test.

The prior complete accepted-source Windows result remains 2,879 pass / 1 fail / 2 skips. Failure at suite elapsed ~5m25s, test reported ~1s, was `No presented video frame is available`, at StartupVideoPresentationTests.cs:55 and FlyleafPlaybackBackend.cs:457. This remains historical failed evidence; the interrupted prerequisite-incomplete run is not used as a comparable sample. No full baseline suite was run, so no full-suite failure-rate comparison is claimed.

## Native path assessment

- Each case synchronously generates a three-second 160x90, 10fps FFV1/PCM fixture with checked FFmpeg exit status. Successful tests validate non-black pixels and the saved timestamp, rather than merely opening a file.
- Open restores a paused playhead at zero or one second under a cloaked startup window. The test reveals the taskbar/window, replaces the native presentation surface, pumps Loaded/ContextIdle, waits the existing 300ms and checks surface visibility, ownership/parenting, uncloaked state, paused state, preserved position and a replaced handle.
- The historical failure occurs after those assertions; it is not the test's hidden-window/incorrect-parent/cloak assertion. That does not prove all compositor/decoder state was ready.
- RevealStartupVideoPresentation disposes the old view and attaches a new one. Backend host replacement is part of this native handoff. CapturePresentedFrameAsync immediately calls TakeSnapshotToBitmapSource; a null bitmap throws the reported exception. The 10-second cancellation token does not poll or await a presented frame. Dispatcher idle and the fixed delay therefore do not themselves assert native frame readiness.
- A renderer/frame handoff race or earlier-suite state could explain full-suite failure with isolated success. No frame-state trace proves either. Stable isolated durations do not establish a changed failure frequency or physical foreground behavior.

## Owner acceptance, workspace and evidence

Jeremy's continuation explicitly accepts all six Windows hands-on checks: Locations/disclosure/selection/expansion; recursive indicators; inherited/leaf capability; navigation/Refresh; ratings/annotations/Collection persistence; shutdown. This supersedes the previous report's pending owner checklist without rewriting its historical results. Existing package/startup/dependency evidence and five-job exact-head CI PASS are retained. The accepted executable is not rebuilt or replaced for diagnostics.

Accepted workspace changes: pre-existing tracked assignment-registry edit; prior untracked `.task-notes/` test artifacts and qualification report; new untracked diagnostic report. New ignored evidence/harness lives under `artifacts\qualification-031\startup-diagnostics`. Baseline tracked/untracked Git status is clean; ignored outputs/cache/TRX and sibling validation TEMP are task-owned. No production/test/script/dependency source was edited, no commit/push occurred, and no user process was terminated. Final inventory found zero task-owned app/test/native processes.

Evidence root: `C:\Git\Agents\LF-WIN-TST-031\artifacts\qualification-031\startup-diagnostics` contains Run-Comparison.ps1, six launcher logs, results.json, timings.json, environment.json, input-hashes.json, playback-bin-hashes.json and remaining-processes.json. TRX files remain in each owning clone's `LightflowStudio.Tests\TestResults\LF031-startup-{source}-{round}.trx`. Launcher logs identify each unique private-desktop inner log. Original full-suite TRX/log and previous package provenance remain untouched.

Final Windows qualification acceptance remains for Jeremy/ChatGPT; no clean-pass claim follows from these reruns. PR #467 stays Draft/open/unmerged; #465 stays open. Explicit final merge authorization is still absent. No release/tag, issue/Project mutation or unrelated work was performed. No Chronicle update warranted for this bounded diagnostic continuation.
