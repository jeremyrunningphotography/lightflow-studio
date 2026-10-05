# Process-level boundary: desktop selection must precede CLR/STA/WPF initialization.
if (-not ('ValidationDesktop' -as [type])) { Add-Type -Path @((Join-Path $PSScriptRoot 'ValidationDesktop.cs'), (Join-Path $PSScriptRoot '..\LightflowStudio\ValidationPresentation.cs')) }
function Invoke-NoninteractiveValidation {
    param([Parameter(Mandatory)][string]$ScriptPath, [hashtable]$Parameters = @{}, [int]$TimeoutSeconds = 7200)
    $root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $run = Join-Path $root ('artifacts\validation\' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    $request = Join-Path $run 'request.xml'
    $log = Join-Path $run 'output.log'
    $temporaryDirectory = Join-Path $root ('.cache\validation-temp\' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null
    @{ Script = [IO.Path]::GetFullPath($ScriptPath); Parameters = $Parameters; TemporaryDirectory = $temporaryDirectory } | Export-Clixml -LiteralPath $request
    # Paths are data in the request. EncodedCommand receives no interpolated script/parameter text.
    $quotedRequest = $request.Replace("'", "''")
    $quotedLog = $log.Replace("'", "''")
    $body = "`$ErrorActionPreference = 'Stop'; try { `$r = Import-Clixml -LiteralPath '$quotedRequest'; `$env:TEMP = `$r.TemporaryDirectory; `$env:TMP = `$r.TemporaryDirectory; `$p = `$r.Parameters; & `$r.Script @p *> '$quotedLog'; if (`$LASTEXITCODE) { exit `$LASTEXITCODE }; exit 0 } catch { `$_ | Out-String | Add-Content -LiteralPath '$quotedLog'; exit 1 }"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($body))
    $exe = (Get-Command powershell.exe).Source
    $desktop = New-Object ValidationDesktop
    Write-Host "Noninteractive validation desktop: $($desktop.Name); log: $log"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        $desktop.Start($exe, "`"$exe`" -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encoded", $root)
        while (-not $desktop.HasExited) {
            if ($timer.Elapsed.TotalSeconds -gt $TimeoutSeconds) { throw "Noninteractive validation timed out; owned process tree will be terminated. Log: $log" }
            Start-Sleep -Milliseconds 250
        }
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log | Write-Host }
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        while ($desktop.ActiveProcesses -ne 0 -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if ($desktop.ActiveProcesses -ne 0) { throw "Validation left $($desktop.ActiveProcesses) owned processes running; cleanup will terminate them. Log: $log" }
        if ($desktop.ExitCode -ne 0) { throw "Noninteractive validation failed (exit $($desktop.ExitCode)). Log: $log" }
        Write-Host 'Noninteractive validation completed; no owned processes remain.'
    } finally {
        $desktop.Dispose()
        Remove-Item -LiteralPath $request -Force
    }
}

