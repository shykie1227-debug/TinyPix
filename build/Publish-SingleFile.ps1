<#
 .SYNOPSIS
   Publish TinyPix 4.0 as a single, self-contained TinyPix.exe.

 .DESCRIPTION
   Windows App SDK 1.5+ supports .NET PublishSingleFile for UNPACKAGED +
   SELF-CONTAINED WinUI 3 apps, which is exactly how TinyPix.App is configured.
   The result is one .exe holding the managed assemblies, the native libraries
   and the Windows App SDK runtime. On first launch the bundle extracts into a
   temp directory (IncludeAllContentForSelfExtract) - that is why
   WindowsAppSdkUndockedRegFreeWinRTInitialize stays enabled in the csproj.

   Required MSBuild properties (Microsoft Learn, "Distribute an unpackaged
   WinUI 3 app" > Single-file EXE):
     WindowsPackageType              = None
     WindowsAppSDKSelfContained      = true
     SelfContained                   = true
     EnableMsixTooling               = true
     IncludeAllContentForSelfExtract = true
     PublishSingleFile               = true

   The portable boundary still applies: TinyPix refuses to start without
   portable.flag next to the executable, and the FFmpeg engine lives in
   Engines/. This script therefore emits a small companion set next to the
   single exe. Nothing is installed and no registry key is written.

 .USAGE
   pwsh ./build/Publish-SingleFile.ps1
   pwsh ./build/Publish-SingleFile.ps1 -SkipFfmpeg
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$ArtifactsDirectory = 'artifacts',
    [switch]$SkipFfmpeg
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ArtifactsDirectory))
$outputRoot = Join-Path $artifactsRoot 'TinyPix-single-file'
$appProject = Join-Path $repoRoot 'src/TinyPix.App/TinyPix.App.csproj'

if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

# Lock file guard: see the long comment in publish-portable.ps1. The -r win-x64
# publish needs the lock disabled (NU1004), and that lets NuGet rewrite the lock
# files, so they are snapshotted and restored around the publish.
$lockFiles = @(Get-ChildItem -Path (Join-Path $repoRoot 'src'), (Join-Path $repoRoot 'tests') `
        -Recurse -Filter 'packages.lock.json' -File -ErrorAction SilentlyContinue)
$lockBackup = Join-Path ([IO.Path]::GetTempPath()) ('tinypix-locks-' + [guid]::NewGuid().ToString('N'))
foreach ($lock in $lockFiles) {
    $relative = $lock.FullName.Substring($repoRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $target = Join-Path $lockBackup $relative
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $lock.FullName -Destination $target -Force
}

Write-Host '==> Publishing single-file win-x64 self-contained bundle'
try {
    dotnet publish $appProject `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:RestoreLockedMode=false `
        -o $outputRoot
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish (single file) failed.' }
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

$exe = Join-Path $outputRoot 'TinyPix.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "TinyPix.exe was not produced in $outputRoot"
}

$stragglers = Get-ChildItem -LiteralPath $outputRoot -File |
    Where-Object { $_.Name -ne 'TinyPix.exe' } |
    Select-Object -ExpandProperty Name
if ($stragglers) {
    Write-Warning "Windows App SDK left files beside the exe: $($stragglers -join ', ')"
}

Write-Host '==> Restoring the portable boundary next to the exe'
Copy-Item -LiteralPath (Join-Path $repoRoot 'assets/Portable/portable.flag') -Destination $outputRoot -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'assets/Portable/OFFLINE_SECURITY.md') -Destination $outputRoot -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES') -Destination (Join-Path $outputRoot 'THIRD_PARTY_NOTICES.txt') -Force
Copy-Item -Path (Join-Path $repoRoot 'assets/Portable/Config') -Destination $outputRoot -Recurse -Force
foreach ($directory in @('Engines', 'Licenses', 'Data', 'Cache', 'Logs')) {
    New-Item -ItemType Directory -Path (Join-Path $outputRoot $directory) -Force | Out-Null
}

if ($SkipFfmpeg) {
    Write-Warning 'SkipFfmpeg requested: video tools will fail until Engines/ffmpeg.exe is supplied.'
}
else {
    & (Join-Path $PSScriptRoot 'Get-FfmpegEngine.ps1') -PortableRoot $outputRoot
}

$exeItem = Get-Item -LiteralPath $exe
$exeHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
"$exeHash  TinyPix.exe" | Set-Content -LiteralPath (Join-Path $outputRoot 'TinyPix.exe.sha256') -Encoding ascii

$totalBytes = (Get-ChildItem -LiteralPath $outputRoot -File -Recurse | Measure-Object -Property Length -Sum).Sum
Write-Host ''
Write-Host '==> Single-file artifact'
Write-Host "    exe       : $exe"
Write-Host "    exe size  : $([math]::Round($exeItem.Length / 1MB, 1)) MB"
Write-Host "    directory : $([math]::Round($totalBytes / 1MB, 1)) MB (exe + portable.flag + Engines)"
Write-Host "    sha256    : $exeHash"
