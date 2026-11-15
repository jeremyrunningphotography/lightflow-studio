# LF-WIN-DEV-052 — bounded validation launcher recovery

2026-11-15. Target release/1.0. Owner authorized bounded infrastructure investigation/correction, independent baseline verification, then #503 continuation only after clearing the stated root-cause gate. No PR, merge, release/tag or main forward-port.

## Disposition

**Recursion protection verified; original desktop misclassification trigger remains unconfirmed. Stop for owner review under recovery assignment section 13.** Do not treat this as completed I01 or authorize Export acceptance. Feature checkpoint `6935a0f82f236a0a26dea68025e2c09f2d8912da` is preserved, unchanged by recovery work, and remains uncompiled in its owning workspace. The release baseline compilation below does not qualify the feature.

## Findings and causal limits

The old scripts call `Invoke-NoninteractiveValidation` whenever `ValidationDesktop.IsNoninteractive` is false. The invocation starts a child with the same script. No entry marker or nested-wrapper rejection existed. Therefore any child that reports false immediately repeats the wrapper path. That unbounded control-flow defect is confirmed statically and accounts for the observed chain of 76 wrappers. The old logs captured only newly created desktop names; they did not record each child's actual desktop, thread/session identity or detection result. They cannot establish why the child took that path.

The actual detector delegates from `scripts/ValidationDesktop.cs` to `LightflowStudio/ValidationPresentation.cs`: it reads the current thread desktop via GetThreadDesktop, requires a LightflowValidation- prefix, and checks that it differs from the input desktop. CreateDesktop creates a named private desktop; CreateProcess starts the child suspended with STARTUPINFO.lpDesktop set to that name, assigns a kill-on-close job, then resumes it. Session is inherited; no environment marker previously influenced classification. Arguments are transferred in CLIXML, then the child's encoded command imports and splats them. No command forwarding defect was found.

Three controlled, nonrecursive identity probes used those unchanged helpers. Direct `-File`, the original encoded-command/CLIXML/redirection contract, and the exact negated PowerShell guard expression all identified valid private desktops in session 1. Expected and actual names matched; IsNoninteractive was true; `-not` evaluated false. Parents reported Default/false. Each direct probe exited 0 with ActiveProcesses 0. These passes do not explain or invalidate the historical failure. No 76-wrapper reproduction or unguarded real test launch was attempted.

The historical workspace-versus-release attribution remains inconclusive. Current baseline with guard succeeds, but no guarded misclassification was observed, and it is unsafe to infer that the prior cause disappeared or was fixed. No speculative change to production desktop detection was made.

## Narrow correction

Only `scripts/NoninteractiveValidation.ps1` changes runtime launcher logic:

1. Child receives the expected generated desktop name in LIGHTFLOW_VALIDATION_EXPECTED_DESKTOP before invoking the requested script.
2. On helper import, a marked child must match that exact actual desktop and pass the existing actual thread/input-desktop isolation check. Marker alone never establishes isolation.
3. An explicit wrapper invocation from a marked or already-private process fails before creating another desktop.
4. A child mismatch emits expected/actual names and a clear nested-wrapping prohibition; failure exits nonzero. Normal child nonzero exit status is preserved instead of being collapsed to a generic wrapper exception.
5. Existing desktop creation, process job membership, cleanup, timeout, application validation, clipboard, CI routing and production behavior stay unchanged.

`scripts/Test-ValidationLauncher.ps1` adds a bounded four-case regression harness. It runs its orchestrator on the caller's ordinary developer shell, then only launches minimal payloads on private desktops; no automated WPF/native app/test is run on the visible input desktop. Use outside a wrapper:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-ValidationLauncher.ps1
```

## Results

| Case | Observed exit | Wrappers created | Result |
| --- | --- | --- | --- |
| Normal payload | 0 | 1 | PASS; expected/actual desktop match, real isolation true, owned descendants zero |
| Payload exits 23 | 23 | 1 | PASS |
| Explicit nested wrapper | 1 | 1 total, no nested creation | PASS; clear rejection |
| Invalid marker on Default | 1 | 0 | PASS; expected/actual mismatch |

The exploratory four-case run and source-controlled four-case harness both passed. These are regression verification after substantive changes, not retries of a failed test. The production ValidationPresentation.cs and ValidationDesktop.cs are unchanged. No marker is set for ordinary application execution.

Independent full clone: `C:\Git\Agents\LF-WIN-DEV-052-ValidationBaseline`, exact release `4cf5e61dbd7ada0019b23e62ff1dd9292d7651b0`; only the corrected launcher was copied in. Release application and test source were unchanged. Required command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Git\Agents\LF-WIN-DEV-052-ValidationBaseline\scripts\Test-Noninteractive.ps1 -Filter 'FullyQualifiedName~ExportDestinationTests' -ResultsName 052-launcher-baseline.trx
```

**6 PASS / 0 FAIL / 0 SKIP**, 99 ms test duration. One wrapper; compilation and test execution reached; wrapper reports no owned processes remaining. Existing xUnit2031 warnings in ApplicationIdentityTests remain warnings. No full suite was run solely to test the launcher. Baseline clone/test outputs and temporary directory are owned by 052; no other workspace or production Catalog was used.

## Remaining gate

Owner decision is required because the trigger for historical desktop misclassification could not be established within the bounded investigation. The guarded launcher provides fail-closed diagnostics on any recurrence, but a successful bounded baseline does not prove that trigger is corrected. Either authorize #503 continuation with this explicit uncertainty/guard or authorize further bounded infrastructure diagnostics with a concrete reproduction opportunity. Do not waive tests or use the visible desktop as a workaround.

No #503 feature code was revised or tested during recovery. Its schema/UI tests, full Release, GPU Export, installer, portable package and isolated startup remain outstanding. No new packaged executable or valid I01 acceptance launch command exists. Earlier research/failure ZIPs and all raw recursive-launch logs are preserved separately. Historical maintenance: Not warranted for a bounded launcher guard with unresolved historical trigger; no completed architecture milestone is claimed.
