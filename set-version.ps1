[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot

$propsPath = Join-Path $repoRoot 'Directory.Build.props'
[xml] $props = Get-Content -Raw -LiteralPath $propsPath
$props.Project.PropertyGroup.VersionPrefix = $Version
$props.Save($propsPath)

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Update-VersionText {
    param([string] $Path, [string] $Pattern, [string] $Replacement)

    $content = [IO.File]::ReadAllText($Path)
    if (-not [regex]::IsMatch($content, $Pattern)) { throw "Version marker was not found in $Path" }
    $updated = [regex]::Replace($content, $Pattern, $Replacement)
    [IO.File]::WriteAllText($Path, $updated, $utf8NoBom)
}

Update-VersionText -Path (Join-Path $repoRoot 'README.md') -Pattern 'Current version: \*\*\d+\.\d+\.\d+\*\*' -Replacement "Current version: **$Version**"

Write-Host "Lightflow Studio version updated to $Version."
