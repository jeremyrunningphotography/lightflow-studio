param(
    [string]$Version,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\dist"),
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$versionProps = [xml](Get-Content -LiteralPath (Join-Path $repositoryRoot "Directory.Build.props") -Raw)
$sourceVersion = [string]$versionProps.Project.PropertyGroup.VersionPrefix
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $sourceVersion }
if ($Version -ne $sourceVersion) {
    throw "Requested version $Version does not match Directory.Build.props version $sourceVersion."
}

$stagingRoot = Join-Path $repositoryRoot "artifacts\release"
$appDirectory = Join-Path $stagingRoot "LightflowStudio"
$ffmpegDirectory = Join-Path $appDirectory "ffmpeg"
$playbackDirectory = Join-Path $appDirectory "playback\ffmpeg"
$project = Join-Path $repositoryRoot "LightflowStudio\LightflowStudio.csproj"

if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
if (Test-Path -LiteralPath $OutputDirectory) { Remove-Item -LiteralPath $OutputDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $appDirectory, $OutputDirectory -Force | Out-Null

Write-Host "Publishing Lightflow Studio $Version..." -ForegroundColor Cyan
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -o $appDirectory
if ($LASTEXITCODE -ne 0) { throw "Application publish failed." }

Copy-Item -LiteralPath (Join-Path $repositoryRoot "PremiereHelper") -Destination (Join-Path $appDirectory "PremiereHelper") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.md") -Destination $appDirectory -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot "LightflowStudio\Assets\Branding\LightflowStudio.ico") -Destination $appDirectory -Force
& (Join-Path $PSScriptRoot "Get-Ffmpeg.ps1") -Destination $ffmpegDirectory
& (Join-Path $PSScriptRoot "Get-PlaybackDependencies.ps1") -Destination $playbackDirectory

$portableZip = Join-Path $OutputDirectory "LightflowStudio-$Version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $appDirectory "*") -DestinationPath $portableZip -CompressionLevel Optimal

if (-not $SkipInstaller) {
    $compilerCandidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($compiler)) {
        throw "Inno Setup 6 was not found. Install it or use -SkipInstaller for a portable-only build."
    }

    & $compiler "/DMyAppVersion=$Version" "/DSourceDir=$appDirectory" "/DOutputDir=$OutputDirectory" `
        (Join-Path $repositoryRoot "installer\LightflowStudio.iss")
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }
}

$checksumFiles = Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" }
$checksumLines = foreach ($file in $checksumFiles) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}
[IO.File]::WriteAllLines((Join-Path $OutputDirectory "SHA256SUMS.txt"), $checksumLines)

Write-Host "Release artifacts created at: $OutputDirectory" -ForegroundColor Green
Get-ChildItem -LiteralPath $OutputDirectory -File | Select-Object Name, Length
