[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Project,
    [Parameter(Mandatory = $true)]
    [string]$PortableRoot,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [Parameter(Mandatory = $true)]
    [string]$DependenciesPath
)

$ErrorActionPreference = 'Stop'
$projectPath = [System.IO.Path]::GetFullPath($Project)
$portablePath = [System.IO.Path]::GetFullPath($PortableRoot)
$outputFile = [System.IO.Path]::GetFullPath($OutputPath)
$dependenciesFile = [System.IO.Path]::GetFullPath($DependenciesPath)

$projectDirectory = [System.IO.Path]::GetDirectoryName($projectPath)
$assetsFile = Join-Path $projectDirectory 'obj/project.assets.json'
if (-not (Test-Path -LiteralPath $assetsFile)) {
    throw "NuGet restore output is missing: $assetsFile"
}
$assets = Get-Content -Raw -LiteralPath $assetsFile | ConvertFrom-Json

$componentsByKey = @{}
$packageInventory = @()
foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -ne 'package') {
        continue
    }
    $separator = $library.Name.LastIndexOf('/')
    if ($separator -le 0) {
        throw "Unexpected NuGet library key: $($library.Name)"
    }
    $name = $library.Name.Substring(0, $separator)
    $version = $library.Name.Substring($separator + 1)
    $key = "$name@$version"
    if (-not $componentsByKey.ContainsKey($key)) {
        $componentsByKey[$key] = [ordered]@{
            type = 'library'
            name = $name
            version = $version
            purl = "pkg:nuget/$([Uri]::EscapeDataString($name))@$([Uri]::EscapeDataString($version))"
        }
        $packageInventory += [ordered]@{ name = $name; version = $version }
    }
}

$dependencyInventory = [ordered]@{
    source = 'obj/project.assets.json'
    project = [System.IO.Path]::GetFileName($projectPath)
    packages = @($packageInventory | Sort-Object name, version)
}
$dependencyInventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $dependenciesFile -Encoding utf8NoBOM

foreach ($assetDirectory in @('Engines', 'Models')) {
    $assetRoot = Join-Path $portablePath $assetDirectory
    if (-not (Test-Path -LiteralPath $assetRoot)) {
        continue
    }
    foreach ($file in Get-ChildItem -LiteralPath $assetRoot -File -Recurse) {
        $relativePath = [System.IO.Path]::GetRelativePath($portablePath, $file.FullName).Replace('\', '/')
        $sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $componentsByKey["file:$relativePath"] = [ordered]@{
            type = 'file'
            name = $relativePath
            hashes = @([ordered]@{ alg = 'SHA-256'; content = $sha256 })
        }
    }
}

$sbom = [ordered]@{
    bomFormat = 'CycloneDX'
    specVersion = '1.6'
    serialNumber = "urn:uuid:$([Guid]::NewGuid())"
    version = 1
    metadata = [ordered]@{
        timestamp = [DateTime]::UtcNow.ToString('O')
        component = [ordered]@{ type = 'application'; name = 'TinyPix'; version = '4.0' }
    }
    components = @($componentsByKey.Keys | Sort-Object | ForEach-Object { $componentsByKey[$_] })
}
$sbom | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputFile -Encoding utf8NoBOM
