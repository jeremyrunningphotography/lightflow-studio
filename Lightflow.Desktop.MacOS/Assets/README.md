# Approved Lightflow v4 macOS icon source

`lightflow-icon-master-1024.png` is copied byte-for-byte from the approved
[lightflow-icon-v4.zip](https://github.com/user-attachments/files/32073302/lightflow-icon-v4.zip)
referenced by the Windows Branding/README.md. Its ICO matches the repository Windows
ICO exactly (SHA256 `2a20e879577b4a21eacb9502d460b93fbcf1aa59bb4dba41965b4d7e442e2772`).

Archive SHA256: `de69c117cf2780a435c9fb80d4ce8c65ef738484756144bf250b042a9b17c3fb`.
Master SHA256: `e3ff3bdda774e394790a89f210b5b7d7024ccf0c169511f1222c59d55ec7de5a`.

Build-MacIcon.sh only downsamples and packages the complete unchanged composition
with Apple sips/iconutil at 16/32/128/256/512 points and 1x/2x (through1024pixels).
No crop, background removal, mask, padding, recoloring or replacement logo is applied.
The approved master is intentionally opaque; its dark square composition is retained,
not converted into a guessed transparent silhouette. Windows assets are untouched.
Build-MacShell.sh copies the .icns into Contents/Resources and declares CFBundleIconFile.
Finder/Dock/switcher visual acceptance and caching behavior require actual Stage C capture.
Do not reset system icon caches or modify normal user apps for task verification.
