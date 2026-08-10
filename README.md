# Lightflow Studio — Native Windows

**Video processing and workflow tools by Jeremy Running Photography.**

Current version: **0.15.0**

Lightflow Studio is a native C#/.NET 8 WPF desktop application for preparing, processing, inspecting, and recovering video media.

## Features

- Folder batch encoding with a collapsed per-file picker, allowing individual videos to be skipped before a batch begins
- Flexible batch destinations: source folders, named subfolders, or a specific output folder
- Asynchronous file details with resolution, frame rate, duration, size, codec/audio tooltips, and warning badges for media outliers
- Configurable `.cube` LUT library with a friendly selection dropdown
- Dedicated Settings tab for default folders, FFmpeg, batch preferences, and advanced encoding controls
- Built-in encoding readiness check for FFmpeg, FFprobe, and NVIDIA H.264/HEVC support
- Self-contained Windows installer and portable package with a verified, pinned FFmpeg build
- Named encoding presets with recommended-default restoration and custom overrides
- NVIDIA NVENC H.264/HEVC with quality, bitrate, tuning, multipass, AQ, 8/10-bit, frame-rate, deinterlace, audio, and container controls
- Branded dark-studio interface with card-based workflows and a multi-size Windows application icon
- 480p, 720p, 1080p, 1440p, 4K UHD, or source-resolution output
- High-quality NVIDIA NVENC H.264 (`p7`, full-resolution multipass, adaptive quantization)
- Normal, salvage, and video-only recovery modes
- Per-file progress, overall progress, and estimated time remaining
- Collapsible Activity Log that stays out of the way until needed
- Remembers the most recently selected LUT and records batch lifecycle summaries
- Protects active encodes with finish-current, close-now, and keep-running close options
- Optional recursive processing and resume/skip support
- FFprobe metadata inspection
- Full decode verification with a CSV report
- Lossless MP4 rewrapping, 1080p editing proxies, and contact sheets
- Experimental Premiere Pro V1 timeline clip exporter
- Accepts a folder path on the command line for future Explorer integration

## Requirements

- Windows 10/11, 64-bit
- NVIDIA GPU and current NVIDIA driver for NVENC encoding
- .NET 8 SDK only when building from source

The installer and portable release include FFmpeg and FFprobe. Users do not need to install .NET, the .NET SDK, or FFmpeg separately.

For development builds, the app searches for FFmpeg in this order:

1. A location selected in **Settings**
2. `ffmpeg\bin\ffmpeg.exe` beside the published application
3. Windows `PATH`

## Build from source

Install the .NET 8 SDK:

```powershell
winget install Microsoft.DotNet.SDK.8
```

Open PowerShell in this folder, then run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\build.ps1
```

Run the unit test suite with:

```powershell
dotnet test .\LightflowStudio.Tests\LightflowStudio.Tests.csproj
```

## Versioning releases

Lightflow Studio follows semantic versioning: patch releases contain fixes and polish, minor releases add backward-compatible features, and major releases mark breaking changes or major stable milestones.

Change the product version from the repository root before preparing a release:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\set-version.ps1 -Version 0.4.0
```

This updates the authoritative build version and every source-controlled place where the current version is displayed. Automated tests fail if those values drift apart.

For the complete Windows installer, portable ZIP, and SHA-256 checksums, install [Inno Setup 6](https://jrsoftware.org/isdl.php) and run:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1
```

Release artifacts are placed in `dist`. The build script downloads the exact FFmpeg package pinned in `dependencies\ffmpeg.json`, verifies its SHA-256, and includes its license, source, and build records in both distributions.

Pull requests and pushes to `main` run the complete test suite before the packaging job can begin. A semantic version tag such as `vX.Y.Z` publishes the validated installer, portable ZIP, and checksums to a GitHub release.

## Encoding presets and advanced options

Lightflow Studio ships with four named starting points:

- **Recommended:** H.264 NVENC, P7, constant quality 18, full-resolution multipass, spatial/temporal AQ, and source audio copy.
- **Maximum Quality:** 10-bit HEVC, constant quality 16, full-resolution multipass, and high-bitrate AAC.
- **Fast Preview:** H.264 P4, constant quality 25, quarter-resolution multipass, and lightweight AAC.
- **Efficient HEVC:** HEVC P6, constant quality 21, full-resolution multipass, and AAC.

Settings can customize the codec, container, NVENC preset, tuning, rate-control mode, quality or bitrates, multipass, adaptive quantization, pixel format, frame rate, deinterlacing, audio encoding, sample rate, channels, and fast-start behavior. Invalid combinations are rejected before settings are saved or encoding begins.

The internal settings model reserves CPU, AMD AMF, and Intel Quick Sync backends for future releases. Only NVIDIA NVENC is enabled in this version.

## FFmpeg setup for source builds

Install through Windows Package Manager:

```powershell
winget install Gyan.FFmpeg
```

Open a new PowerShell window and verify:

```powershell
ffmpeg -version
ffmpeg -hide_banner -encoders | Select-String "h264_nvenc|hevc_nvenc"
```

Alternatively, place `ffmpeg.exe` and `ffprobe.exe` under:

```text
LightflowStudio.exe
ffmpeg\
  bin\
    ffmpeg.exe
    ffprobe.exe
```

## Recovery modes

- **Normal:** retains all audio streams with stream copy.
- **Salvage audio + video:** discards corrupt packets where possible, rebuilds timestamps, uses the first optional audio stream, and re-encodes it to AAC with async resampling.
- **Video only:** processes the primary video stream and produces no audio.

FFmpeg cannot reconstruct absent media data. Salvage output may contain frozen, duplicated, skipped, silent, or visibly corrupted sections.

## Premiere helper

See `PremiereHelper\README.txt`. Adobe has changed Premiere scripting support over time, so treat this helper as experimental and test it on a duplicate project. An Adobe Media Encoder `.epr` preset is required.

## Notes

- Batch output can remain beside each source with a customizable filename suffix, go into a named subfolder, or use a specific destination. Source-relative folders are preserved.
- Active encodes are written beside their destination as `filename.ext.lightflow`. The normal media filename is created or replaced only after FFmpeg succeeds and Lightflow validates the completed partial output. Interrupted artifacts remain visibly incomplete and are safely cleaned when that exact output is retried.
- The LUT dropdown defaults to `J:\Photography\LUTs`. Choose another LUT folder in Settings, or use **Refresh** after adding LUT files to the current folder.
- Application defaults are saved in `settings.json`, while the most recently used batch choices are remembered separately in `state.json` under `%LOCALAPPDATA%\Jeremy Running Photography\Lightflow Studio`.
- An always-on activity log is written to `activity.log` in the same folder — every batch and Tools-tab FFmpeg/FFprobe invocation, its full output, application errors, and unhandled exceptions are recorded there regardless of the in-app Activity Log panel or the **Show encoding details** setting, which only control what is mirrored live in the UI. The log rotates at 5 MB and keeps 3 prior files (`activity.log.1`–`.3`).
- 4K output is 3840×2160 with aspect-preserving scale and letterbox/pillarbox padding when required.
- Contact sheets sample one frame every ten seconds and use the first 16 samples.
- Release packages include the exact verified LGPL FFmpeg build documented in `dependencies\ffmpeg.json` and `THIRD-PARTY-NOTICES.md`.
