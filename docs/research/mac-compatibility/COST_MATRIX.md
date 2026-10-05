# Cost and distribution decision matrix

Verified against current primary sources on **2026-10-05**. USD amounts; tax/local-currency enrollment may differ. Estimates are labeled. No purchases, enrollment, signing or installation were performed.

## Apple paths: six distinct answers

| Path / question | Apple fee | Requirements / friction | Recommendation |
|---|---|---|---|
| A. Build/run own Mac | $0 membership required | Free account/tools; local development signing is distinct from customer trust | Start proofs here |
| B. Share unsigned/unnotarized builds | $0 distribution membership required | Gatekeeper may block; after attempted launch: System Settings → Privacy & Security → Open Anyway → Open; managed policy can prevent exceptions | Controlled internal proofs only |
| C. Professional direct website download | **$99/year** Developer Program | Developer ID, timestamp, hardened runtime, sign nested code, notary submission and staple; normal first-download Open confirmation remains | **First public path** |
| D. Mac App Store | **$99/year**, same membership | App distribution signing, sandbox, review and store packaging; applicable sales commissions separate | Optional later, no requirement to use Store |
| E. Recurring Apple charges | Membership renewal for paid distribution access | No separate per-app signing/notarization charge shown in membership benefits; free development/sharing has no annual membership requirement | Budget $99/year plus local taxes |
| F. Framework/runtime fees | $0 required for selected free core stack | Optional tools/support/controls have independent terms; native/media notices/compliance still work | Avoid mandatory commercial framework dependence |

Sources: [Apple account and program comparison](https://developer.apple.com/support/compare-memberships/), [membership and direct distribution](https://developer.apple.com/programs/), [Gatekeeper as documented May 27, 2026](https://support.apple.com/en-us/102445), [Store rules](https://developer.apple.com/app-store/review/guidelines/#software-requirements). Enterprise Program's $299/year is for qualifying employee distribution and is not Lightflow's public-distribution requirement. Eligible fee waivers are not assumed.

App Store commission is not a fee on distributing a free app. Apple's included-program terms describe 30% standard applicable digital-goods commissions, with 15% qualifying Small Business/certain subscription cases; actual commercial agreements/region/product model must be checked if monetization is chosen. No sales model is inferred here. [Apple included benefits/commissions](https://developer.apple.com/programs/whats-included/).

Unsigned does not mean malware bypass: exceptions do not guarantee a revoked, damaged or malicious app can run. Use per-app documented trust workflow, never globally disable Gatekeeper or strip quarantine as the product installation strategy. Notarized direct distribution does not require App Sandbox; Mac App Store does. Sandboxing introduces user-selected read/write access, security-scoped bookmark lifetime and helper/network constraints. [Bookmark permissions](https://developer.apple.com/documentation/security/accessing-files-from-the-macos-app-sandbox?language=objc).

## Packaging and update work

Proposed layout: Lightflow Studio.app with Info.plist, stable bundle ID/version/icon, Mac executable/runtime, native framework/dylib assets and resources; external user data. Publish osx-arm64 first; distinct x64 package only after acceptance. Universal bundle requires every native slice and loader path, not merely lipo on the entry executable.

ZIP is an archive, DMG a user installation container, PKG an installer; none alone establishes trust. Prefer DMG containing a signed .app for drag-to-Applications; ZIP is an optional alternative. Use PKG only for justified installer behavior and appropriate Developer ID Installer signing. Preserve modes/symlinks and native loader-relative paths. Sign inside-out and notarize final distribution; staple supported app/container, then archive without altering signed contents. Protect signing keys/notary credentials in release-only isolated CI. [Apple packaging](https://developer.apple.com/documentation/xcode/packaging-mac-software-for-distribution?changes=_7), [signing](https://developer.apple.com/documentation/xcode/creating-distribution-signed-code-for-the-mac?changes=_9), [notarization workflow](https://developer.apple.com/documentation/security/customizing-the-notarization-workflow).

JIT runtime requires appropriately scoped hardened-runtime entitlement evaluation; do not copy broad exception sets or assume NativeAOT can eliminate engineering cost. Sign SQLite, decoder libraries, ffmpeg/ffprobe and all executable helpers; verify actual dependency load on a clean Mac. Bundle files cannot be modified for updates/settings. Start with whole-bundle manual replacement, version/source hashes and backup/data-preservation gates. Auto-update/rollback/feed authentication is a separate future decision, not a hidden dependency on a paid distribution service.

## Resource cost matrix

| Resource | Mandatory / optional | Current fact or estimate | Consequence |
|---|---|---|---|
| Avalonia core | Chosen, no fee | MIT | Commercial use allowed without framework subscription |
| Avalonia professional tools/current premium controls | Optional | Separate license/eligibility; legacy FOSS tools exist | Free-control/toolchain spike; do not budget zero for paid offerings |
| MAUI core / Uno core | Alternatives, no core fee | MIT / Apache-2.0 | Does not erase Mac tooling/native work |
| XPF | Not selected | Commercial license | Conflicts with no mandatory framework license preference |
| .NET SDK/runtime and Xcode/CLI tools | Necessary tools, no required paid membership for development | Free access; host/toolchain compatibility constraints | Maintenance/upgrades are engineering work |
| SQLite stack | Necessary, no runtime royalty inferred | MIT provider, Apache2 wrapper, public-domain SQLite | Pin exact native runtime and notices; encryption/support products separate |
| FFmpeg / optional libmpv / LibVLC | Media dependencies, no assumed mandatory vendor fee | LGPL/GPL depends on build; exact source/notices/redistribution obligations | Compliance/build provenance; codec patent rights are separate |
| SkiaSharp / native ImageIO | Proposed image path | MIT / OS framework | Native-dependency and supported-format audit |
| ImageSharp | Not assumed free for all commercial use | Current Six Labors direct closed-source for-profit >$1M gross-revenue rule | Avoid as unconditional zero-fee default |
| IDE/editor | Optional paid IDE | CLI/editor can support free workflow; commercial IDE eligibility varies | Don't make Rider/VS paid license a product build prerequisite |
| Apple Silicon Mac access | Required for truthful native acceptance | Use existing/borrowed/remote host or acquire; price not quoted | Hardware access unavoidable, ownership/purchase not necessarily |
| Intel physical Mac | Optional unless support promised | Additional test/maintenance budget | Don't promise from cross-compilation/Rosetta |
| Displays/audio/removable/SMB/controller fixtures | Needed for relevant acceptance | Existing or additional variable cost | Real device behavior cannot be proven headless |
| Premiere license / Resolve Studio | Integration acceptance dependent | External host subscription/edition terms, no price assumed | Owner test seats/installed supported versions needed |
| Standard public GitHub runners | Current repository public | **$0 standard hosted runner execution**, limits apply | Add Mac CI without assuming mandatory hourly fee |
| Standard private GitHub Mac runner excess | Conditional | **$0.062/billable minute** | 1,000 excess minutes ≈ $62; 5,000 ≈ $310 before storage/tax |
| Larger GitHub runners | Optional | Paid even for public repos | Price according to selected SKU; no included-minute substitution |
| Self-hosted Mac runner | Optional | Current GitHub docs say execution free; hardware/power/ops separate | Dedicated isolated test host; don't expose signing keys to untrusted PRs |
| Distribution bandwidth/hosting | Needed direct-download service | Existing GitHub Releases or separately selected service; no quote | Availability/storage terms still apply |
| Engineering and QA | Unavoidable | Dominant cost; no defensible calendar quote before spikes | Media and Windows view migration lead budget risk |

Sources: [Avalonia tool/core terms](https://docs.avaloniaui.net/tools/faq), [MAUI](https://github.com/dotnet/maui), [Uno](https://github.com/unoplatform/uno), [FFmpeg](https://ffmpeg.org/legal.html), [mpv](https://github.com/mpv-player/mpv/blob/master/Copyright), [Six Labors](https://sixlabors.com/pricing/), [GitHub billing](https://docs.github.com/en/actions/concepts/billing-and-usage), [exact runner rates](https://docs.github.com/en/billing/reference/actions-runner-pricing).

Minimum viable tooling plan: 16 GB Apple Silicon Mac, current qualified macOS/Xcode/.NET, real display/audio, sufficient SSD headroom and disposable fixtures, VS Code/CLI, Instruments/native diagnostics and task-isolated roots. Ideal: 24–32 GB and 1 TB or fast external fixture SSD, two displays, separate native-test host/session, Windows NVENC host, representative peripherals and NLE seats. These are research planning recommendations, not measured Lightflow hardware requirements. The [Xcode host matrix](https://developer.apple.com/xcode/system-requirements) changes; record selected host SDK/deployment floor in release evidence rather than hardcoding a historic Xcode minimum.
