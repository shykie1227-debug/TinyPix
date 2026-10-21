$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$srcRoot = '\\Mac\Home\TinyPix\3.5pro'
$dstRoot = 'C:\TinyPixBuild2'
$logDir  = '\\Mac\Home\TinyPix\3.5pro\artifacts\vm\logs'
$log     = Join-Path $logDir 'build.log'

function Log([string]$message) {
    $stamp = (Get-Date).ToString('HH:mm:ss')
    Add-Content -Path $log -Value ("[{0}] {1}" -f $stamp, $message) -Encoding utf8
}

Set-Content -Path $log -Value '' -Encoding utf8
Log "vm-ui-build start"

# 1. mirror the source tree ---------------------------------------------------
if (-not (Test-Path $dstRoot)) { New-Item -ItemType Directory -Path $dstRoot -Force | Out-Null }

Log "robocopy src"
& robocopy "$srcRoot\src" "$dstRoot\src" /MIR /NFL /NDL /NJH /NJS /NP /R:2 /W:2 | Out-Null
Log ("robocopy src exit=" + $LASTEXITCODE)

foreach ($file in @('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'TinyPix.sln')) {
    $from = Join-Path $srcRoot $file
    if (Test-Path $from) {
        Copy-Item $from (Join-Path $dstRoot $file) -Force
        Log "copied $file"
    }
}

if (Test-Path (Join-Path $srcRoot 'Models')) {
    Log "robocopy Models"
    & robocopy "$srcRoot\Models" "$dstRoot\Models" /MIR /NFL /NDL /NJH /NJS /NP /R:2 /W:2 | Out-Null
    Log ("robocopy Models exit=" + $LASTEXITCODE)
}

# NuGet refuses to touch read-only copies coming from the shared folder.
& attrib -R "$dstRoot\*.*" /S /D
Log "attrib cleared read-only"

# 2. build ------------------------------------------------------------------
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

Push-Location $dstRoot
Log "dotnet --version: $(& dotnet --version)"

$buildArgs = @(
    'build', 'src\TinyPix.App\TinyPix.App.csproj',
    '-c', 'Release',
    '-p:Platform=x64',
    '-p:ContinuousIntegrationBuild=false',
    '-v', 'minimal',
    '-nologo'
)

Log ("dotnet " + ($buildArgs -join ' '))
$output = & dotnet @buildArgs 2>&1
$code = $LASTEXITCODE
$output | ForEach-Object { Add-Content -Path $log -Value $_ -Encoding utf8 }
Log ("dotnet build exit=" + $code)
Pop-Location

Log "vm-ui-build done"
exit $code
