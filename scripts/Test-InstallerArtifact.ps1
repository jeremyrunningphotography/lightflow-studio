[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $InstallerPath,
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $InstallerPath).Path
$file = Get-Item -LiteralPath $resolved
if ($file.Length -lt 1MB) { throw "Installer is unexpectedly small: $resolved" }

$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($resolved)
$productName = $versionInfo.ProductName.Trim()
$companyName = $versionInfo.CompanyName.Trim()
$productVersion = $versionInfo.ProductVersion.Trim()
$description = $versionInfo.FileDescription.Trim()
if ($productName -ne 'Lightflow Studio') {
    throw "Installer ProductName is '$($versionInfo.ProductName)', expected 'Lightflow Studio'."
}
if ($companyName -ne 'Jeremy Running Photography') {
    throw "Installer CompanyName is '$($versionInfo.CompanyName)', expected 'Jeremy Running Photography'."
}
if (-not $productVersion.StartsWith($Version, [StringComparison]::Ordinal)) {
    throw "Installer ProductVersion is '$($versionInfo.ProductVersion)', expected $Version."
}
if ($description -ne 'Lightflow Studio installer') {
    throw "Installer description is '$($versionInfo.FileDescription)'."
}

Write-Host "Generated installer metadata and branding validated: $resolved" -ForegroundColor Green
