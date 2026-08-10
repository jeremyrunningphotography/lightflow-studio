param(
    [string]$Version,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\dist"),
    [ValidateSet("Release", "PullRequest")]
    [string]$Mode = "Release",
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
$totalTimer = [Diagnostics.Stopwatch]::StartNew()

function Write-StageTiming([string]$Name, [Diagnostics.Stopwatch]$Timer) {
    Write-Host ("TIMING {0}: {1:n1}s" -f $Name, $Timer.Elapsed.TotalSeconds) -ForegroundColor DarkCyan
}

if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
if (Test-Path -LiteralPath $OutputDirectory) { Remove-Item -LiteralPath $OutputDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $appDirectory, $OutputDirectory -Force | Out-Null

Write-Host "Publishing Lightflow Studio $Version..." -ForegroundColor Cyan
$stageTimer = [Diagnostics.Stopwatch]::StartNew()
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -o $appDirectory
if ($LASTEXITCODE -ne 0) { throw "Application publish failed." }

Copy-Item -LiteralPath (Join-Path $repositoryRoot "PremiereHelper") -Destination (Join-Path $appDirectory "PremiereHelper") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.md") -Destination $appDirectory -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot "LightflowStudio\Assets\Branding\LightflowStudio.ico") -Destination $appDirectory -Force
& (Join-Path $PSScriptRoot "Get-Ffmpeg.ps1") -Destination $ffmpegDirectory
& (Join-Path $PSScriptRoot "Get-PlaybackDependencies.ps1") -Destination $playbackDirectory
& (Join-Path $PSScriptRoot "Test-PackageContents.ps1") -PackageDirectory $appDirectory
Write-StageTiming "publish, dependency preparation, and staging validation" $stageTimer

if ($Mode -eq "Release") {
    $archiveTimer = [Diagnostics.Stopwatch]::StartNew()
    $portableZip = Join-Path $OutputDirectory "LightflowStudio-$Version-win-x64-portable.zip"
    Compress-Archive -Path (Join-Path $appDirectory "*") -DestinationPath $portableZip -CompressionLevel Optimal
    Write-StageTiming "release portable archive" $archiveTimer
} else {
    Write-Host "PR validation mode: staged package validated; release portable archive skipped." -ForegroundColor Yellow
}

if (-not $SkipInstaller) {
    $installerTimer = [Diagnostics.Stopwatch]::StartNew()
    $compilerCandidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($compiler)) {
        throw "Inno Setup 6 was not found. Install it or use -SkipInstaller for a portable-only build."
    }

    $compilerArguments = @(
        "/DMyAppVersion=$Version",
        "/DSourceDir=$appDirectory",
        "/DOutputDir=$OutputDirectory"
    )
    if ($Mode -eq "PullRequest") { $compilerArguments += "/DValidationBuild=1" }
    $compilerArguments += (Join-Path $repositoryRoot "installer\LightflowStudio.iss")
    & $compiler @compilerArguments
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }
    Write-StageTiming "installer ($Mode mode)" $installerTimer
}

$checksumFiles = Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" }
$checksumLines = foreach ($file in $checksumFiles) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}
[IO.File]::WriteAllLines((Join-Path $OutputDirectory "SHA256SUMS.txt"), $checksumLines)

Write-Host "Release artifacts created at: $OutputDirectory" -ForegroundColor Green
Write-StageTiming "total packaging" $totalTimer
Get-ChildItem -LiteralPath $OutputDirectory -File | Select-Object Name, Length
