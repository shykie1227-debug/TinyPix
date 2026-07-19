[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Root
)

$ErrorActionPreference = 'Stop'
$rootPath = [System.IO.Path]::GetFullPath($Root)

$requiredFiles = @(
    'TinyPix.exe',
    'portable.flag',
    'Config/settings.json',
    'THIRD_PARTY_NOTICES.txt',
    'OFFLINE_SECURITY.md',
    'dependencies.json',
    'sbom.cdx.json',
    'build-manifest.json'
)

$requiredDirectories = @(
    'Runtime',
    'Engines',
    'Templates',
    'Models',
    'Licenses',
    'Config',
    'Data',
    'Cache',
    'Logs'
)

$missing = [System.Collections.Generic.List[string]]::new()
foreach ($relativePath in $requiredFiles) {
    if (-not [System.IO.File]::Exists([System.IO.Path]::Combine($rootPath, $relativePath))) {
        $missing.Add($relativePath)
    }
}

foreach ($relativePath in $requiredDirectories) {
    if (-not [System.IO.Directory]::Exists([System.IO.Path]::Combine($rootPath, $relativePath))) {
        $missing.Add("$relativePath/")
    }
}

if ($missing.Count -gt 0) {
    throw "Portable layout is incomplete: $($missing -join ', ')"
}

$manifest = Get-Content -Raw -LiteralPath ([System.IO.Path]::Combine($rootPath, 'build-manifest.json')) | ConvertFrom-Json
if ($manifest.product -ne 'TinyPix 4.0' -or $manifest.runtimeIdentifier -ne 'win-x64') {
    throw 'build-manifest.json does not describe the TinyPix 4.0 win-x64 release.'
}

Write-Host "Portable layout verified: $rootPath"
