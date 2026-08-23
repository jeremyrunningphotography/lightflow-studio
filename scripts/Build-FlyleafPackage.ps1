param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\dependencies\nuget"),
    [switch]$SkipChecksumValidation
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$manifestPath = Join-Path $repositoryRoot "dependencies\flyleaf.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$workingRoot = Join-Path ([IO.Path]::GetTempPath()) ("lightflow-flyleaf-" + [Guid]::NewGuid().ToString("N"))

function Normalize-ZipArchive([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $normalized = "$Path.normalized"
    $source = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $target = [IO.Compression.ZipFile]::Open($normalized, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($entry in ($source.Entries | Sort-Object FullName)) {
                $entryName = $entry.FullName
                if ($entryName.StartsWith("package/services/metadata/core-properties/")) {
                    $entryName = "package/services/metadata/core-properties/core.psmdcp"
                }
                $copy = $target.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
                $copy.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                if ($entry.FullName.EndsWith('/')) { continue }
                $output = $copy.Open()
                try {
                    if ($entry.FullName -eq "_rels/.rels") {
                        $relationshipXml = '<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/FlyleafLib.nuspec" Id="manifest" /><Relationship Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="/package/services/metadata/core-properties/core.psmdcp" Id="metadata" /></Relationships>'
                        $bytes = [Text.Encoding]::UTF8.GetBytes($relationshipXml)
                        $output.Write($bytes, 0, $bytes.Length)
                    }
                    else {
                        $input = $entry.Open()
                        try { $input.CopyTo($output) }
                        finally { $input.Dispose() }
                    }
                }
                finally { $output.Dispose() }
            }
        }
        finally { $target.Dispose() }
    }
    finally { $source.Dispose() }
    Move-Item -LiteralPath $normalized -Destination $Path -Force
}

try {
    git clone --no-checkout $manifest.sourceRepository $workingRoot
    if ($LASTEXITCODE -ne 0) { throw "Flyleaf source clone failed." }
    git -C $workingRoot checkout --detach $manifest.sourceCommit
    if ($LASTEXITCODE -ne 0) { throw "Flyleaf source commit checkout failed." }
    if ((git -C $workingRoot rev-parse HEAD) -ne $manifest.sourceCommit) {
        throw "Flyleaf source checkout did not resolve to the pinned commit."
    }

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    dotnet restore (Join-Path $workingRoot "FlyleafLib\FlyleafLib.csproj") -p:TargetFramework=net8.0-windows
    if ($LASTEXITCODE -ne 0) { throw "Flyleaf package restore failed." }
    dotnet pack (Join-Path $workingRoot "FlyleafLib\FlyleafLib.csproj") -c Release --no-restore `
        -p:TargetFrameworks=net8.0-windows -p:RepositoryCommit=$($manifest.sourceCommit) `
        -p:ContinuousIntegrationBuild=true "-p:PathMap=$workingRoot=/_/Flyleaf" -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw "Flyleaf package build failed." }

    $packagePath = Join-Path $OutputDirectory $manifest.packageFile
    Normalize-ZipArchive $packagePath
    $actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not $SkipChecksumValidation -and $actualHash -ne $manifest.packageSha256) {
        throw "Flyleaf package checksum mismatch. Expected $($manifest.packageSha256), received $actualHash."
    }
    Write-Host "Rebuilt Flyleaf package: $packagePath ($actualHash)" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $workingRoot) {
        Remove-Item -LiteralPath $workingRoot -Recurse -Force
    }
}
