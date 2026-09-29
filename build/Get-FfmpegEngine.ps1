<#
 .SYNOPSIS
   Fetch the frozen Windows x64 LGPL FFmpeg engine into a portable release
   layout and verify its hash plus encoder surface.

 .DESCRIPTION
   TinyPix never bundles the FFmpeg binary into source control. The pipeline
   downloads it at BUILD time (allowed by LOCAL_RULES.md §1/§6) and copies it
   into <PortableRoot>/Engines so the released product stays 100% offline at
   RUNTIME (LOCAL_RULES.md §2).

   Default source is the BtbN static auto-build "lgpl" variant: it explicitly
   lacks every GPL-only library (libx264, libx265) and includes
   scripts.d/50-openh264.sh, so libopenh264 - the encoder LgplVideoCodec emits -
   is present. See design/DEPENDENCY-BASELINE.md §6.

   The rolling "latest" release changes daily, which breaks reproducibility.
   Pin -ReleaseTag and -ExpectedSha256 for any formal release; the observed
   SHA-256 is always echoed so it can be frozen into DEPENDENCY-BASELINE.md.

 .USAGE
   pwsh ./build/Get-FfmpegEngine.ps1 -PortableRoot artifacts/TinyPix-4.0
   pwsh ./build/Get-FfmpegEngine.ps1 -CacheDir /cache/ffmpeg -ExpectedSha256 <hash>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PortableRoot,

    [string]$ReleaseTag = 'latest',

    [string]$AssetName = 'ffmpeg-master-latest-win64-lgpl.zip',

    [string]$ExpectedSha256 = '',

    [string]$CacheDir = '',

    [switch]$SkipDownload
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($CacheDir)) {
    # Keep the download OUTSIDE the cleaned artifacts tree so manual copy/build
    # cycles can reuse it (AGENTS.md runtime rules).
    $CacheDir = Join-Path $repoRoot '.cache/ffmpeg'
}
New-Item -ItemType Directory -Path $CacheDir -Force | Out-Null

$enginesDirectory = Join-Path $PortableRoot 'Engines'
New-Item -ItemType Directory -Path $enginesDirectory -Force | Out-Null

$zipPath = Join-Path $CacheDir $AssetName
$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$ReleaseTag/$AssetName"

if (-not $SkipDownload -and -not (Test-Path -LiteralPath $zipPath)) {
    Write-Host "==> FFmpeg asset : $AssetName"
    Write-Host "==> Release tag  : $ReleaseTag"
    Write-Host "==> Source URL   : $url"
    Write-Host "==> Downloading to $zipPath"
    Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing
}
elseif ($SkipDownload) {
    Write-Host "==> SkipDownload requested; using cached asset only."
}
else {
    Write-Host "==> Reusing cached asset $zipPath"
}

$actualSha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "==> Asset SHA-256 : $actualSha256"

if ([string]::IsNullOrWhiteSpace($ExpectedSha256)) {
    Write-Warning ('No -ExpectedSha256 supplied. Freeze this value into ' +
        'design/DEPENDENCY-BASELINE.md and pass it again for a reproducible release.')
}
elseif ($actualSha256 -ne $ExpectedSha256.ToLowerInvariant()) {
    throw "FFmpeg asset hash mismatch. expected=$ExpectedSha256 actual=$actualSha256"
}
else {
    Write-Host '==> Asset hash matches the expected value.'
}

$expandRoot = Join-Path $CacheDir "expanded-$ReleaseTag"
if (Test-Path -LiteralPath $expandRoot) {
    Remove-Item -LiteralPath $expandRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $expandRoot -Force | Out-Null
Expand-Archive -LiteralPath $zipPath -DestinationPath $expandRoot -Force

$copied = 0
foreach ($name in @('ffmpeg.exe', 'ffprobe.exe')) {
    $source = Get-ChildItem -LiteralPath $expandRoot -Filter $name -Recurse -File |
        Select-Object -First 1
    if ($null -eq $source) {
        throw "The FFmpeg archive did not contain $name."
    }

    $destination = Join-Path $enginesDirectory $name
    Copy-Item -LiteralPath $source.FullName -Destination $destination -Force
    $copied++
    Write-Host "==> Engine ready : $destination ($([math]::Round($source.Length / 1MB, 1)) MB)"
}

if ($copied -eq 0) {
    throw 'No FFmpeg binaries were copied into the portable Engines directory.'
}

# Licence evidence has to travel with the binary (LGPL redistribution duty).
$licencesDirectory = Join-Path $PortableRoot 'Licenses/FFmpeg'
New-Item -ItemType Directory -Path $licencesDirectory -Force | Out-Null
# Only text evidence travels with the binaries. A previous revision used
# `-Include` against a directory path, which silently matched everything in the
# archive and duplicated ~390 MB of executables into the licence folder.
$licenceFiles = Get-ChildItem -LiteralPath $expandRoot -Recurse -File |
    Where-Object { $_.Name -like 'LICENSE*' -or $_.Name -like 'COPYING*' -or $_.Name -like 'README*' }
foreach ($file in $licenceFiles) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $licencesDirectory $file.Name) -Force
}
if (-not $licenceFiles) {
    Write-Warning 'No LICENSE/COPYING/README file was found in the FFmpeg archive.'
}

# Hard gate: the whole video tool set assumes libopenh264 exists in the LGPL
# build. Fail loudly here rather than shipping an exe whose jobs all fail.
$ffmpeg = Join-Path $enginesDirectory 'ffmpeg.exe'
$encoders = & $ffmpeg -hide_banner -encoders 2>$null
if (-not ($encoders -match 'libopenh264')) {
    throw ('This FFmpeg build does not expose libopenh264. TinyPix video tools ' +
        'depend on it; pick another LGPL build or update LgplVideoCodec.')
}
Write-Host '==> libopenh264 present in the LGPL engine.'

if ($encoders -match 'libx264|libx265|libfdk') {
    throw 'This FFmpeg build exposes a GPL/nonfree encoder; DEPENDENCY-BASELINE.md §6 forbids it.'
}
Write-Host '==> No GPL/nonfree encoder in the engine. OK.'
