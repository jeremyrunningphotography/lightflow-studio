# Avalonia UI proof — partial Windows checkpoint

Date: 2026-10-05. Baseline: `e302a20d888161d6face6e311610d67eab9c1512`.
G3 disposition: **pending / revise prototype**, not pass or an overall framework failure.

Environment: Windows 10.0.26200 reporting Windows platform; x64; Intel Core i9-14900KF
(registry); SDK 8.0.423; runtime 8.0.29; Avalonia 12.1.3; SkiaSharp 3.119.4.
RAM query was denied by the sandbox and is not reported. No Mac hardware, macOS,
Retina or native rendering evidence exists. No numeric owner acceptance budget exists.

## Exercised slice

The separate `net8.0` executable contains one shared Browser/Inspector view and one
shortcut experiment. The Windows/macOS distinction is currently runtime modifier
selection; framework native menus are configured. Neither view forks nor a Mac native
service implementation has been proved. Production extraction is outside this spike.

Grid: virtualized ListBox of fixed-height row bands; 168-DIP tiles; synthetic Preview
decode on visual attachment; disposal on detachment. Details: core TableView, 18
columns to exercise full catalog load, five meaningful synthetic fields, remaining
fields explicitly duplicate type values for load only. It is not a production metadata
projection. Native Details sorting/reordering/persistence are not implemented.

A small expanded Locations tree and a fixed Inspector exercise composition/density,
not large/deep lazy tree virtualization. Search filters the synthetic resident array.
Context menus expose a non-destructive illustrative item; no commands mutate media.
Selected IDs transfer between grid/table and a focused search editor retains Left input.
Headless selection transfer tests cover a seven-item range, not the full accepted
desktop selection/current-item/reordering contract.

## Measurements and limits

See raw JSON/CSV for exact values. One process, 10k first (cold startup), then 100k
(warm framework/JIT). 80 evenly spaced scroll jumps per mode across the full list.
Each sample drains dispatcher/layout and a software render tick. It is not frame
presentation latency, native continuous-scroll responsiveness or a confidence interval.
Capture and selection probes add allocations; reported allocation totals are cumulative
since window creation, so Details totals include preceding Grid work.

At 100k: maximum 5 grid-band containers (25 visible tiles) and 11 Details rows.
6 distinct outer grid containers and 12 distinct Details containers served 400 and
880 observations respectively. Container reuse occurred and counts stayed bounded.
However 412 grid template builds and >2,000 thumbnail loads show **outer container
reuse is not sufficient**. The FuncDataTemplate rebuilds its contents. The naïve
prototype needs stable recycled tile views, cancellation/generations and a bounded
image cache before a performance pass can be considered.

The recorded 100k run has approximately 29 ms Grid and 33 ms Details p95 software
scroll/layout ticks; about 220 MB allocation after Grid and 611 MB cumulative after
Details; about 47 MB live managed memory. These are preliminary measurements, not
accepted tolerances. Every window's thumbnail attachment/disposal counts balanced
(3,091/3,091 in the preserved run). No long-duration leak test has been performed.

Suggested owner-review targets, not requirements: 60-Hz native continuous-scroll p95
frame time at or below 16.7 ms on agreed hardware; realized containers at most two
viewports; an explicit Preview cache cap (candidate 256 MiB); after warmup, no upward
resource trend across 30-minute scroll/filter/resize cycles. Agree corpus, display,
measurement method and budget before acceptance. Headless jump measurements cannot
be compared directly to native frame time.

## Settings, styling and input

The experiment has four collapsed categories, 80 synthetic command rows, inline capture
feedback and fixed disabled Save/Restore buttons. It does **not** implement the accepted
General/Color/Storage/Advanced hierarchy, actual 77-variant catalog, search/expansion
retention, conflict/reservation engine, Use Shortcut confirmation, key-up/repeat latch,
row-local scroll restoration, deactivation focus policy or persistence. Immediate
candidate staging in this toy surface is not accepted Lightflow shortcut semantics.
These omissions block a Settings/shortcut pass; reuse existing neutral contracts later.

Near-black canvas, panel/selection/orange resources and tile dimensions are translated
from the shipping design tokens. Captures show plausible density, but residual Fluent
control chrome, focus accents, narrow Expander header sizing and typography remain.
This is not visual acceptance. Automation names on tiles demonstrate a mechanism;
large custom grid selection peers, screen-reader navigation and VoiceOver remain unproved.

## Native media composition

[Pinned upstream API source](https://github.com/AvaloniaUI/Avalonia/tree/12.1.3/src/Avalonia.Controls/NativeControlHost.cs)
and [official native-interop documentation](https://docs.avaloniaui.net/docs/app-development/native-interop)
document a native host route. A native NSView sits above the framework drawing surface;
ordinary Avalonia overlays cannot simply be assumed to cover it. Complex clips and
framework transforms are constrained. This is a **documented constraint**, not a runtime
failure discovered here. G3 cannot declare composition unblocked without M1's actual
surface and a real Mac fixture.

Conceptual candidates for X1 coordination: native view with native-owned overlays;
or a backend texture/frame lease rendered through Avalonia's custom Skia/compositor
path. Neither is selected or implemented here. Texture import/synchronization,
zero-copy feasibility, scale, color, retained-frame and lifetime must be demonstrated;
a CPU pixel copy is not evidence of GPU texture interoperability.

## Required next evidence

1. Actual arm64 Mac environment/isolation and native window/menu lifecycle, Command
   presentation, Retina resizing, VoiceOver, IME/local focus, native context menus/drop.
2. Stable recycled grid content, bounded thumbnail cache/async work; real varied images
   and realistic slow storage; continuous scrolling, long-duration resource trends.
3. Large/deep lazy tree and stable selection/current-item/scroll anchors across refresh,
   filtering/sorting, layout switches and resize. Current regrouping follows current item,
   not an independently saved top-visible scroll anchor; filtered keyboard navigation
   also still indexes the full synthetic array. Do not reuse as production semantics.
4. Table sorting, column visibility/reorder/persistence and all selection/focus cases.
5. Accepted Settings and shortcut behavior via actual neutral catalog/resolver.
6. Full image corpus/WIC-vs-shared-vs-ImageIO parity and native display color evidence.
7. M1-compatible host/texture fixture: rectangular clipping, overlay z-order/hit testing,
   Retina resize, pointer ownership and same-lease fullscreen handoff.
8. Owner-agreed limits, credible custom-control budget and combined G1/G2/G3 review.

No production migration, M4+, other spike, TourBox, Resolve or release work was started.
No production WPF/native validation or release packaging is claimed. No PR is ready.
