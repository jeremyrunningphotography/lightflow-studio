param(
    [string]$Destination = (Join-Path $PSScriptRoot "..\artifacts\playback\ffmpeg"),
    [string]$CacheDirectory = (Join-Path $PSScriptRoot "..\.cache\ffmpeg")
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$manifestPath = Join-Path $repositoryRoot "dependencies\ffmpeg-playback.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.variant -ne "lgpl-shared") { throw "Playback FFmpeg must use the lgpl-shared variant." }

$archivePath = Join-Path $CacheDirectory $manifest.archiveName
New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
if (-not (Test-Path -LiteralPath $archivePath)) {
    Write-Host "Downloading pinned playback FFmpeg $($manifest.version)..." -ForegroundColor Cyan
    Invoke-WebRequest -UseBasicParsing -Uri $manifest.downloadUrl -OutFile $archivePath
}

$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $manifest.sha256.ToLowerInvariant()) {
    throw "Playback FFmpeg checksum mismatch. Expected $($manifest.sha256), received $actualHash."
}

$extractRoot = Join-Path $CacheDirectory "playback-extracted-$($manifest.version)"
if (-not (Test-Path -LiteralPath $extractRoot)) {
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot -Force
}

$bin = Get-ChildItem -LiteralPath $extractRoot -Recurse -Directory |
    Where-Object { $_.Name -eq "bin" -and (Get-ChildItem -LiteralPath $_.FullName -Filter "avcodec-*.dll").Count -gt 0 } |
    Select-Object -First 1
if ($null -eq $bin) { throw "The verified playback archive did not contain FFmpeg shared libraries." }

if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $Destination "bin"), (Join-Path $Destination "licenses") -Force | Out-Null
Get-ChildItem -LiteralPath $bin.FullName -Filter "*.dll" -File |
    Copy-Item -Destination (Join-Path $Destination "bin") -Force

$ffmpegExe = Join-Path $bin.FullName "ffmpeg.exe"
if (Test-Path -LiteralPath $ffmpegExe) { Copy-Item -LiteralPath $ffmpegExe -Destination (Join-Path $Destination "bin\ffmpeg.exe") -Force }
$ffprobeExe = Join-Path $bin.FullName "ffprobe.exe"
if (Test-Path -LiteralPath $ffprobeExe) { Copy-Item -LiteralPath $ffprobeExe -Destination (Join-Path $Destination "bin\ffprobe.exe") -Force }

$licenseFiles = Get-ChildItem -LiteralPath $extractRoot -Recurse -File |
    Where-Object { $_.Name -match '^(LICENSE|COPYING|README)(\..+)?$' }
foreach ($file in $licenseFiles) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination "licenses\$($file.Name)") -Force
}

$record = @"
Playback FFmpeg distribution record
===================================
Version: $($manifest.version)
Variant: $($manifest.variant)
License: $($manifest.license)
Binary package: $($manifest.downloadUrl)
Verified SHA-256: $actualHash
Corresponding FFmpeg source: $($manifest.sourceUrl)
Build scripts and configuration: $($manifest.buildProjectUrl)

These dynamically loaded FFmpeg libraries are used only by Lightflow Studio's
Flyleaf playback backend. Encoding continues to invoke the separately packaged
FFmpeg command-line executable.
"@
[IO.File]::WriteAllText((Join-Path $Destination "SOURCE-AND-LICENSE.txt"), $record)
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $Destination "ffmpeg-playback-package.json") -Force

Write-Host "Verified playback dependencies prepared at: $Destination" -ForegroundColor Green
