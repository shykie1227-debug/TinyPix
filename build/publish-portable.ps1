[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$ArtifactsDirectory = 'artifacts',
    [switch]$SkipFfmpeg
)

$ErrorActionPreference = 'Stop'

# [System.IO.Path]::GetRelativePath is .NET Core / PowerShell 7 only; Windows
# PowerShell 5.1 runs on .NET Framework. Compute relative paths through
# System.Uri so the manifest works on either host.
function Get-RelativePortablePath {
    param([string]$BasePath, [string]$TargetPath)

    $baseUri = New-Object System.Uri(($BasePath.TrimEnd('\', '/')) + '\')
    $targetUri = New-Object System.Uri($TargetPath)
    $relative = $baseUri.MakeRelativeUri($targetUri).ToString()
    return [System.Uri]::UnescapeDataString($relative) -replace '/', '\'
}

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

# --- lock file guard -------------------------------------------------------
# Publishing self-contained forces -r win-x64, but the committed
# packages.lock.json is generated without a RID, so NuGet rejects it with
# NU1004 ("runtime identifiers have changed"). Two failed workarounds, for the
# record: regenerating every lock with -r win-x64 breaks the RID-less
# `dotnet restore --locked-mode` in CI, and -p:RestorePackagesWithLockFile=false
# is rejected outright by NU1005 because the lock files exist.
#
# So the publish runs with the lock disabled - dependency *versions* are already
# pinned by the preceding `dotnet restore --locked-mode` step - and the lock
# files are snapshotted here and restored afterwards, because disabling the lock
# also lets NuGet rewrite them. The repository must never end up with RID-ful
# lock files from a publish run.
$lockFiles = @(Get-ChildItem -Path (Join-Path $repoRoot 'src'), (Join-Path $repoRoot 'tests') `
        -Recurse -Filter 'packages.lock.json' -File -ErrorAction SilentlyContinue)
$lockBackup = Join-Path ([IO.Path]::GetTempPath()) ('tinypix-locks-' + [guid]::NewGuid().ToString('N'))
foreach ($lock in $lockFiles) {
    $relative = $lock.FullName.Substring($repoRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $target = Join-Path $lockBackup $relative
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $lock.FullName -Destination $target -Force
}

try {
    dotnet publish (Join-Path $repoRoot 'src/TinyPix.App/TinyPix.App.csproj') `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:RestoreLockedMode=false `
        -o $publishRoot
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (portable folder) failed.' }
}
finally {
    foreach ($lock in $lockFiles) {
        $relative = $lock.FullName.Substring($repoRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        $source = Join-Path $lockBackup $relative
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination $lock.FullName -Force
        }
    }
    if (Test-Path -LiteralPath $lockBackup) {
        Remove-Item -LiteralPath $lockBackup -Recurse -Force
    }
}

Copy-Item -LiteralPath $publishRoot -Destination $stagingRoot -Recurse
Copy-Item -Path (Join-Path $repoRoot 'assets/Portable/*') -Destination $stagingRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES') -Destination (Join-Path $stagingRoot 'THIRD_PARTY_NOTICES.txt') -Force

foreach ($directory in @('Runtime', 'Engines', 'Templates', 'Models', 'Licenses', 'Config', 'Data', 'Cache', 'Logs')) {
    New-Item -ItemType Directory -Path (Join-Path $stagingRoot $directory) -Force | Out-Null
}

if (-not $SkipFfmpeg) {
    & (Join-Path $PSScriptRoot 'Get-FfmpegEngine.ps1') -PortableRoot $stagingRoot
}
else {
    Write-Warning 'SkipFfmpeg requested: Engines/ stays empty and every media tool will fail.'
}

& (Join-Path $PSScriptRoot 'generate-sbom.ps1') `
    -Project (Join-Path $repoRoot 'src/TinyPix.App/TinyPix.App.csproj') `
    -PortableRoot $stagingRoot `
    -OutputPath (Join-Path $stagingRoot 'sbom.cdx.json') `
    -DependenciesPath (Join-Path $stagingRoot 'dependencies.json')

$files = Get-ChildItem -LiteralPath $stagingRoot -File -Recurse | Sort-Object FullName
$manifestFiles = foreach ($file in $files) {
    [ordered]@{
        path = (Get-RelativePortablePath -BasePath $stagingRoot -TargetPath $file.FullName).Replace('\', '/')
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
# UTF-8 without BOM via .NET: `Set-Content -Encoding utf8NoBOM` requires
# PowerShell 6+ and breaks on Windows PowerShell 5.1.
$manifestJson = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText(
    (Join-Path $stagingRoot 'build-manifest.json'),
    $manifestJson,
    (New-Object System.Text.UTF8Encoding($false)))

& (Join-Path $PSScriptRoot 'verify-portable-layout.ps1') -Root $stagingRoot

# System.IO.Compression.FileSystem is loaded by default on PowerShell 7 but not
# on Windows PowerShell 5.1, where [System.IO.Compression.ZipFile] is otherwise
# reported as a missing type.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stagingRoot, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $([System.IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath $hashPath -Encoding ascii

Write-Host "Portable package: $zipPath"
Write-Host "SHA-256: $zipHash"
