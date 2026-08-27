param(
    [string]$Version,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\dist"),
    [ValidateSet("Release", "PullRequest")]
    [string]$Mode = "Release",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
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
$publishLockFile = Join-Path $stagingRoot "publish.packages.lock.json"
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
    -p:NuGetLockFilePath=$publishLockFile `
    -p:DebugType=None -p:DebugSymbols=false -o $appDirectory
if ($LASTEXITCODE -ne 0) { throw "Application publish failed." }

$catalogRuntimeCheck = Start-Process -FilePath (Join-Path $appDirectory "LightflowStudio.exe") `
    -ArgumentList "--verify-catalog-runtime" -WorkingDirectory $appDirectory `
    -Wait -PassThru -WindowStyle Hidden
if ($catalogRuntimeCheck.ExitCode -ne 0) { throw "Packaged Catalog SQLite runtime verification failed." }

# Exercise the real packaged WPF startup through MainWindow.Loaded and delayed template rendering.
# The process must remain alive after Browser storage initialization; short-lived XAML/startup crashes fail packaging.
$startupSmoke = Start-Process -FilePath (Join-Path $appDirectory "LightflowStudio.exe") `
    -ArgumentList "--startup-smoke-test", "--jobs-workspace-smoke-test" -WorkingDirectory $appDirectory `
    -PassThru -WindowStyle Hidden
try {
    if ($startupSmoke.WaitForExit(8000)) {
        throw "Packaged application exited during the Browser startup smoke test (exit code $($startupSmoke.ExitCode))."
    }
    Write-Host "Packaged Browser startup and full Jobs workspace activation remained healthy after initialization." -ForegroundColor Green
    $null = $startupSmoke.CloseMainWindow()
    if (-not $startupSmoke.WaitForExit(5000)) { Stop-Process -InputObject $startupSmoke -Force }
}
finally {
    $startupSmoke.Refresh()
    if (-not $startupSmoke.HasExited) { Stop-Process -InputObject $startupSmoke -Force }
    if (-not $startupSmoke.WaitForExit(5000)) {
        throw "Packaged Browser startup smoke process did not terminate during cleanup."
    }
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot "PremiereHelper") -Destination (Join-Path $appDirectory "PremiereHelper") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.md") -Destination $appDirectory -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot "dependencies\flyleaf.json") -Destination (Join-Path $appDirectory "flyleaf-package.json") -Force
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
        "/DOutputDir=$OutputDirectory",
        "/DInstallerAssetsDir=$(Join-Path $repositoryRoot 'installer\Assets')"
    )
    if ($Mode -eq "PullRequest") { $compilerArguments += "/DValidationBuild=1" }
    $compilerArguments += (Join-Path $repositoryRoot "installer\LightflowStudio.iss")
    & $compiler @compilerArguments
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }
    $installerPath = Join-Path $OutputDirectory "LightflowStudio-$Version-win-x64-setup.exe"
    & (Join-Path $PSScriptRoot "Test-InstallerArtifact.ps1") -InstallerPath $installerPath -Version $Version
    Write-StageTiming "installer ($Mode mode)" $installerTimer
}

$checksumFiles = Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" }
$checksumLines = foreach ($file in $checksumFiles) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}
[IO.File]::WriteAllLines((Join-Path $OutputDirectory "SHA256SUMS.txt"), [string[]]@($checksumLines))

Write-Host "Release artifacts created at: $OutputDirectory" -ForegroundColor Green
Write-StageTiming "total packaging" $totalTimer
Get-ChildItem -LiteralPath $OutputDirectory -File | Select-Object Name, Length
