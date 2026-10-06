param([ValidateSet('Seed','Return','VerifyNasFixture')][string]$Phase = 'Seed')
$ErrorActionPreference = 'Stop'
$proofRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $proofRoot
try {
    $baseline = 'aed6c2906637c8a5a9d71c1c18ac24bed48a8724'
    $productionDiff = & git diff $baseline -- LightflowStudio Lightflow.Actions LightflowStudio.Tests scripts
    if ($LASTEXITCODE -ne 0 -or $productionDiff) { throw 'Production source differs from the qualified baseline.' }
    foreach ($pair in @(@('DOTNET_CLI_HOME','cli'),@('NUGET_PACKAGES','nuget'),@('NUGET_HTTP_CACHE_PATH','nuget-http'),@('TMP','tmp'),@('TEMP','tmp'),@('DOTNET_BUNDLE_EXTRACT_BASE_DIR','native-extract'))) {
        $path = Join-Path $proofRoot ('work/' + $pair[1]); New-Item -ItemType Directory -Force $path | Out-Null
        [Environment]::SetEnvironmentVariable($pair[0],$path,'Process')
    }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'; $env:X2_WORKSPACE = $proofRoot
    New-Item -ItemType Directory -Force (Join-Path $proofRoot 'work/evidence') | Out-Null
    & dotnet --info | Out-File (Join-Path $proofRoot 'work/evidence/windows-dotnet-info.txt')
    & git rev-parse HEAD | Out-File (Join-Path $proofRoot 'work/evidence/windows-harness-head.txt')
    & dotnet restore './tools/X2CatalogProof/X2CatalogProof.csproj' --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    & dotnet build './tools/X2CatalogProof/X2CatalogProof.csproj' -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Harness build failed.' }
    $dll = Join-Path $proofRoot 'tools/X2CatalogProof/bin/Release/net8.0/X2CatalogProof.dll'
    if ($Phase -eq 'Seed') {
        $data = Join-Path $proofRoot 'work/x2-windows-seed-data'
        $output = Join-Path $proofRoot 'work/x2-windows-origin'
        if (Test-Path $data) { throw 'Seed data already exists; preserve it and choose a new reviewed run directory.' }
        & dotnet $dll seed $data $output
        if ($LASTEXITCODE -ne 0) { throw 'Windows origin seed failed.' }
        Compress-Archive -LiteralPath (Join-Path $output 'catalog.db'),(Join-Path $output 'snapshot.json'),(Join-Path $output 'manifest.json'),(Join-Path $output 'X2.cube') -DestinationPath (Join-Path $proofRoot 'work/x2-windows-origin.zip')
        Get-FileHash (Join-Path $proofRoot 'work/x2-windows-origin.zip') -Algorithm SHA256
    } elseif ($Phase -eq 'Return') {
        $data = Join-Path $proofRoot 'work/x2-windows-return-data'
        if (Test-Path $data) { throw 'Return data already exists; preserve it.' }
        & dotnet $dll windows-return $data (Join-Path $proofRoot 'work/x2-mac-return') (Join-Path $proofRoot 'work/x2-windows-verified')
        if ($LASTEXITCODE -ne 0) { throw 'Windows return verification failed.' }
    } else {
        $data = Join-Path $proofRoot 'work/x2-windows-nas-data'
        if (Test-Path $data) { throw 'NAS-fixture Windows data already exists; preserve it.' }
        & dotnet $dll windows-nas-edit $data (Join-Path $proofRoot 'work/x2-nas-origin') (Join-Path $proofRoot 'work/x2-windows-nas-return')
        if ($LASTEXITCODE -ne 0) { throw 'Windows same-fixture verification failed.' }
        Compress-Archive -Path (Join-Path $proofRoot 'work/x2-windows-nas-return/*') -DestinationPath (Join-Path $proofRoot 'work/x2-windows-nas-return.zip')
        Get-FileHash (Join-Path $proofRoot 'work/x2-windows-nas-return.zip') -Algorithm SHA256
    }
} finally { Pop-Location }
