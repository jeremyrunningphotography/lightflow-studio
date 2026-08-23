param(
    [Parameter(Mandatory)]
    [string]$PackageDirectory
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory)

if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) {
    throw "Staged package directory does not exist: $packageRoot"
}

$requiredFiles = @(
    "LightflowStudio.exe",
    "LightflowStudio.ico",
    "THIRD-PARTY-NOTICES.md",
    "flyleaf-package.json",
    "PremiereHelper\Export-V1-Clips.jsx",
    "PremiereHelper\README.txt",
    "ffmpeg\bin\ffmpeg.exe",
    "ffmpeg\bin\ffprobe.exe",
    "ffmpeg\ffmpeg-package.json",
    "ffmpeg\SOURCE-AND-LICENSE.txt",
    "playback\ffmpeg\ffmpeg-playback-package.json",
    "playback\ffmpeg\SOURCE-AND-LICENSE.txt"
)
foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $packageRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Staged package is missing required file: $relativePath"
    }
}

$thirdPartyNotices = Get-Content -LiteralPath (Join-Path $packageRoot "THIRD-PARTY-NOTICES.md") -Raw
$requiredNoticeMarkers = @("FlyleafLib 3.11.2-lightflow.1", "6789799a5b29dfd126e1094e847f46cfa9b9be0a", "Microsoft.Data.Sqlite 8.0.29", "SQLitePCLRaw 2.1.6", "sqlite.org/copyright")
foreach ($marker in $requiredNoticeMarkers) {
    if ($thirdPartyNotices.IndexOf($marker, [StringComparison]::Ordinal) -lt 0) {
        throw "Staged third-party notices are missing the Catalog database dependency: $marker"
    }
}

$requiredPlaybackLibraries = @("avcodec-*.dll", "avformat-*.dll", "avutil-*.dll", "swresample-*.dll", "swscale-*.dll")
foreach ($pattern in $requiredPlaybackLibraries) {
    $matches = Get-ChildItem -LiteralPath (Join-Path $packageRoot "playback\ffmpeg\bin") -Filter $pattern -File
    if ($matches.Count -eq 0) { throw "Staged package is missing required playback library: $pattern" }
}

$licenseDirectories = @("ffmpeg\licenses", "playback\ffmpeg\licenses")
foreach ($relativePath in $licenseDirectories) {
    $path = Join-Path $packageRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Container) -or
        (Get-ChildItem -LiteralPath $path -File).Count -eq 0) {
        throw "Staged package is missing dependency license files: $relativePath"
    }
}

$manifestPairs = @(
    @{ Source = "dependencies\flyleaf.json"; Packaged = "flyleaf-package.json" },
    @{ Source = "dependencies\ffmpeg.json"; Packaged = "ffmpeg\ffmpeg-package.json" },
    @{ Source = "dependencies\ffmpeg-playback.json"; Packaged = "playback\ffmpeg\ffmpeg-playback-package.json" }
)
foreach ($pair in $manifestPairs) {
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot $pair.Source) -Algorithm SHA256).Hash
    $packagedHash = (Get-FileHash -LiteralPath (Join-Path $packageRoot $pair.Packaged) -Algorithm SHA256).Hash
    if ($sourceHash -ne $packagedHash) {
        throw "Packaged dependency manifest does not match its pinned source: $($pair.Packaged)"
    }
}

Write-Host "Staged package contents validated at: $packageRoot" -ForegroundColor Green
