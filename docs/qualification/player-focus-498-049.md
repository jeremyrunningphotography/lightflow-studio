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
