# Shared-view decision

Proposed direction: one shared Avalonia layout/style/view-model/action implementation, including Browser Grid/Details, Player controls and overlays, Settings/shortcuts, Collections, Jobs and Export presentation. New proof requires **zero whole-view Windows/Mac forks**. The last three named product surfaces are an architectural projection, not implemented/tested by this spike.

Keep selection, catalog identity, Color intent and semantic actions platform-neutral. The retained Details control is shared owned code, not a Mac-specific replacement view. Its cost is substantial but bounded: 20–40 person-days, then initially 1–3 days/month maintenance; these are unapproved research estimates. Reserve 8–15 days only for a simpler TableView adaptation if upstream measurements later justify it.

Mac adapters: controlled FFmpeg/Metal/CoreAudio backend, IOSurface + MetalSharedEvent leases, ImageIO format gaps, display-profile tagging/notifications, native menus/modifier translation, Finder/Trash/filesystem/lifecycle and signing/notarization. Windows: corresponding GPU presentation/decoder/display services, Explorer/Recycle Bin/filesystem/lifecycle and packaging. Native bridges own handles and fences; shared models own immutable identity and semantic acceptance. Avoid exposing NSView/CAMetalLayer into shared layout.

The proof reflects into Avalonia's raw-input machinery only to inject local test events. Product code should use public routed input and semantic actions. Native Metal layer discovery here is a feasibility probe: the production adapter must bind to the actual presentation surface lifecycle, tag it deterministically, and preserve tagging on replacement. That does not require a fork of a whole view.

Two-platform development should add one shared feature/action/view plus contract tests, then validate narrow adapter behavior and packaged interaction on both systems. New features should not spawn independent Windows and Mac UI implementations.
