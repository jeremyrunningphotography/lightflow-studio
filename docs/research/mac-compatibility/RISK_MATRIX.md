# macOS risk register

Research 2026-10-05, main 1119a907b02ef5cc97c9c62b4f2fddb196e540df. Likelihood L/M/H estimates chance of materially affecting parity/schedule; impact M/H/C (critical). Scores are judgments, not observed failures on Mac. Gate IDs refer to PROPOSED_MAC_ROADMAP.md.

| Risk | Likelihood | Impact | Evidence / trigger | Mitigation and exit evidence |
|---|---|---|---|---|
| Exact reverse frame/PTS parity | H | C | Current Windows fork needed decoded callbacks/seek fixes; generic player position insufficient | G1 corpus exact predecessor/next PTS, silent retained steps, explicit failure; stop port commitment if no feasible backend |
| Native media surface/overlay/fullscreen | H | H | NSView separate compositing; Uno hosting gap | G1 native view/texture proof with clipping, overlay hit testing, no reopen and same lease |
| Audio drift/restart/device loss | H | H | Current split FFmpeg PCM/video path; WinMM unavailable | One clock where feasible; G1 drift/device/sleep/seek assertions and measured output latency |
| Case-sensitive Catalog collisions | H | C | MediaRoots uppercase keys/ignore-case containment; Companion also folds case | G2 accepted identity policy or explicit initial root rejection; collision/round-trip/no silent merge tests |
| Unicode/illegal cross-OS names | M | H | Native filesystems differ; source may include decomposed/case-only/literal backslash names | G2 NFC/NFD and portable naming policy; preserve display/source names; explicit portability exceptions |
| SQLite native load/signing/version | M | C | Correct RID assets exist but no Mac execution | G2/G4 signed packaged load; report 3.53.3/options/schema19; one runtime Catalog/Preview |
| WAL/backup/restore corruption | M | C | Open-copy/journal loss/Unix unlink hazards | Consistent backup API/quiescence, close connections, cross-OS restore/integrity checks |
| Clean-session recovery evidence weakened | M | C | kernel32 identity/durable-flush assumptions | Native stable identity adapter plus crash/replace tests; never replace with mtime-only trust |
| Mature Windows UI regression | H | H | Broad WPF custom grid/panels/focus lifecycle | Separate Windows migration gate, retained shipping WPF, same behavior fixtures and packaged hands-on acceptance |
| Two-shell feature drift | H if fallback | H | Views maintained independently under A/C | One app/core authority, paired feature acceptance, common view models/tokens, explicit adapter exception registry |
| Color/ICC/tone-map/capture mismatch | H | H | D3D versus Metal and WIC versus new image decoding | G1/G3 vector and golden pixels, ICC/source rotation tests, measured tolerance and monitor-aware policy |
| Required image format missing | M | H | WDP/JXR/RAW platform decoder differences | G3 format inventory and explicit supported decoder/failure UI; do not expand RAW claim |
| Large Browser memory/scroll regressions | H | H | Custom WPF virtualization cannot copy unchanged | G3 realistic large media corpus, container/allocation bounds, scroll anchors, native display performance |
| Free table/control route adds work | M | M/H | DataGrid deprecated; paid tooling differs from MIT core | G3 pin maintained free control or costed custom realization; no mandatory license surprises |
| File operations/Trash unsupported locations | H | C | Windows recoverable deletion rejects unsafe shares; Mac capability differs | Native Trash preflight+result verification; no automatic permanent fallback; external/network fixtures |
| Watch/mount/permission races | H | H | Event hints and removable/SMB roots | Reconcile after overflow/wake/remount, generations, offline nondestructive state, grant-denial tests |
| Process orphan/partial output | M | H | Windows job-object boundary not portable | Task-owned native process tree/timeout/cleanup; abrupt parent termination and partial-reservation recovery |
| Mac GUI automation disrupts owner | M | H | No CreateDesktop mechanism | Dedicated host/session/qualified VM; no global automation in current GUI; headless tests separate |
| Hardened-runtime native loader failure | M | H | .NET JIT, media/SQLite/helper dylibs and rpaths | G4 minimal entitlements, inside-out signatures, clean quarantined installed package, unsigned library rejection tests |
| App Store sandbox scope loss | H if chosen | H | Arbitrary roots/process helpers/loopback/pairing grants | Direct download first; separate sandbox bookmark/network/helper feasibility before Store commitment |
| Premiere duplicate/mismatched projection | M | C | Case/path identity and uncertain dispatch journal | G5 real host save/reopen/project-change/folder-grant, stable destination IDs and blocked ambiguous retry |
| Resolve edition/API mismatch | H | H | #322 API proof pending; external scripting Studio-dependent | Keep independent research gate; no Premiere-shaped assumptions or promised unavailable Subclips |
| TourBox hold/release/vendor transport | H | H | Separate physical/vendor gates incomplete | #354/#346 ownership unchanged; Mac physical preset/permissions acceptance without altering semantics |
| Intel support multiplies matrix | H if promised | H | Separate native slices, codecs/devices/performance | Apple Silicon first; explicit owner-funded Intel fixture/support policy |
| Toolchain/runtime lifecycle | H | H | .NET8 support ends 2026-11-10; Apple/Xcode floors shift | Separately accepted LTS upgrade; pin SDK/OS/RID matrices and revisit at release |
| Codec legal/build provenance | M | H | Optional GPL/nonfree/native dependencies/patents | Exact build/license/source manifests; do not redistribute arbitrary downloaded media builds |
| Timeline/effort underestimate | H | H | Media, UI and paths all need substantive work | Time-box ranked proofs; re-estimate after G1–G3; no unsupported release date |

No identified fundamental barrier requires losing Catalog authoring, semantic actions or the central Jobs model. The critical gates can still make a schedule/budget unacceptable. Architecture approval should fund experiments with explicit fail criteria instead of assuming parity from vendor platform lists.
