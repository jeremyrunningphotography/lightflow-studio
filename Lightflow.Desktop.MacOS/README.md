# Lightflow Studio — bounded Mac application shell

LF-MAC-DEV-024 / #451. Build the self-contained Apple Silicon development app:

```sh
scripts/Build-MacShell.sh
```

Requires a .NET 8 SDK, Xcode command-line tools and Python 3 at build time. Set
`DOTNET_COMMAND` to an absolute SDK `dotnet` path when it is not on PATH.
The built app requires explicit `--data-root <absolute isolated task-owned folder>`;
it never defaults to normal user storage. Prepare an empty Catalog folder inside
that root, then select it from Create New Catalog. Existing Catalogs must be
inside the same root and pass the accepted native local APFS lifecycle.

[Qualification, ownership, exact local build/launch commands and acceptance scenarios](../docs/qualification/mac-shell-451.md).
No Move/publication, Preview, media browsing/playback, signing or release claim.
