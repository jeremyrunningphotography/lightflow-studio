# Installer branding assets

`LightflowWizard.png` and `LightflowWizardSmall.png` are deterministic installer renditions of
`LightflowStudio/Assets/Branding/jr-glow-source.png`, which was supplied by Jeremy Running Photography.

- `LightflowWizard.png`: 480 x 918, matching Inno Setup's 240:459 wizard-image aspect ratio at 2x.
- `LightflowWizardSmall.png`: 294 x 294 for crisp high-DPI header presentation.

The images retain the supplied JR mark without restyling. Inno Setup scales them for the active DPI;
its native forced-dark style falls back to standard high-contrast behavior when Windows requires it.
