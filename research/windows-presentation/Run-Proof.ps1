param([switch]$InsideBoundary,[string]$RunName='native-001')
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if(-not $InsideBoundary) {
    . (Join-Path $taskRoot 'scripts\NoninteractiveValidation.ps1')
    Invoke-NoninteractiveValidation -ScriptPath $PSCommandPath -Parameters @{InsideBoundary=$true;RunName=$RunName} -TimeoutSeconds 180
    exit 0
}
$exe=Join-Path $PSScriptRoot 'Harness\bin\Release\net8.0-windows\Harness.exe'
$data=Join-Path $taskRoot ('artifacts\windows-presentation\'+$RunName)
New-Item -ItemType Directory -Path $data -Force | Out-Null
$sources=Get-ChildItem (Join-Path $PSScriptRoot 'Harness') -File | Where-Object Extension -in '.cs','.csproj','.json' | ForEach-Object { @{name=$_.Name;sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash} }
@{agent='LF-WIN-RES-002';sourceCommit=(git -C $taskRoot rev-parse HEAD);sources=$sources;binarySha256=(Get-FileHash $exe -Algorithm SHA256).Hash;assemblySha256=(Get-FileHash ([IO.Path]::ChangeExtension($exe,'.dll')) -Algorithm SHA256).Hash;command='Harness.exe --data-root '+$data} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $data 'provenance.json')
& $exe --data-root $data
if($LASTEXITCODE -ne 0) { throw "Proof failed ($LASTEXITCODE): $data" }
