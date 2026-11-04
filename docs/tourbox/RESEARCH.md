# Supported Console capability evidence

Public official documentation checked 2026-10-04. These findings describe supported configuration
features; they do not claim that the owner's device or this proposed preset has passed hands-on tests.
All capability sources below are TourBox's own documentation. No third-party generator, undocumented
schema or reverse-engineered preset/transport is used.

| Question | Established official evidence | Boundary / action for #354 |
| --- | --- | --- |
| Current desktop family | [Windows downloads](https://tourboxtech.com/en/downloads/windows/) lists stable 5.11.3 (2026-06-12) and 5.12.0 Beta 10.2 (2026-09-30), Windows 10+. [macOS downloads](https://tourboxtech.com/en/downloads/macos/) lists stable 5.11.3, macOS 11+ for that current release. | Recommend stable; record the actual installed/tested version. Older hardware minimums are not the same as current Console minimums. |
| Elite Plus desktop capabilities | [Elite versus Elite Plus](https://tourboxtech.com/en/news/elite-vs-elite-plus.html) documents the same desktop preset, combination, TourMenu and macro tools, USB/Bluetooth on Windows/macOS, and adjustable vibration strength. | Applies to the requested Elite Plus. Firmware/transport/feel remain unmeasured. |
| Real shareable preset | [Desktop import/export](https://www.tourboxtech.com/en/news/import-and-export-presets-in-tourbox.html) supports sharing/backup, Preset List import, selected-preset replacement and export; names `.tb`. | Use a blank owner-authored Console export. No public external-generation schema was established; treat its contents as vendor-owned/opaque to this workflow. Never fabricate it. |
| Mobile format | [iPad import/export](https://www.tourboxtech.com/en/news/export-import-presets-tourbox-ipad.html) names `.tbx`. | Distinct evidence for iPad, not proof that a desktop export should be renamed or cross-imported. Retain Console's actual extension. |
| Blank authoring | [Create presets](https://www.tourboxtech.com/en/news/how-to-create-presets-in-tourbox.html) documents creating a blank preset, customizing it and accessing shared presets. | No Photoshop/Resolve template or copied copyrighted artwork is needed. |
| Foreground application association | [Link/unlink](https://www.tourboxtech.com/en/news/link-and-unlink-application-in-tourbox.html): open the app, enable Auto Switch, choose Not Linked and select it in Application List; relink after updates. [Switching](https://www.tourboxtech.com/en/news/how-to-switch-tourbox-presets.html) documents auto-switch and manual favorite-preset switching. | Link the running LightflowStudio.exe. The docs do not establish the internal path/process matching key. Relink on each installation/import and test foreground transitions. No executable-detection code in Lightflow. |
| Buttons and combinations | [Guide](https://www.tourboxtech.com/en/news/guide-in-tourbox.html) names Side/Top/Tall/Short, Tour, C1/C2, D-pad and Knob/Scroll/Dial, including press actions. [Custom actions](https://www.tourboxtech.com/en/news/how-to-create-custom-actions.html) supports button+button, button+rotary and double-click actions. | Use one Side modifier; no double-clicks. The preset map labels Console controls, not a copied product diagram. |
| Directional rotary events | [Rotating controls](https://www.tourboxtech.com/en/news/how-to-use-the-rotating-section-controls.html) supports separate shortcuts for both directions, rotary presses and speed settings. Knob/Scroll have Prime Four combinations; Dial combinations use Custom Section. | Discrete keyboard operations, not analog semantic transport. Configure Slow first; verify directions and bursts. No one-detent/one-action claim. |
| Modifiers | [Assign keyboard shortcuts](https://tourboxtech.com/oaf/manual/how-to-customize-buttons/) documents entering single/combined shortcuts in the custom panel. | Ctrl/Shift/Alt chords are supported configuration. Exact output, top-row digits and non-US layout behavior still need physical verification. Ctrl+Alt/AltGr is excluded by Lightflow. |
| Standard / UP / REP | [Mode tutorial](https://www.tourboxtech.com/en/news/up-mode-and-rep-mode.html): Standard triggers a command on press; UP on release; REP repeatedly sends commands while held. | These are command trigger modes, not a documented arbitrary-letter key lifetime contract. All proposed buttons use Standard; REP is off for assignment/toggle/creation/Export. |
| AB press/release | [AB mode](https://tourboxtech.com/en/news/tourbox-ab-mode.html) sends shortcut A on press and shortcut B on release; macros/TourMenu/built-ins are excluded. [5.8.0 notes](https://www.tourboxtech.com/en/news/tourbox-console-pc-v5-8-0-update.html) specifically mention long-press fixes for modifiers and Space in A mode. | A press/release **command pair** is not proof of holding C down. #353 exposes one Begin/End keyboard session, not two independently bound commands. Do not substitute AB/toggle behavior. C2 remains a physical probe. |
| Macros and key repeat | [Macro manual](https://tourboxtech.com/oap/manual/macro-commands/) documents shortcut repetition, delay, mouse/text/open operations and grouped commands. | Available, but unnecessary here. Repeated separate key presses can bypass OS-repeat suppression and must never drive toggles, creation or Export. |
| Layers / sub-presets | [5.11.0 release notes](https://www.tourboxtech.com/en/news/tourbox-console-5-11-0-for-pc.html) show automatic Library/Develop sub-presets for Lightroom. Manual preset switching is documented separately above. | Vendor-supported application integrations have sub-presets. Generic automatic Lightflow Browser/Player sub-preset recognition is not established. Use Lightflow semantic contexts and held Side combinations instead. |
| Haptics | [Elite Plus product](https://www.tourboxtech.com/ca/tourbox-elite-plus/) documents two tactile levels; Elite comparison above documents strength configuration in Console. | Start at the lower available level. Whether these preferences are per-preset/global or survive export/import must be recorded from the owner's Console. No Lightflow-triggered haptics or feedback channel is claimed. |
| Cross-platform files | Both desktop download pages and the Elite comparison establish Windows/macOS software support. | Exact file-format portability, modifier translation and application linkage for this custom Lightflow preset remain unverified. Re-author/relink/revalidate on a future native macOS Lightflow build. |

## Artifact decision

Choose **B + C**, eventually A via an authentic export: document/validate the mapping now; the owner
authors it in Console and exports it; only a successful import round trip, provenance check and owner
hardware acceptance justify committing/distributing the real file. No supported public route to generate
this file outside Console was established. The manual specification remains useful independently.

## Observed versus tested

- Repository source: verified merged main `a827470ab0338790c5697d9fcfaebe9231a70670`.
- Host inspected: Microsoft Windows 11 Pro, version 10.0.26200 / build 26200.
- Console installation/version: not verified; no matching entry was found in the standard uninstall
  registry or Program Files checks. This does not rule out another installation location.
- Elite Plus firmware, transport and actual Console behavior: no physical tests performed.
- macOS/iPad/Android behavior: not tested; mobile workflows are outside this preset.
- Static mapping validation is recorded separately in [#354 validation](../validation/tourbox-preset-354.md).

The remaining owner gate concerns Console setup/export and physical acceptance. It is not permission
to investigate a native protocol or to change the accepted Color behavior. #346 retains the deeper
vendor/hardware decision scope.
