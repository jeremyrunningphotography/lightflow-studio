# G1b dependency and licensing boundary

[Exact source/configuration/library/input/binary hashes and linkage](DEPENDENCY_PROVENANCE.json) are authoritative for this run. Native probes dynamically link task-local FFmpeg **n9.0.1**, source `bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa`, unmodified. Runtime/configuration reports **LGPL 2.1 or later**; GPL, NONFREE, VERSION3, GPLV3 and LGPLV3 flags are zero.

Configuration disables everything/autodetection/static/programs/docs, then enables shared avcodec/avformat/avutil/avfilter/swscale/swresample, H264/HEVC/AAC/PCM-s16le decoders, H264/HEVC/AAC parsers, MOV/Matroska demuxers, file protocol, H264/HEVC VideoToolbox accelerators, VideoToolbox and pthreads. G1b adds only the LGPL filter graph needed for `atempo`, `abuffer`, `abuffersink`, `aformat`, `aresample`. The exact configure command, including task prefix, is recorded in JSON and `build_ffmpeg.py`.

Native frameworks are system AppKit, Metal, QuartzCore, IOSurface, CoreVideo and AudioToolbox. No paid/commercial codec/runtime or external GPL-only library is linked. Existing system FFmpeg 9.0.1/libx264 is a fixture generator only; it is not redistributed. Avalonia 11.3.8/MIT is inspected source only, with no new framework runtime or production project.

[FFmpeg's official compliance guidance](https://ffmpeg.org/legal.html) requires attention to dynamic linking, exact corresponding source/build changes, license notices and application/download disclosures, and EULA terms permitting the relevant reverse engineering. Those shipping obligations, replacement/relink capability, translated notices and codec patent considerations remain distribution qualification work. The minimal LGPL build does not by itself establish redistribution compliance. This PR ships research/source/evidence, not native libraries or a signed/notarized Mac product.

The inspected Avalonia Metal shared-event path requires **macOS 12.0**. Record that floor for this particular seam; final supported macOS selection and deployment testing remain owner decisions. It does not constrain every possible fallback path to the same API floor.
