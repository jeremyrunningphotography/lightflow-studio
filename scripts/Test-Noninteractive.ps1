param(
    [string]$Filter,
    [switch]$NoBuild,
    [switch]$NoRestore,
    [string]$ResultsName = 'tests.trx'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NoninteractiveValidation.ps1')
if (-not [ValidationDesktop]::IsNoninteractive) {
    Invoke-NoninteractiveValidation -ScriptPath $PSCommandPath -Parameters $PSBoundParameters
    return
}
[ValidationDesktop]::RequireNoninteractive()
$testArguments = @('test', (Join-Path $PSScriptRoot '..\LightflowStudio.Tests\LightflowStudio.Tests.csproj'), '-c', 'Release', '--disable-build-servers', '--logger', "trx;LogFileName=$ResultsName", '--blame-hang-timeout', '5m', '--blame-hang-dump-type', 'mini')
if ($Filter) { $testArguments += @('--filter', $Filter) }
if ($NoBuild) { $testArguments += '--no-build' }
if ($NoRestore) { $testArguments += '--no-restore' }
# Windows PowerShell wraps native stderr as ErrorRecords when redirected by the
# outer launcher. Let VSTest finish and preserve all failures/TRX; decide by exit code.
$ErrorActionPreference = 'Continue'
& dotnet @testArguments
$testExitCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($testExitCode -ne 0) { throw "Release tests failed (exit $testExitCode)." }
