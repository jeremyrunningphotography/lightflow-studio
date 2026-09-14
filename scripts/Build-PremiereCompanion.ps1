param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\PremiereCompanion'))
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$manifest = Get-Content -LiteralPath (Join-Path $source 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.id -ne 'com.lightflowstudio.premiere' -or $manifest.host.minVersion -ne '26.5.0') {
    throw 'Unexpected companion identity or support floor.'
}
if ($manifest.requiredPermissions.localFileSystem -ne 'request' -or
    @($manifest.requiredPermissions.network.domains).Count -ne 1 -or
    $manifest.requiredPermissions.network.domains[0] -ne 'http://localhost') {
    throw 'Unexpected companion permissions.'
}
Add-Type -AssemblyName System.IO.Compression
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($OutputPath)) -Force | Out-Null
$stream = [IO.File]::Open($OutputPath, [IO.FileMode]::Create)
try {
    $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        # Same flat CCX structure verified with UDT; include only the production payload.
        foreach ($name in @('manifest.json', 'index.html', 'index.js', 'handoff.js', 'bins.js',
            'lightflow-icon-256x256.png', 'lightflow-header-lockup-480x96.png')) {
            $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::Parse('2026-01-01T00:00:00Z')
            $entryStream = $entry.Open()
            try {
                $payload = if ($name.EndsWith('.png')) {
                    Join-Path $PSScriptRoot "..\LightflowStudio\Assets\Branding\$name"
                } else { Join-Path $source $name }
                $bytes = [IO.File]::ReadAllBytes($payload)
                $entryStream.Write($bytes, 0, $bytes.Length)
            } finally { $entryStream.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
Write-Host "Packaged Lightflow Premiere companion $($manifest.version): $OutputPath"
