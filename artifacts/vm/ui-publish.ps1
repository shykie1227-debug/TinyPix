$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$srcRoot = '\\Mac\Home\TinyPix\3.5pro'
$dstRoot = 'C:\TinyPixBuild2'
$logDir  = '\\Mac\Home\TinyPix\3.5pro\artifacts\vm\logs'
$log     = Join-Path $logDir 'publish.log'

function Log([string]$message) {
    $stamp = (Get-Date).ToString('HH:mm:ss')
    Add-Content -Path $log -Value ("[{0}] {1}" -f $stamp, $message) -Encoding utf8
}

Set-Content -Path $log -Value '' -Encoding utf8
Log 'vm-publish start'

if (-not (Test-Path $dstRoot)) { New-Item -ItemType Directory -Path $dstRoot -Force | Out-Null }

# 1. mirror the source tree ---------------------------------------------------
Log 'robocopy src'
& robocopy "$srcRoot\src" "$dstRoot\src" /MIR /NFL /NDL /NJH /NJS /NP /R:2 /W:2 | Out-Null
Log ("robocopy src exit=" + $LASTEXITCODE)

foreach ($file in @('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'TinyPix.sln', 'THIRD_PARTY_NOTICES')) {
    $from = Join-Path $srcRoot $file
    if (Test-Path $from) {
        Copy-Item $from (Join-Path $dstRoot $file) -Force
        Log "copied $file"
    }
}

# 2. build scripts used by the publish ------------------------------------------
$buildDst = Join-Path $dstRoot 'build'
New-Item -ItemType Directory -Path $buildDst -Force | Out-Null
foreach ($file in @('Publish-SingleFile.ps1', 'Get-FfmpegEngine.ps1')) {
    $from = Join-Path $srcRoot "build\$file"
    if (Test-Path $from) {
        Copy-Item $from (Join-Path $buildDst $file) -Force
        Log "copied build\$file"
    }
}

# 3. portable assets the publish lays down beside the exe -----------------------
$assetsDst = Join-Path $dstRoot 'assets\Portable'
New-Item -ItemType Directory -Path $assetsDst -Force | Out-Null
foreach ($file in @('portable.flag', 'OFFLINE_SECURITY.md')) {
    $from = Join-Path $srcRoot "assets\Portable\$file"
    if (Test-Path $from) {
        Copy-Item $from (Join-Path $assetsDst $file) -Force
        Log "copied assets\Portable\$file"
    }
}
& robocopy "$srcRoot\assets\Portable\Config" (Join-Path $assetsDst 'Config') /MIR /NFL /NDL /NJH /NJS /NP /R:2 /W:2 | Out-Null
Log ("robocopy Config exit=" + $LASTEXITCODE)

# NuGet refuses to touch read-only copies coming from the shared folder.
& attrib -R "$dstRoot\*.*" /S /D
Log 'attrib cleared read-only'

# 4. stage the cached FFmpeg asset locally so the publish never hits the network -
$cacheDir = Join-Path $dstRoot '.cache\ffmpeg'
New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
$zipName = 'ffmpeg-master-latest-win64-lgpl.zip'
$zipDst  = Join-Path $cacheDir $zipName
if (Test-Path $zipDst) {
    Log ("ffmpeg asset already staged: " + (Get-Item $zipDst).Length + " bytes")
}
else {
    $zipSrc = Join-Path $srcRoot ".cache\ffmpeg\$zipName"
    if (Test-Path $zipSrc) {
        Log 'copying ffmpeg asset from the shared folder'
        Copy-Item $zipSrc $zipDst -Force
        if (Test-Path $zipDst) {
            Log ("ffmpeg asset staged: " + (Get-Item $zipDst).Length + " bytes")
        }
        else {
            Log 'WARN ffmpeg asset copy failed'
        }
    }
    else {
        Log "WARN ffmpeg asset missing at $zipSrc"
    }
}

# 5. publish ------------------------------------------------------------------
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

Push-Location $dstRoot
Log 'invoking build\Publish-SingleFile.ps1'
$watch = [System.Diagnostics.Stopwatch]::StartNew()
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $buildDst 'Publish-SingleFile.ps1') *>&1 |
    ForEach-Object { Add-Content -Path $log -Value $_ -Encoding utf8 }
$code = $LASTEXITCODE
$watch.Stop()
Log ("publish exit=" + $code + " elapsed=" + [int]$watch.Elapsed.TotalSeconds + "s")
Pop-Location

$outputRoot = Join-Path $dstRoot 'artifacts\TinyPix-single-file'
$exe = Join-Path $outputRoot 'TinyPix.exe'
if (Test-Path $exe) {
    $item = Get-Item $exe
    Log ("exe bytes=" + $item.Length + " (" + [math]::Round($item.Length / 1MB, 1) + " MB)")
    Log ("sha256=" + (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant())
    foreach ($name in @('portable.flag', 'OFFLINE_SECURITY.md', 'THIRD_PARTY_NOTICES.txt')) {
        Log ("companion " + $name + "=" + (Test-Path (Join-Path $outputRoot $name)))
    }
    foreach ($name in @('Engines\ffmpeg.exe', 'Engines\ffprobe.exe')) {
        Log ("engine " + $name + "=" + (Test-Path (Join-Path $outputRoot $name)))
    }
    $total = (Get-ChildItem -LiteralPath $outputRoot -File -Recurse | Measure-Object -Property Length -Sum).Sum
    Log ("directory bytes=" + $total)
}
else {
    Log 'EXE MISSING'
}
Log 'vm-publish done'
exit $code
