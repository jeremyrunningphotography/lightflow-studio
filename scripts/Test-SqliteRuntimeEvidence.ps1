param(
    [Parameter(Mandatory)][string]$ReportPath,
    [Parameter(Mandatory)][string]$LockPath,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [string]$ExtractionRoot = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
if ([version]$report.Version -lt [version]'3.50.2') { throw 'Packaged SQLite is below the security boundary 3.50.2.' }
if ($report.Version -ne '3.53.3' -or $report.CatalogSchema -ne 19 -or -not $report.PreviewRuntimeMatches) {
    throw 'Unexpected SQLite candidate or Catalog/Preview qualification result.'
}
$lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
$familyNames = @('SQLitePCLRaw.bundle_e_sqlite3', 'SQLitePCLRaw.core', 'SQLitePCLRaw.lib.e_sqlite3', 'SQLitePCLRaw.provider.e_sqlite3')
$verifiedManagedGraph = $false
foreach ($framework in $lock.dependencies.PSObject.Properties) {
    $family = @($framework.Value.PSObject.Properties | Where-Object Name -Like 'SQLitePCLRaw.*')
    # Some SDKs include the SQLite-free Actions framework in the publish lock.
    if ($family.Count -eq 0 -and -not $framework.Value.'Microsoft.Data.Sqlite' -and -not $framework.Value.'Microsoft.Data.Sqlite.Core') { continue }
    # RID sections are asset deltas, not a second complete managed graph.
    if ($framework.Name -notlike '*/*' -and $family.Count -ne 4) { throw "Unexpected SQLite graph in $($framework.Name)." }
    if ($framework.Name -notlike '*/*') { $verifiedManagedGraph = $true }
    foreach ($component in $family) {
        if ($component.Name -notin $familyNames -or $component.Value.resolved -ne '2.1.13') { throw 'Stale or unexpected SQLitePCLRaw component.' }
    }
    foreach ($name in @('Microsoft.Data.Sqlite', 'Microsoft.Data.Sqlite.Core')) {
        if ($framework.Name -like '*/*' -and -not $framework.Value.$name) { continue }
        if ($framework.Value.$name.resolved -ne '8.0.29') { throw 'Unexpected managed SQLite provider version.' }
    }
}
if (-not $verifiedManagedGraph) { throw 'Publish lock has no complete managed SQLite graph.' }
$asset = Join-Path $repositoryRoot '.cache\nuget\packages\sqlitepclraw.lib.e_sqlite3\2.1.13\runtimes\win-x64\native\e_sqlite3.dll'
$assetHash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash
if ($report.NativeSha256 -ne $assetHash -or
    (Get-FileHash -LiteralPath $report.NativePath -Algorithm SHA256).Hash -ne $assetHash) {
    throw 'Loaded SQLite native payload differs from the selected NuGet win-x64 asset.'
}
$bytes = [IO.File]::ReadAllBytes($report.NativePath)
$peOffset = [BitConverter]::ToInt32($bytes, 60)
if ([BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x8664) { throw 'SQLite native payload is not Windows x64.' }
$nativeDirectory = Split-Path -Parent $report.NativePath
if ([string]::IsNullOrWhiteSpace($ExtractionRoot) -or
    -not ([IO.Path]::GetFullPath($report.NativePath)).StartsWith([IO.Path]::GetFullPath($ExtractionRoot).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'SQLite was not loaded from the task-owned executable extraction root.'
}
if (@(Get-ChildItem -LiteralPath $nativeDirectory -Recurse -File | Where-Object Name -Match '^(e_sqlite3|sqlite3|winsqlite3)\.dll$').Count -ne 1) {
    throw 'Duplicate or unexpected extracted SQLite payload.'
}
if (@(Get-ChildItem -LiteralPath $PackageDirectory -Recurse -File | Where-Object Name -Match '^(e_sqlite3|sqlite3|winsqlite3)\.dll$').Count -ne 0) {
    throw 'Unexpected loose SQLite DLL beside the single-file executable.'
}
if ([string]::IsNullOrWhiteSpace($report.SourceId)) { throw 'Missing SQLite native source identity.' }
Write-Host "SQLite $($report.Version); source $($report.SourceId); SHA-256 $assetHash; coherent 2.1.13 win-x64 payload verified." -ForegroundColor Green
