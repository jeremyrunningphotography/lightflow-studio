# Approved runtime identity (#241)

These files are unchanged copies from the authoritative
[lightflow-identity-assets-complete.zip](https://github.com/user-attachments/files/32069971/lightflow-identity-assets-complete.zip)
attached to #241. Do not regenerate, crop, recolor, or restyle them.

- `lightflow-header-lockup-480x96.png`: MainWindow's decorative 240 × 48 DIP header; Uniform downscaling inside the existing 60 DIP chrome.
- `lightflow-splash-1280x720.png`: unchanged source for the lightweight startup window. Jeremy's hands-on refinement trims empty canvas using source viewport (300,110,680,480), displayed at 440 DIPs wide with proportional height and Uniform scaling. The full mark, glow, wordmark and tagline remain visible.
- `LightflowStudio.ico`: project ApplicationIcon and WPF Window.Icon; Build-Release copies this same file into the package. Inno Setup uses that copy for SetupIconFile; shortcuts and uninstall identity use the application executable. No extra PNG icon frames are needed by this toolchain.

The superseded JR runtime logo and icon were retired. Existing installer wizard illustrations and publisher/About text remain outside this pass, as directed by the approved asset guidance. The ZIP retains all masters and unused source sizes.
