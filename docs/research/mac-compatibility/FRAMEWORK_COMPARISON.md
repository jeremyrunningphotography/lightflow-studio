# UI strategies and weighted decision

Access date for all sources: 2026-10-05. Ratings are architectural judgments for Lightflow, not measured vendor benchmarks.

## Strategies

| Strategy | Windows / Mac design | Initial impact | Enduring feature cost | Recommendation |
|---|---|---|---|---|
| A | Keep WPF; add Avalonia Mac UI; share app/core/presentation models | Avoid rewriting mature Windows views; still build complete Mac shell and media adapters | Domain once, views twice, native QA twice | One fallback if speed/regression dominates |
| B | Eventually Avalonia UI both, native adapters/backends | Highest transitional regression burden; staged gates required | Product/view feature usually once; two native acceptance paths remain | **Primary** |
| C | WPF Windows + Swift/AppKit/SwiftUI Mac over shared core | Native UX strong, C#/Swift boundary and two expert stacks | Views/presentation integration twice indefinitely | Reject for owner's development-parity priority |
| D | Avalonia XPF for WPF compatibility | Could reuse more WPF surface, but native dependencies still block | Commercial licensing and compatibility maintenance; no magical D3D/WIC port | Credible comparator; reject mandatory license dependence |

Qt or Electron would introduce another language/runtime/control/rendering boundary and still require the same media/path proof. No evidence found that either offers enough benefit here to justify expanding the candidate set. XPF is distinct from free Avalonia core and is not assumed a free migration tool. [XPF license](https://avaloniaui.net/licenses/XPF-ENT.pdf).

## B framework candidates

| Criterion | Avalonia core | .NET MAUI | Uno Skia desktop |
|---|---|---|---|
| Core license | MIT; no required commercial framework fee | MIT | Apache-2.0 |
| Optional paid parts | Pro tools, support, commercial controls, XPF separate; legacy tooling available | Commercial IDE eligibility and third-party controls/support separate | Studio/enterprise/IDE tooling separate from core |
| Mac platform | Native AppKit-facing backend, .NET ordinary or native Mac TFM as needed | Mac Catalyst | AppKit shell with Skia UI |
| Desktop workbench suitability (judgment) | Best fit for dense custom C#/XAML UI | Mobile-first abstractions require more desktop tailoring | Strong shared WinUI-style UI, different from WPF semantics |
| Dark visual identity | Shared styles/templates and vector/custom rendering | Handlers/native variation or extensive custom drawing | Shared Skia drawing/templates |
| Browser Grid/Details/large trees | Virtualization/custom layouts feasible; current DataGrid deprecated; prove free maintained table path | CollectionView available; rich desktop Details/tree needs evaluated controls/custom work | Virtualized lists/grids feasible; WPF custom realization must port |
| Panels/splitters/retained views | Familiar desktop control model; port state ownership deliberately | Desktop panel/focus integration requires custom work | Shared control model, port state ownership |
| Drag/drop, clipboard, focus, shortcuts | Documented native backend integration; local ownership and menu dispatch still app work | Platform handlers and desktop gesture differences | Skia/native interop and platform services; test control focus |
| Native menus/dialogs | Mac native menu integration documented | Catalyst menu/picker conventions | Platform shell integrations; native quality requires acceptance |
| Accessibility | Mac peers via NSAccessibility; custom media/timeline needs authored names/peers | Native controls help but custom controls need accessibility | Current docs list VoiceOver; experiential parity unproved |
| Retina/DPI | Framework scaling available; media pixels/layout rounding need tests | Native scaling plus custom media conversion | Skia scaling plus native host tests |
| Native video surface | NSView/HWND hosting documented; separate-layer airspace constraints | Native handler route; backend/lifetime custom | Documented Mac Skia native-hosting gap is major gating concern |
| WPF migration | Familiar concepts, not source-compatible; attached properties/templates/code-behind port | Substantial rewrite | WinUI/UWP API style, substantial WPF rewrite |
| Headless tests | Strong documented control/layout testing | Shared logic and handler tests; native GUI remains | Documented headless Skia; no native-window proof |
| Health evidence | Current upstream releases 12.1.3 and maintained 11.3 line observed | Active Microsoft repo/release lifecycle | Active upstream repo/current desktop docs |
| Performance claim | Needs real Lightflow grid/media benchmarks | Needs real Lightflow grid/media benchmarks | Needs real Lightflow grid/media benchmarks |
| Overall | **Recommend, subject to Player/free-control gates** | Do not select without mobile product need or contrary spike evidence | Reserve comparison evidence; media-host gap makes weaker first choice |

Sources: [Avalonia license](https://github.com/AvaloniaUI/Avalonia/blob/main/licence.md), [tools](https://docs.avaloniaui.net/tools/faq), [Mac](https://docs.avaloniaui.net/docs/platform-specific-guides/macos), [native interop](https://docs.avaloniaui.net/docs/app-development/native-interop), [performance](https://docs.avaloniaui.net/docs/app-development/performance), [DataGrid](https://docs.avaloniaui.net/controls/data-display/structured-data/datagrid/), [headless](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform), [releases](https://github.com/AvaloniaUI/Avalonia/releases); [MAUI core](https://github.com/dotnet/maui), [MAUI supported platforms](https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms?view=net-maui-10.0); [Uno core](https://github.com/unoplatform/uno), [desktop](https://platform.uno/docs/articles/features/using-skia-desktop.html), [native hosting](https://platform.uno/docs/articles/features/using-skia-hosting-native-controls.html), [accessibility](https://platform.uno/docs/articles/features/accessibility/index.html), [headless](https://platform.uno/docs/articles/features/using-skia-headless.html).

Documentation can span releases. Pin a tested framework version and free-control/native dependencies before an implementation decision. In particular Uno's hosting table references Uno 6.0; treat the documented gap as a risk to retest against the chosen release, not a perpetual impossibility. MAUI's multi-version supported-platform page is not a declaration of Lightflow's minimum OS.

## Weighted strategy score

Scale 1–5, higher is preferable, including lower cost/risk. Formula: sum(weight × score)/100. These are uncertain judgments, with media feasibility a hard gate independent of total. A/B/C all need a new Mac backend; no framework wins the Player simply by being cross-platform.

| Criterion | Weight % | A | B (Avalonia) | C | D (XPF) | Rationale |
|---|---:|---:|---:|---:|---:|---|
| One-feature/two-platform efficiency | 15 | 3 | 5 | 2 | 4 | Shared views reduce enduring duplicate presentation work |
| Functional parity | 10 | 4 | 4 | 4 | 3 | Product core reusable; external/native limits remain |
| Visual parity | 8 | 4 | 5 | 3 | 4 | Shared renderer/tokens strongest consistency |
| Player feasibility | 15 | 3 | 3 | 3 | 2 | Hardest seam; XPF still doesn't replace Windows native graphics |
| Windows regression/migration risk | 10 | 5 | 2 | 5 | 2 | Mature WPF view migration is real cost |
| Mac-native quality | 7 | 4 | 4 | 5 | 3 | Native shell best; shared UI requires deliberate integration |
| Initial effort | 8 | 4 | 2 | 3 | 2 | B adds Windows view migration after Mac shell |
| Ongoing maintenance | 9 | 2 | 5 | 1 | 3 | Two different UI stacks endure duplicated behavior/QA |
| Licensing/cost | 7 | 5 | 5 | 5 | 2 | Free core options; XPF commercial dependence conflicts with preference |
| Ecosystem health | 4 | 4 | 4 | 4 | 4 | Active ecosystems, no guarantee of future support |
| Testability | 4 | 3 | 4 | 3 | 3 | Shared UI headless suite helps but native QA remains |
| Performance | 3 | 4 | 4 | 5 | 3 | Estimates only; native primitives flexible, fixtures decide |
| **Total /5** | **100** | **3.65** | **3.88** | **3.35** | **2.87** | B wins narrowly at baseline |

Weights intentionally emphasize development efficiency and Player correctness while giving meaningful protection to current Windows acceptance. Native visual conventions are required but do not outrank source-relative frame correctness. Performance scores are priors, not empirical throughput.

Sensitivity with the same scores:

| Scenario | Weights in row order above | A | B | C | D | Implication |
|---|---|---:|---:|---:|---:|---|
| Fastest Mac / least Windows disruption | 8,8,5,15,15,5,25,4,7,3,3,2 | 3.88 | 3.29 | 3.53 | 2.54 | A becomes preferred; use explicit fallback |
| Lowest long-term maintenance | 22,10,10,13,5,5,3,16,7,3,4,2 | 3.41 | 4.26 | 2.97 | 3.07 | B wins clearly |

A one-point deterioration in B's Player score lowers its baseline to 3.73; another point reduces it to 3.58 and A wins. A one-point decrease in B's development efficiency lowers it to 3.73. Hence the narrow baseline advantage does not justify a flag-day rewrite. First qualify Player and one meaningful Browser/Settings vertical slice, then approve the Windows migration budget. Scores cannot override a failed required capability or unacceptable mandatory license.
