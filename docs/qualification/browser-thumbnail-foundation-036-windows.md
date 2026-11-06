# LF-MAC-DEV-036 — independent DARKMATTER Windows qualification

Status: **not run**. This checklist prepares the required independent same-source gate; it allocates no agent and authorizes no merge. Use the final implementation head identified by the Phase B handoff, never an earlier Phase A commit or another assignment's outputs. Work in an independent full clone under `C:\Git\Agents`.

1. Read root AGENTS.md and `docs/noninteractive-validation.md`. Verify branch/head/tree and clean source. Compare the exact modified shared files and Windows wrappers to the PR. Retain all failures and environmental exceptions.
2. Run Release tests through `scripts\Test-Noninteractive.ps1` using its existing private-desktop boundary. Focus on Preview persistence/schema/retry/maintenance, ThumbnailGeneration, DerivedWorkScheduling, DerivedMediaMetadata, Color, preferred-frame, video rotation and Browser preview/navigation suites, then run required broader regressions. Run neutral Application/Catalog tests too. Do not bypass the private desktop for WPF/native tests.
3. Confirm schema 4 opens existing Windows disposable Preview fixtures, normal and failed cache retention, forced refresh/cooldown, source/Color/preferred-frame invalidation and no Catalog-authored mutation. Exercise scheduler promotion, repeated viewport cancellation/revisit, lifecycle drain and stale UI completions. Windows defaults must remain WIC for images and packaged FFmpeg for videos; no Mac decoder dependency may activate.
4. Use the Phase B generated fixture hashes. The small CFR/VFR/rotated/long videos are in the evidence ZIP. For RAW samples, use the exact public CC0 URLs and SHA-256 manifest; do not substitute another file from the same camera. Independently catalog identical files, compare the same persisted preferred-frame and Color intent where applicable, and preserve requested/actual source presentation timestamp evidence.
5. Generate posters with the **packaged Windows FFmpeg**, using the existing ExtractThumbnail command. Use ffprobe presentation timestamps and an explicit frame-index oracle to identify the actual selected source frame; the Mac-host reference is preliminary evidence only. Compare each Mac/Windows source ordinal: exact preferred, ±1–2 adjacent frames acceptable for review, greater differences NEEDS WORK/FAIL. Include frame images, hashes, PTS and differences; compare image treatment separately from ordinal identity. Exercise automatic unknown/short/1-second/minimum/10%-region/30-second-clamp boundaries and authored preferred frames.
6. Rebuild the exact Windows package from the PR branch using:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller
```

7. Verify dependency checks and packaged startup smoke pass and drain, executable timestamp newer than source commit, and no leftover smoke process. Record package/executable hashes and isolated task-owned data root. Perform explicit Windows owner hands-on acceptance separately; automated tests cannot supply it.
8. Supply the ready-to-copy launch command with **actual absolute paths**, following this form:

```powershell
& "C:\Git\Agents\LF-MAC-DEV-036-BrowserThumbnailFoundation\artifacts\release\LightflowStudio\LightflowStudio.exe" --data-root "C:\Git\Agents\LF-MAC-DEV-036-BrowserThumbnailFoundation\artifacts\lf036-windows-owner-data"
```

The example path is a preparation convention, not a claim that a Windows package exists. Report actual machine, OS, SDK, source/tree, command lines, pass/fail/skip counts, artifacts and acceptance status. Keep #481 open and PR Draft. Exact-head CI and explicit merge authorization remain required after both platform acceptance gates.
