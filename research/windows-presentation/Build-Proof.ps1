$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
dotnet restore (Join-Path $PSScriptRoot 'Harness\Harness.csproj') --locked-mode
if($LASTEXITCODE) {throw 'Restore failed'}
dotnet publish (Join-Path $PSScriptRoot 'Harness\Harness.csproj') -c Release --no-restore --disable-build-servers -o (Join-Path $root 'artifacts\windows-presentation\package')
if($LASTEXITCODE) {throw 'Publish failed'}
