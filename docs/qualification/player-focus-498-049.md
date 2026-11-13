# LF-WIN-RES-049 — keyboard focus correction

Issue #498; parent #491; target release/1.0. Owner authorizes focus only.
Baseline ca929e586b929d721656a5ca5c5c5fb8fff5f905 includes accepted #496/#501.
Existing independent workspace/branch and research evidence remain preserved.

## Implementation and review boundary

Player speed-popup closure defers a local focus handoff until input completes.
It runs only while the same source generation is loaded/visible/enabled, the
speed popup is closed, its ComboBox or owned popup item still owns focus, and its window
is active. Another input control/window wins. Programmatic source restoration
cannot schedule a handoff. Open speed popup retains native keyboard processing.
Escape restores the opening speed through the existing selection handler before
the ComboBox performs native closure. Source reset closes and clears that local
popup transaction; no old selection is applied to replacement media.
Neutral Player chrome can take navigation focus; text, selection, slider, button,
menu and Subclips input owners are excluded. The WPF media surface is focusable
so its existing pointer handler can release stale dropdown ownership; the native
video window retains its existing input path. No global routing or visual style
change. No decoder, playback calculation, queue, range, export or dependency edit.

## Validation / handoff gate

Focused routed-focus/native-step tests and relevant existing Player/Browser,
Subclips/Jobs, speed and frame-step regressions precede full Windows Release,
fresh local package, installer and portable gates. Exact counts, source/tree,
package hashes and original failures are recorded in the I01 evidence ZIP.
Programmatically raised WPF key/pointer events are not physical input acceptance.
No PR before owner hands-on acceptance; no merge/release/main forward-port.

## Eight-point owner acceptance checklist

1. Select each speed (1/8, 1/4, 1/2, 1, 2, 4); Left/Right frame navigation works.
2. After using speed control, click video preview; Player arrows work immediately.
3. Click neutral Player background; navigation works without another focus click.
4. Previous/Next Frame buttons work at every speed with the existing step size.
5. Open speed dropdown; arrows navigate its options, without stepping the video.
6. Enter confirms; Escape cancels/closes without leaving Player or changing speed.
7. Text, sliders, other dropdowns, menus/dialogs retain their own keyboard behavior.
8. Browser, Subclips, Jobs and existing Player/marker/asset navigation are unchanged.

## Deferred follow-up proposal (not created)

Search of open repository issues found #498 and #345 for backward-frame terms;
#345 is controller architecture and is not authority to implement decoder work.
Historical #135/#344 cover earlier backend/arrow contracts but are not a current
standalone cross-platform scrubbing performance specification. Recommend a new
owner-approved issue titled **Player: frame-accurate sequential scrubbing across
Windows and macOS**. Proposed scope: actual decoded predecessor/frame identity,
sequential intermediate presentation and prompt direction changes; measure rate,
backlog/overload, VFR/duplicate-PTS/zero boundaries and visible-PTS mark semantics
using existing platform backends. Preserve the research evidence, including
~171 ms backward median at1080p60,~4.66 s backlog and~2.10 s simplified prototype.
No architecture or implementation authorized: large caches, new schedulers,
final-target coalescing, decoder replacement and broad redesign remain deferred.
Do not create the issue without owner authorization. These defects are not fixed
by #498's focus correction. Surface-teardown latency also remains unresolved.

Historical maintenance: Not warranted — bounded focus bug, no major milestone.

Automated focus tests wait for native popup opening/closure and explicitly seed
keyboard-highlighted item focus; programmatic opening differs from physical input.
Native menu capture could not be established reliably on the private desktop;
the attempted fixture failures remain in evidence. Menu mouse/keyboard interaction
requires owner acceptance. No passing automated menu-capture result is claimed.

## Owner acceptance and final qualification continuation

Owner accepted all eight I01 checks above **8/8 PASS**, no corrections, at
`b1592ddf560dc6c1ff34e68f0413a8079a221362` (production subtree
`ad4edd7f59776d27543d7e03adffc177b8c6db33`). This supersedes the historical
pending owner/menu acceptance wording above; synthetic tests remain synthetic.
The owner conditionally authorizes final qualification, one implementation PR,
and normal merge only after green required exact-head CI and preserved source.
No further approval is needed when all conditions hold; stop on any red required
gate, meaningful regression or need to change accepted production.

Original release baseline: `ca929e586b929d721656a5ca5c5c5fb8fff5f905`.
Current release verified at `293cd4b9ec43c0a7207d6cb271b08131c5d85f98`:
#501 and #502 normally merged; #502 post-merge CI43511787157 succeeded.
Reconciled source `c2fb1fcb6a08937577c4e5ba58f9b756a0148d79`, production subtree
`687cf634924c1616ac804264e6fbc434a77cb54b`. Only registry conflict resolved;
all four accepted focus files byte-identical, existing merged Export files unchanged.

Mandatory deferred-code audit: diff ca929e5..b1592dd contains only new
PlayerViewerHost.Focus.cs and focus-only changes in Review.cs, host XAML and
TryHandleShortcut. Decoder seeking/reconstruction/native reuse/caches, step queues,
frame presentation, VFR predecessor/duplicate-PTS/zero and teardown code unchanged.
No experimental optimization remains. The quicker 1080p owner observation is not
a measured optimization result; 4K latency/backlog and research findings stay deferred.
No new follow-up issue created. Prior I01 ZIP SHA256:
`c4acb25bad456a518c791d0f29540cb445b7ce64a5f8c4bd68f9f666d7f4936b`.
Historical focused309 PASS / full2578 PASS,2 baseline Browser failures,7 skips and
package PASS remain preserved. Final fresh qualification is pending; no claim of
clean full suite, PR, merge, release or main forward-port yet.

## Final qualification — stopped at required red gate

Test source: e241cc02f237ec095ed4930bfd7b1a147287c325, production subtree
687cf634924c1616ac804264e6fbc434a77cb54b. Final focused358 PASS /0 FAIL /0 SKIP;
local companion90 PASS. Complete unfiltered Windows Release2589 PASS /2 FAIL /8
existing opt-in SKIP. Failures:

- FlyleafPlaybackIntegrationTests.StartupRestore_NativeVideoSurvivesTaskbarReveal(seconds:0).
- FlyleafPlaybackIntegrationTests.StartupRestore_NativeVideoSurvivesTaskbarReveal(seconds:1).

Both throw InvalidOperationException: No presented video frame is available, at
CapturePresentedFrameAsync after native taskbar reveal. Both also passed in the
358-case focused run. Existing backend/service and startup test are unchanged from
current release293cd4b; the host has only accepted focus differences. A bounded,
isolated checkout/build of unchanged293cd4b passed the same two cases2/2; the
accepted branch was restored. This does not reproduce or prove a baseline failure.
Full-context cause remains unclassified; no production/test change or test weakening.

The required full-suite gate remains failed. Per owner stop gate: no final package
rebuild, PR, exact-head hosted CI, merge or branch retirement. Historical I01 package
and source evidence remain preserved, with installer/portable moved within the same
task workspace before rebuild could replace dist. Native validation cleanup confirmed;
slot handed directly to051. Local registry records the block. No administrative PR,
new scrubbing issue, release/tag, main forward-port or production Catalog operation.
