# Third-Party Notices

Lightflow Studio includes FFmpeg and FFprobe executables produced by the
[BtbN FFmpeg Builds project](https://github.com/BtbN/FFmpeg-Builds).

The pinned package is an LGPL build and remains the property of its respective
copyright holders. FFmpeg is licensed under the GNU Lesser General Public
License, version 2.1 or later. Lightflow Studio invokes these programs as
separate command-line processes.

The distributed application includes:

- The original license and build documentation found in the verified binary package.
- A machine-readable package manifest containing the exact binary URL and SHA-256.
- A `SOURCE-AND-LICENSE.txt` record linking to the corresponding FFmpeg source
  revision and the build scripts/configuration used to produce the package.

FFmpeg project and source: <https://ffmpeg.org/>

This notice is informational and is not legal advice. The third-party license
files included with the application control the use and redistribution of
those components.

## Playback components

Interactive video playback uses FlyleafLib 3.10.4 and
Flyleaf.FFmpeg.Bindings 8.1.0. Both packages are licensed under the GNU Lesser
General Public License, version 3.0 or later. Lightflow Studio keeps these
libraries behind its own playback API and does not modify their source.

- Flyleaf source: <https://github.com/SuRGeoNix/Flyleaf/tree/v3.10.4>
- Flyleaf FFmpeg bindings source: <https://github.com/SuRGeoNix/Flyleaf.FFmpeg.Generator>

Flyleaf uses dynamically loaded FFmpeg shared libraries. Lightflow distributes
the pinned BtbN `lgpl-shared` build recorded in
`playback/ffmpeg/ffmpeg-playback-package.json`. The package is built from the
same FFmpeg source revision recorded there, and its exact archive checksum,
corresponding FFmpeg source, and BtbN build scripts are documented in
`playback/ffmpeg/SOURCE-AND-LICENSE.txt`. GPL and nonfree BtbN variants are not
used.

Flyleaf also depends on the Vortice.Windows and SharpGen.Runtime projects,
distributed under the MIT License:

- Vortice.Windows: <https://github.com/amerkoleci/Vortice.Windows>
- SharpGenTools: <https://github.com/SharpGenTools/SharpGenTools>

NuGet package license files and the source repositories above are authoritative.

## Catalog database components

The durable Lightflow Catalog uses Microsoft.Data.Sqlite 8.0.29, distributed
under the MIT License:

- Microsoft.Data.Sqlite source and license: <https://github.com/dotnet/efcore>

Microsoft.Data.Sqlite uses SQLitePCLRaw 2.1.6, also distributed under the MIT
License, to load the bundled native `e_sqlite3` library:

- SQLitePCLRaw source and license: <https://github.com/ericsink/SQLitePCL.raw>

SQLite itself is in the public domain. The SQLite project and public-domain
dedication are available at <https://www.sqlite.org/copyright.html>.

The exact managed and native package graph, versions, and package hashes are
recorded in the NuGet `packages.lock.json` files. The native SQLite library is
embedded in Lightflow's self-contained single-file executable and extracted by
the .NET host at runtime.
