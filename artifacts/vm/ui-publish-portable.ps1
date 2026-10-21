$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$srcRoot = '\\Mac\Home\TinyPix\3.5pro'
$dstRoot = 'C:\TinyPixBuild2'
$logDir  = '\\Mac\Home\TinyPix\3.5pro\artifacts\vm\logs'
$log     = Join-Path $logDir 'publish-portable.log'

function Log([string]$message) {
    $stamp = (Get-Date).ToString('HH:mm:ss')
    Add-Content -Path $log -Value ("[{0}] {1}" -f $stamp, $message) -Encoding utf8
}

Set-Content -Path $log -Value '' -Encoding utf8
Log 'vm-publish-portable start'

# The tree was already staged by ui-publish.ps1; only the extra scripts are new.
$buildDst = Join-Path $dstRoot 'build'
New-Item -ItemType Directory -Path $buildDst -Force | Out-Null
foreach ($file in @('publish-portable.ps1', 'Get-FfmpegEngine.ps1', 'generate-sbom.ps1', 'verify-portable-layout.ps1')) {
    $from = Join-Path $srcRoot "build\$file"
    if (Test-Path $from) {
        Copy-Item $from (Join-Path $buildDst $file) -Force
        Log "copied build\$file"
    }
    else {
        Log "WARN missing build\$file"
    }
}
& attrib -R "$dstRoot\*.*" /S /D
Log 'attrib cleared read-only'

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

Push-Location $dstRoot
Log 'invoking build\publish-portable.ps1'
$watch = [System.Diagnostics.Stopwatch]::StartNew()
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $buildDst 'publish-portable.ps1') *>&1 |
    ForEach-Object { Add-Content -Path $log -Value $_ -Encoding utf8 }
$code = $LASTEXITCODE
$watch.Stop()
Log ("portable publish exit=" + $code + " elapsed=" + [int]$watch.Elapsed.TotalSeconds + "s")
Pop-Location

$zip = Join-Path $dstRoot 'artifacts\TinyPix-4.0-Windows-x64-Portable.zip'
if (Test-Path $zip) {
    $item = Get-Item $zip
    Log ("zip bytes=" + $item.Length + " (" + [math]::Round($item.Length / 1MB, 1) + " MB)")
    Log ("zip sha256=" + (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant())
}
else {
    Log 'ZIP MISSING'
}
Log 'vm-publish-portable done'
exit $code
