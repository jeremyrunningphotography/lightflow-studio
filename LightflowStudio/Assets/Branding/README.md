# Approved runtime identity (#241)

The header and splash are unchanged copies from the authoritative
[lightflow-identity-assets-v2.zip](https://github.com/user-attachments/files/32071850/lightflow-identity-assets-v2.zip)
attached to #241. The Windows icon is an unchanged copy from the final
[lightflow-icon-v4.zip](https://github.com/user-attachments/files/32073302/lightflow-icon-v4.zip).
Do not regenerate, crop, recolor, restyle, or add padding to these assets.

- `lightflow-header-lockup-480x96.png`: MainWindow's decorative 240 × 48 DIP header; Uniform downscaling inside the existing 60 DIP chrome.
- `lightflow-splash-1280x720.png`: unchanged source for the lightweight startup window. Jeremy's hands-on refinement trims empty canvas using source viewport (300,110,680,480), displayed at 440 DIPs wide with proportional height and Uniform scaling. The full mark, glow, wordmark and tagline remain visible.
- `LightflowStudio.ico`: project ApplicationIcon; Build-Release copies this same file into the package. Inno Setup uses that copy for SetupIconFile; shortcuts and uninstall identity use the application executable. WPF cannot decode the supplied v4 ICO, so MainWindow/window/taskbar use the unchanged supplied `lightflow-icon-256x256.png` instead.

Revision 4 replaces only the prior Windows icon bytes at the same resource path. The v2 brighter STUDIO header, splash bytes, and all presentation behavior are unchanged. The v4 icon preserves the complete composition at approximately 92% canvas fill; the supplied 256-pixel PNG provides WPF decoder compatibility.

The superseded core JR icon/header treatment was retired. `jr-glow-source.png` remains solely for the existing About image; it is not a v2 identity asset. Existing installer wizard illustrations and publisher/About text remain outside this pass. The ZIP retains all masters and unused source sizes.
