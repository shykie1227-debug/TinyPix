[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$ArtifactsDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ArtifactsDirectory))
$stagingRoot = Join-Path $artifactsRoot 'TinyPix-4.0'
$publishRoot = Join-Path $artifactsRoot 'publish-win-x64'
$zipPath = Join-Path $artifactsRoot 'TinyPix-4.0-Windows-x64-Portable.zip'
$hashPath = "$zipPath.sha256"

foreach ($path in @($stagingRoot, $publishRoot, $zipPath, $hashPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

dotnet publish (Join-Path $repoRoot 'src/TinyPix.App/TinyPix.App.csproj') `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishRoot

Copy-Item -LiteralPath $publishRoot -Destination $stagingRoot -Recurse
Copy-Item -Path (Join-Path $repoRoot 'assets/Portable/*') -Destination $stagingRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES') -Destination (Join-Path $stagingRoot 'THIRD_PARTY_NOTICES.txt') -Force

foreach ($directory in @('Runtime', 'Engines', 'Templates', 'Models', 'Licenses', 'Config', 'Data', 'Cache', 'Logs')) {
    New-Item -ItemType Directory -Path (Join-Path $stagingRoot $directory) -Force | Out-Null
}

& (Join-Path $PSScriptRoot 'generate-sbom.ps1') `
    -Project (Join-Path $repoRoot 'src/TinyPix.App/TinyPix.App.csproj') `
    -PortableRoot $stagingRoot `
    -OutputPath (Join-Path $stagingRoot 'sbom.cdx.json') `
    -DependenciesPath (Join-Path $stagingRoot 'dependencies.json')

$files = Get-ChildItem -LiteralPath $stagingRoot -File -Recurse | Sort-Object FullName
$manifestFiles = foreach ($file in $files) {
    [ordered]@{
        path = [System.IO.Path]::GetRelativePath($stagingRoot, $file.FullName).Replace('\', '/')
        size = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{
    product = 'TinyPix 4.0'
    runtimeIdentifier = 'win-x64'
    packaging = 'unpackaged-self-contained-folder'
    createdUtc = [DateTime]::UtcNow.ToString('O')
    files = @($manifestFiles)
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stagingRoot 'build-manifest.json') -Encoding utf8NoBOM

& (Join-Path $PSScriptRoot 'verify-portable-layout.ps1') -Root $stagingRoot

[System.IO.Compression.ZipFile]::CreateFromDirectory($stagingRoot, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $([System.IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath $hashPath -Encoding ascii

Write-Host "Portable package: $zipPath"
Write-Host "SHA-256: $zipHash"
