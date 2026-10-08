param([Parameter(Mandatory)][string]$DataRoot)
$ErrorActionPreference='Stop'
$records=Get-Content (Join-Path $DataRoot 'records.json') -Raw|ConvertFrom-Json
$failed=@($records|Where-Object { $_.kind -eq 'assertion' -and -not $_.data.pass })
if($failed.Count) {throw 'Evidence contains failed assertions'}
$accepted=@{}
foreach($record in $records|Where-Object kind -eq UIAccepted) {
    $id=$record.data.id
    if($accepted.ContainsKey([string]$id.Token)) {throw 'Duplicate accepted token'}
    $accepted[[string]$id.Token]=$id|ConvertTo-Json -Compress
}
foreach($record in $records|Where-Object kind -eq capture) {
    $data=$record.data
    if($accepted[[string]$data.id.Token] -ne ($data.id|ConvertTo-Json -Compress)) {throw 'Capture full identity is not accepted'}
    $file=Join-Path $DataRoot ($data.name+'.bgra')
    if((Get-FileHash $file -Algorithm SHA256).Hash -ne $data.sha256) {throw 'Capture hash mismatch'}
    if((Get-Item $file).Length -ne ($data.Width*$data.Height*4)) {throw 'Capture size mismatch'}
}
$last=($records|Where-Object kind -eq teardown|Select-Object -Last 1).data
if($last.live -ne 0 -or $last.allocated -ne $last.destroyed) {throw 'Unbalanced teardown'}
if(@($records|Where-Object kind -eq cadence).Count -ne 2) {throw 'Missing cadence records'}
Write-Host "Verified $($accepted.Count) accepted full identities, capture hashes, cadence and balanced teardown."
