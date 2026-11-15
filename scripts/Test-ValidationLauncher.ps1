param(
    [ValidateSet('Harness', 'Payload', 'Nested', 'Invalid')]
    [string]$Case = 'Harness',
    [int]$ChildExitCode = 0
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if ($Case -ne 'Harness') {
    if ($Case -eq 'Invalid') { $env:LIGHTFLOW_VALIDATION_EXPECTED_DESKTOP = 'LightflowValidation-invalid' }
    . (Join-Path $PSScriptRoot 'NoninteractiveValidation.ps1')
    if (-not [ValidationDesktop]::IsNoninteractive) {
        Invoke-NoninteractiveValidation -ScriptPath $PSCommandPath -Parameters $PSBoundParameters -TimeoutSeconds 20
        return
    }
    [pscustomobject]@{
        ProcessId = $PID
        Session = [Diagnostics.Process]::GetCurrentProcess().SessionId
        ExpectedDesktop = $env:LIGHTFLOW_VALIDATION_EXPECTED_DESKTOP
        ActualDesktop = [ValidationDesktop]::CurrentDesktopName
        IsNoninteractive = [ValidationDesktop]::IsNoninteractive
    } | ConvertTo-Json -Compress | Write-Host
    if ($Case -eq 'Nested') { Invoke-NoninteractiveValidation -ScriptPath $PSCommandPath -TimeoutSeconds 20 }
    exit $ChildExitCode
}

# This test harness runs on the caller's ordinary desktop. Payloads alone enter
# private desktops. No WPF/native application or test executes on the input desktop.
if ($env:LIGHTFLOW_VALIDATION_EXPECTED_DESKTOP) { throw 'Run this harness outside a validation wrapper.' }
. (Join-Path $PSScriptRoot 'NoninteractiveValidation.ps1')
if ([ValidationDesktop]::IsNoninteractive) { throw 'Run this harness from the ordinary developer shell.' }
$run = Join-Path $root ('artifacts\launcher-tests\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$exe = Join-Path $PSHOME $(if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' })
$validationDirectory = Join-Path $root 'artifacts\validation'
$results = @()
foreach ($scenario in @(
    @{ Name = 'normal'; Case = 'Payload'; Code = 0; ExpectedCode = 0; Wrappers = 1; Message = 'no owned processes remain' },
    @{ Name = 'exit23'; Case = 'Payload'; Code = 23; ExpectedCode = 23; Wrappers = 1; Message = 'exit 23' },
    @{ Name = 'nested'; Case = 'Nested'; Code = 0; ExpectedCode = 1; Wrappers = 1; Message = 'Nested private-desktop launchers are prohibited' },
    @{ Name = 'invalid'; Case = 'Invalid'; Code = 0; ExpectedCode = 1; Wrappers = 0; Message = 'desktop mismatch' }
)) {
    $before = @(Get-ChildItem -LiteralPath $validationDirectory -Directory -ErrorAction SilentlyContinue).Count
    $log = Join-Path $run ($scenario.Name + '.log')
    # Windows PowerShell turns redirected native stderr into ErrorRecords. Preserve
    # it and decide using both the actual exit code and the expected diagnostics.
    $ErrorActionPreference = 'Continue'
    $output = & $exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $PSCommandPath -Case $scenario.Case -ChildExitCode $scenario.Code 2>&1
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    $text = $output | Out-String
    $text | Set-Content -LiteralPath $log
    $created = @(Get-ChildItem -LiteralPath $validationDirectory -Directory -ErrorAction SilentlyContinue).Count - $before
    if ($code -ne $scenario.ExpectedCode -or $created -ne $scenario.Wrappers -or -not $text.Contains($scenario.Message)) {
        throw "Launcher regression '$($scenario.Name)' failed: exit=$code wrappers=$created. Log: $log"
    }
    $results += [pscustomobject]@{ Case = $scenario.Name; ExitCode = $code; Wrappers = $created; Result = 'PASS' }
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'results.json')
$results | Format-Table | Out-String | Write-Host
Write-Host "Launcher regression evidence: $run"
